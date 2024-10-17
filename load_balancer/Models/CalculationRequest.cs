namespace YourProject.Models
{
    public class CalculationRequest
    {
        public string CalculationId { get; set; } = string.Empty;
        public string InputFilePath { get; set; } = string.Empty;
        public string CallbackUrl { get; set; } = string.Empty;
        public long InputFileSize { get; set; }
        public int Transaction { get; set; }
        public bool ManualCache { get; set; }
        public bool ExpenseCache { get; set; }
        public string SourceSystem { get; set; } = string.Empty;
        public string QueryString { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string DestinationUrl { get; set; } = string.Empty;
        public string AssignedEndpointName { get; set; } = string.Empty;
    }
}