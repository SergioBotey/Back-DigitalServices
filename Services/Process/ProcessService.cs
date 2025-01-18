using System;
using System.IO;
using System.Data;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text;
using Dapper;
using System.Data.SqlClient;
using digital_services.Objects.App;
using Newtonsoft.Json;

namespace digital_services.Services.Process
{
    public class ProcessService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly TokenValidationService _tokenValidationService;
        private readonly DatabaseConfig _databaseService;

        public ProcessService(DatabaseConfig databaseService, TokenValidationService tokenValidationService, IHttpClientFactory httpClientFactory)
        {
            _databaseService = databaseService;
            _tokenValidationService = tokenValidationService;
            _httpClientFactory = httpClientFactory;
        }
        public void ExecuteProcessing(string apiAction, List<Dictionary<string, object>> dataNecessary, List<Dictionary<string, string>> formDataEntries, string baseDir)
        {
            _ = Task.Run(async () =>
            {
                var httpClient = _httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromHours(3);

                var requestData = new
                {
                    DataNecessary = dataNecessary,
                    FormDataEntries = formDataEntries,
                    BaseDir = baseDir
                };

                // Log en archivo para depuración
                try
                {
                    // Definir la ruta del archivo de log
                    var logFilePath = Path.Combine("C:\\DS", "requestDataLog.txt");
                    // Guardar la info serializada en el archivo
                    File.AppendAllText(logFilePath, 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - requestData: {JsonConvert.SerializeObject(requestData)}{Environment.NewLine}");
                }
                catch (Exception logEx)
                {
                    // Manejo de errores para el proceso de logging
                    Console.WriteLine($"Error al escribir el log: {logEx.Message}");
                }

                var jsonContent = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");
                        
                try
                {
                    // Disparar la solicitud HTTP pero no esperar por la respuesta (fire and forget)
                    Console.WriteLine("apiAction", apiAction);
                    var response = await httpClient.PostAsync(apiAction, jsonContent);
                    if (!response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"El servidor respondió con un error: {response.StatusCode}, {responseContent}");
                        // Logear o manejar la respuesta del error como consideres necesario
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ocurrió un error durante la preparación o el envío de la solicitud: {ex.Message}");
                    // Logear o manejar la excepción como consideres necesario
                }
            });
        }

        public async Task<bool> UpdateAdvancedDetails(UpdateAdvancedDetailsRequest request)
        {
            // Validación de token
            bool isValid = await _tokenValidationService.IsValidTokenAsync(request.Token);
            if (!isValid)
            {
                Console.WriteLine("Token inválido");
                return false;
            }

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", request.ProcessId);
                    parameters.Add("@variable_id", request.VariableId);
                    parameters.Add("@status_id", request.StatusId);
                    parameters.Add("@path", request.Path);
                    parameters.Add("@size", request.Size);
                    parameters.Add("@qty_files", request.QtyFiles);
                    parameters.Add("@qty_transactions", request.QtyTransactions);
                    parameters.Add("@execution_time_start", request.ExecutionTimeStart);
                    parameters.Add("@execution_time_end", request.ExecutionTimeEnd);

                    await connection.ExecuteAsync("sp_UpdateAdvancedDetailsProcessDetailSystem", parameters, commandType: CommandType.StoredProcedure);

                    Console.WriteLine("Detalles avanzados actualizados exitosamente.");
                    return true;
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    Console.WriteLine("Error interno del servidor al actualizar detalles avanzados.");
                    return false;
                }
            }
        }

        public async Task<bool> UpdateProcessGeneralStatus(string processId, int statusId, string error_message = null)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", processId);
                    parameters.Add("@status_id", statusId);
                    parameters.Add("@error_message", error_message);

                    await connection.ExecuteAsync("sp_UpdateProcessStatus", parameters, commandType: CommandType.StoredProcedure);

                    return true;
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return false;
                }
            }
        }

        public async Task<bool> UpdateProcessResult(string processId, DateTime executionTimeEnd, string result, string resultPath, string resultSize)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("@process_id", processId);
                    parameters.Add("@execution_time_end", executionTimeEnd);
                    parameters.Add("@result", result);
                    parameters.Add("@result_path", resultPath);
                    parameters.Add("@result_size", resultSize);

                    // Agregando Console.WriteLine para cada parámetro
                    Console.WriteLine("Process ID: " + processId);
                    Console.WriteLine("Execution Time End: " + executionTimeEnd.ToString());
                    Console.WriteLine("Result: " + result);
                    Console.WriteLine("Result Path: " + resultPath);
                    Console.WriteLine("Result Size: " + resultSize);

                    await connection.ExecuteAsync("sp_UpdateProcessResultFinal", parameters, commandType: CommandType.StoredProcedure);

                    return true;
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return false;
                }
            }
        }

        public async Task<bool> CheckErrorProcess(string processId)
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Preparar el script SQL para verificar si existen detalles de proceso con status_id = 4
                    string sql = @"
                SELECT COUNT(*)
                FROM process_detail_system
                WHERE process_id = @ProcessId AND status_id = 4";

                    var parameters = new { ProcessId = processId };

                    // Ejecutar el script SQL con los parámetros
                    var count = await connection.ExecuteScalarAsync<int>(sql, parameters);

                    // Si count es mayor que 0, significa que al menos un detalle de proceso tiene status_id = 4
                    return count == 0;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    // Considera cómo quieres manejar los errores: 
                    // Retornar false podría indicar que hay un error, pero también podría confundirse con un estado de error real.
                    // Podrías necesitar una estrategia diferente dependiendo de tus necesidades.
                    return false;
                }
            }
        }


    }
}