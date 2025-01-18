using Dapper;
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using digital_services.Utilities;
using digital_services.Controllers;
using digital_services.Objects.App;
using System.Collections.Generic;

namespace digital_services.Services.Output
{
    public class OutputService
    {
        private readonly DatabaseConfig _databaseService;
        private const string OutputDirectoryKey = "output_directory";

        public OutputService(DatabaseConfig databaseService)
        {
            _databaseService = databaseService;
        }
        public OutputResult GenerateOutput(string baseDir, List<Dictionary<string, object>> dataNecessary, List<ProcessDetail> detailsList, string procesoId, string outputDirectoryJson)
        {
            try
            {
                if (dataNecessary == null || !dataNecessary.Any() || detailsList == null || !detailsList.Any())
                {
                    return new OutputResult { Status = "Error: Input data is missing" };
                }

                Console.WriteLine("Iniciando la generación de la salida...");

                var outputDirectoryValue = GetOutputDirectory(dataNecessary);
                if (string.IsNullOrWhiteSpace(outputDirectoryValue))
                {
                    return new OutputResult { Status = "Error: No output directory found" };
                }

                var variablesConfig = GetVariablesConfig(dataNecessary);
                if (variablesConfig == null || !variablesConfig.Any())
                {
                    return new OutputResult { Status = "Error: No variablesConfig found" };
                }

                // Imprimir variablesConfig para depuración
                Console.WriteLine("variablesConfig obtenido:");
                Console.WriteLine(JsonConvert.SerializeObject(variablesConfig, Formatting.Indented));

                var sistemaConfig = GetSistemaConfig(variablesConfig);
                if (sistemaConfig == null || !sistemaConfig.Any())
                {
                    return new OutputResult { Status = "Error: No sistema config found" };
                }

                Console.WriteLine("sistemaConfig obtenido:");
                Console.WriteLine(JsonConvert.SerializeObject(sistemaConfig, Formatting.Indented));

                ProcessDetails(baseDir, detailsList, sistemaConfig, outputDirectoryValue);
                ZipOutputFiles(outputDirectoryValue, procesoId, outputDirectoryJson);

                return new OutputResult { Status = "Success", ZipFilePath = Path.Combine(outputDirectoryValue, "files_available", $"{procesoId}.zip") };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error durante la generación de salida: {ex.Message}\n{ex.StackTrace}");
                return new OutputResult { Status = $"Error: {ex.Message}" };
            }
        }

        private string GetOutputDirectory(List<Dictionary<string, object>> dataNecessary)
        {
            var outputDirectoryValue = dataNecessary?.FirstOrDefault(d => d.ContainsKey(OutputDirectoryKey))?[OutputDirectoryKey];

            if (outputDirectoryValue == null)
            {
                throw new Exception("No se encontró el directorio de salida.");
            }

            return outputDirectoryValue.ToString();
        }

        private Dictionary<string, List<Dictionary<string, object>>> GetVariablesConfig(List<Dictionary<string, object>> dataNecessary)
        {
            try
            {
                // Imprimir los datos de entrada para depuración
                Console.WriteLine("Datos de entrada:");
                Console.WriteLine(JsonConvert.SerializeObject(dataNecessary, Formatting.Indented));

                // Buscar la entrada 'variablesConfig'
                var configDict = dataNecessary.FirstOrDefault(item => item.ContainsKey("variablesConfig"));
                var variablesConfigJObject = configDict?["variablesConfig"] as JObject;

                // Verificar si 'variablesConfig' fue encontrado y no es nulo
                if (variablesConfigJObject == null)
                {
                    throw new Exception("No se encontró 'variablesConfig' o está vacío.");
                }

                // Deserializar 'variablesConfig' en la estructura esperada
                var variablesConfig = variablesConfigJObject.ToObject<Dictionary<string, List<Dictionary<string, object>>>>();
                if (variablesConfig == null || !variablesConfig.Any())
                {
                    throw new Exception("'variablesConfig' está presente pero no contiene datos o su estructura es incorrecta.");
                }

                // Devolver el diccionario deserializado
                return variablesConfig;
            }
            catch (InvalidCastException ex)
            {
                // Capturar y manejar errores de conversión de tipos
                throw new Exception("Error al procesar 'variablesConfig' (conversión de tipo): " + ex.Message);
            }
            catch (JsonSerializationException ex)
            {
                // Capturar y manejar errores de deserialización JSON
                throw new Exception("Error al deserializar 'variablesConfig': " + ex.Message);
            }
            catch (Exception ex)
            {
                // Capturar y manejar cualquier otro tipo de error
                throw new Exception("Error desconocido: " + ex.Message);
            }
        }

