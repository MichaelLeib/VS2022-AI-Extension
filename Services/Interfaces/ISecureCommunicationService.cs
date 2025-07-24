using System.Net.Http;
using System.Threading.Tasks;
using OllamaAssistant.Services.Implementation;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for managing secure communication with Ollama server
    /// </summary>
    public interface ISecureCommunicationService
    {
        /// <summary>
        /// Configures HTTP client for secure communication
        /// </summary>
        void ConfigureHttpClient(HttpClient httpClient);

        /// <summary>
        /// Validates that the endpoint supports HTTPS
        /// </summary>
        Task<bool> ValidateHttpsEndpointAsync(string endpoint);

        /// <summary>
        /// Stores API key securely in VS settings
        /// </summary>
        Task<bool> StoreApiKeyAsync(string apiKey);

        /// <summary>
        /// Retrieves the stored API key
        /// </summary>
        string GetApiKey();

        /// <summary>
        /// Clears stored API key
        /// </summary>
        Task<bool> ClearApiKeyAsync();

        /// <summary>
        /// Checks if an API key is configured
        /// </summary>
        bool HasApiKey();

        /// <summary>
        /// Signs a request for authentication and integrity
        /// </summary>
        void SignRequest(HttpRequestMessage request, string payload = null);

        /// <summary>
        /// Validates a request signature (for incoming requests if needed)
        /// </summary>
        bool ValidateRequestSignature(HttpRequestMessage request, string payload, string secret);

        /// <summary>
        /// Stores API secret securely
        /// </summary>
        Task<bool> StoreApiSecretAsync(string apiSecret);

        /// <summary>
        /// Retrieves stored credentials as a secure object
        /// </summary>
        SecureCredentials GetSecureCredentials();
    }
}