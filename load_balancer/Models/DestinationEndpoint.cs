namespace YourProject.Models
{
    public class DestinationEndpoint
    {
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
        public int ConcurrentCapacity { get; set; } // Max concurrent requests
        public long TotalFileSizeCapacity { get; set; } // Max total file size of concurrent requests
        public long IndividualFileSizeCapacity { get; set; } // Max individual request file size
        public string Url { get; set; } = string.Empty;
    }
}