        private List<Dictionary<string, object>> GetSistemaConfig(Dictionary<string, List<Dictionary<string, object>>> variablesConfig)
        {
            var sistemaConfig = variablesConfig["Sistema"] as List<Dictionary<string, object>>;

            if (sistemaConfig == null || !sistemaConfig.Any())
            {
                throw new Exception("No se encontró la configuración del sistema.");
            }

            return sistemaConfig;
        }

        private void ProcessDetails(string baseDir, List<ProcessDetail> detailsList, List<Dictionary<string, object>> sistemaConfig, string outputDirectoryValue)
        {
            // Imprimir el contenido de detailsList para depuración
            Console.WriteLine("detailsList:");
            if (detailsList != null)
            {
                Console.WriteLine(JsonConvert.SerializeObject(detailsList, Formatting.Indented));
            }
            else
            {
                Console.WriteLine("null");
            }

            // Imprimir el contenido de sistemaConfig para depuración
            Console.WriteLine("sistemaConfig:");
            if (sistemaConfig != null)
            {
                Console.WriteLine(JsonConvert.SerializeObject(sistemaConfig, Formatting.Indented));
            }
            else
            {
                Console.WriteLine("null");
            }

            // Imprimir el valor de outputDirectoryValue para depuración
            Console.WriteLine("outputDirectoryValue: " + outputDirectoryValue ?? "null");

            // Resto del código...
            if (detailsList == null || !detailsList.Any())
            {
                Console.WriteLine("Error: La lista de detalles está vacía o es nula.");
                return;
            }

            if (sistemaConfig == null || !sistemaConfig.Any())
            {
                Console.WriteLine("Error: El sistemaConfig está vacío o es nulo.");
                return;
            }

            //System.Threading.Thread.Sleep(20000);

            if (detailsList == null || !detailsList.Any())
            {
                Console.WriteLine("Error: La lista de detalles está vacía o es nula.");
                return;
            }

            if (sistemaConfig == null || !sistemaConfig.Any())
            {
                Console.WriteLine("Error: El sistemaConfig está vacío o es nulo.");
                return;
            }

            if (string.IsNullOrWhiteSpace(outputDirectoryValue))
            {
                Console.WriteLine("Error: El valor de outputDirectoryValue está vacío o es nulo.");
                return;
            }

            string availableResultsDirectory = Path.Combine(outputDirectoryValue, "files_available");

            if (!Directory.Exists(availableResultsDirectory))
            {
                try
                {
                    Directory.CreateDirectory(availableResultsDirectory);
                    Console.WriteLine($"Directorio creado: {availableResultsDirectory}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al crear el directorio {availableResultsDirectory}. Detalles: {ex.Message}");
                    return;
                }
            }

            string sourceDirectory = string.Empty;

            foreach (var detail in detailsList)
            {
                if (string.IsNullOrWhiteSpace(detail.Path))
                {
                    Console.WriteLine("Error: El Path en el detalle está vacío o es nulo.");
                    continue;
                }

                int variableId;
                if (!int.TryParse(detail.VariableId, out variableId))
                {
                    Console.WriteLine($"Error: No se pudo convertir VariableId ({detail.VariableId}) a int.");
                    continue;
                }

                var matchedConfig = sistemaConfig.FirstOrDefault(d => Convert.ToInt32(d["id"]) == variableId);

                if (matchedConfig == null)
                {
                    Console.WriteLine($"matchedConfig es null para VariableId = {variableId}");
                    continue;
                }

                if (!matchedConfig.ContainsKey("is_excluded") || !matchedConfig.ContainsKey("name"))
                {
                    Console.WriteLine($"Error: La configuración coincidente para VariableId = {variableId} no tiene las claves requeridas.");
                    continue;
                }

                if ((bool)matchedConfig["is_excluded"])
                {
                    Console.WriteLine($"matchedConfig es excluded para VariableId = {variableId}");
                    continue;
                }

                sourceDirectory = detail.Path.TrimStart('\\');
                sourceDirectory = Path.Combine(baseDir, sourceDirectory); // Asignar el fullPath corregido a sourceDirectory
                Console.WriteLine($"Ruta combinada y asignada a sourceDirectory: {sourceDirectory}");

                // Usar tanto 'value' como 'name' para la estructura de directorio de destino.
                string matchedConfigValue = matchedConfig["value"].ToString();
                //string matchedConfigName = matchedConfig["name"].ToString();
                Console.WriteLine($"matchedConfigValue: {matchedConfigValue}");

                //string valueDirectory = Path.Combine(availableResultsDirectory, matchedConfigValue);
                string destinationDirectory = Path.Combine(availableResultsDirectory, matchedConfigValue);
                Console.WriteLine("Iniciando el proceso de copiado...");
                Console.WriteLine($"Directorio de destino: {destinationDirectory}");

                // Validar y crear la carpeta basada en 'value' si no existe.
                /*
                if (!Directory.Exists(valueDirectory))
                {
                    Directory.CreateDirectory(valueDirectory);
                }*/

                // Validar y crear la subcarpeta basada en 'name' si no existe.
                if (!Directory.Exists(destinationDirectory))
                {
                    Console.WriteLine($"El directorio de destino '{destinationDirectory}' no existe. Creándolo...");
                    Directory.CreateDirectory(destinationDirectory);
                    Console.WriteLine($"Directorio de destino '{destinationDirectory}' creado.");
                }
                else
                {
                    Console.WriteLine($"El directorio de destino '{destinationDirectory}' ya existe.");
                }

                if (Directory.Exists(sourceDirectory))
                {
                    Console.WriteLine($"El directorio de origen '{sourceDirectory}' existe. Iniciando copia de archivos...");
                    try
                    {
                        // Copiar todas las subcarpetas y sus contenidos al nuevo lugar.
                        foreach (var dirPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
                        {
                            var destDirPath = dirPath.Replace(sourceDirectory, destinationDirectory);
                            Console.WriteLine($"Creando directorio: {destDirPath}");
                            Directory.CreateDirectory(destDirPath);
                        }

                        // Copiar todos los archivos y reemplazarlos en el destino si ya existen.
                        foreach (var newPath in Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories))
                        {
                            var destFilePath = newPath.Replace(sourceDirectory, destinationDirectory);
                            Console.WriteLine($"Copiando archivo: {newPath} a {destFilePath}");
                            File.Copy(newPath, destFilePath, true);
                        }
                        Console.WriteLine("Copia de archivos completada exitosamente.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al copiar archivos desde '{sourceDirectory}' a '{destinationDirectory}'. Detalles: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"El directorio de origen '{sourceDirectory}' no existe.");
                }

                sourceDirectory = string.Empty;
            }
        }

        private void ZipOutputFiles(string outputDirectoryValue, string procesoId, string outputDirectoryJson)
        {
            string availableResultsDirectory = Path.Combine(outputDirectoryValue, "files_available");
            string zipFilePath = Path.Combine(availableResultsDirectory, $"{procesoId}.zip");

            // Verificar y eliminar el archivo ZIP si ya existe.
            if (File.Exists(zipFilePath))
            {
                try
                {
                    File.Delete(zipFilePath);
                    System.Threading.Thread.Sleep(500); // Dar tiempo para asegurarnos de que el archivo se haya eliminado.
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"No se pudo eliminar el archivo ZIP existente. Detalles: {ex.Message}");
                    return;
                }
            }

            LogInfo($"outputDirectoryJson recibido: {outputDirectoryJson}");

            // Organizar archivos según la estructura de outputDirectoryJson, si tiene valor
            if (!string.IsNullOrEmpty(outputDirectoryJson))
            {
                LogInfo($"Organizando archivos según la estructura de outputDirectoryJson: {outputDirectoryJson}");
                OrganizeFiles(outputDirectoryJson, availableResultsDirectory);
            }

            // Crear el archivo ZIP con la nueva estructura
            try
            {
                using (FileStream zipToOpen = new FileStream(zipFilePath, FileMode.Create))
                {
                    using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                    {
                        // Agregar las carpetas renombradas al ZIP
                        foreach (var dir in Directory.GetDirectories(availableResultsDirectory))
                        {
                            string directoryName = Path.GetFileName(dir);
                            foreach (var fileToZip in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                            {
                                string entryName = Path.Combine(directoryName, fileToZip.Substring(dir.Length + 1));
                                archive.CreateEntryFromFile(fileToZip, entryName);
                            }
                        }

                        // Agregar los archivos movidos al "parent" al ZIP
                        foreach (var fileToZip in Directory.GetFiles(availableResultsDirectory, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            string entryName = Path.GetFileName(fileToZip);
                            archive.CreateEntryFromFile(fileToZip, entryName);
                        }
                    }
                }
                Console.WriteLine($"Archivo zip creado en: {zipFilePath}");

                // Una vez que el archivo ZIP está creado, eliminar las carpetas
                foreach (var dirPath in Directory.GetDirectories(availableResultsDirectory))
                {
                    if (dirPath != zipFilePath) // Solo eliminamos las carpetas, no el archivo ZIP
                    {
                        Directory.Delete(dirPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al crear el archivo ZIP. Detalles: {ex.Message}");
            }
        }

        // Organizar carpetas - JSON
        private void OrganizeFiles(string outputDirectoryJson, string availableResultsDirectory)
        {
            try
            {
                LogInfo($"Iniciando la organización de archivos con el archivo JSON ubicado en: {outputDirectoryJson}");

                // Leer el contenido del archivo JSON antes de deserializarlo
                string jsonContent = System.IO.File.ReadAllText(outputDirectoryJson);

                // Deserializar el JSON para obtener la estructura de carpetas
                var folderStructure = JsonConvert.DeserializeObject<FolderStructure>(jsonContent);
                if (folderStructure == null || folderStructure.Folders == null || !folderStructure.Folders.Any())
                {
                    LogInfo("No se pudo deserializar el archivo JSON o la estructura está vacía.");
                    return;
                }

                LogInfo($"Estructura de carpetas deserializada correctamente. Total de carpetas especificadas en JSON: {folderStructure.Folders.Count}");

                // Obtener los nombres de carpetas mapeadas en el JSON
                var mappedFolders = new HashSet<string>(folderStructure.Folders.Select(f => f.Name));
                LogInfo($"Carpetas mapeadas en el JSON: {string.Join(", ", mappedFolders)}");

                // Obtener todas las carpetas existentes en el directorio
                var allDirectories = System.IO.Directory.GetDirectories(availableResultsDirectory);
                LogInfo($"Carpetas actuales en el directorio '{availableResultsDirectory}': {string.Join(", ", allDirectories.Select(System.IO.Path.GetFileName))}");

                foreach (var folder in folderStructure.Folders)
                {
                    string oldFolderPath = System.IO.Path.Combine(availableResultsDirectory, folder.Name);
                    string newFolderPath = System.IO.Path.Combine(availableResultsDirectory, folder.NewName);

                    // Verificar si la carpeta original existe
                    if (System.IO.Directory.Exists(oldFolderPath))
                    {
                        try
                        {
                            // Verificar si la carpeta de destino ya existe antes de renombrar
                            if (!System.IO.Directory.Exists(newFolderPath))
                            {
                                // Renombrar la carpeta
                                System.IO.Directory.Move(oldFolderPath, newFolderPath);
                                LogInfo($"Carpeta renombrada de '{oldFolderPath}' a '{newFolderPath}'");
                            }
                            else
                            {
                                LogInfo($"La carpeta destino '{newFolderPath}' ya existe. No se puede renombrar '{oldFolderPath}'");
                            }

                            // Mover y renombrar los archivos dentro de la carpeta renombrada
                            foreach (var fileAction in folder.Files)
                            {
                                string oldFilePath = System.IO.Path.Combine(newFolderPath, fileAction.Name);

                                // Ajuste para definir la nueva ruta
                                string newFilePath = fileAction.MoveToParent
                                    ? System.IO.Path.Combine(availableResultsDirectory, fileAction.NewName) // Mover al mismo nivel que las carpetas renombradas
                                    : System.IO.Path.Combine(newFolderPath, fileAction.NewName); // Mantener dentro de la carpeta renombrada

                                if (System.IO.File.Exists(oldFilePath))
                                {
                                    try
                                    {
                                        // Verificar si el archivo de destino ya existe
                                        if (!System.IO.File.Exists(newFilePath))
                                        {
                                            // Mover y renombrar el archivo
                                            System.IO.File.Move(oldFilePath, newFilePath);
                                            LogInfo($"Archivo movido de '{oldFilePath}' a '{newFilePath}'");
                                        }
                                        else
                                        {
                                            LogInfo($"El archivo destino '{newFilePath}' ya existe. No se puede mover '{oldFilePath}'");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogInfo($"Error al mover el archivo '{oldFilePath}'. Detalles: {ex.Message}");
                                    }
                                }
                                else
                                {
                                    LogInfo($"El archivo '{oldFilePath}' no existe. No se puede mover.");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogInfo($"Error al renombrar la carpeta '{oldFolderPath}'. Detalles: {ex.Message}");
                        }
                    }
                    else
                    {
                        LogInfo($"La carpeta '{oldFolderPath}' no existe. No se puede renombrar.");
                    }
                }

                // Eliminar carpetas que no están mapeadas en el JSON
                foreach (var directory in allDirectories)
                {
                    string folderName = System.IO.Path.GetFileName(directory);
                    if (!mappedFolders.Contains(folderName))
                    {
                        try
                        {
                            System.IO.Directory.Delete(directory, true); // Eliminar la carpeta y su contenido
                            LogInfo($"Carpeta '{folderName}' eliminada porque no está mapeada en el JSON.");
                        }
                        catch (Exception ex)
                        {
                            LogInfo($"Error al eliminar la carpeta '{folderName}'. Detalles: {ex.Message}");
                        }
                    }
                }

                LogInfo("Organización de archivos finalizada con éxito.");
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

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