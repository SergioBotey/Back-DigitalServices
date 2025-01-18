namespace digital_services.Objects.App
{
    public class SaveProcessDetailRequest
    {
        public string Token { get; set; }
        public string ProcessId { get; set; }
        public int VariableId { get; set; }
    }
}