namespace digital_services.Objects.App
{
    public class UpdateProcessRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int StatusId { get; set; }
    }
}