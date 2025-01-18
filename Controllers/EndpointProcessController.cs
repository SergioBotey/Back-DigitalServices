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
    [Route("api/endpoint-process/")]
    public class EndpointProcessController : Controller
    {
        private readonly string _baseDir;
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;

        public EndpointProcessController(IOptions<DirectoriesConfiguration> directoriesConfigOptions, DatabaseConfig databaseService, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService)
        {
            _baseDir = directoriesConfigOptions.Value.BaseDir;
            _databaseService = databaseService;
            _apiSettings = apiSettingsOptions.Value;
            _tokenValidationService = tokenValidationService;
        }

        /* ------ Endpoints - Actualización de datos de procesos ------ */
        [HttpPost("save-process-detail")]
        public async Task<IActionResult> SaveProcessDetail([FromBody] SaveProcessDetailRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@variable_id", request.VariableId);

                    await connection.ExecuteAsync("sp_SaveProcessDetailSystem", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Detalle insertado exitosamente." });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al insertar detalle.");
                }
            }
        }

        [HttpPost("update-process")]
        public async Task<IActionResult> UpdateProcess([FromBody] UpdateProcessRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@status_id", request.StatusId);
                    parameters.Add("@error_message", null);

                    await connection.ExecuteAsync("sp_UpdateProcessStatus", parameters, commandType: CommandType.StoredProcedure);
                    return Ok(new { Message = "Proceso actualizado exitosamente." });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar el proceso.");
                }
            }
        }

        [HttpPost("update-process-message")]
        public async Task<IActionResult> UpdateProcess([FromBody] UpdateProcessMessageRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@status_id", request.StatusId);
                    parameters.Add("@error_message", request.Message);

                    await connection.ExecuteAsync("sp_UpdateProcessStatus", parameters, commandType: CommandType.StoredProcedure);
                    return Ok(new { Message = "Proceso actualizado exitosamente." });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar el proceso.");
                }
            }
        }

        [HttpPost("update-process-status")]
        public async Task<IActionResult> UpdateProcessStatus([FromBody] UpdateProcessStatusRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@variable_id", request.VariableId);
                    parameters.Add("@status_id", request.StatusId);

                    await connection.ExecuteAsync("sp_UpdateStatusProcessDetailSystem", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Estado actualizado exitosamente." });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar estado.");
                }
            }
        }

        [HttpPost("update-process-status-results")]
        public async Task<IActionResult> UpdateProcessStatusResults([FromBody] UpdateProcessStatusRequestResults request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid) return BadRequest("Token inválido");

            // Calcula el tamaño del directorio
            var dirInfo = new DirectoryInfo(request.Path);
            var sizeInBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
            var sizeInMB = (sizeInBytes / 1024f) / 1024f;  // Convertir a MB

            // Calcula la cantidad de archivos y transacciones
            //var qtyFiles = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Count();
            var qtyFiles = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly).Count();
            var qtyTransactions = qtyFiles;  // Asumiendo un archivo por transacción. Modificar según sea necesario.
            string relativePath = request.Path.Replace(_baseDir, string.Empty);

            // Obtener el tamaño en formato legible.
            string sizeReadable = FileProcessingUtility.ConvertBytesToReadableFormat(sizeInBytes);

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@variable_id", request.VariableId);
                    parameters.Add("@path", relativePath);
                    parameters.Add("@size", sizeReadable);
                    parameters.Add("@qty_files", qtyFiles);
                    parameters.Add("@qty_transactions", qtyTransactions);
                    parameters.Add("@execution_time_start", request.ExecutionTimeStart);
                    parameters.Add("@execution_time_end", request.ExecutionTimeEnd);

                    await connection.ExecuteAsync("sp_UpdateStatusProcessDetailSystemResults", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Resultados actualizados exitosamente." });
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar estado.");
                }
            }
        }

        [HttpPost("update-process-queue")]
        public async Task<IActionResult> UpdateProcessQueue([FromBody] UpdateProcessQueue request)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Quitar el último segmento de la ruta de respuesta
                    var pathSegments = request.ResponsePath.TrimEnd('\\').Split('\\');
                    var basePath = string.Join("\\", pathSegments.Take(pathSegments.Length - 1));

                    // Preparar el primer script SQL para actualizar el registro principal
                    string sql = @"UPDATE process_queue SET queue_technology_response = @QueueTechnologyResponse,
                                queue_response_path = @QueueResponsePath,
                                queue_is_ready_to_next = 1,
                                queue_status = CASE WHEN @IsSuccess = 1 THEN 16 ELSE 18 END
                                WHERE process_id = @ProcessId AND queue_reference_base_path = @BasePath";

                    var parameters = new
                    {
                        ProcessId = request.ProcessId,
                        QueueTechnologyResponse = request.IsSuccess ? "Ejecución con éxito" : "Hubo un error a nivel de Modeler",
                        QueueResponsePath = request.ResponsePath,
                        IsSuccess = request.IsSuccess ? 1 : 0,
                        BasePath = basePath // Usando la ruta base modificada como parámetro
                    };

                    // Ejecutar el primer UPDATE
                    await connection.ExecuteAsync(sql, parameters);

                    // Preparar el segundo script SQL para actualizar las filas adicionales que cumplan las condiciones
                    string sqlAdditionalUpdate = @"UPDATE process_queue
                                            SET queue_status = 16,
                                                queue_is_ready_to_next = 1
                                            WHERE process_id = @ProcessId
                                              AND queue_technology_response = 'Ejecución con éxito'
                                              AND queue_reference_base_path <> @BasePath";

                    // Ejecutar el segundo UPDATE
                    await connection.ExecuteAsync(sqlAdditionalUpdate, new { ProcessId = request.ProcessId, BasePath = basePath });

                    return Ok(new { Message = "Información de la cola actualizada exitosamente." });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar la información de la cola.");
                }
            }
        }

        [HttpPost("update-processing-technology")]
        public async Task<IActionResult> UpdateProcessingTechnology([FromBody] UpdateProcessingTechnologyRequest request)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Preparar el script SQL para actualizar la tabla process_queue
                    string sql = @"UPDATE process_queue SET queue_is_processing_technology = @IsProcessingTechnology
                                WHERE process_id = @ProcessId AND queue_reference_base_path = @ReferencePath";

                    var parameters = new
                    {
                        ProcessId = request.ProcessId,
                        IsProcessingTechnology = request.IsProcessingTechnology ? 1 : 0,
                        ReferencePath = request.ReferencePath
                    };

                    // Ejecutar el script SQL con los parámetros
                    await connection.ExecuteAsync(sql, parameters);

                    return Ok(new { Message = "Información de la cola actualizada correctamente." });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar la información de la cola.");
                }
            }
        }

        [HttpPost("update-process-data-path")]
        public async Task<IActionResult> UpdateProcessDataPath([FromBody] UpdateProcessQueueDataToProcessRequest request)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Preparar el script SQL para actualizar la tabla process_queue
                    string sql = @"UPDATE process_queue
                                    SET queue_is_send_to_process = 1,
                                        queue_path_data_to_process = @DataToProcessPath
                                    WHERE process_id = @ProcessId
                                    AND queue_reference_base_path = @QueueReferenceBasePath";

                    var parameters = new
                    {
                        ProcessId = request.ProcessId,
                        QueueReferenceBasePath = request.QueueReferenceBasePath,
                        DataToProcessPath = request.DataToProcessPath
                    };

                    // Ejecutar el script SQL con los parámetros
                    await connection.ExecuteAsync(sql, parameters);

                    return Ok(new { Message = "Ruta de datos y estado actualizados exitosamente." });
                }
                catch (Exception e)
                {
                    // Capturar mensaje detallado de la excepción
                    string errorMessage = $"Error al actualizar process_queue: {e.Message}. Stack Trace: {e.StackTrace}";

                    if (e.InnerException != null)
                    {
                        errorMessage += $" Inner Exception: {e.InnerException.Message}. Inner Stack Trace: {e.InnerException.StackTrace}";
                    }

                    // Registrar el error en los logs
                    Console.WriteLine(errorMessage);

                    // Devolver un mensaje de error con detalles al cliente
                    return StatusCode(500, new { Message = "Error interno del servidor al actualizar la información de la cola.", Details = errorMessage });
                }
            }
        }

        //###############################################################################################

        [HttpPost("update-process-queue-reprocess")]
        public async Task<IActionResult> UpdateProcessDataPath([FromBody] UpdateProcessQueueDataReprocess request)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Preparar el script SQL para actualizar la tabla process_queue
                    string sql = @"UPDATE process_queue
                                SET queue_technology_response = 'Solicitud enviada con éxito',
                                    queue_status = 15,
                                    queue_is_ready_to_next = 0,
                                    queue_is_processing_technology = 0,
                                    queue_message = 'Reprocesado: se detectó al menos un archivo de 0KB'
                                WHERE process_id = @ProcessId
                                AND queue_reference_base_path = @QueueReferenceBasePath";

                    var parameters = new
                    {
                        ProcessId = request.ProcessId,
                        QueueReferenceBasePath = request.ReferenceQueue
                    };

                    // Ejecutar el script SQL con los parámetros
                    await connection.ExecuteAsync(sql, parameters);

                    return Ok(new { Message = "Ruta de datos, estado y mensaje actualizados exitosamente." });
                }
                catch (Exception e)
                {
                    // Capturar mensaje detallado de la excepción
                    string errorMessage = $"Error al actualizar process_queue: {e.Message}. Stack Trace: {e.StackTrace}";

                    if (e.InnerException != null)
                    {
                        errorMessage += $" Inner Exception: {e.InnerException.Message}. Inner Stack Trace: {e.InnerException.StackTrace}";
                    }

                    // Registrar el error en los logs
                    Console.WriteLine(errorMessage);

                    // Devolver un mensaje de error con detalles al cliente
                    return StatusCode(500, new { Message = "Error interno del servidor al actualizar la información de la cola.", Details = errorMessage });
                }
            }
        }
        
        //###############################################################################################

        [HttpGet("getEstimateWaitTime")]
        public async Task<IActionResult> GetEstimatedWaitTime()
        {
            // Define la consulta SQL
            string sqlQuery = @"
                SELECT 
                    CEILING(
                        (CASE 
                            WHEN COUNT(pq.id) % 2 = 1 THEN COUNT(pq.id) - 1
                            ELSE COUNT(pq.id)
                        END) / 2.0
                    ) * 3 AS 'TiempoEstimadoEsperaHoras'
                FROM 
                    process_queue pq
                JOIN 
                    process p ON pq.process_id = p.id
                JOIN 
                    process_status ps ON pq.queue_status = ps.status_id
                WHERE 
                    ps.status_id in (12, 15);";

            // Abre la conexión a la base de datos
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                // Ejecuta la consulta y obtiene el tiempo estimado de espera
                var tiempoEstimado = await connection.QuerySingleAsync<int>(sqlQuery);

                // Retorna el resultado como una respuesta JSON
                return Ok(new { TiempoEstimadoEsperaHoras = tiempoEstimado });
            }
        }
        [HttpPost("reprocess-ticket")]
        public async Task<IActionResult> ReprocessTicket(string ticketId, int flujo)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new
                    {
                        process_id = ticketId,
                        flujo = flujo
                    };

                    // Ejecutar el script SQL con los parámetros
                    await connection.ExecuteAsync("sp_update_process_queue", parameters, commandType: CommandType.StoredProcedure);
                    return Ok(new { Message = "Proceso actualizado exitosamente." });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al actualizar la información de la cola.");
                }
            }
        }

    }

}