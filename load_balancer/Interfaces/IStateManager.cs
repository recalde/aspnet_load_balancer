namespace YourProject.Interfaces
{
    public interface IStateManager
    {
        Task SaveCalculationRequestAsync(CalculationRequest request);
        Task<CalculationRequest?> GetCalculationRequestAsync(string calculationId);
        Task RemoveCalculationRequestAsync(string calculationId);
        Task<IEnumerable<CalculationRequest>> GetCalculationsInProgressAsync();
        Task<IEnumerable<CalculationRequest>> GetCalculationHistoryAsync(int hours);
        Task CleanupOldEntriesAsync(TimeSpan retentionPeriod);

        // Capacity tracking methods
        Task<bool> TryAcquireEndpointCapacityAsync(string endpointName, long fileSize);
        Task ReleaseEndpointCapacityAsync(string endpointName, long fileSize);
        Task<IEnumerable<EndpointCapacityStatus>> GetEndpointCapacitiesAsync();
    }
}