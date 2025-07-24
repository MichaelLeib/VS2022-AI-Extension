using System;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Services.Implementation;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for managing Ollama server connection, health monitoring, and graceful degradation
    /// </summary>
    public interface IOllamaConnectionManager
    {
        /// <summary>
        /// Whether the connection to Ollama is currently healthy
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// The last health status received from the server
        /// </summary>
        OllamaHealthStatus LastHealthStatus { get; }

        /// <summary>
        /// How long ago the last health check was performed
        /// </summary>
        TimeSpan TimeSinceLastHealthCheck { get; }

        /// <summary>
        /// Fired when the connection status changes
        /// </summary>
        event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Fired when a health check completes
        /// </summary>
        event EventHandler<HealthCheckCompletedEventArgs> HealthCheckCompleted;

        /// <summary>
        /// Starts periodic health monitoring
        /// </summary>
        Task StartMonitoringAsync();

        /// <summary>
        /// Stops health monitoring
        /// </summary>
        Task StopMonitoringAsync();

        /// <summary>
        /// Performs an immediate health check
        /// </summary>
        Task<OllamaHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attempts to reconnect to the Ollama server
        /// </summary>
        Task<bool> AttemptReconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current connection info for display
        /// </summary>
        Task<string> GetConnectionInfoAsync();

        /// <summary>
        /// Handles graceful degradation when Ollama is unavailable
        /// </summary>
        Task HandleServerUnavailableAsync(string operation = null);
    }
}