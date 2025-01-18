using Dapper;
using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Options;
using digital_services.Objects.App;

namespace digital_services.Controllers
{
    [Route("api/queue/")]
    public class QueueController : Controller
    {
        private static SemaphoreSlim logSemaphore = new SemaphoreSlim(1, 1);
        private readonly TokenValidationService _tokenValidationService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;

        public QueueController(DatabaseConfig databaseService, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService, IHttpClientFactory httpClientFactory)
        {
            _databaseService = databaseService;
            _apiSettings = apiSettingsOptions.Value;
            _tokenValidationService = tokenValidationService;
            _httpClientFactory = httpClientFactory;
        }

        /* ------ Ingreso de ticket a la cola ------ */
        [HttpPost("enqueue")]
        public async Task<IActionResult> Enqueue()
        {
            string requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            var jsonObject = JObject.Parse(requestBody);

            try
            {
                // Extracción de la ruta base desde 'FolderPath'
                var folderPath = jsonObject["AdditionalData"]["nivel_1_data_modeler"]["FolderPath"].ToString();
                var additionalDataFileName = "additional_data.txt";
                var additionalDataFilePath = Path.Combine(folderPath, additionalDataFileName);

                // Serialización de AdditionalData para almacenamiento
                string additionalDataContent = JsonConvert.SerializeObject(jsonObject["AdditionalData"]);
                await System.IO.File.WriteAllTextAsync(additionalDataFilePath, additionalDataContent);

                // Deserialización de los datos requeridos desde jsonObject
                var technology = jsonObject["Technology"].ToString();
                var technologyEndpoint = jsonObject["TechnologyEndpoint"].ToString();
                var prevEndpointApi = jsonObject["PrevEndpointApi"].ToString();
                var nextEndpointApi = jsonObject["NextEndpointApi"].ToString();
                var basePathReference = jsonObject["BasePathReference"].ToString();
                var processId = jsonObject["AdditionalData"]["nivel_1_data_modeler"]["ProcessId"].ToString();
                var queueStatus = 12; // Asumiendo un estado ejemplo, ajusta según sea necesario

                // Extraer prioridad si está presente en el JSON, si no se recibe, usar 0
                int priority = jsonObject["AdditionalData"]["Priority"]?.ToObject<int>() ?? 0;

                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", processId);
                    parameters.Add("@queue_technology", technology);
                    parameters.Add("@queue_ip_address", _apiSettings.ModelerIpAddressWithPort); // Ejemplo IP, ajusta según sea necesario
                    parameters.Add("@queue_status", queueStatus);
                    parameters.Add("@queue_technology_endpoint", technologyEndpoint);
                    parameters.Add("@queue_api_prev", prevEndpointApi);
                    parameters.Add("@queue_api_next", nextEndpointApi);
                    parameters.Add("@queue_additional_data", additionalDataFilePath); // Ruta al archivo .txt con datos adicionales
                    parameters.Add("@queue_reference_base_path", basePathReference); // Incluyendo 'basePathReference' en los parámetros
                    parameters.Add("@queue_priority", priority);  // Se agrega el valor de prioridad
                    parameters.Add("@error_message", null); // Ajustar según sea necesario
                    parameters.Add("@external_reference", null); // Ajustar según sea necesario

                    await connection.ExecuteAsync("sp_InsertProcessQueue", parameters, commandType: CommandType.StoredProcedure);
                }

                Console.WriteLine("Inserción en la cola realizada con éxito.");
                return Ok(new { success = true, message = "Inserción en la cola realizada con éxito." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error durante el proceso de encolado: {ex.Message}");
                return BadRequest("Error en el proceso de encolado.");
            }
        }

        /********** ------ Ejecución de tickets en cola ------ **********/
        [HttpGet("run")]
        public async Task<IActionResult> Run()
        {
            try
            {
                // Obtiene una conexión de base de datos
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // ************** 1. Process Run Queue  ******************//
                    var processesToRun = await connection.QueryAsync<dynamic>(
                        "sp_GetProcessesToRun",
                        commandType: CommandType.StoredProcedure);

                    if (!processesToRun.Any())
                    {
                        Console.WriteLine("No hay procesos disponibles para ejecutar o el balanceador está ocupado.");
                        return Ok(new { Message = "No hay procesos disponibles para ejecutar o el balanceador está ocupado." });
                    }

                    // Revisar si el SP devolvió un mensaje indicativo como 'El balanceador está ocupado'
                    var firstProcess = processesToRun.FirstOrDefault();
                    if (firstProcess != null && firstProcess.Message == "El balanceador está ocupado")
                    {
                        Console.WriteLine("El balanceador está ocupado.");
                        return Ok(new { Message = "No hay procesos disponibles para ejecutar o el balanceador está ocupado." });
                    }

                    var tasks = processesToRun.Select(process =>
                        Task.Run(async () =>
                        {
                            await ProcessEachAsync(process, connection);
                        })
                    );

                    await Task.WhenAll(tasks);
                    return Ok(new { Message = "Procesos enviados correctamente a ejecutar." });
                }
            }
            catch (Exception ex)
            {
                // Agrega información detallada del error incluyendo el stack trace
                string errorMessage = $"Error al ejecutar procesos: {ex.Message}. Stack Trace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner Exception: {ex.InnerException.Message}. Inner Stack Trace: {ex.InnerException.StackTrace}";
                }

                Console.WriteLine(errorMessage);
                await LogMessage(errorMessage);
                return BadRequest(new { Message = errorMessage });
            }
        }

