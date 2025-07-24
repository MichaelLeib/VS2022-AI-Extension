using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// HTTP client for communicating with Ollama API with retry logic and connection pooling
    /// </summary>
    public class OllamaHttpClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _connectionSemaphore;
        private readonly OllamaHttpClientConfig _config;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public OllamaHttpClient(string baseUrl, OllamaHttpClientConfig config = null)
        {
            _config = config ?? new OllamaHttpClientConfig();
            
            // Configure HttpClientHandler for connection pooling and performance
            var handler = new HttpClientHandler()
            {
                MaxConnectionsPerServer = _config.MaxConnectionsPerServer,
                UseCookies = false,
                UseProxy = false
            };

            // Configure automatic decompression
            if (handler.SupportsAutomaticDecompression)
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            }

            // Enable SSL/TLS best practices
            if (baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
                {
                    // For production, implement proper certificate validation
                    // For now, accept valid certificates and self-signed for localhost
                    if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None)
                        return true;
                        
                    // Allow self-signed certificates for localhost
                    var uri = new Uri(baseUrl);
                    if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                        uri.Host.Equals("127.0.0.1"))
                    {
                        return true;
                    }
                    
                    return false;
                };
            }

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs)
            };

            // Configure default headers for better performance and identification
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OllamaAssistant/1.0");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            _httpClient.DefaultRequestHeaders.ConnectionClose = false; // Keep connections alive

            // Apply secure communication configuration if available
            //_secureCommunication?.ConfigureHttpClient(_httpClient);

            _connectionSemaphore = new SemaphoreSlim(_config.MaxConcurrentRequests, _config.MaxConcurrentRequests);
        }

        #region Public Methods

        /// <summary>
        /// Sends a completion request to Ollama with retry logic
        /// </summary>
        public async Task<OllamaResponse> SendCompletionAsync(OllamaRequest request, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaHttpClient));

            return await ExecuteWithRetryAsync(async () =>
            {
                await _connectionSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var json = JsonSerializer.Serialize(request, GetJsonOptions());
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    // Create request message for signing
                    var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/generate")
                    {
                        Content = content
                    };

                    // Sign request if secure communication is available
                    //_secureCommunication?.SignRequest(requestMessage, json);

                    var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        
                        // Determine if this is a retryable error
                        if (IsRetryableStatusCode(response.StatusCode))
                        {
                            throw new OllamaRetryableException($"Ollama API error ({response.StatusCode}): {error}");
                        }
                        else
                        {
                            throw new OllamaConnectionException($"Ollama API error ({response.StatusCode}): {error}");
                        }
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    // Handle streaming vs non-streaming responses
                    if (request.Stream)
                    {
                        return ParseStreamingResponse(responseContent);
                    }
                    else
                    {
                        return JsonSerializer.Deserialize<OllamaResponse>(responseContent, GetJsonOptions());
                    }
                }
                catch (HttpRequestException ex)
                {
                    // Check for specific network conditions for better error handling
                    if (IsNetworkUnavailable(ex))
                    {
                        throw new OllamaConnectionException($"Network is unavailable. Please check your internet connection: {ex.Message}", ex);
                    }
                    else if (IsServerUnavailable(ex))
                    {
                        throw new OllamaConnectionException($"Ollama server is not reachable. Please verify the server is running at {_httpClient.BaseAddress}: {ex.Message}", ex);
                    }
                    else
                    {
                        throw new OllamaRetryableException($"Network error connecting to Ollama: {ex.Message}", ex);
                    }
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || cancellationToken.IsCancellationRequested)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OllamaConnectionException("Request was cancelled", ex);
                    }
                    else
                    {
                        throw new OllamaRetryableException($"Ollama request timed out after {_config.TimeoutMs}ms. Consider increasing the timeout in settings.", ex);
                    }
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OllamaConnectionException("Request was cancelled", ex);
                }
                catch (JsonException ex)
                {
                    throw new OllamaModelException($"Invalid response from Ollama: {ex.Message}", ex);
                }
                finally
                {
                    _connectionSemaphore.Release();
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Checks if Ollama is available with basic health information
        /// </summary>
        public async Task<OllamaHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return new OllamaHealthStatus { IsAvailable = false, Error = "Client disposed", Timestamp = DateTime.UtcNow };

            var startTime = DateTime.UtcNow;
            
            try
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMilliseconds(_config.HealthCheckTimeoutMs));

                var response = await _httpClient.GetAsync("api/tags", cts.Token);
                var responseTime = DateTime.UtcNow - startTime;

                var healthStatus = new OllamaHealthStatus
                {
                    IsAvailable = response.IsSuccessStatusCode,
                    ResponseTimeMs = (int)responseTime.TotalMilliseconds,
                    StatusCode = response.StatusCode,
                    Timestamp = DateTime.UtcNow
                };

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    healthStatus.Error = string.IsNullOrEmpty(errorContent) 
                        ? $"HTTP {response.StatusCode}" 
                        : $"HTTP {response.StatusCode}: {errorContent}";
                }

                return healthStatus;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new OllamaHealthStatus 
                { 
                    IsAvailable = false, 
                    Error = "Health check cancelled by user",
                    ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || !cancellationToken.IsCancellationRequested)
            {
                return new OllamaHealthStatus
                {
                    IsAvailable = false,
                    Error = $"Health check timed out after {_config.HealthCheckTimeoutMs}ms",
                    ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (HttpRequestException ex)
            {
                var error = IsNetworkUnavailable(ex) 
                    ? "Network unavailable - check internet connection"
                    : IsServerUnavailable(ex)
                        ? $"Ollama server unavailable at {_httpClient.BaseAddress} - verify server is running"
                        : $"Network error: {ex.Message}";

                return new OllamaHealthStatus
                {
                    IsAvailable = false,
                    Error = error,
                    ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                return new OllamaHealthStatus
                {
                    IsAvailable = false,
                    Error = $"Unexpected error: {ex.Message}",
                    ResponseTimeMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Simple availability check for backward compatibility
        /// </summary>
        public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            var health = await CheckHealthAsync(cancellationToken);
            return health.IsAvailable;
        }

        /// <summary>
        /// Gets available models from Ollama
        /// </summary>
        public async Task<OllamaModelsResponse> GetModelsAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaHttpClient));

            try
            {
                var response = await _httpClient.GetAsync("api/tags", cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new OllamaConnectionException($"Failed to get models: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<OllamaModelsResponse>(content, GetJsonOptions());
            }
            catch (HttpRequestException ex)
            {
                if (IsNetworkUnavailable(ex))
                {
                    throw new OllamaConnectionException("Cannot retrieve models - network unavailable. Please check your internet connection.", ex);
                }
                else if (IsServerUnavailable(ex))
                {
                    throw new OllamaConnectionException($"Cannot retrieve models - Ollama server unavailable at {_httpClient.BaseAddress}. Please verify the server is running.", ex);
                }
                else
                {
                    throw new OllamaConnectionException($"Network error getting models: {ex.Message}", ex);
                }
            }
            catch (JsonException ex)
            {
                throw new OllamaModelException($"Invalid models response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Gets information about Ollama version
        /// </summary>
        public async Task<OllamaVersionResponse> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaHttpClient));

            try
            {
                var response = await _httpClient.GetAsync("api/version", cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    throw new OllamaConnectionException($"Failed to get version: {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<OllamaVersionResponse>(content, GetJsonOptions());
            }
            catch (HttpRequestException ex)
            {
                if (IsNetworkUnavailable(ex))
                {
                    throw new OllamaConnectionException("Cannot retrieve version - network unavailable. Please check your internet connection.", ex);
                }
                else if (IsServerUnavailable(ex))
                {
                    throw new OllamaConnectionException($"Cannot retrieve version - Ollama server unavailable at {_httpClient.BaseAddress}. Please verify the server is running.", ex);
                }
                else
                {
                    throw new OllamaConnectionException($"Network error getting version: {ex.Message}", ex);
                }
            }
            catch (JsonException ex)
            {
                throw new OllamaModelException($"Invalid version response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Sends a streaming completion request
        /// </summary>
        public async Task<List<OllamaStreamResponse>> SendStreamingCompletionAsync(
            OllamaRequest request, 
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OllamaHttpClient));

            request.Stream = true;
            
            await _connectionSemaphore.WaitAsync(cancellationToken);
            try
            {
                var json = JsonSerializer.Serialize(request, GetJsonOptions());
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Create request message for signing
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/generate")
                {
                    Content = content
                };

                // Sign request if secure communication is available
                //_secureCommunication?.SignRequest(requestMessage, json);

                var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new OllamaConnectionException($"Ollama API error ({response.StatusCode}): {error}");
                }

                var stream = await response.Content.ReadAsStreamAsync();
                var reader = new System.IO.StreamReader(stream);

                string line;
                List<OllamaStreamResponse> responses = new List<OllamaStreamResponse>();
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        OllamaStreamResponse streamResponse;
                        try
                        {
                            streamResponse = JsonSerializer.Deserialize<OllamaStreamResponse>(line, GetJsonOptions());
                        }
                        catch (JsonException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to parse streaming response: {ex.Message}");
                            continue;
                        }

                        responses.Add(streamResponse);

                        if (streamResponse.Done)
                            break;
                    }
                }

                return responses;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        /// <summary>
        /// Updates the base URL for the Ollama server
        /// </summary>
        public void UpdateBaseUrl(string baseUrl)
        {
            lock (_lockObject)
            {
                if (!_disposed)
                {
                    _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                }
            }
        }

        /// <summary>
        /// Updates the request timeout
        /// </summary>
        public void UpdateTimeout(int timeoutMs)
        {
            lock (_lockObject)
            {
                if (!_disposed)
                {
                    _httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Executes an operation with exponential backoff retry logic
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromMilliseconds(_config.BaseRetryDelayMs);
            
            for (int attempt = 0; attempt <= _config.MaxRetryAttempts; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (OllamaRetryableException) when (attempt < _config.MaxRetryAttempts)
                {
                    // Wait with exponential backoff
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(Math.Min(
                        delay.TotalMilliseconds * _config.RetryBackoffMultiplier, 
                        _config.MaxRetryDelayMs));
                }
                catch (OllamaRetryableException ex) when (attempt == _config.MaxRetryAttempts)
                {
                    // Final attempt failed, throw original exception
                    throw new OllamaConnectionException($"Request failed after {_config.MaxRetryAttempts + 1} attempts: {ex.Message}", ex);
                }
            }
            
            // This should never be reached, but compiler requires it
            throw new InvalidOperationException("Retry logic error");
        }

        /// <summary>
        /// Determines if an HTTP status code indicates a retryable error
        /// </summary>
        private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            switch (statusCode)
            {
                case HttpStatusCode.InternalServerError:
                case HttpStatusCode.BadGateway:
                case HttpStatusCode.ServiceUnavailable:
                case HttpStatusCode.GatewayTimeout:
                case HttpStatusCode.RequestTimeout:
                case HttpStatusCode.TooManyRequests:
                case HttpStatusCode.InsufficientStorage:
                case HttpStatusCode.NotExtended:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Determines if the exception indicates network is completely unavailable
        /// </summary>
        private static bool IsNetworkUnavailable(HttpRequestException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("no such host") ||
                   message.Contains("network is unreachable") ||
                   message.Contains("no route to host") ||
                   message.Contains("name resolution failed") ||
                   message.Contains("dns") ||
                   ex.Data.Contains("NetworkUnavailable");
        }

        /// <summary>
        /// Determines if the exception indicates the Ollama server is unavailable
        /// </summary>
        private static bool IsServerUnavailable(HttpRequestException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("connection refused") ||
                   message.Contains("connection reset") ||
                   message.Contains("connection aborted") ||
                   message.Contains("connection timed out") ||
                   message.Contains("actively refused") ||
                   message.Contains("target machine actively refused");
        }

        private JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }

        private OllamaResponse ParseStreamingResponse(string content)
        {
            // For streaming responses, we get multiple JSON objects separated by newlines
            // We need to combine them into a single response
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var responses = new List<OllamaStreamResponse>();

            foreach (var line in lines)
            {
                try
                {
                    var streamResponse = JsonSerializer.Deserialize<OllamaStreamResponse>(line, GetJsonOptions());
                    responses.Add(streamResponse);
                }
                catch (JsonException)
                {
                    // Skip invalid lines
                    continue;
                }
            }

            // Combine all responses into a single response
            var combinedResponse = new StringBuilder();
            var totalTokens = 0;
            var totalDuration = 0L;

            foreach (var response in responses)
            {
                if (!string.IsNullOrEmpty(response.Response))
                {
                    combinedResponse.Append(response.Response);
                }
                
                if (response.EvalCount.HasValue)
                    totalTokens += response.EvalCount.Value;
                
                if (response.TotalDuration.HasValue)
                    totalDuration = Math.Max(totalDuration, response.TotalDuration.Value);
            }

            return new OllamaResponse
            {
                Response = combinedResponse.ToString(),
                Done = true,
                TotalDuration = totalDuration,
                EvalCount = totalTokens
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            _httpClient?.Dispose();
            _connectionSemaphore?.Dispose();
        }

        #endregion
    }

    #region Configuration Classes

    /// <summary>
    /// Configuration for OllamaHttpClient
    /// </summary>
    public class OllamaHttpClientConfig
    {
        /// <summary>
        /// Request timeout in milliseconds
        /// </summary>
        public int TimeoutMs { get; set; } = 30000;

        /// <summary>
        /// Health check timeout in milliseconds
        /// </summary>
        public int HealthCheckTimeoutMs { get; set; } = 5000;

        /// <summary>
        /// Maximum number of concurrent requests
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 3;

        /// <summary>
        /// Maximum connections per server
        /// </summary>
        public int MaxConnectionsPerServer { get; set; } = 10;

        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Base delay for retry attempts in milliseconds
        /// </summary>
        public int BaseRetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Maximum delay for retry attempts in milliseconds
        /// </summary>
        public int MaxRetryDelayMs { get; set; } = 10000;

        /// <summary>
        /// Multiplier for exponential backoff
        /// </summary>
        public double RetryBackoffMultiplier { get; set; } = 2.0;
    }

    /// <summary>
    /// Health status information for Ollama server
    /// </summary>
    public class OllamaHealthStatus
    {
        /// <summary>
        /// Whether the server is available
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// Response time in milliseconds
        /// </summary>
        public int ResponseTimeMs { get; set; }

        /// <summary>
        /// HTTP status code from health check
        /// </summary>
        public HttpStatusCode? StatusCode { get; set; }

        /// <summary>
        /// Error message if not available
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Timestamp of health check
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    #endregion

    #region Data Models

    /// <summary>
    /// Request object for Ollama API
    /// </summary>
    public class OllamaRequest
    {
        public string Model { get; set; }
        public string Prompt { get; set; }
        public string System { get; set; }
        public OllamaOptions Options { get; set; }
        public bool Stream { get; set; } = false;
        public string Context { get; set; }
    }

    /// <summary>
    /// Options for Ollama requests
    /// </summary>
    public class OllamaOptions
    {
        public double? Temperature { get; set; }
        public int? TopK { get; set; }
        public double? TopP { get; set; }
        public int? NumPredict { get; set; }
        public string[] Stop { get; set; }
    }

    /// <summary>
    /// Response from Ollama API
    /// </summary>
    public class OllamaResponse
    {
        public string Model { get; set; }
        public string Response { get; set; }
        public bool Done { get; set; }
        public string Context { get; set; }
        public long? TotalDuration { get; set; }
        public long? LoadDuration { get; set; }
        public int? PromptEvalCount { get; set; }
        public long? PromptEvalDuration { get; set; }
        public int? EvalCount { get; set; }
        public long? EvalDuration { get; set; }
    }

    /// <summary>
    /// Streaming response from Ollama API
    /// </summary>
    public class OllamaStreamResponse
    {
        public string Model { get; set; }
        public string Response { get; set; }
        public bool Done { get; set; }
        public string Context { get; set; }
        public long? TotalDuration { get; set; }
        public long? LoadDuration { get; set; }
        public int? PromptEvalCount { get; set; }
        public long? PromptEvalDuration { get; set; }
        public int? EvalCount { get; set; }
        public long? EvalDuration { get; set; }
    }

    /// <summary>
    /// Response from Ollama models API
    /// </summary>
    public class OllamaModelsResponse
    {
        public OllamaModelInfo[] Models { get; set; }
    }

    /// <summary>
    /// Information about an Ollama model
    /// </summary>
    public class OllamaModelInfo
    {
        public string Name { get; set; }
        public string ModifiedAt { get; set; }
        public long Size { get; set; }
        public string Digest { get; set; }
    }

    /// <summary>
    /// Response from Ollama version API
    /// </summary>
    public class OllamaVersionResponse
    {
        public string Version { get; set; }
    }

    #endregion

    #region Exception Classes

    /// <summary>
    /// Base exception for Ollama-related errors
    /// </summary>
    public class OllamaExtensionException : Exception
    {
        public OllamaExtensionException(string message) : base(message) { }
        public OllamaExtensionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception for Ollama connection errors
    /// </summary>
    public class OllamaConnectionException : OllamaExtensionException
    {
        public OllamaConnectionException(string message) : base(message) { }
        public OllamaConnectionException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception for retryable Ollama errors
    /// </summary>
    public class OllamaRetryableException : OllamaExtensionException
    {
        public OllamaRetryableException(string message) : base(message) { }
        public OllamaRetryableException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception for Ollama model errors
    /// </summary>
    public class OllamaModelException : OllamaExtensionException
    {
        public OllamaModelException(string message) : base(message) { }
        public OllamaModelException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception for context capture errors
    /// </summary>
    public class ContextCaptureException : OllamaExtensionException
    {
        public ContextCaptureException(string message) : base(message) { }
        public ContextCaptureException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Exception for suggestion processing errors
    /// </summary>
    public class SuggestionProcessingException : OllamaExtensionException
    {
        public SuggestionProcessingException(string message) : base(message) { }
        public SuggestionProcessingException(string message, Exception innerException) : base(message, innerException) { }
    }

    #endregion
}
