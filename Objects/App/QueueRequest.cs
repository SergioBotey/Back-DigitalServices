using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // Asegúrate de tener este using para JObject
using System.Collections.Generic;

namespace digital_services.Objects.App
{
    public class Archivo
    {
        [JsonProperty("Item1")]
        public string Item1 { get; set; }
        
        [JsonProperty("Item2")]
        public string Item2 { get; set; }
    }

    public class DataModeler
    {
        [JsonProperty("FolderPath")]
        public string FolderPath { get; set; }
        
        [JsonProperty("ProcessId")]
        public string ProcessId { get; set; }
        
        [JsonProperty("Type")]
        public int Type { get; set; }
        
        [JsonProperty("FlowName")]
        public string FlowName { get; set; }
        
        [JsonProperty("TaskName")]
        public string TaskName { get; set; }
        
        [JsonProperty("Archivos")]
        public Dictionary<string, Archivo> Archivos { get; set; }
    }

    public class AdditionalData
    {
        [JsonProperty("nivel_1_data_modeler")]
        public DataModeler Nivel_1_Data_Modeler { get; set; }
        
        // Cambiado a JObject para aceptar cualquier estructura JSON
        [JsonProperty("nivel_2_data_next_endpoint")]
        public JObject Nivel_2_Data_Next_Endpoint { get; set; }
    }

    public class RequestData
    {
        [JsonProperty("AdditionalData")]
        public AdditionalData AdditionalData { get; set; }
        
        [JsonProperty("Technology")]
        public string Technology { get; set; }
        
        [JsonProperty("TechnologyEndpoint")]
        public string TechnologyEndpoint { get; set; }
        
        [JsonProperty("PrevEndpointApi")]
        public string PrevEndpointApi { get; set; }
        
        [JsonProperty("NextEndpointApi")]
        public string NextEndpointApi { get; set; }
    }

    public class QueueRequest
    {
        [JsonProperty("Data")]
        public RequestData Data { get; set; }
    }
}
