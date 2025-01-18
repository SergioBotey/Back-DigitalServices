using Newtonsoft.Json;

namespace digital_services.Objects.App
{
    public class TicketResponseWithSSL
    {
        [JsonProperty("ID Ticket")]
        public string ID_Ticket { get; set; }
        
        [JsonProperty("SSL")]
        public string SSL { get; set; }  // Nuevo campo para mostrar la información concatenada de SSL

        [JsonProperty("Creado por")]
        public string NombreUsuario { get; set; }  // Este se llenará en C#

        public string Servicio { get; set; }

        [JsonProperty("Fecha Inicio")]
        public string FechaHoraInicio { get; set; }

        [JsonProperty("Fecha Fin")]
        public string FechaHoraFin { get; set; }

        [JsonProperty("Estado")]
        public string Estado { get; set; }

        [JsonProperty("Estado-Color")]
        public string Color { get; set; }

        public string Notas { get; set; }
    }
}
