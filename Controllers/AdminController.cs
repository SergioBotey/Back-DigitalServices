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
using Microsoft.Extensions.Options;
using digital_services.Objects.Auth;
using digital_services.Objects.App;
using Dapper;

namespace digital_services.Controllers
{
    [Route("api/admin")]
    public class AdminController : Controller
    {
        private readonly TokenValidationService _tokenValidationService;
        private readonly ApiSettings _apiSettings;
        private readonly DatabaseConfig _databaseService;

        public AdminController(DatabaseConfig databaseService, IOptions<ApiSettings> apiSettingsOptions, TokenValidationService tokenValidationService)
        {
            _databaseService = databaseService;
            _apiSettings = apiSettingsOptions.Value;
            _tokenValidationService = tokenValidationService;
        }

        [HttpGet("test")]
        public IActionResult TestEndpoint()
        {
            return Ok("Mensaje de prueba desde el endpoint 'test'");
        }

        // Nuevo endpoint para listar datos de los usuarios
        [HttpGet("list-users")]
        public async Task<IActionResult> ListUsers()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var users = await connection.QueryAsync<dynamic>("sp_ListUserDetails", commandType: CommandType.StoredProcedure);

                    // Verifica si se obtuvieron resultados
                    if (users == null || !users.Any())
                    {
                        return NotFound("No se encontraron usuarios.");
                    }

                    return Ok(users);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para listar áreas
        [HttpGet("list-areas")]
        public async Task<IActionResult> ListAreas()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var areas = await connection.QueryAsync<dynamic>("SELECT * FROM userArea");

                    if (areas == null || !areas.Any())
                    {
                        return NotFound("No se encontraron áreas.");
                    }

                    return Ok(areas);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para listar usuarios por id de área
        [HttpGet("list-users-by-area/{areaId}")]
        public async Task<IActionResult> ListUsersByArea(int areaId)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Consulta para obtener los usuarios que pertenecen a un área específica
                    var users = await connection.QueryAsync<dynamic>(
                        "SELECT email, name, area_id, enabled, created_at, updated_at FROM [user] WHERE area_id = @AreaId",
                        new { AreaId = areaId }
                    );

                    if (users == null || !users.Any())
                    {
                        return NotFound("No se encontraron usuarios para el área especificada.");
                    }

                    return Ok(users);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para listar roles
        [HttpGet("list-roles")]
        public async Task<IActionResult> ListRoles()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var roles = await connection.QueryAsync<dynamic>("SELECT * FROM userRol");

                    if (roles == null || !roles.Any())
                    {
                        return NotFound("No se encontraron roles.");
                    }

                    return Ok(roles);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para listar perfiles según el rol_id
        [HttpGet("list-profiles/{rolId}")]
        public async Task<IActionResult> ListProfiles(int rolId)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var profiles = await connection.QueryAsync<dynamic>(
                        "SELECT * FROM profile WHERE rol_id = @RolId",
                        new { RolId = rolId }
                    );

                    if (profiles == null || !profiles.Any())
                    {
                        return NotFound("No se encontraron perfiles para el rol especificado.");
                    }

                    return Ok(profiles);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para listar detalles de roles, perfiles, permisos y empresas
        [HttpGet("list-role-details")]
        public async Task<IActionResult> ListRoleDetails()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var roleDetails = await connection.QueryAsync<dynamic>("sp_ListRoleDetails", commandType: CommandType.StoredProcedure);

                    // Verifica si se obtuvieron resultados
                    if (roleDetails == null || !roleDetails.Any())
                    {
                        return NotFound("No se encontraron detalles de roles.");
                    }

                    return Ok(roleDetails);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        /************ Empresas ************/

        // Nuevo endpoint para listar empresas
        [HttpGet("list-companies")]
        public async Task<IActionResult> ListCompanies()
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var companies = await connection.QueryAsync<dynamic>("sp_ListCompanies", commandType: CommandType.StoredProcedure);

                    if (companies == null || !companies.Any())
                    {
                        return NotFound("No se encontraron empresas.");
                    }

