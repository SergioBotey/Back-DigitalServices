namespace digital_services.Objects.App
{
    public class UpdateProcessGeneralStatusRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int StatusId { get; set; }
    }
}