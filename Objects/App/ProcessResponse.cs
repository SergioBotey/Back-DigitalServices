using System;
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class ProcessResponse
    {
        public string ProcesoId { get; set; }
        public List<ProcessDetail> Details { get; set; }
    }
}