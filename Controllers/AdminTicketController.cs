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
using Microsoft.AspNetCore.StaticFiles;
using Newtonsoft.Json;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using Dapper;
using digital_services.Utilities;
using digital_services.Objects.App;

namespace digital_services.Controllers
{
    [Route("api/admin-ticket/")]
    public class AdminTicketController : Controller
    {
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;
        private readonly string _baseDir;
        private string _baseDirProcessing;
        private string _baseDirSettings;

        public AdminTicketController(DatabaseConfig databaseService, IOptions<DirectoriesConfiguration> directoriesConfigOptions, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService)
        {
            _databaseService = databaseService;
            _baseDir = directoriesConfigOptions.Value.BaseDir;
            _apiSettings = apiSettingsOptions.Value;
            _baseDirProcessing = Path.Combine(_baseDir, "Processing");
            _baseDirSettings = Path.Combine(_baseDir, "Settings");
            _tokenValidationService = tokenValidationService;
        }

        /* ------ Operaciones ------ */
        [HttpPost("get-tickets")]
        public async Task<IActionResult> Tickets([FromBody] TicketRequest request)
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
                    // Ejecutar el nuevo SP que no recibe parámetros
                    var data = await connectionDb2.QueryAsync<dynamic>("sp_ListAllUserOperations", commandType: CommandType.StoredProcedure);

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
                                else
                                {
                                    // Si no se encuentra el nombre del usuario en la base de datos, asigna un valor por defecto
                                    userNamesCache[item.UserEmail] = "Nombre no disponible";
                                }
                            }
                        }
                    }

                    // Crear una lista basada en el resultado del procedimiento almacenado
                    List<TicketResponseWithSSL> listaApilada = data.Select(ele => new TicketResponseWithSSL
                    {
                        ID_Ticket = ele.IdTicket.ToString(),
                        NombreUsuario = userNamesCache.ContainsKey(ele.UserEmail) ? userNamesCache[ele.UserEmail] : "Nombre no disponible",  // Asignar el nombre del usuario o un valor por defecto
                        Servicio = ele.Servicio,
                        FechaHoraInicio = $"{ele.FechaHoraInicio.Day:D2}-{ele.FechaHoraInicio.Month:D2}-{ele.FechaHoraInicio.Year % 100:D2} | {ele.FechaHoraInicio.Hour:D2}:{ele.FechaHoraInicio.Minute:D2}:{ele.FechaHoraInicio.Second:D2}",
                        FechaHoraFin = ele.FechaHoraFin,
                        Estado = ele.Estado,
                        Color = ele.EstadoColor,
                        Notas = ele.Notas,
                        SSL = ele.SSL  // Asignar el valor de SSL obtenido del SP
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

        [HttpPost("search-tickets-admin")]
        public async Task<IActionResult> SearchTicketsAdmin([FromBody] AdminSearchRequest request)
        {
            // Log de parámetros recibidos
            Console.WriteLine($"[DEBUG] Parámetros recibidos:");
            Console.WriteLine($"Token: {request.Token}");
            Console.WriteLine($"TicketId: {request.TicketId}");
            Console.WriteLine($"Notes: {request.Notes}");
            Console.WriteLine($"Service: {request.Service}");
            Console.WriteLine($"StatusId: {request.StatusId}");
            Console.WriteLine($"StartDate: {request.StartDate}");
            Console.WriteLine($"EndDate: {request.EndDate}");

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid)
            {
                Console.WriteLine("[ERROR] Token inválido");
                return BadRequest("Token inválido");
            }

            Dictionary<string, string> userNamesCache = new Dictionary<string, string>();

            using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Preparar parámetros para el SP
                    var parameters = new DynamicParameters();
                    parameters.Add("@TicketId", request.TicketId);
                    parameters.Add("@Notes", request.Notes);
                    parameters.Add("@Service", request.Service);
                    parameters.Add("@StatusId", request.StatusId);
                    parameters.Add("@StartDate", request.StartDate);
                    parameters.Add("@EndDate", request.EndDate);

                    // Log de parámetros enviados al SP
                    Console.WriteLine($"[DEBUG] Ejecutando procedimiento almacenado 'sp_AdminSearchTickets' con los parámetros:");
                    foreach (var param in parameters.ParameterNames)
                    {
                        Console.WriteLine($"  {param}: {parameters.Get<dynamic>(param)}");
                    }

                    // Ejecutar el SP
                    var data = await connectionDb2.QueryAsync<dynamic>(
                        "sp_AdminSearchTickets",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    Console.WriteLine($"[DEBUG] SP ejecutado correctamente. Registros obtenidos: {data.Count()}");

                    using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                    {
                        foreach (var item in data)
                        {
                            if (!userNamesCache.TryGetValue(item.UserEmail, out string userName))
                            {
                                Console.WriteLine($"[DEBUG] Buscando nombre para el email: {item.UserEmail}");
                                userName = await connectionDb1.QueryFirstOrDefaultAsync<string>(
                                    "SELECT name FROM dbo.[user] WHERE email = @Email",
                                    new { Email = item.UserEmail }
                                );

                                if (userName != null)
                                {
                                    Console.WriteLine($"[DEBUG] Nombre encontrado: {userName}");
                                    userNamesCache[item.UserEmail] = userName;
                                }
                                else
                                {
                                    Console.WriteLine($"[WARNING] No se encontró nombre para el email: {item.UserEmail}");
                                    userNamesCache[item.UserEmail] = "Nombre no disponible";
                                }
                            }
                        }
                    }

                    // Mapear los resultados a la respuesta
                    List<TicketResponse> tickets = data.Select(ele => new TicketResponse
                    {
                        ID_Ticket = ele.IdTicket.ToString(),
                        NombreUsuario = userNamesCache.ContainsKey(ele.UserEmail) ? userNamesCache[ele.UserEmail] : "Nombre no disponible",
                        Servicio = ele.Servicio,
                        FechaHoraInicio = $"{ele.FechaHoraInicio:dd-MM-yy | HH:mm:ss}",
                        FechaHoraFin = ele.FechaHoraFin,
                        Estado = ele.Estado,
                        Color = ele.EstadoColor,
                        Notas = ele.Notas
                    }).OrderByDescending(t => DateTime.ParseExact(t.FechaHoraInicio, "dd-MM-yy | HH:mm:ss", CultureInfo.InvariantCulture)).ToList();

                    Console.WriteLine($"[DEBUG] Datos procesados correctamente. Total de tickets: {tickets.Count}");

                    return Ok(tickets);
                }
                catch (SqlException e)
                {
                    Console.WriteLine($"[ERROR] Error ejecutando el SP 'sp_AdminSearchTickets': {e.Message}");
                    return StatusCode(500, "Error interno del servidor al obtener datos.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Excepción general: {ex.Message}");
                    return StatusCode(500, "Error inesperado en el servidor.");
                }
            }
        }

        [HttpPost("report-tickets-admin")]
        public async Task<IActionResult> ReportTicketsAdmin([FromBody] ReportRequest request)
        {
            Console.WriteLine($"[DEBUG] Parámetros recibidos:");
            Console.WriteLine($"Token: {request.Token}");
            Console.WriteLine($"Email: {request.Email}");
            Console.WriteLine($"StartDate: {request.StartDate}");
            Console.WriteLine($"EndDate: {request.EndDate}");

            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid)
            {
                Console.WriteLine("[ERROR] Token inválido");
                return BadRequest("Token inválido");
            }

            try
            {
                var tempPath = Path.GetTempPath();
                var ticketsExcelFile = Path.Combine(tempPath, "Tickets.xlsx");
                var processQueueExcelFile = Path.Combine(tempPath, "ProcessQueue.xlsx");
                var zipFile = Path.Combine(tempPath, "Report.zip");

                // Eliminar archivos existentes
                if (System.IO.File.Exists(ticketsExcelFile)) System.IO.File.Delete(ticketsExcelFile);
                if (System.IO.File.Exists(processQueueExcelFile)) System.IO.File.Delete(processQueueExcelFile);
                if (System.IO.File.Exists(zipFile)) System.IO.File.Delete(zipFile);

                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // Generar Excel a partir del SP `sp_ObtenerTicketsPorFechas`
                    var ticketsResults = await connection.QueryMultipleAsync(
                        "sp_ObtenerTicketsPorFechas",
                        new { FechaInicio = request.StartDate, FechaFin = request.EndDate },
                        commandType: CommandType.StoredProcedure
                    );

                    var ticketsData = (await ticketsResults.ReadAsync<dynamic>()).ToList();
                    var detailsData = (await ticketsResults.ReadAsync<dynamic>()).ToList();
                    var systemDetailsData = (await ticketsResults.ReadAsync<dynamic>()).ToList();

                    using (var package = new ExcelPackage())
                    {
                        var worksheet1 = package.Workbook.Worksheets.Add("Tickets");
                        worksheet1.Cells["A1"].LoadFromDictionaries(ticketsData.Select(t => (IDictionary<string, object>)t).ToList(), true);

                        var worksheet2 = package.Workbook.Worksheets.Add("Detalles");
                        worksheet2.Cells["A1"].LoadFromDictionaries(detailsData.Select(d => (IDictionary<string, object>)d).ToList(), true);

                        var worksheet3 = package.Workbook.Worksheets.Add("Detalles del Sistema");
                        worksheet3.Cells["A1"].LoadFromDictionaries(systemDetailsData.Select(s => (IDictionary<string, object>)s).ToList(), true);

                        package.SaveAs(new FileInfo(ticketsExcelFile));
                    }

                    // Generar Excel a partir del SP `sp_ObtenerProcessQueueConDetalles`
                    var processQueueData = (await connection.QueryAsync<dynamic>(
                        "sp_ObtenerProcessQueueConDetalles",
                        new { FechaInicio = request.StartDate, FechaFin = request.EndDate },
                        commandType: CommandType.StoredProcedure
                    )).ToList();

                    using (var package = new ExcelPackage())
                    {
                        var worksheet = package.Workbook.Worksheets.Add("ProcessQueue");
                        if (processQueueData.Any())
                        {
                            worksheet.Cells["A1"].LoadFromDictionaries(processQueueData.Select(d => (IDictionary<string, object>)d).ToList(), true);
                        }
                        else
                        {
                            worksheet.Cells["A1"].Value = "No hay datos disponibles.";
                        }
                        package.SaveAs(new FileInfo(processQueueExcelFile));
                    }
                }

                // Crear archivo ZIP con los dos Excel generados
                using (var zip = ZipFile.Open(zipFile, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(ticketsExcelFile, "Tickets.xlsx");
                    zip.CreateEntryFromFile(processQueueExcelFile, "ProcessQueue.xlsx");
                }

                // Preparar archivo ZIP para descarga
                var memory = new MemoryStream();
                using (var stream = new FileStream(zipFile, FileMode.Open))
                {
                    await stream.CopyToAsync(memory);
                }
                memory.Position = 0;
                return File(memory, "application/zip", "Report.zip");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] {ex.Message}");
                return StatusCode(500, "Error interno del servidor.");
            }
        }

        [HttpPost("get-tickets-details")]
        public async Task<IActionResult> GetProcessDetails([FromBody] dynamic request)
        {
            string processId = request.processId;
            string userEmail = request.userEmail;

            if (string.IsNullOrEmpty(processId) || string.IsNullOrEmpty(userEmail))
                return BadRequest("El ID del proceso y el email del usuario son requeridos.");

            // (Opcional) Validación de token si es necesario
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");

            using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@ProcessID", processId);
                    //parameters.Add("@UserEmail", userEmail);

                    // Ejecutar el procedimiento almacenado
                    var resultSets = await connectionDb2.QueryMultipleAsync("sp_GetProcessAndQueueAndContentDetails", parameters, commandType: CommandType.StoredProcedure);

                    // Leer los tres SELECTs del procedimiento almacenado
                    var processMainData = resultSets.Read<dynamic>().FirstOrDefault();
                    var processQueueData = resultSets.Read<dynamic>().ToList();
                    var processTypeData = resultSets.Read<dynamic>().FirstOrDefault();

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
                        mainData = processMainData,    // Datos del primer SELECT
                        queueDetails = processQueueData, // Datos del segundo SELECT
                        processType = processTypeData, // Datos del tercer SELECT
                        createdBy = userName           // Añadir el nombre de la persona que creó el ticket
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

    }
}