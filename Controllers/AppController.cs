using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OfficeOpenXml;
using Dapper;
using digital_services.Services.Input;
using digital_services.Services.Process;
using digital_services.Services.Output;
using digital_services.Services.Validation;
using digital_services.Objects.App;
using digital_services.Utilities;

namespace digital_services.Controllers
{
    [Route("api/app")]
    public class AppController : Controller
    {
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;
        private readonly InputService _inputService;
        private readonly ProcessService _processService;
        private readonly OutputService _outputService;
        private readonly ValidationService _validationService;
        private static HttpClient client = new HttpClient();
        private readonly string _baseDir;
        private string _baseDirProcessing;
        private string _baseDirSettings;
        private readonly string _pythonDir;

        public AppController(DatabaseConfig databaseService, IOptions<DirectoriesConfiguration> directoriesConfigOptions, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService, InputService inputService, ProcessService processService, OutputService outputService, ValidationService validationService)
        {
            _databaseService = databaseService;
            _baseDir = directoriesConfigOptions.Value.BaseDir;
            _apiSettings = apiSettingsOptions.Value;
            _baseDirProcessing = Path.Combine(_baseDir, "Processing");
            _baseDirSettings = Path.Combine(_baseDir, "Settings");
            _pythonDir = directoriesConfigOptions.Value.PythonDir;
            _tokenValidationService = tokenValidationService;
            _inputService = inputService;
            _processService = processService;
            _outputService = outputService;
            _validationService = validationService;
        }

