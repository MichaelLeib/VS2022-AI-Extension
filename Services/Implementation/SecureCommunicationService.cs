using System;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing secure communication with Ollama server
    /// </summary>
    [Export(typeof(ISecureCommunicationService))]
    public class SecureCommunicationService : ISecureCommunicationService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private readonly IVSSettingsPersistenceService _vsPersistence;
        private readonly object _lockObject = new object();
        
        // Encryption for local storage
        private readonly byte[] _entropy = Encoding.UTF8.GetBytes("OllamaAssistant_v1");
        private const string ApiKeySettingName = "OllamaApiKey_Encrypted";
        private const string ApiSecretSettingName = "OllamaApiSecret_Encrypted";

        public SecureCommunicationService(
            ISettingsService settingsService,
            IVSSettingsPersistenceService vsPersistence = null,
            ILogger logger = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _vsPersistence = vsPersistence;
            _logger = logger;
        }

        #region HTTPS Configuration

        /// <summary>
        /// Configures HTTP client for secure communication
        /// </summary>
        public void ConfigureHttpClient(HttpClient httpClient)
        {
            if (httpClient == null)
                throw new ArgumentNullException(nameof(httpClient));

            try
            {
                // Force HTTPS if not on localhost
                var baseUri = new Uri(_settingsService.OllamaEndpoint);
                if (!IsLocalEndpoint(baseUri) && baseUri.Scheme == "http")
                {
                    _logger?.LogWarningAsync("Non-local endpoint using HTTP. Consider using HTTPS for security.", "SecureCommunication").Wait();
                    
                    // Optionally force HTTPS (commented out to maintain compatibility)
                    // var httpsUri = new UriBuilder(baseUri) { Scheme = "https", Port = baseUri.Port == 80 ? 443 : baseUri.Port };
                    // httpClient.BaseAddress = httpsUri.Uri;
                }

                // Add security headers
                httpClient.DefaultRequestHeaders.Add("X-Request-ID", Guid.NewGuid().ToString());
                httpClient.DefaultRequestHeaders.Add("X-Client-Version", GetExtensionVersion());
                
                // Add API key if configured
                var apiKey = GetApiKey();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                }

                // Configure timeout for security
                httpClient.Timeout = TimeSpan.FromMilliseconds(_settingsService.OllamaTimeout);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error configuring secure HTTP client", "SecureCommunication").Wait();
                throw new SecurityException("Failed to configure secure communication", ex);
            }
        }

        /// <summary>
        /// Validates that the endpoint supports HTTPS
        /// </summary>
        public async Task<bool> ValidateHttpsEndpointAsync(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return false;

            try
            {
                var uri = new Uri(endpoint);
                
                // Local endpoints don't require HTTPS
                if (IsLocalEndpoint(uri))
                    return true;

                // Check if HTTPS is supported
                if (uri.Scheme == "https")
                    return true;

                // Try to connect to HTTPS version
                var httpsUri = new UriBuilder(uri) { Scheme = "https", Port = uri.Port == 80 ? 443 : uri.Port };
                
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    
                    try
                    {
                        var response = await client.GetAsync(httpsUri.Uri);
                        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                    }
                    catch
                    {
                        // HTTPS not available
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error validating HTTPS endpoint", "SecureCommunication").Wait();
                return false;
            }
        }

        #endregion

        #region API Key Management

        /// <summary>
        /// Stores API key securely in VS settings
        /// </summary>
        public async Task<bool> StoreApiKeyAsync(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                // Clear stored key
                return await ClearApiKeyAsync();
            }

            try
            {
                // Encrypt the API key using Windows DPAPI
                var encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiKey),
                    _entropy,
                    DataProtectionScope.CurrentUser);

                // Store in VS settings
                var base64Encrypted = Convert.ToBase64String(encryptedData);
                
                if (_vsPersistence != null)
                {
                    await _vsPersistence.SaveSettingAsync(ApiKeySettingName, base64Encrypted);
                    await _logger?.LogInfoAsync("API key stored securely", "SecureCommunication");
                    return true;
                }
                else
                {
                    // Fallback to in-memory storage (less secure)
                    lock (_lockObject)
                    {
                        _inMemoryApiKey = apiKey;
                    }
                    await _logger?.LogWarningAsync("API key stored in memory only (VS persistence unavailable)", "SecureCommunication");
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to store API key", "SecureCommunication");
                return false;
            }
        }

        /// <summary>
        /// Retrieves the stored API key
        /// </summary>
        public string GetApiKey()
        {
            try
            {
                // Try to get from VS settings
                if (_vsPersistence != null)
                {
                    var encryptedBase64 = _vsPersistence.GetSettingAsync<string>(ApiKeySettingName).Result;
                    if (!string.IsNullOrEmpty(encryptedBase64))
                    {
                        var encryptedData = Convert.FromBase64String(encryptedBase64);
                        var decryptedData = ProtectedData.Unprotect(encryptedData, _entropy, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(decryptedData);
                    }
                }

                // Fallback to in-memory storage
                lock (_lockObject)
                {
                    return _inMemoryApiKey;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Failed to retrieve API key", "SecureCommunication").Wait();
                return null;
            }
        }

        /// <summary>
        /// Clears stored API key
        /// </summary>
        public async Task<bool> ClearApiKeyAsync()
        {
            try
            {
                if (_vsPersistence != null)
                {
                    await _vsPersistence.DeleteSettingAsync(ApiKeySettingName);
                }

                lock (_lockObject)
                {
                    _inMemoryApiKey = null;
                }

                await _logger?.LogInfoAsync("API key cleared", "SecureCommunication");
                return true;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to clear API key", "SecureCommunication");
                return false;
            }
        }

        /// <summary>
        /// Checks if an API key is configured
        /// </summary>
        public bool HasApiKey()
        {
            var key = GetApiKey();
            return !string.IsNullOrEmpty(key);
        }

        #endregion

        #region Request Signing

        /// <summary>
        /// Signs a request for authentication and integrity
        /// </summary>
        public void SignRequest(HttpRequestMessage request, string payload = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var nonce = Guid.NewGuid().ToString("N");
                
                // Add timestamp and nonce headers
                request.Headers.Add("X-Timestamp", timestamp);
                request.Headers.Add("X-Nonce", nonce);
                
                // Create signature if we have a secret
                var apiSecret = GetApiSecret();
                if (!string.IsNullOrEmpty(apiSecret))
                {
                    var signature = CreateRequestSignature(request.Method.ToString(), request.RequestUri.PathAndQuery, timestamp, nonce, payload, apiSecret);
                    request.Headers.Add("X-Signature", signature);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Failed to sign request", "SecureCommunication").Wait();
                // Don't throw - allow unsigned requests
            }
        }

        /// <summary>
        /// Validates a request signature (for incoming requests if needed)
        /// </summary>
        public bool ValidateRequestSignature(HttpRequestMessage request, string payload, string secret)
        {
            if (request == null || string.IsNullOrEmpty(secret))
                return false;

            try
            {
                // Extract headers
                if (!request.Headers.TryGetValues("X-Timestamp", out var timestampValues) ||
                    !request.Headers.TryGetValues("X-Nonce", out var nonceValues) ||
                    !request.Headers.TryGetValues("X-Signature", out var signatureValues))
                {
                    return false;
                }

                var timestamp = timestampValues.FirstOrDefault();
                var nonce = nonceValues.FirstOrDefault();
                var providedSignature = signatureValues.FirstOrDefault();

                if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(providedSignature))
                    return false;

                // Check timestamp is recent (within 5 minutes)
                if (long.TryParse(timestamp, out var unixTime))
                {
                    var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                    var timeDiff = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalMinutes);
                    if (timeDiff > 5)
                        return false;
                }

                // Calculate expected signature
                var expectedSignature = CreateRequestSignature(
                    request.Method.ToString(),
                    request.RequestUri.PathAndQuery,
                    timestamp,
                    nonce,
                    payload,
                    secret);

                // Compare signatures
                return string.Equals(providedSignature, expectedSignature, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Failed to validate request signature", "SecureCommunication").Wait();
                return false;
            }
        }

        #endregion

        #region Credential Storage

        /// <summary>
        /// Stores API secret securely
        /// </summary>
        public async Task<bool> StoreApiSecretAsync(string apiSecret)
        {
            if (string.IsNullOrEmpty(apiSecret))
            {
                return await ClearApiSecretAsync();
            }

            try
            {
                // Encrypt the API secret
                var encryptedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiSecret),
                    _entropy,
                    DataProtectionScope.CurrentUser);

                var base64Encrypted = Convert.ToBase64String(encryptedData);
                
                if (_vsPersistence != null)
                {
                    await _vsPersistence.SaveSettingAsync(ApiSecretSettingName, base64Encrypted);
                    await _logger?.LogInfoAsync("API secret stored securely", "SecureCommunication");
                    return true;
                }
                else
                {
                    lock (_lockObject)
                    {
                        _inMemoryApiSecret = apiSecret;
                    }
                    await _logger?.LogWarningAsync("API secret stored in memory only", "SecureCommunication");
                    return true;
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Failed to store API secret", "SecureCommunication");
                return false;
            }
        }

        /// <summary>
        /// Retrieves stored credentials as a secure object
        /// </summary>
        public SecureCredentials GetSecureCredentials()
        {
            return new SecureCredentials
            {
                ApiKey = GetApiKey(),
                ApiSecret = GetApiSecret(),
                Endpoint = _settingsService.OllamaEndpoint
            };
        }

        #endregion

        #region Private Methods

        private string _inMemoryApiKey;
        private string _inMemoryApiSecret;

        private bool IsLocalEndpoint(Uri uri)
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1") ||
                   uri.Host.StartsWith("192.168.") ||
                   uri.Host.StartsWith("10.") ||
                   uri.Host.StartsWith("172.");
        }

        private string GetExtensionVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "1.0.0.0";
            }
            catch
            {
                return "1.0.0.0";
            }
        }

        private string GetApiSecret()
        {
            try
            {
                if (_vsPersistence != null)
                {
                    var encryptedBase64 = _vsPersistence.GetSettingAsync<string>(ApiSecretSettingName).Result;
                    if (!string.IsNullOrEmpty(encryptedBase64))
                    {
                        var encryptedData = Convert.FromBase64String(encryptedBase64);
                        var decryptedData = ProtectedData.Unprotect(encryptedData, _entropy, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(decryptedData);
                    }
                }

                lock (_lockObject)
                {
                    return _inMemoryApiSecret;
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> ClearApiSecretAsync()
        {
            try
            {
                if (_vsPersistence != null)
                {
                    await _vsPersistence.DeleteSettingAsync(ApiSecretSettingName);
                }

                lock (_lockObject)
                {
                    _inMemoryApiSecret = null;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private string CreateRequestSignature(string method, string path, string timestamp, string nonce, string payload, string secret)
        {
            // Create string to sign
            var dataToSign = $"{method}\n{path}\n{timestamp}\n{nonce}\n{payload ?? ""}";
            
            // Create HMAC-SHA256 signature
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
                return Convert.ToBase64String(hash);
            }
        }

        #endregion
    }

    /// <summary>
    /// Container for secure credentials
    /// </summary>
    public class SecureCredentials
    {
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string Endpoint { get; set; }
        
        /// <summary>
        /// Clears sensitive data from memory
        /// </summary>
        public void Clear()
        {
            ApiKey = null;
            ApiSecret = null;
            Endpoint = null;
        }
    }

    /// <summary>
    /// Custom security exception
    /// </summary>
    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
        public SecurityException(string message, Exception innerException) : base(message, innerException) { }
    }
}