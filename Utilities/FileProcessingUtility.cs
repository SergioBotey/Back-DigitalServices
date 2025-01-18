using System;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Net.Http;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace digital_services.Utilities
{
    public static class FileProcessingUtility
    {
        public static Dictionary<string, string> CreateDirectoryStructure(string processId, string baseDir)
        {
            string currentDate = DateTime.Now.ToString("yyyy-MM-dd");
            string rootPath = Path.Combine(baseDir, currentDate);
            string processPath = Path.Combine(rootPath, processId);
            string configPath = Path.Combine(processPath, "config");
            string inputPath = Path.Combine(processPath, "input");
            string outputPath = Path.Combine(processPath, "output");
            string zipPath = Path.Combine(inputPath, "zipped");

            Directory.CreateDirectory(configPath);
            Directory.CreateDirectory(inputPath);
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(zipPath);

            return new Dictionary<string, string>
           {
               { "rootPath", rootPath },
               { "processPath", processPath },
               { "configPath", configPath },
               { "inputPath", inputPath },
               { "outputPath", outputPath },
               { "zipPath", zipPath }
           };
        }

        public static async Task<Dictionary<string, string>> SaveZipFile(Microsoft.AspNetCore.Http.IFormFile zipFile, string zipPath)
        {
            //using (var stream = new FileStream($"{zipPath}.zip", FileMode.Create))
            //var fullZipPath = Path.Combine(zipPath, zipFile.FileName);
            //Console.WriteLine("sdsfdsfsfds: " + Path.Combine(zipPath, zipFile.FileName));

            var fullZipPath = Path.Combine(zipPath, zipFile.FileName);
            using (var stream = new FileStream(fullZipPath, FileMode.Create))
            {
                await zipFile.CopyToAsync(stream);
            }

            return new Dictionary<string, string>
           {
               { "fileNameZip", fullZipPath },
           };
        }

        public static void ExtractZipFile(string zipFilePath, string outputPath, bool overwrite = false)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // Construir la ruta completa para el archivo o directorio extraído
                    string destinationPath = Path.Combine(outputPath, entry.FullName);

                    // Verifica si la entrada es un directorio
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // Asegura que el directorio de destino exista
                        string directoryPath = Path.GetDirectoryName(destinationPath);
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

                        // Verificar si el archivo ya existe
                        if (File.Exists(destinationPath))
                        {
                            if (overwrite)
                            {
                                // Sobrescribir el archivo existente
                                entry.ExtractToFile(destinationPath, true);
                            }
                            else
                            {
                                // Puedes decidir ignorarlo, registrar un mensaje o manejar esta situación de otra manera
                                Console.WriteLine($"El archivo {destinationPath} ya existe.");
                            }
                        }
                        else
                        {
                            // Extraer el archivo si no existe
                            entry.ExtractToFile(destinationPath);
                        }
                    }
                }
            }
        }

        public static Dictionary<string, string> ClassifyFiles(string inputPath)
        {
            var directories = new Dictionary<string, string>();
            var files = Directory.GetFiles(inputPath);

            foreach (var file in files)
            {
                string ext = Path.GetExtension(file).TrimStart('.').ToLower();
                string destinationFolder = Path.Combine(inputPath, "unzipped", ext);

                if (!Directory.Exists(destinationFolder))
                {
                    Directory.CreateDirectory(destinationFolder);
                }

                System.IO.File.Move(file, Path.Combine(destinationFolder, Path.GetFileName(file)));

                if (!directories.ContainsKey(ext + "_file"))
                {
                    directories.Add(ext + "_file", destinationFolder);
                }
            }
            return directories;
        }

        public static string ConvertBytesToReadableFormat(long bytes)
        {
            string size = "0 B";
            if (bytes >= 1073741824)
            {
                size = string.Format("{0:##.##} GB", bytes / 1073741824.0);
            }
            else if (bytes >= 1048576)
            {
                size = string.Format("{0:##.##} MB", bytes / 1048576.0);
            }
            else if (bytes >= 1024)
            {
                size = string.Format("{0:##.##} KB", bytes / 1024.0);
            }
            else if (bytes > 0 && bytes < 1024)
            {
                size = string.Format("{0} B", bytes);
            }
            return size;
        }

        public static string GetReadableFileSize(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            double bytes = fileInfo.Length;

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes /= 1024;
            }

            return string.Format("{0:0.##} {1}", bytes, sizes[order]);
        }

        public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException("La carpeta fuente no existe o no se pudo encontrar: " + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                if (File.Exists(tempPath)) // Verificar si el archivo ya existe
                {
                    // Agregar un sufijo al nombre del archivo (por ejemplo, un timestamp)
                    string newName = $"{Path.GetFileNameWithoutExtension(tempPath)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(tempPath)}";
                    tempPath = Path.Combine(destDirName, newName);
                }
                file.CopyTo(tempPath, false);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }
    }
}