        /* ------ Procesamientos de Digital Services ------ */
        [HttpPost("send")]
        [RequestSizeLimit(943718400)] // Límite de tamaño de la solicitud a 900 MB
        [RequestFormLimits(MultipartBodyLengthLimit = 943718400)] // Límite de tamaño del cuerpo multipart a 900 MB
        public async Task<IActionResult> Send()
        {
            try
            {
                /* ------------------------ Input ------------------------ */
                // Se verifica si el token enviado es válido
                // Preparación de directorios y lectura de datos enviados desde el form
                // Se preparan las rutas y se manejan los archivos de tipo file
                // Se ingresa la información en el DB

                // Validación de token
                bool isValid = await _tokenValidationService.IsValidTokenAsync(Request.Form["token"].ToString());
                if (!isValid) return BadRequest("Token inválido");

                // Preparación de directorios y lectura de datos
                var (processId, paths, shouldInsert) = await _inputService.PrepareFileProcessing(Request.Form["content_reference"].ToString(), _baseDirProcessing);

                //Manejo de archivos enviados
                var files = Request.Form.Files;
                var processingResults = new List<Dictionary<string, object>>();
                foreach (var file in files)
                {
                    var result = await _inputService.HandleFile(file, paths);
                    processingResults.Add(result);
                }

                var formDataEntries = _inputService.ExtractFormData(Request.Form, processingResults);
                // Convertir formDataEntries a una cadena JSON para imprimir
                //string formDataEntriesJson = JsonConvert.SerializeObject(formDataEntries, Formatting.Indented);
                //Console.WriteLine("FormDataEntriesBack: " + formDataEntriesJson);

                //Datos necesarios: Outputpath y variables_config
                var dataNecessary = new List<Dictionary<string, object>>();
                dataNecessary.Add(new Dictionary<string, object> { { "process_id", processId } });
                dataNecessary.Add(new Dictionary<string, object> { { "api_action_ds", _apiSettings.ApiActionDs } });
                dataNecessary.Add(new Dictionary<string, object> { { "input_directory", paths["inputPath"] } });
                dataNecessary.Add(new Dictionary<string, object> { { "output_directory", paths["outputPath"] } });
                dataNecessary.Add(new Dictionary<string, object> { { "config_directory", paths["configPath"] } });
                var contentId = Request.Form["content_id"].ToString();
                var parameters = new DynamicParameters();
                parameters.Add("@contentId", contentId);

                //Variables
                Dictionary<string, List<Dictionary<string, object>>> variablesConfig = new Dictionary<string, List<Dictionary<string, object>>>();
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    var contentVariableResults = await connection.QueryAsync("sp_GetContentVariables", parameters, commandType: CommandType.StoredProcedure);
                    if (contentVariableResults != null && contentVariableResults.Any())
                    {
                        foreach (var result in contentVariableResults)
                        {
                            var tipo = result.tipo.ToString();
                            if (!variablesConfig.ContainsKey(tipo))
                            {
                                variablesConfig[tipo] = new List<Dictionary<string, object>>();
                            }

                            var itemValue = (int)result.type_id == 2
                                ? result.value.ToString()
                                : Path.Combine(_baseDir, result.value.ToString());

                            var item = new Dictionary<string, object>
                        {
                            { "id", result.id },
                            { "type_id", result.type_id },
                            { "name", result.name.ToString() },
                            { "value", itemValue }
                        };

                            // Si el tipo es "Sistema" (type_id es 2), añadimos los campos adicionales.
                            if ((int)result.type_id == 2)
                            {
                                item["is_final"] = result.is_final;
                                item["is_excluded"] = result.is_excluded;
                            }

                            variablesConfig[tipo].Add(item);
                        }

                        dataNecessary.Add(new Dictionary<string, object> { { "variablesConfig", variablesConfig } });
                    }
                }

                Console.WriteLine("processing results: " + JsonConvert.SerializeObject(processingResults, Formatting.Indented));
                Console.WriteLine("Entradas: " + JsonConvert.SerializeObject(formDataEntries, Formatting.Indented));

                /* --- Traslado de archivos de configuración ---*/
                // Función lambda para obtener una ruta específica de dataNecessary
                Func<string, string> getPath = key =>
                    dataNecessary.FirstOrDefault(d => d.ContainsKey(key))?[key]?.ToString();

                // Obtener la ruta del directorio de destino
                var destinationFolder = getPath("config_directory");
                if (string.IsNullOrEmpty(destinationFolder))
                {
                    Console.WriteLine("Error: No se pudo obtener el directorio de configuración destino.");
                    return BadRequest("Error: No se pudo obtener el directorio de configuración destino.");
                }

                // Función lambda para copiar un archivo al directorio de destino y devolver la nueva ubicación
                Func<string, string> copyToDestinationAndGetFinalPath = sourcePath =>
                {
                    var destinationPath = Path.Combine(destinationFolder, Path.GetFileName(sourcePath));
                    System.IO.File.Copy(sourcePath, destinationPath, overwrite: true);
                    return destinationPath;
                };

                // Verifica si variablesConfig tiene la key "Configuración-Files"
                if (variablesConfig.ContainsKey("Configuración-Files"))
                {
                    // Itera sobre los archivos de configuración
                    foreach (var configItem in variablesConfig["Configuración-Files"])
                    {
                        // Si type_id es 1, copia el archivo
                        if ((int)configItem["type_id"] == 1)
                        {
                            var sourcePath = configItem["value"].ToString();
                            if (string.IsNullOrEmpty(sourcePath))
                            {
                                Console.WriteLine($"Error: No se pudo obtener la ruta para {configItem["name"].ToString()}");
                                return BadRequest($"Error: No se pudo obtener la ruta para {configItem["name"].ToString()}");
                            }
                            var finalPath = copyToDestinationAndGetFinalPath(sourcePath);

                            // Añadimos el nuevo campo 'value_final' al diccionario configItem
                            configItem["value_final"] = finalPath;
                        }
                    }
                }

                //Console.WriteLine("Datos necesarios: " + JsonConvert.SerializeObject(dataNecessary, Formatting.Indented));

                //Fixed data
                var service_id = _inputService.GetValueFromEntries(formDataEntries, "service_id");
                var content_id = _inputService.GetValueFromEntries(formDataEntries, "content_id");
                var process_type_name = _inputService.GetValueFromEntries(formDataEntries, "process_type_name");
                var email_user = _inputService.GetValueFromEntries(formDataEntries, "email_user");
                //var api_action = _apiSettings.ApiTest == null ? _inputService.GetValueFromEntries(formDataEntries, "api_action") : _apiSettings.ApiTest;
                var api_action = string.IsNullOrEmpty(_apiSettings.ApiTest)
                ? _inputService.GetValueFromEntries(formDataEntries, "api_action")
                : _apiSettings.ApiTest;

                var api_action_validate = _inputService.GetValueFromEntries(formDataEntries, "api_action_validate");

                /// *************** Registro de proceso en DB *************** ///
                var parameters_register = new DynamicParameters();
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    var notesEntry = formDataEntries.FirstOrDefault(e => e["name"].ToString().Equals("notes"));
                    var notes = notesEntry?["data_value"].ToString();
                    var contentReference = Request.Form["content_reference"].ToString();
                    var company = Request.Form["company"].ToString().Split('|');
                    var profile_id = company[0];
                    var company_code = company[1];

                    parameters_register.Add("@process_id", processId);
                    parameters_register.Add("@service_id", service_id);
                    parameters_register.Add("@user_email", email_user);
                    parameters_register.Add("@notes", notes);
                    parameters_register.Add("@should_insert", shouldInsert);  // Agrega el booleano
                    parameters_register.Add("@content_reference", contentReference);  // Agrega el content_reference
                    parameters_register.Add("@profile_id", profile_id);  // Agrega el profile_id
                    parameters_register.Add("@company_code", company_code);  // Agrega el company_code
                    await connection.ExecuteAsync("sp_InsertProcess", parameters_register, commandType: CommandType.StoredProcedure);

                    /// *************** Registro de datos de detalle en DB *************** ///
                    // Crear y escribir en el archivo data_output.txt
                    var fullPath = Path.Combine(paths["configPath"], "data_output.txt");
                    var outputData = new Dictionary<string, object>
                {
                    { "_baseDir", _baseDir },
                    { "dataNecessary", dataNecessary }
                };
                    var jsonOutput = JsonConvert.SerializeObject(outputData, Formatting.Indented);
                    await System.IO.File.WriteAllTextAsync(fullPath, jsonOutput);

                    // Llamada al procedimiento almacenado sp_InsertProcessData
                    var parameters_spInsertProcessData = new DynamicParameters();
                    parameters_spInsertProcessData.Add("@process_id", processId);
                    parameters_spInsertProcessData.Add("@data_path", fullPath.Replace(_baseDir, ""));
                    await connection.ExecuteAsync("sp_InsertProcessData", parameters_spInsertProcessData, commandType: CommandType.StoredProcedure);

                    /// *************** Registro de detalles en DB *************** ///
                    foreach (var entry in formDataEntries)
                    {
                        if (entry.ContainsKey("element-id") && int.TryParse(entry["element-id"].ToString(), out int parsedElementId))
                        {
                            var elementId = Convert.ToInt32(entry["element-id"]);
                            var name = entry["name"].ToString();
                            var dataValue = entry["data_value"].ToString();
                            var inputSize = entry.ContainsKey("input_size") && entry["input_size"] != null ? entry["input_size"].ToString() : "";
                            var inputQtyFiles = entry.ContainsKey("input_qty_files") && entry["input_qty_files"] != null ? entry["input_qty_files"].ToString() : "";
                            var dataLabel = entry.ContainsKey("element-label") && entry["element-label"].ToString() != "N/A" ? entry["element-label"].ToString() : null; // Nuevo campo opcional

                            var parameters_detail = new DynamicParameters();
                            parameters_detail.Add("@process_id", processId);
                            parameters_detail.Add("@element_id", elementId);
                            parameters_detail.Add("@name", name);

                            if (!string.IsNullOrEmpty(inputSize) || !string.IsNullOrEmpty(inputQtyFiles))
                            {
                                parameters_detail.Add("@data_value", dataValue.Replace(_baseDir, ""));
                            }
                            else
                            {
                                parameters_detail.Add("@data_value", dataValue);
                            }

                            parameters_detail.Add("@input_size", inputSize);
                            parameters_detail.Add("@input_qty_files", inputQtyFiles);
                            parameters_detail.Add("@data_label", dataLabel); // Agregar el nuevo parámetro opcional

                            await connection.ExecuteAsync("sp_InsertProcessDetail", parameters_detail, commandType: CommandType.StoredProcedure);
                        }
                    }
                }

