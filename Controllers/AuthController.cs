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
using digital_services.Objects.Auth;

namespace digital_services.Controllers
{
    [Route("api/auth")]
    public class AuthController : Controller
    {
        private readonly DatabaseConfig _databaseService;

        public AuthController(DatabaseConfig databaseService)
        {
            _databaseService = databaseService;
        }

        //Para efectos de pruebas y de registrar a un nuevo usuario
        // SP: InsertUser
        [HttpGet("register-user")]
        public async Task<IActionResult> RegisterUser()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                // Datos ficticios para la inserción
                string email = "victor.palomino@pe.ey.com";
                string name = "Victor Palomino";
                var password = ComputeSha256Hash("victor123"); // Haces el hash en C#
                int area_id = 1;
                bool enabled = true;

                // Llamada al procedimiento almacenado
                var parameters = new DynamicParameters();
                parameters.Add("@Email", email);
                parameters.Add("@Name", name);
                parameters.Add("@Password", password);
                parameters.Add("@AreaId", area_id);
                parameters.Add("@Enabled", enabled);

                await connection.ExecuteAsync("sp_InsertUser", parameters, commandType: CommandType.StoredProcedure);

                return Ok(new { Message = "Usuario registrado con éxito." });
            }
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Primero, verificar si el usuario está inactivo
                    var userStatus = await connection.QueryFirstOrDefaultAsync<int?>(
                        @"SELECT enabled FROM [user] WHERE email = @Email",
                        new { Email = request.Email }
                    );

                    if (userStatus == null)
                    {
                        return Unauthorized("No se encontró el usuario o las credenciales son incorrectas.");
                    }

                    if (userStatus == 0)
                    {
                        return StatusCode(409, new { Message = "La cuenta está inactiva." });
                    }

                    // Usar DynamicParameters para manejar los parámetros del procedimiento almacenado
                    var parameters = new DynamicParameters();
                    parameters.Add("userEmail", request.Email);
                    parameters.Add("userPassword", ComputeSha256Hash(request.Password));
                    parameters.Add("tokenOutput", dbType: DbType.String, direction: ParameterDirection.Output, size: 50);

                    var resultSets = await connection.QueryMultipleAsync("sp_AuthenticateLogin", parameters, commandType: CommandType.StoredProcedure);

                    var userData = resultSets.ReadFirstOrDefault<dynamic>();
                    if (userData == null)
                    {
                        return Unauthorized("No se encontró el usuario o las credenciales son incorrectas.");
                    }

                    var roles = resultSets.Read<dynamic>().ToList();
                    var profiles = resultSets.Read<dynamic>().ToList();
                    var permissions = resultSets.Read<dynamic>().ToList();
                    var companies = resultSets.Read<dynamic>().ToList();

                    // Obtener el valor del parámetro de salida
                    var tokenValue = parameters.Get<string>("tokenOutput");
                    if (string.IsNullOrEmpty(tokenValue))
                    {
                        return Unauthorized("La autenticación falló, el token no fue generado.");
                    }

                    var response = new
                    {
                        user = userData,
                        token = tokenValue,
                        roles = roles,
                        profiles = profiles,
                        permissions = permissions,
                        companies = companies
                    };

                    return Ok(response);
                }
                catch (SqlException ex)
                {
                    string errorDetails = $"Mensaje: {ex.Message}\n" +
                                          $"Stack Trace: {ex.StackTrace}\n" +
                                          $"Source: {ex.Source}\n" +
                                          $"Fecha: {DateTime.Now}";

                    return StatusCode(500, new
                    {
                        IsAuth = false,
                        Mensaje = "Ocurrió un error en la base de datos.",
                        Error = errorDetails
                    });
                }
                catch (Exception ex)
                {
                    string errorDetails = $"Mensaje: {ex.Message}\n" +
                                          $"Stack Trace: {ex.StackTrace}\n" +
                                          $"Source: {ex.Source}\n" +
                                          $"Fecha: {DateTime.Now}";

                    return StatusCode(500, new
                    {
                        IsAuth = false,
                        Mensaje = "Ocurrió un error al procesar la solicitud.",
                        Error = errorDetails
                    });
                }
            }
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] dynamic request)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                // Usar DynamicParameters para manejar los parámetros
                var parameters = new DynamicParameters();
                parameters.Add("UserToken", request.UserToken.ToString());

                // Llamada al procedimiento almacenado
                await connection.ExecuteAsync("sp_LogoutUser", parameters, commandType: CommandType.StoredProcedure);

                return Ok("Usuario desconectado con éxito.");
            }
        }

        public string ComputeSha256Hash(string rawData)
        {
            // Create a SHA256   
            using (SHA256 sha256Hash = SHA256.Create())
            {
                // ComputeHash - returns byte array  
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));

                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}