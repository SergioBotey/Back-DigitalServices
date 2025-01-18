namespace digital_services.Objects.App
{
    public class SearchOperationsRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string Company { get; set; }
        public string TicketId { get; set; } // Nuevo campo para el ID del ticket
        public string Notes { get; set; } // Nuevo campo para las notas
        public string Service { get; set; } // Nuevo campo para el servicio (content_reference)
        public string Status { get; set; } // Nuevo campo para el status_id
    }
}
