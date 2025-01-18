namespace digital_services.Objects.App
{
    public class UpdateProcessQueueDataToProcessRequest
    {
        public string ProcessId { get; set; }
        public string QueueReferenceBasePath { get; set; }
        public string DataToProcessPath { get; set; }
    }
}