        [HttpGet("run-priority")]
        public async Task<IActionResult> RunPriority()
        {
            try
            {
                // Obtiene una conexión de base de datos
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // ************** 1. Process Run Queue  ******************//
                    var processesToRun = await connection.QueryAsync<dynamic>(
                        "sp_GetProcessesToRunPriority",
                        commandType: CommandType.StoredProcedure);

                    if (!processesToRun.Any())
                    {
                        Console.WriteLine("No hay procesos disponibles prioritarios para ejecutar o el balanceador está ocupado.");
                        return Ok(new { Message = "No hay procesos disponibles prioritarios para ejecutar o el balanceador está ocupado." });
                    }

                    // Revisar si el SP devolvió un mensaje indicativo como 'El balanceador está ocupado'
                    var firstProcess = processesToRun.FirstOrDefault();
                    if (firstProcess != null && firstProcess.Message == "El balanceador está ocupado")
                    {
                        Console.WriteLine("El balanceador está ocupado.");
                        return Ok(new { Message = "No hay procesos disponibles prioritarios para ejecutar o el balanceador está ocupado." });
                    }

                    var tasks = processesToRun.Select(process =>
                        Task.Run(async () =>
                        {
                            await ProcessEachAsync(process, connection);
                        })
                    );

                    await Task.WhenAll(tasks);
                    return Ok(new { Message = "Procesos enviados correctamente a ejecutar." });
                }
            }
            catch (Exception ex)
            {
                // Agrega información detallada del error incluyendo el stack trace
                string errorMessage = $"Error al ejecutar procesos: {ex.Message}. Stack Trace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner Exception: {ex.InnerException.Message}. Inner Stack Trace: {ex.InnerException.StackTrace}";
                }

