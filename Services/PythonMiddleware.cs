using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class PythonMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _pythonScriptPath;
    private readonly string _logFilePath = Path.Combine(Path.GetTempPath(), "PythonScriptLog.txt");

    public PythonMiddleware(RequestDelegate next, DirectoriesConfiguration directoriesConfig)
    {
        _next = next;
        _pythonScriptPath = directoriesConfig.PythonScriptPath;
    }

    public async Task Invoke(HttpContext context)
    {
        if (!IsPythonScriptLogged())
        {
            StartPythonScript();
            LogPythonScriptStart();
            Console.WriteLine("El servidor Python se ha iniciado en Localhost:7000.");
        }
        else
        {
            Console.WriteLine("El servidor Python ya está encendido.");
        }

        await _next(context);
    }

    private bool IsPythonScriptLogged()
    {
        return File.Exists(_logFilePath);
    }

    private void LogPythonScriptStart()
    {
        using (var stream = File.Create(_logFilePath))
        {
            byte[] info = new System.Text.UTF8Encoding(true).GetBytes("Python Script Running");
            stream.Write(info, 0, info.Length);
        }
    }

    private void StartPythonScript()
    {
        var process = Process.Start("python", _pythonScriptPath);
        process.EnableRaisingEvents = true;

        process.Exited += (sender, e) =>
        {
            if (File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
            }
        };
    }
}