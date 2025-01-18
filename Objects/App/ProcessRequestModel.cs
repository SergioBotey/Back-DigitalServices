using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class ProcessRequestModel
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public string ProcessTypeName { get; set; }
        public string ApiAction { get; set; }
        public string ApiActionValidate { get; set; }
        public List<Dictionary<string, object>> DataNecessary { get; set; }
        public List<Dictionary<string, string>> FormDataEntries { get; set; }
    }
}