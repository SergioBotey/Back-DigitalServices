using Dapper;
using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using Microsoft.Extensions.Options;
using digital_services.Objects.App;
using digital_services.Utilities;

namespace digital_services.Controllers
{
    [Route("api/utilities")]
    public class UtilitiesController : Controller
    {
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;
        private readonly string _baseDir;

        public UtilitiesController(DatabaseConfig databaseService, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService, IOptions<DirectoriesConfiguration> directoriesConfigOptions)
        {
            _databaseService = databaseService;
            _apiSettings = apiSettingsOptions.Value;
            _tokenValidationService = tokenValidationService;
            _baseDir = directoriesConfigOptions.Value.BaseDir;
        }

        [HttpGet("get-legacy-name")]
        public async Task<IActionResult> GetRazonSocial(string ruc)
        {
            if (string.IsNullOrEmpty(ruc))
            {
                return BadRequest("El RUC es requerido.");
            }

            using (var httpClient = new HttpClient())
            {
                try
                {
                    string apiUrl = $"https://api.apis.net.pe/v1/ruc?numero={ruc}";
                    var response = await httpClient.GetAsync(apiUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        return StatusCode((int)response.StatusCode, "Error al conectar con el servicio externo.");
                    }

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var responseData = JsonConvert.DeserializeObject<dynamic>(jsonResponse);

                    if (responseData == null || string.IsNullOrEmpty(responseData.nombre.ToString()))
                    {
                        return NotFound("No se encontró una empresa con el RUC proporcionado.");
                    }

                    return Ok(responseData.nombre.ToString());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener la razón social.");
                }
            }

            /*
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@RUC", ruc);

                    //string query = "SELECT TOP 1 razon_social FROM data_companies WHERE ruc = @RUC";
                    string query = "SELECT TOP 1 razon_social FROM data_companies WHERE ruc = @RUC ORDER BY fec_creacion DESC";
                    var razonSocial = await connection.QueryFirstOrDefaultAsync<string>(query, parameters);

                    if (razonSocial == null)
                    {
                        return NotFound("No se encontró una empresa con el RUC proporcionado.");
                    }

                    return Ok(razonSocial);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener la razón social.");
                }
            }*/
        }

        /* ------ Renderización dinámica ------ */
        [HttpPost("dynamic-rendering")]
        public async Task<IActionResult> DynamicRendering([FromBody] DynamicRenderingRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            // Intenta obtener data desde Database2
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@ContentReference", request.Reference);

                    var data = await connection.QueryAsync<dynamic>("sp_GetDynamicRendering", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(data);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener datos dinámicos.");
                }
            }
        }

