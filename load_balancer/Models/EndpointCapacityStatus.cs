namespace YourProject.Models
{
    public class EndpointCapacityStatus
    {
        public string EndpointName { get; set; } = string.Empty;
        public int CurrentConcurrentRequests { get; set; }
        public long CurrentTotalFileSize { get; set; }
    }
}