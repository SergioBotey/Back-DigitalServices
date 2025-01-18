using System.Collections.Generic;

namespace digital_services.Objects.Auth
{
    public class UpdateUserProfilesDto
    {
        public string Email { get; set; }         // Correo electrónico del usuario
        public List<int> ProfileIds { get; set; } // Lista de IDs de perfiles que se asignarán al usuario
    }

}