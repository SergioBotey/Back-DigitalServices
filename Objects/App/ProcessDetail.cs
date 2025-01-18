using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class ProcessDetail
    {
        public string VariableId { get; set; }
        public string Path { get; set; }
        public DateTime ExecutionTimeStart { get; set; }
        public DateTime ExecutionTimeEnd { get; set; }
        public string ErrorMessage { get; set; }
    }
}