using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class UpdateProcessStatusRequestResults
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int VariableId { get; set; }
        public string Path { get; set; }
        public DateTime? ExecutionTimeStart { get; set; }
        public DateTime? ExecutionTimeEnd { get; set; }
    }
}