using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class UpdateAdvancedDetailsRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int VariableId { get; set; }
        public int StatusId { get; set; }
        public string Path { get; set; }
        public string Size { get; set; }
        public string QtyFiles { get; set; }
        public string QtyTransactions { get; set; }
        public DateTime? ExecutionTimeStart { get; set; }
        public DateTime? ExecutionTimeEnd { get; set; }
    }
}