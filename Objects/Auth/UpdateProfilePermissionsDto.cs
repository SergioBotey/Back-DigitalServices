using System.Collections.Generic;

namespace digital_services.Objects.Auth
{
    public class UpdateProfilePermissionsDto
    {
        public int ProfileId { get; set; }
        public List<int> PermissionIds { get; set; } // Lista de IDs de permisos asignados
    }
}