                Console.WriteLine(errorMessage);
                await LogMessage(errorMessage);
                return BadRequest(new { Message = errorMessage });
            }
        }

        /********** ------ Ejecución de Tickets listos para tecnología ------ **********/
        [HttpGet("run-technology")]
        public async Task<IActionResult> GetProcessesForTechnology()
        {
            try
            {
                // Parámetros de salida para recibir desde el procedimiento almacenado
                var parameters = new DynamicParameters();
                parameters.Add("@ProcessSelected", dbType: DbType.Boolean, direction: ParameterDirection.Output);
                parameters.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                // Abre una conexión a la base de datos
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // Ejecuta el procedimiento almacenado para obtener los procesos listos para tecnología
                    var processes = await connection.QueryAsync<dynamic>(
                        "sp_GetProcessesReadyForTechnology",
                        parameters,
                        commandType: CommandType.StoredProcedure);

                    // Verifica las variables de salida para saber si se seleccionó un proceso
                    bool processSelected = parameters.Get<bool>("@ProcessSelected");
                    int rowsAffected = parameters.Get<int>("@RowsAffected");

                    // Si no se seleccionó ningún proceso, retorna un mensaje apropiado
                    if (!processSelected)
                    {
                        return Ok(new { Message = "Sin procesos a procesar con la tecnología." });
                    }

                    // Si no hay filas afectadas, significa que no se seleccionó ningún proceso
                    if (rowsAffected == 0)
                    {
                        return Ok(new { Message = "No hay procesos listos para procesar en tecnología." });
                    }

                    // Si se seleccionó un proceso, retornamos los detalles en formato JSON
                    return Ok(processes);
                }
            }
            catch (Exception ex)
            {
                // En caso de error, devuelve el mensaje con detalles
                string errorMessage = $"Error al obtener procesos: {ex.Message}. Stack Trace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner Exception: {ex.InnerException.Message}. Inner Stack Trace: {ex.InnerException.StackTrace}";
                }

                Console.WriteLine(errorMessage);
                await LogMessage(errorMessage);
                return BadRequest(new { Message = errorMessage });
            }
        }

        [HttpGet("run-technology-priority")]
        public async Task<IActionResult> GetProcessesForTechnologyPriority()
        {
            try
            {
                // Parámetros de salida para recibir desde el procedimiento almacenado
                var parameters = new DynamicParameters();
                parameters.Add("@ProcessSelected", dbType: DbType.Boolean, direction: ParameterDirection.Output);
                parameters.Add("@RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                // Abre una conexión a la base de datos
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // Ejecuta el procedimiento almacenado para obtener los procesos listos para tecnología
                    var processes = await connection.QueryAsync<dynamic>(
                        "sp_GetPriorityProcessesReadyForTechnology",
                        parameters,
                        commandType: CommandType.StoredProcedure);

                    // Verifica las variables de salida para saber si se seleccionó un proceso
                    bool processSelected = parameters.Get<bool>("@ProcessSelected");
                    int rowsAffected = parameters.Get<int>("@RowsAffected");

                    // Si no se seleccionó ningún proceso, retorna un mensaje apropiado
                    if (!processSelected)
                    {
                        return Ok(new { Message = "Sin procesos a procesar con la tecnología." });
                    }

                    // Si no hay filas afectadas, significa que no se seleccionó ningún proceso
                    if (rowsAffected == 0)
                    {
                        return Ok(new { Message = "No hay procesos listos para procesar en tecnología." });
                    }

                    // Si se seleccionó un proceso, retornamos los detalles en formato JSON
                    return Ok(processes);
                }
            }
            catch (Exception ex)
            {
                // En caso de error, devuelve el mensaje con detalles
                string errorMessage = $"Error al obtener procesos: {ex.Message}. Stack Trace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner Exception: {ex.InnerException.Message}. Inner Stack Trace: {ex.InnerException.StackTrace}";
                }

                Console.WriteLine(errorMessage);
                await LogMessage(errorMessage);
                return BadRequest(new { Message = errorMessage });
            }
        }

        /********** ------ Ejecución de next API - Results ------ **********/
        [HttpGet("run-next")]
        public async Task<IActionResult> RunNextProcesses()
        {
            try
            {
                await LogMessage("Iniciando ejecución de RunNextProcesses.");

                string base_action_api;
                string output_api;

                // Función auxiliar para extraer la base de la URL de una acción de API
                string GetBaseApiUrl(string apiActionUrl)
                {
                    var uri = new Uri(apiActionUrl);
                    string baseApiUrl = uri.GetLeftPart(UriPartial.Authority);
                    string path = uri.AbsolutePath;
                    int apiIndex = path.IndexOf("/api/", StringComparison.OrdinalIgnoreCase) + "/api/".Length;
                    string basePath = path.Substring(0, apiIndex);
                    return baseApiUrl + basePath; // Devuelve la base hasta '/api/'
                }

                // Obtener la URL base de la acción del API
                base_action_api = _apiSettings.ApiActionDs; // Asegúrate de que _apiSettings y ApiActionDs están definidos y accesibles
                output_api = GetBaseApiUrl(base_action_api) + "app/output";

                await LogMessage($"Base API action URL obtenida: {base_action_api}");
                await LogMessage($"Output API URL: {output_api}");

                // Obtiene una conexión de base de datos
                using (var connection = await _databaseService.Database2.OpenConnectionAsync()) // Asegúrate de que _databaseService y Database2 están definidos y accesibles
                {
                    await LogMessage("Conexión a la base de datos establecida.");

                    // Obtener procesos listos para el siguiente paso
                    var processesToRunNext = await connection.QueryAsync<dynamic>(
                        "sp_GetProcessToRunNext",
                        commandType: CommandType.StoredProcedure);

                    if (!processesToRunNext.Any())
                    {
                        string message = "No hay procesos listos para el siguiente paso disponibles para ejecutar.";
                        await LogMessage(message);
                        return Ok(new { Message = message });
                    }

                    await LogMessage($"Procesos listos para el siguiente paso encontrados: {processesToRunNext.Count()}");

                    var tasksNext = processesToRunNext.Select(process =>
                        Task.Run(async () =>
                        {
                            await ProcessEachNextAsync(process, connection); // Asegúrate de que el método ProcessEachNextAsync está definido y es accesible
                        })
                    );

                    await Task.WhenAll(tasksNext);

                    // Envío de Output para procesos listos para el siguiente paso
                    var groupedProcessesNext = processesToRunNext.GroupBy(p => p.process_id).Select(g => g.First());
                    HttpClient httpClient = _httpClientFactory.CreateClient(); // Asegúrate de que _httpClientFactory está definido y accesible

                    foreach (var process in groupedProcessesNext)
                    {
                        var json = JsonConvert.SerializeObject(new { ProcessId = process.process_id });
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        await LogMessage($"Enviando datos al Output API para el proceso {process.process_id}.");

                        var response = await httpClient.PostAsync(output_api, content);
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorResponse = await response.Content.ReadAsStringAsync();
                            string errorMessage = $"Error en la solicitud para el proceso {process.process_id}: {response.StatusCode}. Mensaje: {errorResponse}";
                            await LogMessage(errorMessage);
                        }
                        else
                        {
                            await LogMessage($"Datos enviados exitosamente al Output API para el proceso {process.process_id}.");
                        }
                    }

                    string successMessage = "Procesos listos para el siguiente paso ejecutados correctamente y datos enviados.";
                    await LogMessage(successMessage);
                    return Ok(new { Message = successMessage });
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error al ejecutar procesos siguientes: {ex.Message}. Stack Trace: {ex.StackTrace}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" Inner Exception: {ex.InnerException.Message}. Inner Stack Trace: {ex.InnerException.StackTrace}";
                }

                await LogMessage(errorMessage);
                return BadRequest(new { Message = errorMessage });
            }
        }

        ///######################## Process Each Run ############################
        public async Task ProcessEachAsync(dynamic process, IDbConnection connection)
        {
            var originalQueueStatus = 12; // Estado "Registrado"
            var originalProcessStatus = 19; // Estado "Pendiente"
            var processingQueueStatus = 15; // Estado "En Proceso"
            var errorQueueStatus = 4; // Estado de error
            List<int> updatedTaskIds = new List<int>();

            try
            {
                if (!await ValidateProcessStatusAsync(process, connection)) return;

                var httpClient = _httpClientFactory.CreateClient();
                // Se ha removido la línea que establecía el timeout, ya que se está haciendo un envío tipo "fire and forget".
                // httpClient.Timeout = TimeSpan.FromMinutes(7);

                await UpdateQueueStatusAsync(process, connection, processingQueueStatus);
                AdditionalData additionalData = await DeserializeAdditionalDataAsync(process);
                if (additionalData == null)
                {
                    throw new InvalidOperationException("Los datos adicionales no pueden ser nulos.");
                }

                var taskIdsResult = await UpdateModelerProcessStatusAsync(additionalData, process, connection, processingQueueStatus);
                updatedTaskIds.AddRange(taskIdsResult);

                // Se envía la solicitud a Modeler sin esperar la respuesta completa (fire and forget)
                await ExecuteTechnologyModelerAsync(additionalData, process, connection, httpClient);
                await LogMessage($"Proceso {process.process_id} enviado con éxito para su procesamiento.");
            }
            catch (Exception ex)
            {
                string detailedErrorMessage;
                if (ex is TimeoutException)
                {
                    detailedErrorMessage = "El equipo de procesamientos de Modeler no respondió en el tiempo esperado y podría estar inalcanzable.";
                }
                else
                {
                    detailedErrorMessage = $"Se produjo un error al procesar en Modeler: {ex.Message}";
                }

                await LogMessage($"Error durante el procesamiento: {detailedErrorMessage}");
                await RollbackStateUpdatesAsync(connection, process.id, updatedTaskIds, originalQueueStatus, originalProcessStatus, errorQueueStatus, detailedErrorMessage, process.process_id);
                throw;
            }
        }

        private async Task<bool> ValidateProcessStatusAsync(dynamic process, IDbConnection connection)
        {
            var currentStatus = await connection.QueryFirstOrDefaultAsync<int>(
                "SELECT queue_status FROM process_queue WHERE id = @Id",
                new { Id = process.id });

            if (currentStatus != 12)
            {
                await LogMessage($"El proceso {process.process_id} no está en estado 'Registrado' y no será procesado.");
                return false;
            }

            return true;
        }

        private async Task UpdateQueueStatusAsync(dynamic process, IDbConnection connection, int newStatus)
        {
            // Actualiza el estado de la cola e incrementa el contador de reintentos en 1
            await connection.ExecuteAsync(
                "UPDATE process_queue SET queue_status = @Status, retry_count = retry_count + 1 WHERE id = @Id",
                new { Status = newStatus, Id = process.id });

            await LogMessage($"Estado de la cola actualizado a {newStatus} para el proceso {process.process_id}.");
        }

        private async Task<AdditionalData> DeserializeAdditionalDataAsync(dynamic process)
        {
            string path = process.queue_additional_data.ToString();
            if (!System.IO.File.Exists(path))
            {
                await LogMessage($"Archivo no encontrado: {path}");
                return null;
            }

            string json = await System.IO.File.ReadAllTextAsync(path);
            return JsonConvert.DeserializeObject<AdditionalData>(json);
        }

        private async Task<List<int>> UpdateModelerProcessStatusAsync(AdditionalData additionalData, dynamic process, IDbConnection connection, int processingQueueStatus)
        {
            List<int> localUpdatedTaskIds = new List<int>();

            await LogMessage($"Inicio de UpdateModelerProcessStatusAsync para el proceso {process.process_id} con Status: {processingQueueStatus}.");

            try
            {
                var processTasks = additionalData.Nivel_2_Data_Next_Endpoint["ProcessTasks"].ToObject<Dictionary<string, dynamic>>();
                var processTasksJson = JsonConvert.SerializeObject(processTasks);
                await LogMessage($"Se encontraron {processTasks.Count} tareas para procesar. Detalle de tareas: {processTasksJson}");

                await connection.ExecuteAsync("UPDATE process SET status_id = @StatusId WHERE id = @Id", new { StatusId = processingQueueStatus, Id = process.process_id });
                await LogMessage($"Estado del proceso {process.process_id} actualizado a {processingQueueStatus}.");

                foreach (var task in processTasks)
                {
                    var taskId = task.Key;
                    var procesoId = (int)task.Value.proceso_id;
                    try
                    {
                        await LogMessage($"Procesando TaskId: {taskId}, con Proceso ID: {process.process_id} para actualización.");

                        string updateStatusQuery = "UPDATE process_detail_system SET status_id = @StatusId WHERE variable_id = @VariableId AND process_id = @ProcessId";
                        var rowsAffected = await connection.ExecuteAsync(updateStatusQuery, new { StatusId = processingQueueStatus, VariableId = procesoId, ProcessId = process.process_id });

                        await LogMessage($"Consulta de actualización ejecutada para TaskId: {taskId}, con Proceso ID: {process.process_id}. Filas afectadas: {rowsAffected}.");
                        localUpdatedTaskIds.Add(procesoId); // Ensure procesoId is being added, which is the correct int identifier for each task
                    }
                    catch (Exception ex)
                    {
                        await LogMessage($"Error al procesar TaskId: {taskId}, con Proceso ID: {process.process_id}. Excepción: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                await LogMessage($"Error general en UpdateModelerProcessStatusAsync para el proceso {process.process_id}. Excepción: {ex.Message}");
            }

            await LogMessage($"Finalización de UpdateModelerProcessStatusAsync para el proceso {process.process_id}. Tareas actualizadas: {localUpdatedTaskIds.Count}");

            return localUpdatedTaskIds;
        }

        private async Task ExecuteTechnologyModelerAsync(AdditionalData additionalData, dynamic process, IDbConnection connection, HttpClient httpClient)
        {
            var modelerData = additionalData.Nivel_1_Data_Modeler;
            string api_action_ds = _apiSettings.ApiActionDs;
            var requestData = new
            {
                FolderPath = modelerData.FolderPath,
                ProcessId = modelerData.ProcessId,
                Type = modelerData.Type,
                FlowName = modelerData.FlowName,
                TaskName = modelerData.TaskName,
                Archivos = modelerData.Archivos,
                ApiActionDs = api_action_ds
            };

            var requestDataJson = JsonConvert.SerializeObject(requestData);
            var content = new StringContent(requestDataJson, Encoding.UTF8, "application/json");
            await LogMessage($"Technology Endpoint => {process.queue_technology_endpoint}");

            // "Fire and forget": Lanza el PostAsync pero no espera a que se complete antes de actualizar la DB
            var postTask = httpClient.PostAsync(process.queue_technology_endpoint, content);

            string updateQuery = @"UPDATE process_queue 
                                    SET queue_technology_response = @Response 
                                    WHERE id = @Id 
                                    AND queue_reference_base_path = @QueueReferenceBasePath";

            try
            {
                Console.WriteLine("Actualizando la base de datos con la siguiente consulta:");
                Console.WriteLine(updateQuery);

                await connection.ExecuteAsync(updateQuery, new
                {
                    Response = "Solicitud enviada con éxito",
                    Id = process.id,
                    QueueReferenceBasePath = process.queue_reference_base_path
                });

                Console.WriteLine($"Proceso {process.process_id} actualizado correctamente en la base de datos.");
                await LogMessage($"Proceso {process.process_id} marcado como enviado.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al actualizar la base de datos para el proceso {process.process_id}: {ex.Message}");
                await LogMessage($"Error al actualizar la base de datos para el proceso {process.process_id}: {ex.Message}");
                throw;
            }

            try
            {
                // Esperamos la finalización del postTask, pero no bloqueamos el flujo principal antes de actualizar DB
                await postTask;
                await LogMessage("Solicitud enviada correctamente al endpoint de tecnología (Modeler Call).");
            }
            catch (Exception ex)
            {
                // Si ocurre un error al enviar la solicitud después de actualizar la DB
                await LogMessage($"Error al enviar la solicitud al endpoint de tecnología: {ex.Message}");
            }
        }


        ///######################## Process Each Next ############################
        public async Task ProcessEachNextAsync(dynamic process, IDbConnection connection)
        {
            var originalQueueStatus = 12; // Estado "Registrado"
            var originalProcessStatus = 19; // Estado "Pendiente"
            var processingQueueStatus = 15; // Estado "En Proceso"
            var errorQueueStatus = 4; // Estado de error
            List<int> updatedTaskIds = new List<int>();

            try
            {
                await LogMessage($"Iniciando ProcessEachNextAsync para el proceso {process.process_id}.");

                // Crear el cliente HttpClient usando HttpClientFactory con un timeout de 5 minutos
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5); // Establece el timeout en 5 minutos

                AdditionalData additionalData = await DeserializeAdditionalDataAsync(process);
                if (additionalData == null)
                {
                    string errorMessage = $"Los datos adicionales no pueden ser nulos para el proceso {process.process_id}.";
                    await LogMessage(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                // Formar la ruta completa del inputPath
                string inputPath = Path.Combine(process.queue_path_data_to_process.ToString(), "results");
                await LogMessage($"Input path para el proceso {process.process_id}: {inputPath}");

                // Definir el outputPath como newResultsFolderPath
                string outputPath = additionalData.Nivel_2_Data_Next_Endpoint["ResultsFolderPath"].ToString();
                await LogMessage($"Output path para el proceso {process.process_id}: {outputPath}");

                // === ELIMINAR ARCHIVOS PREVIOS EN outputPath ===
                try
                {
                    if (Directory.Exists(outputPath))
                    {
                        var filesToDelete = Directory.GetFiles(outputPath);
                        foreach (var file in filesToDelete)
                        {
                            System.IO.File.Delete(file);
                        }
                        await LogMessage($"Archivos eliminados en {outputPath}: {filesToDelete.Length}");
                    }
                    else
                    {
                        await LogMessage($"El directorio {outputPath} no existe. No se eliminarán archivos.");
                    }
                }
                catch (Exception exDel)
                {
                    await LogMessage($"Error al intentar eliminar archivos previos en {outputPath}: {exDel.Message}");
                    // Dependiendo de tu lógica, podrías decidir si aquí lanzas excepción o sigues adelante.
                }

                // Crear el objeto con el inputPath y outputPath para enviarlo al endpoint
                var downloadRequest = new
                {
                    inputPath = inputPath,
                    outputPath = outputPath
                };

                // Serializar el objeto a JSON
                var requestContent = new StringContent(JsonConvert.SerializeObject(downloadRequest), Encoding.UTF8, "application/json");

                // Hacer la solicitud POST al nuevo endpoint para descargar y copiar los resultados
                string downloadResultsEndpoint = "http://localhost:5050/api/execute-process/download-results";
                await LogMessage($"Enviando solicitud al endpoint de descarga de resultados para el proceso {process.process_id}.");

                var response = await httpClient.PostAsync(downloadResultsEndpoint, requestContent);

                if (!response.IsSuccessStatusCode)
                {
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"Error en la solicitud de descarga de resultados para el proceso {process.process_id}: {response.StatusCode}. Mensaje: {errorResponse}";
                    await LogMessage(errorMessage);
                    return;
                }

                await LogMessage($"Descarga de resultados exitosa para el proceso {process.process_id}.");

                // Si la solicitud fue exitosa, continuar con el siguiente paso
                await ExecuteNextApiAsync(additionalData, process, httpClient, connection);
            }
            catch (TaskCanceledException ex)
            {
                if (!ex.CancellationToken.IsCancellationRequested)
                {
                    string errorMessage = $"El proceso de descarga superó el tiempo límite de 5 minutos para el proceso {process.process_id}.";
                    await LogMessage(errorMessage);
                }
                else
                {
                    string errorMessage = $"Error durante el procesamiento del proceso {process.process_id}: {ex.Message}";
                    await LogMessage(errorMessage);
                }
                throw;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error durante el procesamiento del proceso {process.process_id}: {ex.Message}";
                await LogMessage(errorMessage);
                throw;
            }
        }

        private async Task ExecuteNextApiAsync(AdditionalData additionalData, dynamic process, HttpClient httpClient, IDbConnection connection)
        {
            // Verifica si la próxima API a llamar está definida
            if (!string.IsNullOrEmpty(process.queue_api_next))
            {
                await LogMessage($"Preparando para enviar datos a la siguiente API: {process.queue_api_next} para el proceso {process.process_id}");

                var requestData = JsonConvert.SerializeObject(additionalData.Nivel_2_Data_Next_Endpoint);
                await LogMessage($"Datos serializados para enviar para el proceso {process.process_id}: {requestData}");

                // Construimos la solicitud manualmente
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, process.queue_api_next)
                {
                    Content = new StringContent(requestData, Encoding.UTF8, "application/json")
                };

                // Agregamos el header con el valor de 'queue_reference_base_path'
                // Puedes usar el nombre de header que quieras, por ejemplo: "X-Queue-Reference-Base-Path"
                requestMessage.Headers.Add("X-Queue-Reference-Base-Path", process.queue_reference_base_path);

                await LogMessage($"Enviando solicitud a {process.queue_api_next} para el proceso {process.process_id}...");

                // Ejecutamos la solicitud usando SendAsync en lugar de PostAsync
                var response = await httpClient.SendAsync(requestMessage);

                // Log del código de estado
                await LogMessage($"[SendAsync] Código de estado: {response.StatusCode}");
                var rawResponseContent = await response.Content.ReadAsStringAsync();
                await LogMessage($"[SendAsync] Contenido respuesta:\n{rawResponseContent}");

                // Verificamos la respuesta de la solicitud
                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    string errorMessage = $"Error en la solicitud a la siguiente API {process.queue_api_next} para el proceso {process.process_id}: {response.StatusCode}. Mensaje: {errorResponse}";
                    await LogMessage(errorMessage);
                    return;
                }

                // Deserializamos el contenido de la respuesta
                var responseContent = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<dynamic>(responseContent);

                // Verificamos si el mensaje contiene "archivo de 0 KB"
                if (responseObject?.message != null &&
                    ((string)responseObject.message).Contains("archivo de 0 KB", StringComparison.InvariantCultureIgnoreCase))
                {
                    await LogMessage($"Se detectó un archivo de 0 KB en el mensaje. No se actualizará la base de datos para el proceso {process.process_id}.");
                    return;
                }

                await LogMessage($"Datos enviados exitosamente a la siguiente API para el proceso {process.process_id}.");

                // Actualiza el estado de la cola y el campo 'queue_is_ready_to_next' en la base de datos
                string updateStatusQuery = @"UPDATE process_queue
                                            SET queue_status = @Status, 
                                                queue_is_ready_to_next = 0 
                                            WHERE id = @Id";
                await LogMessage($"Actualizando estado de la cola y queue_is_ready_to_next para el proceso {process.process_id}...");

                await connection.ExecuteAsync(updateStatusQuery, new { Status = 3, Id = process.id });

                await LogMessage($"Estado de la cola actualizado a 3 y 'queue_is_ready_to_next' a 0 para el proceso {process.process_id}.");
            }
            else
            {
                string message = $"No se ha definido 'queue_api_next' para el proceso {process.process_id}, no se realizará ninguna acción.";
                await LogMessage(message);
            }
        }

        //################### Rollback #####################
        private async Task RollbackStateUpdatesAsync(IDbConnection connection, int processId, List<int> updatedTaskIds, int originalQueueStatus, int originalProcessStatus, int errorQueueStatus, string errorMessage, string processId_)
        {
            try
            {
                // Determinar si se debería revertir al estado original o marcar como error
                int statusToApply = errorQueueStatus != default(int) ? errorQueueStatus : originalQueueStatus;

                // Revertir el estado de la cola al original o a un estado de error, según corresponda
                await connection.ExecuteAsync(
                    "UPDATE process_queue SET queue_status = @Status, error_message = @ErrorMessage WHERE id = @Id",
                    new { Status = statusToApply, ErrorMessage = errorMessage, Id = processId });

                // Actualizar el estado y el mensaje de error en la tabla de process
                await connection.ExecuteAsync(
                    "UPDATE process SET status_id = @StatusId, error_message = @ErrorMessage WHERE id = @Id",
                    new { StatusId = errorQueueStatus, ErrorMessage = errorMessage, Id = processId_ });

                string statusMessage = statusToApply == errorQueueStatus
                    ? $"[Rollback] Estado de la cola revertido al estado de error ({errorQueueStatus}) para el proceso {processId_}."
                    : $"[Rollback] Estado de la cola revertido al original ({originalQueueStatus}) para el proceso {processId_}.";

                Console.WriteLine(statusMessage);
                await LogMessage(statusMessage);

                // Si no hay tareas actualizadas, no hay necesidad de proceder más
                if (!updatedTaskIds.Any())
                {
                    Console.WriteLine("[Rollback] No hay tareas de Modeler actualizadas para revertir.");
                    await LogMessage("[Rollback] No hay tareas de Modeler actualizadas para revertir.");
                    return;
                }

                // Revertir los estados de las tareas de Modeler actualizadas
                foreach (var taskId in updatedTaskIds)
                {
                    await connection.ExecuteAsync(
                        "UPDATE process_detail_system SET status_id = @StatusId WHERE process_id = @ProcessId AND variable_id = @VariableId",
                        new { StatusId = originalProcessStatus, ProcessId = processId_, VariableId = taskId });

                    string taskMessage = $"[Rollback] Estado de la tarea {taskId} del proceso {processId_} revertido al original ({originalProcessStatus}).";
                    Console.WriteLine(taskMessage);
                    await LogMessage(taskMessage);
                }
            }
            catch (Exception ex)
            {
                // Logear el error específico para diagnóstico
                string errorMessage_ = $"[Rollback Error] Error al intentar revertir los cambios para el proceso {processId_}. Detalle del error: {ex.Message}";
                //Console.WriteLine(errorMessage_);
                await LogMessage(errorMessage_);
                throw; // Mantener el rethrow simple para preservar el stack trace original
            }
        }

        //############################################################

        private async Task LogMessage(string message)
        {
            string logFilePath = @"C:\Digital-Services\debugger.txt";
            string logEntry = $"{DateTime.Now}: {message}\n";
            string separator = "--------------------------------\n";
            string logContent = logEntry + separator; // Prepara el contenido del log con la línea separadora al final

            await logSemaphore.WaitAsync(); // Espera hasta obtener el acceso exclusivo

            try
            {
                if (System.IO.File.Exists(logFilePath))
                {
                    string existingContent = await System.IO.File.ReadAllTextAsync(logFilePath);
                    logContent += existingContent;
                }
                await System.IO.File.WriteAllTextAsync(logFilePath, logContent);
            }
            finally
            {
                logSemaphore.Release(); // Libera el semáforo para que otro proceso o hilo pueda acceder
            }
        }

        //############################################################
        private async Task ProcessEachAsyncOld(dynamic process, IDbConnection connection)
        {
            var originalQueueStatus = 12; // Estado "Registrado"
            var originalProcessStatus = 19; // Estado "Registrado"
            var processingQueueStatus = 15; // Estado "En Proceso"
            var errorQueueStatus = 4; // Considera agregar un estado de error si es relevante para tu aplicación
            List<int> updatedTaskIds = new List<int>();

            try
            {
                await LogMessage($"Iniciando procesamiento del proceso {process.process_id}.");
                if (await connection.QueryFirstOrDefaultAsync<int>("SELECT queue_status FROM process_queue WHERE id = @Id", new { Id = process.id }) != 12)
                {
                    await LogMessage($"El proceso {process.process_id} no está en estado de 'Registrado' y no será procesado.");
                    Console.WriteLine($"El proceso {process.process_id} no está en estado de 'Registrado' y no será procesado.");
                    return;
                }

                await LogMessage($"Estado de la cola actualizado a {processingQueueStatus} para el proceso {process.process_id} iniciando.");
                string initialUpdateQuery = "UPDATE process_queue SET queue_status = @Status WHERE id = @Id";
                await connection.ExecuteAsync(initialUpdateQuery, new { Status = processingQueueStatus, Id = process.id });

                string additionalDataPath = process.queue_additional_data.ToString();
                await LogMessage($"Procesando: {additionalDataPath}");

                if (!System.IO.File.Exists(additionalDataPath))
                {
                    Console.WriteLine($"Archivo no encontrado: {additionalDataPath}");
                    await LogMessage($"Archivo no encontrado: {additionalDataPath}");
                    return;
                }

                string additionalDataJson = await System.IO.File.ReadAllTextAsync(additionalDataPath);
                AdditionalData additionalData = JsonConvert.DeserializeObject<AdditionalData>(additionalDataJson);
                string newResultsFolderPath = additionalData.Nivel_2_Data_Next_Endpoint["ResultsFolderPath"].ToString();

                await LogMessage($"Datos deserializados para el proceso {process.process_id}");
                Console.WriteLine($"Datos deserializados para el proceso {process.process_id}");
                //await LogMessage($"Ruta de carpeta de resultados: {newResultsFolderPath}");
                //Console.WriteLine($"Ruta de carpeta de resultados: {newResultsFolderPath}");


                /************* Actualización de estado (en curso) de procesos de Modeler  *************/
                var processTasks = additionalData.Nivel_2_Data_Next_Endpoint["ProcessTasks"].ToObject<Dictionary<string, dynamic>>();
                var updateTasks = processTasks.Select(async task =>
                {
                    var taskKey = task.Key;
                    var procesoId = (int)task.Value.proceso_id; // Asegúrate de que este cast sea seguro
                    await LogMessage($"Task Key: {taskKey}, Proceso ID: {procesoId}");
                    //Console.WriteLine($"Task Key: {taskKey}, Proceso ID: {procesoId}");

                    string updateStatusQuery = "UPDATE process_detail_system SET status_id = @StatusId WHERE variable_id = @VariableId AND process_id = @ProcessId";
                    await LogMessage($"Ejecutando consulta de actualización: {updateStatusQuery} con StatusId = {processingQueueStatus}, VariableId = {procesoId}, ProcessId = {process.process_id}");
                    var rowsAffected = await connection.ExecuteAsync(updateStatusQuery, new { StatusId = processingQueueStatus, VariableId = procesoId, ProcessId = process.process_id });

                    await LogMessage($"Actualizado: {rowsAffected} filas afectadas para el proceso ID {procesoId} y el task {taskKey}.");
                    //Console.WriteLine($"Actualizado: {rowsAffected} filas afectadas para el proceso ID {procesoId} y el task {taskKey}.");
                    updatedTaskIds.Add(procesoId);
                });

                await Task.WhenAll(updateTasks);

                /************* 1era fase - Executing Technology (Modeler)  *************/
                var requestData = new
                {
                    additionalData.Nivel_1_Data_Modeler.FolderPath,
                    additionalData.Nivel_1_Data_Modeler.ProcessId,
                    additionalData.Nivel_1_Data_Modeler.Type,
                    additionalData.Nivel_1_Data_Modeler.FlowName,
                    additionalData.Nivel_1_Data_Modeler.TaskName,
                    Archivos = additionalData.Nivel_1_Data_Modeler.Archivos
                };

                HttpClient httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromHours(2);

                var requestDataJson = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(requestDataJson, Encoding.UTF8, "application/json");

                await LogMessage($"Technology Endpoint => {process.queue_technology_endpoint}");
                //Console.WriteLine($"Technology Endpoint => {process.queue_technology_endpoint}");

                var response = await httpClient.PostAsync(process.queue_technology_endpoint, content);
                if (!response.IsSuccessStatusCode)
                {
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    await LogMessage($"Error en la solicitud: {response.StatusCode}. Mensaje: {errorResponse}");
                    //Console.WriteLine($"Error en la solicitud: {response.StatusCode}. Mensaje: {errorResponse}");
                    return;
                }

                // Guardamos directamente el stream del contenido de la respuesta en el archivo ZIP
                string resultZipPath = Path.Combine(newResultsFolderPath, $"{process.process_id}_result.zip");
                using (var fileStream = new FileStream(resultZipPath, FileMode.Create))
                {
                    if (response.Content != null)
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                    else
                    {
                        await LogMessage("La respuesta no contiene el contenido esperado.");
                        //Console.WriteLine("La respuesta no contiene el contenido esperado.");
                        return;
                    }
                }

                // Extraer el contenido del archivo ZIP en el directorio especificado
                ZipFile.ExtractToDirectory(resultZipPath, newResultsFolderPath, true);
                System.IO.File.Delete(resultZipPath); // Opcional, si deseas eliminar el archivo ZIP después de extraerlo

                string updateQuery = "UPDATE process_queue SET queue_technology_response = @Response WHERE id = @Id";
                var updateResult = await connection.ExecuteAsync(updateQuery, new { Response = "Execution success", Id = process.id });

                await LogMessage($"Proceso {process.process_id} completado y respuesta procesada con éxito.");
                ///Console.WriteLine($"Proceso {process.process_id} completado y respuesta procesada con éxito.");


                /************* 2da fase - Executing Next API (C#) *************/
                if (!string.IsNullOrEmpty(process.queue_api_next))
                {
                    string nivel2DataJson = JsonConvert.SerializeObject(additionalData.Nivel_2_Data_Next_Endpoint);
                    //Console.WriteLine($"JSON enviado al siguiente API ({process.queue_api_next}): {nivel2DataJson}");

                    HttpClient nextHttpClient = _httpClientFactory.CreateClient();
                    nextHttpClient.Timeout = TimeSpan.FromMinutes(15); // Ajusta este valor según sea necesario
                    var nextContent = new StringContent(nivel2DataJson, Encoding.UTF8, "application/json");

                    var nextResponse = await nextHttpClient.PostAsync(process.queue_api_next, nextContent);
                    if (!nextResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Error en la solicitud al siguiente API: {nextResponse.StatusCode}. Mensaje: {await nextResponse.Content.ReadAsStringAsync()}");
                    }
                    else
                    {
                        Console.WriteLine("Datos enviados exitosamente al siguiente API.");
                        // Aquí se realiza la actualización del campo queue_status en la base de datos
                        string updateStatusQuery = "UPDATE process_queue SET queue_status = @Status WHERE id = @Id";
                        var statusUpdateResult = await connection.ExecuteAsync(updateStatusQuery, new { Status = 3, Id = process.id });
                        Console.WriteLine($"Estado de la cola actualizado a 3 para el proceso {process.process_id}.");
                    }
                }

            }
            catch (Exception ex)
            {
                await LogMessage($"Error durante el procesamiento: {ex.Message}");
                Console.WriteLine($"Error durante el procesamiento: {ex.Message}");
                //await RollbackStateUpdatesAsync(connection, process.id, updatedTaskIds, originalQueueStatus, originalProcessStatus, errorQueueStatus);
                // Si es un error crítico y necesitas informar al método llamador, puedes lanzar una excepción aquí también.
                throw; // Propaga la excepción para manejarla en el método llamador.
            }
        }

        private async Task<bool> ValidateEndpointResponse(HttpClient httpClient, string uri, int timeoutSeconds)
        {
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            try
            {
                var response = await httpClient.GetAsync(uri, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    await LogMessage($"Respuesta exitosa del endpoint de validación: {uri}");
                    return true;
                }
                else
                {
                    await LogMessage($"Respuesta no exitosa del endpoint de validación: {uri}, Código de estado: {response.StatusCode}");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                await LogMessage($"Tiempo de espera excedido para el endpoint de validación: {uri}");
                throw new TimeoutException("Tiempo de espera excedido al validar el endpoint.");
            }
            catch (Exception ex)
            {
                await LogMessage($"Error al realizar la validación del endpoint: {uri}, Error: {ex.Message}");
                throw;
            }
        }

    }
}