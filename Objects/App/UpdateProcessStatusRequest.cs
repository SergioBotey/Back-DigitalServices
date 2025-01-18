namespace digital_services.Objects.App
{
    public class UpdateProcessStatusRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int VariableId { get; set; }
        public int StatusId { get; set; }
    }
}