                    return Ok(companies);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para registrar una empresa
        [HttpPost("store-company")]
        public async Task<IActionResult> StoreCompany([FromBody] CompanyDto companyDto)
        {
            if (companyDto == null || string.IsNullOrEmpty(companyDto.Name) || string.IsNullOrEmpty(companyDto.TaxId))
            {
                return BadRequest("Nombre y RUC son obligatorios.");
            }

            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Obtener el último código que comienza con 'EY'
                    var lastCode = await connection.QuerySingleOrDefaultAsync<string>(
                        @"SELECT TOP 1 code 
                    FROM company 
                    WHERE code LIKE 'EY%' 
                    ORDER BY code DESC");

                    int newNumber = 1;
                    if (lastCode != null)
                    {
                        string lastNumberStr = lastCode.Substring(2);
                        if (int.TryParse(lastNumberStr, out int lastNumber))
                        {
                            newNumber = lastNumber + 1;
                        }
                    }

                    // Generar el nuevo código
                    string newCode = "EY" + newNumber.ToString("D3");

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Code", newCode, DbType.String);
                    parameters.Add("Name", companyDto.Name, DbType.String);
                    parameters.Add("Description", companyDto.Description, DbType.String);
                    parameters.Add("TaxId", companyDto.TaxId, DbType.String);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_InsertCompany", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el valor del parámetro de salida
                    int rowsAffected = parameters.Get<int>("RowsAffected");

                    if (rowsAffected > 0)
                    {
                        return Ok("Empresa registrada exitosamente.");
                    }
                    else
                    {
                        return StatusCode(500, "Ocurrió un error al registrar la empresa.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para obtener una empresa por código
        [HttpGet("get-company/{code}")]
        public async Task<IActionResult> GetCompanyByCode(string code)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    var parameters = new DynamicParameters();
                    parameters.Add("Code", code, DbType.String);

                    var company = await connection.QuerySingleOrDefaultAsync<dynamic>(
                        "sp_GetCompanyByCode", parameters, commandType: CommandType.StoredProcedure);

                    if (company == null)
                    {
                        return NotFound("No se encontró la empresa.");
                    }

                    return Ok(company);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para actualizar una empresa
        [HttpPut("update-company")]
        public async Task<IActionResult> UpdateCompany([FromBody] CompanyUpdateDto companyUpdateDto)
        {
            if (companyUpdateDto == null || string.IsNullOrEmpty(companyUpdateDto.Code))
            {
                return BadRequest("El código de la empresa es obligatorio.");
            }

            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Code", companyUpdateDto.Code, DbType.String);
                    parameters.Add("Name", companyUpdateDto.Name, DbType.String);
                    parameters.Add("Description", companyUpdateDto.Description, DbType.String);
                    parameters.Add("TaxId", companyUpdateDto.TaxId, DbType.String);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_UpdateCompany", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el valor del parámetro de salida
                    int rowsAffected = parameters.Get<int>("RowsAffected");

                    if (rowsAffected > 0)
                    {
                        return Ok("Empresa actualizada exitosamente.");
                    }
                    else
                    {
                        return StatusCode(500, "Ocurrió un error al actualizar la empresa.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para eliminar una empresa
        [HttpDelete("delete-company/{code}")]
        public async Task<IActionResult> DeleteCompany(string code)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Code", code, DbType.String);
                    parameters.Add("IsUsedInProfile", dbType: DbType.Boolean, direction: ParameterDirection.Output);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_DeleteCompany", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener los valores de los parámetros de salida
                    bool isUsedInProfile = parameters.Get<bool>("IsUsedInProfile");
                    int rowsAffected = parameters.Get<int>("RowsAffected");

                    // Verificar si la empresa está en uso
                    if (isUsedInProfile)
                    {
                        return BadRequest("No se puede eliminar la empresa porque está asociada a uno o más perfiles.");
                    }

                    // Verificar si la eliminación fue exitosa
                    if (rowsAffected > 0)
                    {
                        return Ok("Empresa eliminada exitosamente.");
                    }
                    else
                    {
                        return StatusCode(500, "Ocurrió un error al eliminar la empresa.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        // Nuevo endpoint para obtener los datos de un perfil por ID
        [HttpGet("get-profile/{id}")]
        public async Task<IActionResult> GetProfileById(int id)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    using (var multi = await connection.QueryMultipleAsync("sp_GetProfileById", new { Id = id }, commandType: CommandType.StoredProcedure))
                    {
                        // Obtener los datos del perfil
                        var profileData = await multi.ReadSingleOrDefaultAsync<dynamic>();

                        if (profileData == null)
                        {
                            return NotFound("No se encontró el perfil.");
                        }

                        // Obtener los permisos asociados al perfil
                        var permissions = await multi.ReadAsync<dynamic>();

                        // Retornar la descripción del perfil junto con los permisos asociados
                        return Ok(new
                        {
                            data = profileData,
                            permissions = permissions
                        });
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPost("create-profile")]
        public async Task<IActionResult> CreateProfile([FromBody] CreateProfileDto createProfileDto)
        {
            if (createProfileDto == null || string.IsNullOrEmpty(createProfileDto.Description))
            {
                return BadRequest("La descripción del perfil es obligatoria.");
            }

            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Description", createProfileDto.Description, DbType.String);
                    parameters.Add("RoleId", createProfileDto.RoleId, DbType.Int32);
                    parameters.Add("CompanyId", createProfileDto.CompanyId, DbType.String);
                    parameters.Add("NewProfileId", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_CreateProfile", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el ID del nuevo perfil creado
                    int newProfileId = parameters.Get<int>("NewProfileId");

                    if (newProfileId > 0)
                    {
                        return Ok(new { Message = "Perfil creado exitosamente.", ProfileId = newProfileId });
                    }
                    else
                    {
                        return StatusCode(500, "Ocurrió un error al crear el perfil.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpGet("get-permissions-by-profile/{id}")]
        public async Task<IActionResult> GetPermissionsByProfileId(int id)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutar el stored procedure
                    var permissions = await connection.QueryAsync<dynamic>(
                        "sp_GetPermissionsByProfileId",
                        new { ProfileId = id },
                        commandType: CommandType.StoredProcedure);

                    return Ok(permissions);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPost("update-profile-permissions")]
        public async Task<IActionResult> UpdateProfilePermissions([FromBody] UpdateProfilePermissionsDto updateProfilePermissionsDto)
        {
            if (updateProfilePermissionsDto == null || updateProfilePermissionsDto.ProfileId <= 0)
            {
                return BadRequest("El ID del perfil es obligatorio.");
            }

            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Convertir la lista de PermissionIds a una cadena CSV
                    string permissionIdsCsv = string.Join(",", updateProfilePermissionsDto.PermissionIds);

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("ProfileId", updateProfilePermissionsDto.ProfileId, DbType.Int32);
                    parameters.Add("PermissionIds", permissionIdsCsv, DbType.String);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_UpdateProfilePermissions", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Permisos actualizados exitosamente." });
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPost("update-company-profile")]
        public async Task<IActionResult> UpdateCompanyProfile([FromBody] UpdateCompanyProfileDto dto)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Validar que el DTO no esté vacío
                    if (dto == null || string.IsNullOrEmpty(dto.CompanyId))
                    {
                        return BadRequest("El ID de la compañía es requerido.");
                    }

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("CompanyId", dto.CompanyId, DbType.String);
                    parameters.Add("ProfileId", dto.ProfileId, DbType.Int32);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_UpdateCompanyProfile", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el número de filas afectadas
                    int rowsAffected = parameters.Get<int>("RowsAffected");

                    if (rowsAffected > 0)
                    {
                        return Ok(new { Message = "Compañía actualizada exitosamente." });
                    }
                    else
                    {
                        return NotFound("Perfil no encontrado.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        //Obtención de perfiles del usuario
        [HttpGet("get-profiles-by-user/{email}")]
        public async Task<IActionResult> GetProfilesByUser(string email)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutar el stored procedure
                    var profiles = await connection.QueryAsync<dynamic>(
                        "sp_GetProfilesByUserEmail",
                        new { Email = email },
                        commandType: CommandType.StoredProcedure);

                    return Ok(profiles);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPost("update-user-profiles")]
        public async Task<IActionResult> UpdateUserProfiles([FromBody] UpdateUserProfilesDto dto)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Validar que la lista de perfiles no sea nula o vacía
                    string profileIdsCsv = dto.ProfileIds != null && dto.ProfileIds.Any()
                        ? string.Join(",", dto.ProfileIds)
                        : null;

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Email", dto.Email, DbType.String);
                    parameters.Add("ProfileIds", profileIdsCsv, DbType.String);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_UpdateUserProfiles", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Perfiles actualizados exitosamente." });
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPost("register-user")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterUserDto userDto)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Hash de la contraseña utilizando ComputeSha256Hash
                    var passwordHash = ComputeSha256Hash(userDto.Password);

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Email", userDto.Email, DbType.String);
                    parameters.Add("Name", userDto.Name, DbType.String);
                    parameters.Add("PasswordHash", passwordHash, DbType.String);
                    parameters.Add("AreaId", userDto.AreaId, DbType.Int32);
                    parameters.Add("ProfileId", userDto.ProfileId, DbType.Int32);
                    parameters.Add("Result", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_RegisterUser", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el resultado del procedimiento almacenado
                    int result = parameters.Get<int>("Result");

                    if (result == 1)
                    {
                        return Ok(new { Message = "Usuario registrado exitosamente." });
                    }
                    else if (result == -1)
                    {
                        return Conflict(new { Message = "El correo ya está registrado." });
                    }
                    else
                    {
                        return StatusCode(500, "Ocurrió un error al registrar el usuario.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpGet("get-user-details/{email}")]
        public async Task<IActionResult> GetUserDetails(string email)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    using (var multi = await connection.QueryMultipleAsync("sp_GetUserDetails", new { Email = email }, commandType: CommandType.StoredProcedure))
                    {
                        // Obtener los detalles del usuario
                        var userDetails = await multi.ReadSingleOrDefaultAsync<dynamic>();

                        if (userDetails == null)
                        {
                            return NotFound(new { Message = "Usuario no encontrado." });
                        }

                        // Obtener los perfiles asociados al usuario
                        var profiles = await multi.ReadAsync<dynamic>();

                        // Retornar los datos de manera estructurada
                        return Ok(new
                        {
                            userData = userDetails,
                            profiles = profiles,
                            lastConnection = userDetails.ÚltimaConexión
                        });
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpPut("update-user")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserDto userDto)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Hash de la contraseña si se proporciona
                    string passwordHash = !string.IsNullOrWhiteSpace(userDto.Password)
                        ? ComputeSha256Hash(userDto.Password)
                        : null;

                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Email", userDto.Email, DbType.String);
                    parameters.Add("Name", userDto.Name, DbType.String);
                    parameters.Add("AreaId", userDto.AreaId, DbType.Int32);
                    parameters.Add("Enabled", userDto.Enabled, DbType.Boolean);
                    parameters.Add("PasswordHash", passwordHash, DbType.String);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_UpdateUser", parameters, commandType: CommandType.StoredProcedure);

                    return Ok(new { Message = "Usuario actualizado exitosamente." });
                }
                catch (SqlException ex)
                {
                    return StatusCode(500, new { Message = "Error en la base de datos.", Error = ex.Message });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Message = "Error general.", Error = ex.Message });
                }
            }
        }

        [HttpDelete("delete-user/{email}")]
        public async Task<IActionResult> DeleteUser(string email)
        {
            using (var connection = await _databaseService.Database1.OpenConnectionAsync())
            {
                try
                {
                    // Parámetros para el stored procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("Email", email, DbType.String);
                    parameters.Add("RowsAffected", dbType: DbType.Int32, direction: ParameterDirection.Output);

                    // Ejecutar el stored procedure
                    await connection.ExecuteAsync("sp_DeleteUser", parameters, commandType: CommandType.StoredProcedure);

                    // Obtener el número de filas afectadas
                    int rowsAffected = parameters.Get<int>("RowsAffected");

                    if (rowsAffected > 0)
                    {
                        return Ok(new { Message = "Usuario eliminado exitosamente." });
                    }
                    else
                    {
                        return NotFound(new { Message = "Usuario no encontrado." });
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);
                }
            }
        }

        [HttpGet("list-services")]
        public async Task<IActionResult> ListServices()
        {
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutar el Stored Procedure para obtener los servicios
                    var services = await connection.QueryAsync<dynamic>("sp_ListServices", commandType: CommandType.StoredProcedure);

                    if (services == null || !services.Any())
                    {
                        return NotFound("No se encontraron servicios.");
                    }

                    // Devolver la lista de servicios
                    return Ok(services);
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);  // Manejo de excepciones específicas de SQL
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);  // Manejo de excepciones generales
                }
            }
        }

        [HttpGet("list-services-tickets")]
        public async Task<IActionResult> GetServicesTickets()
        {
            // Usamos una conexión a Database2 para ejecutar el stored procedure
            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Ejecutamos el stored procedure y obtenemos los datos
                    var servicesWithContent = await connection.QueryAsync<dynamic>(
                        "sp_GetServicesWithContent",
                        commandType: CommandType.StoredProcedure
                    );

                    // Si no hay datos, devolvemos un 404 Not Found
                    if (!servicesWithContent.Any())
                    {
                        return NotFound("No se encontraron servicios con contenido.");
                    }

                    // Devolvemos los datos en formato JSON
                    return Ok(servicesWithContent);
                }
                catch (SqlException ex)
                {
                    // Manejo de excepciones SQL
                    Console.WriteLine(ex.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener datos.");
                }
                catch (Exception ex)
                {
                    // Manejo de excepciones generales
                    return StatusCode(500, new { Message = "Error general.", Error = ex.Message });
                }
            }
        }

        //############### Priorizacion Tickets
        [HttpGet("get-user-services-prioritization")]
        public async Task<IActionResult> GetUserServicesPrioritization()
        {
            // Crear un diccionario para almacenar los nombres de los usuarios obtenidos
            Dictionary<string, string> userNamesCache = new Dictionary<string, string>();

            // Obtener datos de la tabla service_user en Database2
            using (var connectionDb2 = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    var servicesData = await connectionDb2.QueryAsync<dynamic>(
                        "sp_GetUserServicesPrioritization",
                        commandType: CommandType.StoredProcedure
                    );

                    // Conectar a Database1 para obtener los nombres de los usuarios
                    using (var connectionDb1 = await _databaseService.Database1.OpenConnectionAsync())
                    {
                        foreach (var item in servicesData)
                        {
                            // Si el nombre de usuario ya está en el caché, no hacemos la consulta nuevamente
                            if (!userNamesCache.TryGetValue(item.user_email, out string userName))
                            {
                                userName = await connectionDb1.QueryFirstOrDefaultAsync<string>(
                                    "SELECT name FROM dbo.[user] WHERE email = @Email",
                                    new { Email = item.user_email }
                                );

                                // Cachear el nombre del usuario
                                if (userName != null)
                                {
                                    userNamesCache[item.user_email] = userName;
                                }
                            }
                        }
                    }

                    // Crear una lista con los resultados, incluyendo la fecha de registro
                    var resultList = servicesData.Select(item => new
                    {
                        Id = item.id,
                        UserEmail = item.user_email,
                        UserName = userNamesCache[item.user_email], // Asignar el nombre del usuario
                        ServiceCode = item.service_code,
                        ServiceName = item.name,
                        DateRegister = item.date_register // Incluir la fecha de registro
                    }).ToList();

                    return Ok(resultList);
                }
                catch (SqlException e)
                {
                    Console.WriteLine(e.ToString());
                    return StatusCode(500, "Error interno del servidor al obtener datos.");
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { Message = "Error general.", Error = ex.Message });
                }
            }
        }

