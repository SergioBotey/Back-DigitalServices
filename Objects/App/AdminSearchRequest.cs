using System;

namespace digital_services.Objects.App
{
    public class AdminSearchRequest
    {
        public string Token { get; set; }
        public string TicketId { get; set; }
        public string Notes { get; set; }
        public string Service { get; set; }
        public int? StatusId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}