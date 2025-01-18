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

namespace digital_services.Services.Validation
{
    public class ValidationService
    {
        private readonly DatabaseConfig _databaseService;
        private readonly TokenValidationService _tokenValidationService;

        public ValidationService(DatabaseConfig databaseService, TokenValidationService tokenValidationService)
        {
            _databaseService = databaseService;
            _tokenValidationService = tokenValidationService;
        }
        public async Task<HttpResponseMessage> ExecuteValidation(string apiActionValidate, List<Dictionary<string, object>> dataNecessary, List<Dictionary<string, string>> formDataEntries)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromHours(1);

                var requestData = new
                {
                    DataNecessary = dataNecessary,
                    FormDataEntries = formDataEntries,
                    BaseDir = ""
                };

                var jsonContent = new StringContent(JsonConvert.SerializeObject(requestData), Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync(apiActionValidate, jsonContent);

                return response;
            }
        }

        public async Task<bool> DeleteTicketFiles(DeleteTicketFilesRequest request)
        {
            var path = request.Path;

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

                    await connection.ExecuteAsync("sp_DeleteTicketData", parameters, commandType: CommandType.StoredProcedure);

                    // Verificar si la ruta de la carpeta existe y eliminarla
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, true); // true para eliminar la carpeta y su contenido
                        Console.WriteLine($"Carpeta {path} eliminada exitosamente.");
                    }
                    else
                    {
                        Console.WriteLine($"La carpeta {path} no existe o ya fue eliminada.");
                    }

                    Console.WriteLine("Datos del ticket eliminados exitosamente.");
                    return true;
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    Console.WriteLine("Error interno del servidor al eliminar datos del ticket.");
                    return false;
                }
                catch (IOException ioEx)
                {
                    Console.WriteLine(ioEx.ToString());
                    Console.WriteLine("Error al eliminar la carpeta.");
                    return false;
                }
            }
        }

    }
}