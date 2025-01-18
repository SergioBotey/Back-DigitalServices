using System;

namespace digital_services.Objects.App
{
    public class ReportRequest
    {
        public string Token { get; set; }
        public string Email { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}