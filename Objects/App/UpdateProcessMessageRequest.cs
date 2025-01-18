namespace digital_services.Objects.App
{
    public class UpdateProcessMessageRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int StatusId { get; set; }
        public string Message { get; set; }
    }
}