                var response = new
                {
                    Token = Request.Form["token"].ToString(),
                    ProcessId = processId,
                    ProcessTypeName = process_type_name, // Asegúrate de obtener este valor como se hace en el endpoint Process
                    ApiAction = api_action, // De igual manera, asegúrate de obtener este valor
                    ApiActionValidate = api_action_validate, // De igual manera, asegúrate de obtener este valor
                    DataNecessary = dataNecessary,
                    FormDataEntries = formDataEntries
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                // Registrar la excepción en el archivo de log
                LogException(ex);
                return StatusCode(500, "Ocurrió un error inesperado, revisa los logs para más detalles.");
            }
        }

        [HttpPost("validate")]
        public async Task<IActionResult> Validate([FromBody] ProcessRequestModel requestModel)
        {
            try
            {
                var token = requestModel.Token;
                var processId = requestModel.ProcessId;
                string path_ticket = new DirectoryInfo(requestModel.DataNecessary.FirstOrDefault(d => d.ContainsKey("input_directory"))?["input_directory"].ToString())?.Parent?.FullName;

                // Convertir requestModel a una cadena JSON para imprimir
                string requestModelJson = JsonConvert.SerializeObject(requestModel, Formatting.Indented);
                Console.WriteLine($"Request model recibido: {requestModelJson}"); // Imprime la solicitud recibida

                var response = await _validationService.ExecuteValidation(requestModel.ApiActionValidate, requestModel.DataNecessary, requestModel.FormDataEntries);
                string responseBody = await response.Content.ReadAsStringAsync(); // Lee el cuerpo de la respuesta

                if (response.IsSuccessStatusCode)
                {
                    //Console.WriteLine($"Respuesta exitosa: {responseBody}"); // Imprime la respuesta exitosa
                    return Ok(responseBody); // Envía el cuerpo de la respuesta al cliente
                }
                else
                {
                    //Console.WriteLine($"Error en la validación: {responseBody}"); // Imprime el contenido del error
                    var deleteRequest = new DeleteTicketFilesRequest
                    {
                        Token = token,
                        ProcessId = processId,
                        Path = path_ticket,
                    };
                    await _validationService.DeleteTicketFiles(deleteRequest);

                    return StatusCode((int)response.StatusCode, responseBody);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                return StatusCode(500, "Ocurrió un error inesperado, revisa los logs para más detalles.");
            }
        }

        [HttpPost("process")]
        public async Task<IActionResult> Process([FromBody] ProcessRequestModel requestModel)
        {
            var token = requestModel.Token;
            var processId = requestModel.ProcessId;
            var process_type_name = requestModel.ProcessTypeName;
            var api_action = requestModel.ApiAction;
            var dataNecessary = requestModel.DataNecessary;
            var formDataEntries = requestModel.FormDataEntries;

            bool isStatusUpdated = await _processService.UpdateProcessGeneralStatus(processId, 2);
            if (!isStatusUpdated)
            {
                Console.WriteLine("Error al actualizar el estado general del proceso.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Error al actualizar el estado general del proceso.");
            }

            Console.WriteLine($"process_type_name: {process_type_name}");

            if (process_type_name.Equals("Python", StringComparison.OrdinalIgnoreCase))
            {
                // Lógica para el procesamiento en Python
                _processService.ExecuteProcessing(api_action, dataNecessary, formDataEntries, _baseDir);
                Console.WriteLine("Entramos a Python");

                return Accepted(new { Message = "Proceso iniciado", ProcessId = processId });
            }
            else if (process_type_name.Equals("C#", StringComparison.OrdinalIgnoreCase))
            {
                _processService.ExecuteProcessing(api_action, dataNecessary, formDataEntries, _baseDir);

                return Accepted(new { Message = "Proceso iniciado", ProcessId = processId });
            }
            else if (process_type_name.Contains("Modeler", StringComparison.OrdinalIgnoreCase)) // Aquí está la modificación
            {
                // Lógica para el procesamiento en Modeler
                _processService.ExecuteProcessing(api_action, dataNecessary, formDataEntries, _baseDir);

                return Accepted(new { Message = "Proceso iniciado", ProcessId = processId });
            }
            else
            {
                return BadRequest("Tipo de proceso no soportado.");
            }

            // Si llegaste hasta aquí y no has retornado nada aún, debes retornar algo.
            return BadRequest("Tipo de proceso no reconocido o no implementado aún.");
        }


        [HttpPost("output")]
        public async Task<IActionResult> Output([FromBody] dynamic request)
        {
            try
            {
                /* ------------------------ Output ------------------------ */
                // Se comprimen los resultados en formato ZIP.
                // Se registra a nivel de BD los detalles del sistema

                // Convertir el cuerpo de la solicitud 'dynamic' a una cadena JSON y imprimirlo
                string requestBodyJson = JsonConvert.SerializeObject(request);
                Console.WriteLine($"Datos recibidos en Output: {requestBodyJson}");

                string processId = request.ProcessId;
                //Console.WriteLine("processId => {0}", request.ProcessId);
                if (string.IsNullOrEmpty(processId))
                {
                    return BadRequest("ProcessId es requerido.");
                }

                string dataPath = string.Empty;
                List<ProcessDetail> detailsList = new List<ProcessDetail>();

                // Asegúrate de que outputDirectoryJson esté declarado al inicio, en el mismo contexto donde lo usarás
                string outputDirectoryJson = string.Empty; // Declarar fuera del bloque para que esté disponible en todo el método

                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // Validación de subprocesos en estado 'procesando'
                    var processingSubprocessesCount = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM process_detail_system WHERE process_id = @ProcessId AND status_id = 15",
                        new { ProcessId = processId }
                    );

                    if (processingSubprocessesCount > 0)
                    {
                        return BadRequest("Aún no se puede generar el output para el proceso dado que hay otros subprocesos procesando.");
                    }

                    // Obtener output_directory_json desde el procedimiento almacenado
                    outputDirectoryJson = await connection.ExecuteScalarAsync<string>(
                        "sp_GetContentVariableOutput",
                        new { ProcessId = processId },
                        commandType: CommandType.StoredProcedure
                    );

                    // Si no se obtiene un valor, establecer una cadena vacía
                    if (string.IsNullOrEmpty(outputDirectoryJson))
                    {
                        outputDirectoryJson = string.Empty; // Establecer vacío si no hay valor
                        LogInfo($"outputDirectoryJson no contiene ningún valor para processId: {processId}. Estableciendo valor vacío.");
                    }
                    else
                    {
                        // Combinar con _baseDir si se obtiene un valor
                        outputDirectoryJson = Path.Combine(_baseDir, outputDirectoryJson);
                        LogInfo($"Ruta combinada de output_directory_json: {outputDirectoryJson}");
                        Console.WriteLine($"Ruta combinada de output_directory_json: {outputDirectoryJson}");
                    }

                    using (var multi = await connection.QueryMultipleAsync("sp_GetOutputDataAndDetails", new { ProcessId = processId }, commandType: CommandType.StoredProcedure))
                    {
                        var dataPathResult = await multi.ReadFirstOrDefaultAsync<string>();
                        if (dataPathResult == null) { return NotFound($"No se encontró data_path para el process_id: {processId}"); }
                        if (dataPathResult.StartsWith("\\")) { dataPathResult = dataPathResult.TrimStart('\\'); }

                        dataPath = Path.Combine(_baseDir, dataPathResult);
                        detailsList = (await multi.ReadAsync<ProcessDetail>()).ToList();
                        Console.WriteLine($"Ruta _baseDir: {_baseDir}");
                        Console.WriteLine($"Ruta completa del archivo: {dataPath}");
                    }
                }
                // Leer el archivo especificado por data_path y continuar como antes
                if (!System.IO.File.Exists(dataPath))
                {
                    return NotFound("Archivo especificado en data_path no existe.");
                }
                var jsonContent = await System.IO.File.ReadAllTextAsync(dataPath);
                var outputData = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);
                var dataNecessary = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(outputData["dataNecessary"].ToString());
                var __baseDir = outputData["_baseDir"].ToString();

