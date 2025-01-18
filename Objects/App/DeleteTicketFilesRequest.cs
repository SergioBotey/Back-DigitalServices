using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class DeleteTicketFilesRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public string Path { get; set; }
    }
}