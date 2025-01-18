using System;

namespace digital_services.Objects.App
{
    public class TicketsDS
    {
        public string IdTicket { get; set; }
        public string Tipo { get; set; }
        public string Servicio { get; set; }
        public DateTime FechaHoraInicio { get; set; }
        public string FechaHoraFin { get; set; }
        public string Estado { get; set; }
        public string Color { get; set; }
        public string Notas { get; set; }
    }
}