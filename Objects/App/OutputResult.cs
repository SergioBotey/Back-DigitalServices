namespace digital_services.Objects.App
{
    public class OutputResult
    {
        public string Status { get; set; }
        public string ZipFilePath { get; set; }
        public string OutputSize { get; set; }
        public int OutputQtyFiles { get; set; }
        public int OutputQtyTransactions { get; set; }

        public string OutputExecutionTime { get; set; }
    }
}