        [HttpPost("add-user-service-prioritization")]
        public async Task<IActionResult> AddUserServicePrioritization([FromBody] ServiceUserDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.UserEmail) || dto.ServiceId <= 0)
            {
                return BadRequest("Datos inválidos.");
            }

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Definir el parámetro de salida para capturar el valor de retorno del Stored Procedure
                    var parameters = new DynamicParameters();
                    parameters.Add("ServiceId", dto.ServiceId);
                    parameters.Add("UserEmail", dto.UserEmail);
                    parameters.Add("ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

                    // Llamar al Stored Procedure
                    await connection.ExecuteAsync(
                        "sp_InsertServiceUser",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    // Obtener el valor de retorno
                    int returnValue = parameters.Get<int>("ReturnValue");

                    if (returnValue == -1)
                    {
                        // Manejar el caso en que el registro ya existe
                        return Conflict("La combinación de usuario y servicio ya existe.");
                    }

                    if (returnValue == 0)
                    {
                        // Operación exitosa
                        return Ok("Datos guardados exitosamente.");
                    }
                    else
                    {
                        // En caso de algún valor de retorno inesperado
                        return StatusCode(500, "No se pudo guardar la priorización de usuario y servicio.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);  // Manejo de excepciones específicas de SQL
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);  // Manejo de excepciones generales
                }
            }
        }

        [HttpDelete("delete-user-service-prioritization/{id}")]
        public async Task<IActionResult> DeleteUserServicePrioritization(int id)
        {
            if (id <= 0)
            {
                return BadRequest("ID inválido.");
            }

            using (var connection = await _databaseService.Database2.OpenConnectionAsync())
            {
                try
                {
                    // Definir el parámetro de salida para capturar el número de filas afectadas
                    var parameters = new DynamicParameters();
                    parameters.Add("Id", id);
                    parameters.Add("ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

                    // Llamar al Stored Procedure para eliminar el registro
                    await connection.ExecuteAsync(
                        "sp_DeleteServiceUserById",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    // Obtener el valor de retorno
                    int affectedRows = parameters.Get<int>("ReturnValue");

                    if (affectedRows > 0)
                    {
                        return Ok("Registro eliminado exitosamente.");
                    }
                    else
                    {
                        return NotFound("No se encontró ningún registro con el ID especificado.");
                    }
                }
                catch (SqlException ex)
                {
                    return HandleSqlException(ex);  // Manejo de excepciones específicas de SQL
                }
                catch (Exception ex)
                {
                    return HandleGeneralException(ex);  // Manejo de excepciones generales
                }
            }
        }

        private string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        // Manejo de excepciones SQL
        // Manejo de excepciones SQL
        private IActionResult HandleSqlException(SqlException ex)
        {
            string errorDetails = $"Mensaje: {ex.Message}\n" +
                                  $"Stack Trace: {ex.StackTrace}\n" +
                                  $"Source: {ex.Source}\n" +
                                  $"Fecha: {DateTime.Now}";

            // Imprimir detalles en la consola
            Console.WriteLine("SQL Exception:");
            Console.WriteLine(errorDetails);

            return StatusCode(500, new
            {
                Mensaje = "Ocurrió un error en la base de datos.",
                Error = errorDetails
            });
        }

        // Manejo de excepciones generales
        private IActionResult HandleGeneralException(Exception ex)
        {
            string errorDetails = $"Mensaje: {ex.Message}\n" +
                                  $"Stack Trace: {ex.StackTrace}\n" +
                                  $"Source: {ex.Source}\n" +
                                  $"Fecha: {DateTime.Now}";

            // Imprimir detalles en la consola
            Console.WriteLine("General Exception:");
            Console.WriteLine(errorDetails);

            return StatusCode(500, new
            {
                Mensaje = "Ocurrió un error al procesar la solicitud.",
                Error = errorDetails
            });
        }
    }
}