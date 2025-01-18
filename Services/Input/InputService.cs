using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using digital_services.Utilities;
using System.Data.SqlClient;
using Dapper;

namespace digital_services.Services.Input
{
    public class InputService
    {
        private readonly DatabaseConfig _databaseService;
        private readonly ApiSettings _apiSettings;
        public InputService(DatabaseConfig databaseService, IOptions<ApiSettings> apiSettingsOptions)
        {
            _databaseService = databaseService;
            _apiSettings = apiSettingsOptions.Value;
        }
        /*
        public async Task<string> GenerateProcessId()
        {
            //string idPrefix = "DT";
            string idPrefix = string.IsNullOrEmpty(_apiSettings.ApiPrefix) ? "DS" : "DT";
            try
            {
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    var query = "SELECT COUNT(*) + 10 FROM process";
                    int nextId = await connection.ExecuteScalarAsync<int>(query);
                    return $"{idPrefix}{nextId}";
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
        }*/

        public async Task<string> GenerateProcessId()
        {
            // Prefijo del ID, usando "DS" por defecto si ApiPrefix no está configurado
            string idPrefix = string.IsNullOrEmpty(_apiSettings.ApiPrefix) ? "DS" : "DT";

            try
            {
                using (var connection = await _databaseService.Database2.OpenConnectionAsync())
                {
                    // Obtener el siguiente valor de la secuencia
                    var query = "SELECT NEXT VALUE FOR ProcessIdSequence";
                    int nextIdNumber = await connection.ExecuteScalarAsync<int>(query);

                    // Generar el nuevo ID concatenando el prefijo con el número de la secuencia
                    string newId = $"{idPrefix}{nextIdNumber}";

                    // Opcional: Puedes insertar el nuevo proceso o hacer otras operaciones
                    // var insertQuery = "INSERT INTO process (id, service_id, user_email, status_id, date_register) VALUES (@newId, @serviceId, @userEmail, @statusId, @dateRegister)";
                    // await connection.ExecuteAsync(insertQuery, new { newId, serviceId = 1, userEmail = "test@example.com", statusId = 1, dateRegister = DateTime.Now });

                    return newId;
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
        }

        public async Task<int> ShouldInsertContentReference(string contentReference)
        {
            try
            {
                using (var connection = await _databaseService.Database1.OpenConnectionAsync())
                {
                    //var query = "SELECT COUNT(*) FROM permission WHERE code_reference = @contentReference AND parent_permission_id IS NOT NULL";
                    var query = "SELECT COUNT(*) FROM permission WHERE code_reference = @contentReference AND is_check=1";
                    int result = await connection.ExecuteScalarAsync<int>(query, new { contentReference });

                    return result > 0 ? 1 : 0;
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
        }

        public string GetValueFromEntries(List<Dictionary<string, string>> formDataEntries, string keyName)
        {
            var entry = formDataEntries.FirstOrDefault(e => e.ContainsKey(keyName));
            return entry != null ? entry[keyName] : null;
        }

        public string GetPathFromConfig(List<Dictionary<string, object>> dataNecessary, string keyName)
        {
            var configData = dataNecessary.FirstOrDefault(d => d.ContainsKey("variablesConfig"));

            if (configData != null && configData["variablesConfig"] is Dictionary<string, List<Dictionary<string, object>>> variablesConfig)
            {
                foreach (var category in variablesConfig)
                {
                    foreach (var item in category.Value)
                    {
                        if (item["name"].ToString() == keyName)
                        {
                            return item["value"].ToString();
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("No se encontró el diccionario de configuración o no tiene el formato esperado.");
            }

            return "";
        }

        public async Task<(string processId, Dictionary<string, string> paths, int shouldInsert)> PrepareFileProcessing(string contentReference, string baseDir)
        {
            //var processId = FileProcessingUtility.GenerateProcessId(contentReference);
            var processId = await GenerateProcessId();
            var paths = FileProcessingUtility.CreateDirectoryStructure(processId, baseDir);

            // Llamamos al método para verificar si el contentReference debe ser insertado
            int shouldInsert = await ShouldInsertContentReference(contentReference);

            return (processId, paths, shouldInsert);
        }


        public async Task<Dictionary<string, object>> HandleFile(IFormFile file, Dictionary<string, string> paths)
        {
            var result = new Dictionary<string, object>();
            var directoryForFile = Path.Combine(paths["inputPath"], file.Name);
            if (file != null && file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                string directoryForZip = Path.Combine(paths["zipPath"], file.Name);
                Directory.CreateDirectory(directoryForZip);
                var savedZipDetails = await FileProcessingUtility.SaveZipFile(file, directoryForZip);
                var extractionPath = Path.Combine(paths["inputPath"], file.Name);
                Directory.CreateDirectory(extractionPath);

                //FileProcessingUtility.ExtractZipFile(savedZipDetails["fileNameZip"], extractionPath);
                FileProcessingUtility.ExtractZipFile(savedZipDetails["fileNameZip"], extractionPath, true);
                // Renombrar archivos con caracteres no válidos después de la extracción
                foreach (var extractedFilePath in Directory.EnumerateFiles(extractionPath))
                {
                    var fileName = Path.GetFileName(extractedFilePath);
                    var sanitizedFileName = SanitizeFileName(fileName);
                    if (sanitizedFileName != fileName)
                    {
                        var sanitizedFilePath = Path.Combine(extractionPath, sanitizedFileName);
                        File.Move(extractedFilePath, sanitizedFilePath);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(directoryForFile);
                var filePath = Path.Combine(directoryForFile, file.FileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
            }
            result[file.Name] = directoryForFile;
            return result;
        }
        private string SanitizeFileName(string fileName)
        {
            // Convertir a una cadena que contenga solo caracteres ASCII.
            string validChars = new string(fileName.Where(c => c <= sbyte.MaxValue).ToArray());
            // Reemplazar caracteres no válidos con un subrayado u otro caracter válido.
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            foreach (char c in invalidChars)
            {
                validChars = validChars.Replace(c, '_');
            }
            return validChars;
        }

        public List<Dictionary<string, string>> ExtractFormData(IFormCollection form, List<Dictionary<string, object>> processingResults)
        {
            var formEntries = new List<Dictionary<string, string>>();
            var fixedKeys = new List<string>
            {
                "token", "content_id", "content_reference", "content_name",
                "process_type_name", "api_action", "api_action_validate", "service_id", "email_user"
            };

            foreach (var key in form.Keys)
            {
                if (fixedKeys.Contains(key))
                {
                    var fixedEntry = new Dictionary<string, string>
                    {
                        { key, form[key] }
                    };
                    formEntries.Add(fixedEntry);
                    continue; // No procesar más esta clave, vamos a la siguiente.
                }

                var entry = new Dictionary<string, string>
                {
                    { "name", key },
                    { "data_value", form[key] },
                    { "input_size", null },
                    { "input_qty_files", null },
                    { "element-id", form.ContainsKey(key + "-element-id") ? form[key + "-element-id"].ToString() : "N/A" },
                    { "element-label", form.ContainsKey(key + "-element-label") ? form[key + "-element-label"].ToString() : "N/A" }
                };

                if (!key.EndsWith("-element-id") && !key.EndsWith("-element-label") && entry["data_value"] != "undefined")
                {
                    formEntries.Add(entry);
                }
            }

            foreach (var file in form.Files)
            {
                var filePath = processingResults.FirstOrDefault(pr => pr.ContainsKey(file.Name))?[file.Name].ToString() ?? "N/A";

                if (Directory.Exists(filePath))
                {
                    var entry = new Dictionary<string, string>
                    {
                        { "name", file.Name },
                        { "data_value", filePath },
                        { "input_size", FileProcessingUtility.ConvertBytesToReadableFormat(new DirectoryInfo(filePath).EnumerateFiles().Sum(fi => fi.Length)) },
                        { "input_qty_files", new DirectoryInfo(filePath).EnumerateFiles().Count().ToString() },
                        { "element-id", form.ContainsKey(file.Name + "-element-id") ? form[file.Name + "-element-id"].ToString() : "N/A" },
                        { "element-label", form.ContainsKey(file.Name + "-element-label") ? form[file.Name + "-element-label"].ToString() : "N/A" }
                    };

                    if (entry["data_value"] != "undefined")
                    {
                        formEntries.Add(entry);
                    }
                }
            }

            return formEntries
                .OrderBy(e => e.ContainsKey("element-id") ? (int.TryParse(e["element-id"], out var id) ? id : int.MaxValue) : int.MaxValue)
                .ToList();
        }

        /*
        public async Task<Dictionary<string, object>> HandleZipFile(IFormFile zipFile, Dictionary<string, string> paths)
        {
            var result = new Dictionary<string, object>();

            // Guardar archivos zip en la carpeta "zipped"
            if (zipFile != null && zipFile.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var savedZipDetails = await FileProcessingUtility.SaveZipFile(zipFile, paths["zipPath"]);
                FileProcessingUtility.ExtractZipFile(savedZipDetails["fileNameZip"], Path.Combine(paths["inputPath"], zipFile.Name.Replace(".zip", "")));
            }
            else // Para cualquier otro archivo
            {
                var filePath = Path.Combine(paths["inputPath"], zipFile.Name);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await zipFile.CopyToAsync(stream);
                }
            }

            return result;
        }*/
    }
}