        [HttpPost("service-areas")]
        public async Task<IActionResult> GetServiceAreasByContentCode([FromBody] dynamic request)
        {
            string contentCode = request.contentCode;

            if (string.IsNullOrEmpty(contentCode))
                return BadRequest("El código de contenido es requerido.");

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@contentCode", contentCode);

                    var data = await connection.QueryAsync<dynamic>("sp_GetServiceAreasByContentCode", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(data);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener áreas de servicio.");
                }
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatuses()
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutar el procedimiento almacenado para obtener los estatus
                    var statuses = await connection.QueryAsync<dynamic>("sp_GetStatusesFromProcess", commandType: CommandType.StoredProcedure);

                    // Mapear el resultado si es necesario
                    var result = statuses.Select(status => new
                    {
                        value = status.status_id,
                        label = status.status_name,
                        description = status.description,
                        color = status.color
                    }).ToList();

                    return Ok(result);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener los estatus.");
                }
            }
        }

        /************ Templates por input *************/
        [HttpPost("get-templates")]
        public async Task<IActionResult> GetTemplatesInput([FromBody] dynamic request)
        {
            string elementId_ = request.elementId;

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@element_id_", elementId_);

                    var data = await connection.QueryAsync<dynamic>("sp_GetTemplatesInput", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(data);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener áreas de servicio.");
                }
            }
        }

        [HttpPost("download-template")]
        public async Task<IActionResult> DownloadTemplate([FromBody] dynamic request)
        {
            string template_id = request.templateId;
            Console.WriteLine("sí entró => {0}", template_id);

            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.token.ToString());
            if (!isValid) return BadRequest("Token inválido");
            string tempCompressionDir = "";

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@TemplateID", template_id);

                    var data = await connection.QueryFirstOrDefaultAsync<dynamic>("GetTemplateDownload", parameters, commandType: CommandType.StoredProcedure);

                    // Registrar la respuesta del procedimiento almacenado
                    Console.WriteLine($"Respuesta del procedimiento almacenado GetTemplateDownload: {data}");

                    if (data == null) return NotFound("No se encontraron resultados para el ID proporcionado.");
                    string partialResultPath = data.template_value;

                    if (string.IsNullOrEmpty(partialResultPath))
                        return Ok(new { Success = false, Message = "La plantilla no está disponible o no existe." });

                    partialResultPath = partialResultPath.TrimStart('\\');
                    string sourcePath = Path.Combine(_baseDir, partialResultPath);
                    Console.WriteLine($"Respuesta sourcePath: {sourcePath}");

                    // Verificar si el archivo existe
                    if (!System.IO.File.Exists(sourcePath))
                        return Ok(new { Success = false, Message = "El archivo no está disponible o no existe." });

                    // Crear un nombre de archivo para el archivo .zip basado en la fecha y hora
                    string dateTimeFormat = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string zipFileName = dateTimeFormat + "_" + Path.GetFileName(partialResultPath) + ".zip";
                    string zipPath = Path.Combine(tempCompressionDir, zipFileName);

                    // Crear el directorio temporal 'compresion' si no existe
                    tempCompressionDir = @"C:\compresion";
                    if (!Directory.Exists(tempCompressionDir))
                        Directory.CreateDirectory(tempCompressionDir);

                    // Comprimir el archivo directamente en un archivo .zip
                    using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                    {
                        zip.CreateEntryFromFile(sourcePath, Path.GetFileName(partialResultPath));
                    }

                    // Leer el archivo .zip y convertir a Base64
                    byte[] fileBytes = await System.IO.File.ReadAllBytesAsync(zipPath);
                    string fileBase64 = Convert.ToBase64String(fileBytes);

                    // Limpiar: eliminar el archivo .zip temporal
                    System.IO.File.Delete(zipPath);

                    return Ok(new { FileBase64 = fileBase64, FileName = zipFileName });
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

        /********* Utilitarios especificos por SIRE *********/
        [HttpPost("check-invoice-portal")]
        public async Task<IActionResult> CheckInvoicePortal([FromBody] dynamic request)
        {
            string ticketId = request.TicketId;
            string engagement = request.Engagement;
            string ruc = request.RUC;
            string periodo = request.Periodo;
            string mes = request.Mes;

            //Console.WriteLine("se recibió el request");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@ticketId", ticketId);

                    var data = await connection.QueryAsync<dynamic>("sp_CheckInvoiceDetails", parameters, commandType: CommandType.StoredProcedure);

                    // Diccionario para recoger errores de validación
                    var validationErrors = new Dictionary<string, string>();

                    // Verificar si se retornaron datos del SP
                    if (!data.Any())
                    {
                        validationErrors.Add("NoData", "No se encontraron resultados para el ticket en la BD, se sugiere ingresar un ID de ticket existente.");
                    }
                    else
                    {
                        foreach (var detail in data)
                        {
                            string name = detail.name;
                            string value = detail.data_value;
                            if (name == "engagement" && value != engagement)
                            {
                                validationErrors.Add("engagement", $"Se esperaba para el engagement: {value}, pero se obtuvo {engagement}.");
                            }
                            else if (name == "ruc" && value != ruc)
                            {
                                validationErrors.Add("ruc", $"Se esperaba para el RUC: {value}, pero se obtuvo {ruc}.");
                            }
                            else if (name == "periodo" && value != periodo)
                            {
                                validationErrors.Add("periodo", $"Se esperaba para el año: {value}, pero se obtuvo {periodo}.");
                            }
                            else if (name == "mes" && value != mes)
                            {
                                validationErrors.Add("mes", $"Se esperaba para el mes: {value}, pero se obtuvo {mes}.");
                            }
                        }
                    }

                    if (validationErrors.Any())
                    {
                        // Registrando los errores en la consola
                        foreach (var error in validationErrors)
                        {
                            Console.WriteLine($"Error de validación Invoice Portal para {error.Key}: {error.Value}");
                        }
                        // Devuelve BadRequest con el diccionario de errores
                        return BadRequest(new { message = "La validación de datos ha fallado.", errors = validationErrors });
                    }

                    return Ok("Los datos coinciden.");
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al verificar los detalles de la factura.");
                }
            }
        }

        /********* Utilitarios especificos por SIGET *********/
        [HttpGet("getManagers")]
        public async Task<IActionResult> GetManagers()
        {
            using (var connection = await _databaseService.Database3.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutar el procedimiento almacenado para obtener los gerentes activos
                    var managers = await connection.QueryAsync<dynamic>("ObtenerGerentesActivos", commandType: CommandType.StoredProcedure);

                    // Mapear el resultado al formato deseado (value, label)
                    var result = managers.Select(manager => new
                    {
                        value = manager.Id,
                        label = manager.description
                    }).ToList();

                    return Ok(result);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener la lista de gerentes.");
                }
            }
        }

        [HttpGet("getCompaniesByManager/{id_manager}")]
        public async Task<IActionResult> GetCompaniesByManager(int id_manager)
        {
            using (var connection = await _databaseService.Database3.OpenConnectionAsync())
            {
                try
                {
                    // Crear los parámetros para el procedimiento almacenado
                    var parameters = new DynamicParameters();
                    parameters.Add("@GerenteID", id_manager);

                    // Ejecutar el procedimiento almacenado para obtener las empresas por gerente
                    var companies = await connection.QueryAsync<dynamic>("ObtenerEmpresasPorGerente", parameters, commandType: CommandType.StoredProcedure);

                    // Modificar el resultado para tener value y label
                    var result = companies.Select(company => new
                    {
                        value = $"{company.id}|{id_manager}", // El ID concatenado con el ID del gerente
                        label = $"{company.description}" // Nombre de la empresa seguido del RUC
                    }).ToList();

                    return Ok(result);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener las empresas por gerente.");
                }
            }
        }

        [HttpGet("getRucByCompany/{value}")]
        public async Task<IActionResult> GetRucByCompany(string value)
        {
            using (var connection = await _databaseService.Database3.OpenConnectionAsync())
            {
                try
                {
                    // Separar el value para obtener el companyId
                    var splitValues = value.Split('|');
                    if (splitValues.Length != 2)
                    {
                        return BadRequest("Formato de valor incorrecto. Debe ser 'companyId|id_manager'.");
                    }

                    int companyId;
                    if (!int.TryParse(splitValues[0], out companyId))
                    {
                        return BadRequest("El ID de la empresa no es válido.");
                    }

                    // Realizar la consulta directa para obtener el RUC basado en el companyId
                    var query = "SELECT ruc FROM dbo.op_m_empresas WHERE id = @CompanyId";
                    var ruc = await connection.QueryFirstOrDefaultAsync<string>(query, new { CompanyId = companyId });

                    if (ruc == null)
                    {
                        return NotFound("No se encontró la empresa con el ID proporcionado.");
                    }

                    return Ok(ruc); // Retornar directamente el valor del RUC
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener el RUC de la empresa.");
                }
            }
        }

        [HttpGet("getServiciosByManagerEmpresa/{id_compuesto}")]
        public async Task<IActionResult> GetServiciosByEmpresa(string id_compuesto)
        {
            using (var connection = await _databaseService.Database3.OpenConnectionAsync())
            {
                try
                {
                    // Separar el parámetro en ID de la empresa y ID del gerente
                    var ids = id_compuesto.Split('|');
                    if (ids.Length != 2)
                    {
                        return BadRequest("El formato del ID compuesto es incorrecto.");
                    }

                    int id_empresa = int.Parse(ids[0]);
                    int id_manager = int.Parse(ids[1]);

                    // Crear los parámetros para el procedimiento almacenado
                    var parameters = new DynamicParameters();
                    parameters.Add("@GerenteID", id_manager);
                    parameters.Add("@EmpresaID", id_empresa);

                    // Ejecutar el procedimiento almacenado para obtener los servicios por gerente y empresa
                    var servicios = await connection.QueryAsync<dynamic>("ObtenerServiciosPorGerenteEmpresa", parameters, commandType: CommandType.StoredProcedure);

                    // Modificar el ID de cada servicio para incluir el ID de la empresa y el ID del gerente al final del ID existente
                    var modifiedServicios = servicios.Select(servicio => new
                    {
                        description = servicio.description,
                        id = $"{servicio.id}|{id_empresa}|{id_manager}"
                    }).ToList();

                    return Ok(modifiedServicios);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener los servicios por gerente y empresa.");
                }
            }
        }

    }
}