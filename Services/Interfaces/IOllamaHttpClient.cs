using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for HTTP communication with Ollama server
    /// </summary>
    public interface IOllamaHttpClient
    {
        /// <summary>
        /// Sends a POST request to the Ollama server
        /// </summary>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="data">Request data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Ollama response</returns>
        Task<OllamaResponse> PostAsync(string endpoint, object data, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a GET request to the Ollama server
        /// </summary>
        /// <param name="endpoint">API endpoint</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Model information or other response data</returns>
        Task<T> GetAsync<T>(string endpoint, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests connection to the Ollama server
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if connection is successful</returns>
        Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets health status of the Ollama server
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Health status information</returns>
        Task<HealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the base URL for the Ollama server
        /// </summary>
        /// <param name="baseUrl">Base URL of the server</param>
        void SetBaseUrl(string baseUrl);

        /// <summary>
        /// Sets the request timeout
        /// </summary>
        /// <param name="timeoutSeconds">Timeout in seconds</param>
        void SetTimeout(int timeoutSeconds);
    }
}