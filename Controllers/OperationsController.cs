using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.StaticFiles;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using Dapper;
using digital_services.Utilities;
using digital_services.Objects.App;

namespace digital_services.Controllers
{
    [Route("api/operations/")]
    public class OperationsController : Controller
    {
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;
        private readonly string _baseDir;
        private string _baseDirProcessing;
        private string _baseDirSettings;

        public OperationsController(DatabaseConfig databaseService, IOptions<DirectoriesConfiguration> directoriesConfigOptions, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService)
        {
            _databaseService = databaseService;
            _baseDir = directoriesConfigOptions.Value.BaseDir;
            _apiSettings = apiSettingsOptions.Value;
            _baseDirProcessing = Path.Combine(_baseDir, "Processing");
            _baseDirSettings = Path.Combine(_baseDir, "Settings");
            _tokenValidationService = tokenValidationService;
        }

        /* ------ Operaciones ------ */
        [HttpPost("get-operations")]
        public async Task<IActionResult> Operations([FromBody] OperationsRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            // Crear un diccionario para almacenar los nombres de los usuarios obtenidos
            Dictionary<string, string> userNamesCache = new Dictionary<string, string>();

            // Intenta obtener data desde Database2
            using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@UserEmail", request.Email);
                    parameters.Add("@CompanyCode", request.Company);

                    var data = await connectionDb2.QueryAsync<dynamic>("sp_ListUserOperations_new", parameters, commandType: CommandType.StoredProcedure);

                    // Conexión a Database1 para obtener los nombres de los usuarios
                    using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                    {
                        foreach (var item in data)
                        {
                            // Si el nombre de usuario ya está en el caché, no hacemos la consulta nuevamente
                            if (!userNamesCache.TryGetValue(item.UserEmail, out string userName))
                            {
                                userName = await connectionDb1.QueryFirstOrDefaultAsync<string>(
                                    "SELECT name FROM dbo.[user] WHERE email = @Email",
                                    new { Email = item.UserEmail });

                                if (userName != null)
                                {
                                    userNamesCache[item.UserEmail] = userName;
                                }
                            }
                        }
                    }

                    // Crear una lista basada en el resultado del procedimiento almacenado
                    List<TicketResponse> listaApilada = data.Select(ele => new TicketResponse
                    {
                        ID_Ticket = ele.IdTicket.ToString(),
                        NombreUsuario = userNamesCache[ele.UserEmail],  // Asignar el nombre del usuario
                        Servicio = ele.Servicio,
                        FechaHoraInicio = $"{ele.FechaHoraInicio.Day:D2}-{ele.FechaHoraInicio.Month:D2}-{ele.FechaHoraInicio.Year % 100:D2} | {ele.FechaHoraInicio.Hour:D2}:{ele.FechaHoraInicio.Minute:D2}:{ele.FechaHoraInicio.Second:D2}",
                        FechaHoraFin = ele.FechaHoraFin,
                        Estado = ele.Estado,
                        Color = ele.EstadoColor,
                        Notas = ele.Notas
                    }).ToList();

                    // Convertir la fecha y hora de inicio a DateTime y ordenar la lista en orden descendente
                    listaApilada = listaApilada.OrderByDescending(t => DateTime.ParseExact(t.FechaHoraInicio, "dd-MM-yy | HH:mm:ss", CultureInfo.InvariantCulture)).ToList();

                    return Ok(listaApilada);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener datos dinámicos.");
                }
            }
        }

        [HttpPost("search-operations")]
        public async Task<IActionResult> SearchOperations([FromBody] SearchOperationsRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            // Crear un diccionario para almacenar los nombres de los usuarios obtenidos
            Dictionary<string, string> userNamesCache = new Dictionary<string, string>();

            using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@UserEmail", request.Email);
                    parameters.Add("@CompanyCode", request.Company);
                    parameters.Add("@TicketId", request.TicketId);
                    parameters.Add("@Notes", request.Notes);
                    parameters.Add("@Service", request.Service);
                    parameters.Add("@Status", request.Status);

                    // Construir el comando EXEC para depuración
                    var debugExecCommand = new StringBuilder("EXEC sp_SearchUserOperations ");
                    debugExecCommand.AppendLine($"@UserEmail = '{request.Email}',");
                    debugExecCommand.AppendLine($"@CompanyCode = '{request.Company}',");
                    debugExecCommand.AppendLine($"@TicketId = '{(string.IsNullOrEmpty(request.TicketId) ? "NULL" : request.TicketId)}',");
                    debugExecCommand.AppendLine($"@Notes = '{(string.IsNullOrEmpty(request.Notes) ? "NULL" : request.Notes)}',");
                    debugExecCommand.AppendLine($"@Service = '{(string.IsNullOrEmpty(request.Service) ? "NULL" : request.Service)}',");
                    debugExecCommand.AppendLine($"@Status = '{(string.IsNullOrEmpty(request.Status) ? "NULL" : request.Status)}'");

                    // Escribir en la consola el comando generado
                    //Console.WriteLine("Debug EXEC Command:");
                    //Console.WriteLine(debugExecCommand.ToString());

                    var data = await connectionDb2.QueryAsync<dynamic>(
                        "sp_SearchUserOperations", 
                        parameters, 
                        commandType: CommandType.StoredProcedure
                    );

                    // Conexión a Database1 para obtener los nombres de los usuarios
                    using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                    {
                        foreach (var item in data)
                        {
                            if (!userNamesCache.TryGetValue(item.UserEmail, out string userName))
                            {
                                userName = await connectionDb1.QueryFirstOrDefaultAsync<string>(
                                    "SELECT name FROM dbo.[user] WHERE email = @Email",
                                    new { Email = item.UserEmail });

                                if (userName != null)
                                {
                                    userNamesCache[item.UserEmail] = userName;
                                }
                            }
                        }
                    }

                    // Crear una lista basada en el resultado del procedimiento almacenado
                    List<TicketResponse> listaApilada = data.Select(ele => new TicketResponse
                    {
                        ID_Ticket = ele.IdTicket.ToString(),
                        NombreUsuario = userNamesCache[ele.UserEmail],  // Asignar el nombre del usuario
                        Servicio = ele.Servicio,
                        FechaHoraInicio = $"{ele.FechaHoraInicio:dd-MM-yy | HH:mm:ss}",
                        FechaHoraFin = ele.FechaHoraFin,
                        Estado = ele.Estado,
                        Color = ele.EstadoColor,
                        Notas = ele.Notas
                    }).ToList();

                    listaApilada = listaApilada.OrderByDescending(t => DateTime.ParseExact(t.FechaHoraInicio, "dd-MM-yy | HH:mm:ss", CultureInfo.InvariantCulture)).ToList();

                    return Ok(listaApilada);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener datos dinámicos.");
                }
            }
        }

        /* ------ Descarga de resultados ------ */
        [HttpPost("download-results")]
        public async Task<IActionResult> DownloadResults([FromBody] dynamic request)
        {
            string processId = request.processId;
            string userEmail = request.userEmail;

            //Console.WriteLine("sí entró => {0}", processId);

            if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(userEmail))
                return BadRequest("El ID del proceso y el email del usuario son requeridos.");

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");

            if (Regex.IsMatch(processId, @"^DG-\d{4}$"))
            {
                string apiUrl = "http://10.20.103.132:7066/api/Tickets/get-download-path";
                //string apiUrl = "http://localhost:5269/api/Tickets/get-download-path";
                string payload = $"{apiUrl}?email={userEmail}&processId={processId}";

                using (HttpClient httpClient = new HttpClient())
                {
                    try
                    {
                        HttpResponseMessage apiRes = await httpClient.GetAsync(payload);
                        if (apiRes.IsSuccessStatusCode)
                        {
                            string fullPath = await apiRes.Content.ReadAsStringAsync();
                            if (!System.IO.File.Exists(fullPath))
                            {
                                //return NotFound("El archivo de resultados no está disponible o no existe.");
                                return Ok(new { Success = false, Message = "El archivo de resultados no está disponible o no existe." });
                            }


                            byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                            string fileBase64 = Convert.ToBase64String(fileBytes);

                            return Ok(new { FileBase64 = fileBase64, FileName = Path.GetFileName(fullPath) });
                        }
                        else
                        {
                            return Ok(new { Success = false, Message = "Error al descargar resultados." });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en la solicitud: {ex.Message}");
                        return Ok(new { Success = false, Message = "Error al descargar resultados." });
                    }
                }
            }
            else
            {
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    try
                    {
                        var parameters = new DynamicParameters();
                        parameters.Add("@ProcessID", processId);
                        parameters.Add("@UserEmail", userEmail);

                        var data = await connection.QueryFirstOrDefaultAsync<dynamic>("sp_GetProcessByIdAndUserEmail", parameters, commandType: CommandType.StoredProcedure);

                        if (data == null) return NotFound("No se encontraron resultados para el ID y correo electrónico proporcionados.");

                        string partialResultPath = data.result_path;
                        //Console.WriteLine("partialResultPath => {0}", partialResultPath);

                        if (string.IsNullOrEmpty(partialResultPath))
                            //return NotFound("El archivo de resultados no está disponible o no existe.");
                            return Ok(new { Success = false, Message = "El archivo de resultados no está disponible o no existe." });

                        //Console.WriteLine("_baseDir => {0}", _baseDir);

                        partialResultPath = partialResultPath.TrimStart('\\');
                        string fullPath = Path.Combine(_baseDir, partialResultPath);
                        //Console.WriteLine("fullPath => {0}", fullPath);

                        if (!System.IO.File.Exists(fullPath))
                            //return NotFound("El archivo de resultados no está disponible o no existe.");
                            return Ok(new { Success = false, Message = "El archivo de resultados no está disponible o no existe." });

                        byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                        string fileBase64 = Convert.ToBase64String(fileBytes);
                        //Console.WriteLine("FileBase64 enviado al cliente: " + fileBase64.Substring(0, 100) + "..."); // Solo imprimimos los primeros 100 caracteres para no saturar la consola.

                        return Ok(new { FileBase64 = fileBase64, FileName = Path.GetFileName(partialResultPath) });
                    }
                    catch (SqlException e)
                    {
                        Console.WriteLine(e.ToString());
                        return StatusCode(500, "Error interno del servidor al descargar los resultados.");
                    }
                }

            }
        }

        /* ------ Descarga de archivos de entrada ------ */
        [HttpPost("download-files-input")]
        public async Task<IActionResult> DownloadFilesInput([FromBody] dynamic request)
        {
            string processId = request.processId;
            string detailId = request.detailId;
            string userEmail = request.userEmail;

            //Console.WriteLine("sí entró => {0}", processId);

            if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(userEmail))
                return BadRequest("El ID del proceso y el email del usuario son requeridos.");

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");
            string tempCompressionDir = "";

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@ProcessID", processId);
                    parameters.Add("@DetailID", detailId);

                    var data = await connection.QueryFirstOrDefaultAsync<dynamic>("sp_GetDetailFileInput", parameters, commandType: CommandType.StoredProcedure);

                    if (data == null) return NotFound("No se encontraron resultados para el ID proporcionado.");

                    string partialResultPath = data.data_value;
                    //Console.WriteLine("partialResultPath => {0}", partialResultPath);

                    if (string.IsNullOrEmpty(partialResultPath))
                        //return NotFound("El archivo de resultados no está disponible o no existe.");
                        return Ok(new { Success = false, Message = "El archivo de resultados no está disponible o no existe." });

                    //Console.WriteLine("_baseDir => {0}", _baseDir);

                    partialResultPath = partialResultPath.TrimStart('\\');
                    string sourcePath = Path.Combine(_baseDir, partialResultPath);
                    if (!Directory.Exists(sourcePath))
                        return Ok(new { Success = false, Message = "El directorio de entrada no está disponible o no existe." });

                    // Creando el directorio temporal 'compresion' si no existe
                    tempCompressionDir = @"C:\compresion";
                    if (!Directory.Exists(tempCompressionDir))
                        Directory.CreateDirectory(tempCompressionDir);

                    // Copiando el directorio de origen al directorio de compresión
                    string destPath = Path.Combine(tempCompressionDir, Path.GetFileName(partialResultPath));
                    FileProcessingUtility.DirectoryCopy(sourcePath, destPath, true);

                    // Comprimir el directorio en un archivo .zip
                    string zipPath = Path.Combine(tempCompressionDir, processId + "_" + Path.GetFileName(partialResultPath) + ".zip");
                    ZipFile.CreateFromDirectory(destPath, zipPath);

                    // Leer el archivo .zip y convertir a Base64
                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
                    string fileBase64 = Convert.ToBase64String(fileBytes);
                    //Console.WriteLine("FileBase64 enviado al cliente: " + fileBase64.Substring(0, 100) + "...");

                    // Limpiar: eliminar la carpeta temporal 'compresion' y su contenido
                    Directory.Delete(tempCompressionDir, true);

                    return Ok(new { FileBase64 = fileBase64, FileName = Path.GetFileName(zipPath) });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al descargar los resultados.");
                }
                finally
                {
                    // Limpiar: eliminar la carpeta temporal 'compresion' si existe
                    if (Directory.Exists(tempCompressionDir))
                        Directory.Delete(tempCompressionDir, true);
                }
            }
        }

        /* ------ Descarga de archivos de salida ------ */
        [HttpPost("download-files-output")]
        public async Task<IActionResult> DownloadFilesOutput([FromBody] dynamic request)
        {
            string processId = request.processId;
            string detailId = request.detailId;
            string userEmail = request.userEmail;

            //Console.WriteLine("sí entró => {0}", processId);

            if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(userEmail))
                return BadRequest("El ID del proceso y el email del usuario son requeridos.");

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");
            string tempCompressionDir = "";

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@ProcessID", processId);
                    parameters.Add("@DetailID", detailId);

                    var data = await connection.QueryFirstOrDefaultAsync<dynamic>("sp_GetDetailFileOutput", parameters, commandType: CommandType.StoredProcedure);

                    if (data == null) return NotFound("No se encontraron resultados para el ID proporcionado.");

                    string partialResultPath = data.path;
                    string valor = data.valor;
                    Console.WriteLine("partialResultPath => {0}", partialResultPath);

                    if (string.IsNullOrEmpty(partialResultPath))
                        //return NotFound("El archivo de resultados no está disponible o no existe.");
                        return Ok(new { Success = false, Message = "El archivo de resultados no está disponible o no existe." });

                    Console.WriteLine("_baseDir => {0}", _baseDir);

                    partialResultPath = partialResultPath.TrimStart('\\');
                    string sourcePath = Path.Combine(_baseDir, partialResultPath);
                    if (!Directory.Exists(sourcePath))
                        return Ok(new { Success = false, Message = "El directorio de entrada no está disponible o no existe." });

                    // Creando el directorio temporal 'compresion' si no existe
                    tempCompressionDir = @"C:\compresion";
                    if (!Directory.Exists(tempCompressionDir))
                        Directory.CreateDirectory(tempCompressionDir);

                    // Copiando el directorio de origen al directorio de compresión
                    string destPath = Path.Combine(tempCompressionDir, Path.GetFileName(partialResultPath));
                    FileProcessingUtility.DirectoryCopy(sourcePath, destPath, true);

                    // Comprimir el directorio en un archivo .zip
                    string zipPath = Path.Combine(tempCompressionDir, processId + "_" + Path.GetFileName(partialResultPath) + "_" + valor + ".zip");
                    ZipFile.CreateFromDirectory(destPath, zipPath);

                    // Leer el archivo .zip y convertir a Base64
                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
                    string fileBase64 = Convert.ToBase64String(fileBytes);
                    Console.WriteLine("FileBase64 enviado al cliente: " + fileBase64.Substring(0, 100) + "...");

                    // Limpiar: eliminar la carpeta temporal 'compresion' y su contenido
                    Directory.Delete(tempCompressionDir, true);

                    return Ok(new { FileBase64 = fileBase64, FileName = Path.GetFileName(zipPath) });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al descargar los resultados.");
                }
                finally
                {
                    // Limpiar: eliminar la carpeta temporal 'compresion' si existe
                    if (Directory.Exists(tempCompressionDir))
                        Directory.Delete(tempCompressionDir, true);
                }
            }
        }

        [HttpPost("get-process-details")]
        public async Task<IActionResult> GetProcessDetails([FromBody] dynamic request)
        {
            string processId = request.processId;
            string userEmail = request.userEmail;
            string processType = request.processType;

            if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(userEmail))
                return BadRequest("El ID del proceso y el email del usuario son requeridos.");

            // (Opcional) Validación de token si es necesario
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");
            if (processType != "SIGET")
            {
                using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
                {
                    try
                    {
                        var parameters = new DynamicParameters();
                        parameters.Add("@ProcessID", processId);
                        parameters.Add("@UserEmail", userEmail);

                        var resultSets = await connectionDb2.QueryMultipleAsync("sp_GetProcessAndDetails", parameters, commandType: CommandType.StoredProcedure);

                        var processMainData = resultSets.Read<dynamic>().FirstOrDefault();
                        var processDetailData = resultSets.Read<dynamic>().ToList();
                        var processDetailSystemData = resultSets.Read<dynamic>().ToList();

                        if (processMainData == null)
                            return NotFound("No se encontraron detalles para el ID y correo electrónico proporcionados.");

                        // Conexión a Database1 para obtener el nombre y tax_id (RUC) de la compañía
                        using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                        {
                            var companyData = await connectionDb1.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT name, tax_id FROM dbo.[company] WHERE code = @CompanyCode",
                                new { CompanyCode = processMainData.company });

                            if (companyData != null)
                            {
                                // Si el código de la compañía es "EY001", solo mostramos el nombre
                                if (processMainData.company == "EY001")
                                {
                                    processMainData.company = companyData.name;
                                }
                                else
                                {
                                    processMainData.company = $"{companyData.name} - {companyData.tax_id}";
                                }
                            }
                        }

                        // Conexión a Database1 para obtener el nombre del usuario
                        string userName;
                        using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                        {
                            userName = await connectionDb1.QueryFirstOrDefaultAsync<string>(
                                "SELECT name FROM dbo.[user] WHERE email = @Email",
                                new { Email = processMainData.user_email });
                        }

                        var response = new
                        {
                            mainData = processMainData,
                            details = processDetailData,
                            systemDetails = processDetailSystemData,
                            createdBy = userName  // Añadir el nombre de la persona que creó el ticket
                        };

                        return Ok(response);
                    }
                    catch (SqlException e)
                    {
                        Console.WriteLine(e.ToString());
                        return StatusCode(500, "Error interno del servidor al obtener los detalles del proceso.");
                    }
                }
            }
            else
            {
                string apiUrl = "http://10.20.103.132:7066/api/Tickets/get-process-details";
                //string apiUrl = "http://localhost:5269/api/Tickets/get-process-details";
                string payload = $"{apiUrl}?email={userEmail}&processId={processId}";

                using (HttpClient httpClient = new HttpClient())
                {
                    try
                    {
                        HttpResponseMessage apiRes = await httpClient.GetAsync(payload);
                        if (apiRes.IsSuccessStatusCode)
                        {
                            string jsonResponse = await apiRes.Content.ReadAsStringAsync();
                            Dictionary<string, dynamic> response = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(jsonResponse);

                            return Ok(response);
                        }
                        else
                        {
                            Console.WriteLine($"Error en la solicitud: {apiRes.StatusCode} - {apiRes.ReasonPhrase}");
                            return Ok(new Dictionary<string, dynamic>());
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error en la solicitud: {ex.Message}");
                        return Ok(new Dictionary<string, dynamic>());
                    }
                }
            }
        }

        // ******* SIGET ********
        private async Task<List<TicketsDS>> ObtenerTicketsDesdeApiAsync(string email)
        {
            string apiUrl = "http://10.20.103.132:7066/api/Tickets/getTicketsDS";
            string payload = $"{apiUrl}?user={email}";
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(payload);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();
                        List<TicketsDS> tickets_api = JsonConvert.DeserializeObject<List<TicketsDS>>(jsonResponse);
                        Console.WriteLine(tickets_api);
                        return tickets_api;
                    }
                    else
                    {
                        Console.WriteLine($"Error en la solicitud: {response.StatusCode} - {response.ReasonPhrase}");
                        return new List<TicketsDS>();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en la solicitud: {ex.Message}");
                    return new List<TicketsDS>();
                }
            }
        }
    }

}