using System;
using System.Threading.Tasks;
using OllamaAssistant.Services.Implementation;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for managing Visual Studio integration resilience and error recovery
    /// </summary>
    public interface IVSIntegrationResilienceService
    {
        /// <summary>
        /// Whether VS integration is currently healthy
        /// </summary>
        bool IsVSHealthy { get; }

        /// <summary>
        /// Number of consecutive VS integration failures
        /// </summary>
        int ConsecutiveFailures { get; }

        /// <summary>
        /// Fired when VS health status changes
        /// </summary>
        event EventHandler<VSHealthStatusChangedEventArgs> VSHealthStatusChanged;

        /// <summary>
        /// Performs a comprehensive health check of VS integration
        /// </summary>
        Task<VSIntegrationHealthResult> CheckVSIntegrationHealthAsync();

        /// <summary>
        /// Attempts to recover from VS integration failures
        /// </summary>
        Task<bool> AttemptVSIntegrationRecoveryAsync();

        /// <summary>
        /// Reports a VS integration failure
        /// </summary>
        Task ReportVSIntegrationFailureAsync(string component, string operation, Exception exception = null);

        /// <summary>
        /// Executes an operation with VS integration error handling
        /// </summary>
        Task<T> ExecuteWithVSErrorHandlingAsync<T>(Func<Task<T>> operation, string component, string operationName, T defaultValue = default(T));

        /// <summary>
        /// Executes an operation with VS integration error handling (void return)
        /// </summary>
        Task ExecuteWithVSErrorHandlingAsync(Func<Task> operation, string component, string operationName);

        /// <summary>
        /// Determines if a VS operation should be attempted based on current health
        /// </summary>
        bool ShouldAttemptVSOperation();

        /// <summary>
        /// Forces a health check if enough time has passed
        /// </summary>
        Task<bool> EnsureVSHealthAsync();
    }
}