                /******** Generación de output ********/
                var zipResult = _outputService.GenerateOutput(__baseDir, dataNecessary, detailsList, processId, outputDirectoryJson);
                Console.WriteLine($"Zip file created at: {zipResult.ZipFilePath}");

                bool hasNoErrors = await _processService.CheckErrorProcess(processId);
                int newStatusId = hasNoErrors ? 3 : 4;
                bool isStatusUpdated_ = await _processService.UpdateProcessGeneralStatus(processId, newStatusId);
                if (!isStatusUpdated_) { Console.WriteLine("Error al actualizar el estado general del proceso."); }

                var ExecutionTimeEnd = DateTime.Now;
                string readableSize = FileProcessingUtility.GetReadableFileSize(zipResult.ZipFilePath);

                string resultMessage = hasNoErrors ? "Procesamiento exitoso" : "Procesamiento finalizado con errores";
                bool isStatusFinish = await _processService.UpdateProcessResult(processId, ExecutionTimeEnd, resultMessage, (zipResult.ZipFilePath).Replace(_baseDir, ""), readableSize);
                if (!isStatusFinish) { Console.WriteLine("Error al actualizar el resultado general del proceso."); }

                return Ok(new { Message = "Output generado con éxito" });

            }
            catch (Exception ex)
            {
                // Registrar la excepción en el archivo de log
                LogException(ex);
                return StatusCode(500, "Ocurrió un error inesperado, revisa los logs para más detalles.");
            }
        }

        // Método para registrar las excepciones en el archivo log
        private void LogException(Exception ex)
        {
            string logDirectory = @"C:\Digital-Services";
            string logFilePath = Path.Combine(logDirectory, "app_log.txt");

            // Crear el directorio si no existe
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Construir el mensaje de error
            string logMessage = $@"
                ============================
                Fecha y Hora: {DateTime.Now}
                Mensaje: {ex.Message}
                Tipo: {ex.GetType().FullName}
                StackTrace: {ex.StackTrace}
                ============================
                ";

            // Escribir el log en el archivo, creando el archivo si no existe
            System.IO.File.AppendAllText(logFilePath, logMessage);
        }

        // Método para registrar información general en el archivo log
        private void LogInfo(string message)
        {
            string logDirectory = @"C:\Digital-Services";
            string logFilePath = Path.Combine(logDirectory, "app_info_log.txt");

            // Crear el directorio si no existe
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            // Construir el mensaje de log
            string logMessage = $@"
                ============================
                Fecha y Hora: {DateTime.Now}
                Mensaje: {message}
                ============================
                ";

            // Escribir el log en el archivo, creando el archivo si no existe
            System.IO.File.AppendAllText(logFilePath, logMessage);
        }

    }
}