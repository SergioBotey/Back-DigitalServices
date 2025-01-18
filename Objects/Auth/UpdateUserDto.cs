namespace digital_services.Objects.Auth
{
    public class UpdateUserDto
    {
        public string Email { get; set; }  // El correo es necesario para identificar al usuario, pero no se puede cambiar
        public string Name { get; set; }   // El nombre del usuario
        public string Password { get; set; }  // La contraseña, opcional (puede ser null o vacío si no se actualiza)
        public int AreaId { get; set; }    // El ID del área a la que pertenece el usuario
        public int Enabled { get; set; }   // El estatus del usuario (1 para Activo, 0 para Inactivo)
    }

}