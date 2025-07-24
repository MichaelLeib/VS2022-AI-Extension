using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Services;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of IOllamaService for communicating with Ollama AI
    /// </summary>
    public class OllamaService : IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private readonly ErrorHandler _errorHandler;
        private readonly SecurityValidator _securityValidator;
        private OllamaHttpClient _httpClient;
        private readonly object _lockObject = new object();
        private bool _disposed;
        
        // Reference to connection manager (set by orchestrator)
        private IOllamaConnectionManager _connectionManager;

        public OllamaService(
            ISettingsService settingsService, 
            ILogger logger = null, 
            ErrorHandler errorHandler = null)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger;
            _errorHandler = errorHandler;
            _securityValidator = new SecurityValidator(_settingsService.MaxRequestSizeKB);
            
            InitializeHttpClient();
            
            // Subscribe to settings changes
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        #region Properties

        public string Endpoint => _settingsService.OllamaEndpoint;

        #endregion

        #region Public Methods

        public async Task<string> GetCompletionAsync(string prompt, string context, List<CursorHistoryEntry> history, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return string.Empty;

            return await _errorHandler?.ExecuteWithErrorHandlingAsync(async () =>
            {
                await _logger?.LogDebugAsync($"Getting completion for prompt: {prompt?.Substring(0, Math.Min(50, prompt.Length))}...", "OllamaService");
                
                var enhancedPrompt = BuildEnhancedPrompt(prompt, context, history);
                var request = CreateCompletionRequest(enhancedPrompt, context);

                var response = await _httpClient.SendCompletionAsync(request, cancellationToken);
                
                var result = response?.Response ?? string.Empty;
                await _logger?.LogDebugAsync($"Completion received: {result?.Length ?? 0} characters", "OllamaService");
                
                return result;
            }, "GetCompletion") ?? await GetCompletionFallbackAsync(prompt, context, history, cancellationToken);
        }

        private async Task<string> GetCompletionFallbackAsync(string prompt, string context, List<CursorHistoryEntry> history, CancellationToken cancellationToken)
        {
            // Check if we should even attempt a connection
            if (_connectionManager != null && !_connectionManager.ShouldAttemptConnection())
            {
                await _logger?.LogDebugAsync("Skipping completion request - system in offline mode", "OllamaService");
                return string.Empty;
            }

            try
            {
                var enhancedPrompt = BuildEnhancedPrompt(prompt, context, history);
                var request = CreateCompletionRequest(enhancedPrompt, context);

                var response = await _httpClient.SendCompletionAsync(request, cancellationToken);
                
                return response?.Response ?? string.Empty;
            }
            catch (OllamaConnectionException ex) when (ex.Message.Contains("network unavailable"))
            {
                await _logger?.LogWarningAsync($"Network unavailable for completion request: {ex.Message}", "OllamaService");
                await _connectionManager?.HandleServerUnavailableAsync("completion request - network unavailable");
                return string.Empty;
            }
            catch (OllamaConnectionException ex) when (ex.Message.Contains("server unavailable"))
            {
                await _logger?.LogWarningAsync($"Ollama server unavailable for completion request: {ex.Message}", "OllamaService");
                await _connectionManager?.HandleServerUnavailableAsync("completion request - server unavailable");
                return string.Empty;
            }
            catch (OllamaConnectionException ex)
            {
                await _logger?.LogWarningAsync($"Ollama connection error: {ex.Message}", "OllamaService");
                await _connectionManager?.HandleServerUnavailableAsync("completion request");
                return string.Empty;
            }
            catch (OllamaModelException ex)
            {
                await _logger?.LogWarningAsync($"Ollama model error: {ex.Message}", "OllamaService");
                // Model errors don't affect connection status
                return string.Empty;
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                await _logger?.LogDebugAsync("Completion request cancelled by user", "OllamaService");
                return string.Empty;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Unexpected error in GetCompletionAsync", "OllamaService");
                await _connectionManager?.HandleServerUnavailableAsync("completion request - unexpected error");
                return string.Empty;
            }
        }

        public async Task<CodeSuggestion> GetCodeSuggestionAsync(CodeContext codeContext, CancellationToken cancellationToken)
        {
            if (codeContext == null)
                return new CodeSuggestion();

            var requestStart = DateTime.UtcNow;
            var identifier = GetRequestIdentifier(codeContext);
            var success = false;

            try
            {
                // Check if we should even attempt a connection
                if (_connectionManager != null && !_connectionManager.ShouldAttemptConnection())
                {
                    await _logger?.LogDebugAsync("Skipping code suggestion request - system in offline mode", "OllamaService");
                    return new CodeSuggestion { Text = "", Description = "Offline - AI suggestions unavailable" };
                }

                try
                {
                    // Security validation
                    if (_settingsService.FilterSensitiveData)
                    {
                        var securityValidationResult = _securityValidator.ValidateCodeContext(codeContext);
                        if (!securityValidationResult.IsValid)
                        {
                            await _logger?.LogWarningAsync($"Code context failed security validation: {securityValidationResult.ErrorMessage}", "OllamaService");
                            return new CodeSuggestion();
                        }
                    }

                    var prompt = BuildCodeCompletionPrompt(codeContext);
                    var contextString = BuildContextString(codeContext);

                    // Sanitize input if enabled
                    if (_settingsService.FilterSensitiveData)
                    {
                        prompt = _securityValidator.SanitizeInput(prompt);
                        contextString = _securityValidator.SanitizeInput(contextString);
                    }

                    // Validate request size
                    var fullRequest = prompt + contextString;
                    if (!_securityValidator.IsRequestSizeValid(fullRequest))
                    {
                        await _logger?.LogWarningAsync("Request exceeds maximum size limit", "OllamaService");
                        return new CodeSuggestion();
                    }

                    var request = CreateCompletionRequest(prompt, contextString);

                    var response = await _httpClient.SendCompletionAsync(request, cancellationToken);

                    // Validate AI response before processing
                    var validationResult = ValidateAIResponse(response);
                    if (!validationResult.IsValid)
                    {
                        await _logger?.LogWarningAsync($"AI response validation failed: {validationResult.ErrorMessage}", "OllamaService");

                        // Try fallback suggestion if available
                        var fallbackSuggestion = GenerateFallbackSuggestion(codeContext, validationResult.ErrorType);
                        if (fallbackSuggestion != null)
                        {
                            await _logger?.LogDebugAsync("Using fallback suggestion due to invalid AI response", "OllamaService");
                            return fallbackSuggestion;
                        }

                        return new CodeSuggestion { Text = "", Description = $"Invalid AI response: {validationResult.ErrorMessage}" };
                    }

                    var suggestion = ParseCodeSuggestion(response.Response, codeContext);

                    // Additional validation of the parsed suggestion
                    var suggestionValidationResult = ValidateCodeSuggestion(suggestion, codeContext);
                    if (!suggestionValidationResult.IsValid)
                    {
                        await _logger?.LogWarningAsync($"Code suggestion validation failed: {suggestionValidationResult.ErrorMessage}", "OllamaService");

                        // Try to fix the suggestion or provide fallback
                        var correctedSuggestion = AttemptSuggestionCorrection(suggestion, codeContext, suggestionValidationResult);
                        if (correctedSuggestion != null)
                        {
                            await _logger?.LogDebugAsync("Applied correction to invalid suggestion", "OllamaService");
                            return correctedSuggestion;
                        }

                        var fallbackSuggestion = GenerateFallbackSuggestion(codeContext, suggestionValidationResult.ErrorType);
                        if (fallbackSuggestion != null)
                        {
                            return fallbackSuggestion;
                        }

                        return new CodeSuggestion { Text = "", Description = $"Invalid suggestion: {suggestionValidationResult.ErrorMessage}" };
                    }

                    // Validate the suggestion for security
                    if (_settingsService.FilterSensitiveData)
                    {
                        var suggestionValidation = _securityValidator.ValidateSuggestion(suggestion);
                        if (!suggestionValidation.IsValid)
                        {
                            await _logger?.LogWarningAsync($"AI suggestion failed security validation: {suggestionValidation.ErrorMessage}", "OllamaService");
                            return new CodeSuggestion();
                        }
                    }

                    success = true;
                    return suggestion;
                }
                catch (OllamaConnectionException ex) when (ex.Message.Contains("network unavailable"))
                {
                    await _logger?.LogWarningAsync($"Network unavailable for code suggestion: {ex.Message}", "OllamaService");
                    await _connectionManager?.HandleServerUnavailableAsync("code suggestion - network unavailable");
                    return new CodeSuggestion { Text = "", Description = "Network unavailable - check internet connection" };
                }
                catch (OllamaConnectionException ex) when (ex.Message.Contains("server unavailable"))
                {
                    await _logger?.LogWarningAsync($"Ollama server unavailable for code suggestion: {ex.Message}", "OllamaService");
                    await _connectionManager?.HandleServerUnavailableAsync("code suggestion - server unavailable");
                    return new CodeSuggestion { Text = "", Description = "Ollama server unavailable - verify server is running" };
                }
                catch (OllamaConnectionException ex)
                {
                    await _logger?.LogWarningAsync($"Connection error getting code suggestion: {ex.Message}", "OllamaService");
                    await _connectionManager?.HandleServerUnavailableAsync("code suggestion");
                    return new CodeSuggestion { Text = "", Description = "Connection error - AI suggestions unavailable" };
                }
                catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    await _logger?.LogDebugAsync("Code suggestion request cancelled by user", "OllamaService");
                    return new CodeSuggestion { Text = "", Description = "Request cancelled" };
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Unexpected error getting code suggestion", "OllamaService");
                    return new CodeSuggestion { Text = "", Description = "Error - AI suggestions temporarily unavailable" };
                }
                finally
                {
                    var elapsed = DateTime.UtcNow - requestStart;
                    await _logger?.LogDebugAsync($"Code suggestion request completed in {elapsed.TotalMilliseconds} ms (Success: {success})", "OllamaService");
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Unexpected error in GetCodeSuggestionAsync", "OllamaService");
                return new CodeSuggestion { Text = "", Description = "Error - AI suggestions temporarily unavailable" };
            }   
        }

        public async Task<JumpRecommendation> GetJumpRecommendationAsync(CodeContext codeContext, CancellationToken cancellationToken)
        {
            if (codeContext == null)
                return new JumpRecommendation { Direction = JumpDirection.None };

            try
            {
                var prompt = BuildJumpAnalysisPrompt(codeContext);
                var contextString = BuildContextString(codeContext);
                var request = CreateCompletionRequest(prompt, contextString);

                var response = await _httpClient.SendCompletionAsync(request, cancellationToken);
                
                if (response?.Response == null)
                    return new JumpRecommendation { Direction = JumpDirection.None };

                return ParseJumpRecommendation(response.Response, codeContext);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting jump recommendation: {ex.Message}");
                return new JumpRecommendation { Direction = JumpDirection.None };
            }
        }

        /// <summary>
        /// Gets a streaming completion from Ollama
        /// </summary>
        public async Task<List<string>> GetStreamingCompletionAsync(
            string prompt, 
            string context, 
            List<CursorHistoryEntry> history, 
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return new List<string>();

            var enhancedPrompt = BuildEnhancedPrompt(prompt, context, history);
            var request = CreateCompletionRequest(enhancedPrompt, context);
            request.Stream = true;

            var streamingResponses = await _httpClient.SendStreamingCompletionAsync(request, cancellationToken);
            var results = new List<string>();

            foreach (var response in streamingResponses)
            {
                if (!string.IsNullOrEmpty(response.Response))
                {
                    results.Add(response.Response);
                }

                if (response.Done)
                    break;
            }

            return results;
        }

        /// <summary>
        /// Gets a streaming code suggestion from Ollama
        /// </summary>
        public async Task<List<CodeSuggestion>> GetStreamingCodeSuggestionAsync(
            CodeContext codeContext, 
            CancellationToken cancellationToken = default)
        {
            if (codeContext == null)
                return new List<CodeSuggestion>();

            var prompt = BuildCodeCompletionPrompt(codeContext);
            var contextString = BuildContextString(codeContext);
            var request = CreateCompletionRequest(prompt, contextString);
            request.Stream = true;

            var accumulatedResponse = new StringBuilder();
            var results = new List<CodeSuggestion>();

            foreach (var response in await _httpClient.SendStreamingCompletionAsync(request, cancellationToken))
            {
                if (!string.IsNullOrEmpty(response.Response))
                {
                    accumulatedResponse.Append(response.Response);
                    
                    // Parse partial suggestion
                    var partialSuggestion = ParseCodeSuggestion(accumulatedResponse.ToString(), codeContext);
                    partialSuggestion.IsPartial = !response.Done;
                    
                    results.Add(partialSuggestion);
                }

                if (response.Done)
                    break;
            }

            return results;
        }

        public async Task<bool> IsAvailableAsync()
        {
            // Quick check for offline mode
            if (_connectionManager != null && !_connectionManager.ShouldAttemptConnection())
            {
                return false;
            }

            try
            {
                return await _httpClient.IsAvailableAsync();
            }
            catch (OllamaConnectionException ex)
            {
                await _logger?.LogDebugAsync($"Availability check failed: {ex.Message}", "OllamaService");
                await _connectionManager?.HandleServerUnavailableAsync("availability check");
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets detailed health status of the Ollama server
        /// </summary>
        public async Task<OllamaHealthStatus> GetHealthStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await _httpClient.CheckHealthAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error checking Ollama health status", "OllamaService");
                return new OllamaHealthStatus 
                { 
                    IsAvailable = false, 
                    Error = ex.Message 
                };
            }
        }

        public async Task<ModelInfo> GetModelInfoAsync()
        {
            try
            {
                var modelsResponse = await _httpClient.GetModelsAsync();
                var versionResponse = await _httpClient.GetVersionAsync();
                
                var currentModel = modelsResponse?.Models?.FirstOrDefault(m => 
                    m.Name.Contains(_settingsService.OllamaModel));

                return new ModelInfo
                {
                    Name = _settingsService.OllamaModel,
                    Version = versionResponse?.Version ?? "unknown",
                    Description = currentModel != null ? $"Size: {FormatSize(currentModel.Size)}" : "Model information unavailable",
                    MaxContextSize = EstimateContextSize(_settingsService.OllamaModel),
                    SupportsCodeCompletion = IsCodeModel(_settingsService.OllamaModel),
                    ParameterCount = EstimateParameterCount(_settingsService.OllamaModel)
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting model info: {ex.Message}");
                return new ModelInfo
                {
                    Name = _settingsService.OllamaModel,
                    Version = "unknown",
                    Description = "Model information unavailable",
                    MaxContextSize = 2048,
                    SupportsCodeCompletion = true,
                    ParameterCount = "unknown"
                };
            }
        }

        public async Task SetModelAsync(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return;

            _settingsService.OllamaModel = modelName;
            _settingsService.SaveSettings();
            await _logger?.LogInfoAsync($"Model changed to: {modelName}", "OllamaService");
        }

        /// <summary>
        /// Gets list of available models from Ollama server
        /// </summary>
        public async Task<IEnumerable<OllamaModelInfo>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetModelsAsync(cancellationToken);
                return response?.Models ?? Enumerable.Empty<OllamaModelInfo>();
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting available models", "OllamaService");
                return Enumerable.Empty<OllamaModelInfo>();
            }
        }

        /// <summary>
        /// Gets the currently selected model with detailed information
        /// </summary>
        public async Task<OllamaModelInfo> GetCurrentModelAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var models = await GetAvailableModelsAsync(cancellationToken);
                return models.FirstOrDefault(m => m.Name.Equals(_settingsService.OllamaModel, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error getting current model info", "OllamaService");
                return null;
            }
        }

        /// <summary>
        /// Validates if a model is suitable for code completion
        /// </summary>
        public async Task<bool> IsModelSuitableForCodeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return false;

            try
            {
                // Check if it's a known code model
                if (IsCodeModel(modelName))
                    return true;

                // Test the model with a simple code completion prompt
                var testRequest = new OllamaRequest
                {
                    Model = modelName,
                    Prompt = "Complete this C# function: public int Add(int a, int b) {",
                    Options = new OllamaOptions { NumPredict = 50 }
                };

                var response = await _httpClient.SendCompletionAsync(testRequest, cancellationToken);
                var suggestion = response?.Response?.Trim();

                // Check if the response looks like code
                return !string.IsNullOrEmpty(suggestion) && 
                       (suggestion.Contains("return") || suggestion.Contains(";") || suggestion.Contains("}"));
            }
            catch (Exception ex)
            {
                await _logger?.LogWarningAsync($"Error testing model {modelName}: {ex.Message}", "OllamaService");
                return false;
            }
        }

        public void SetEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || endpoint == _settingsService.OllamaEndpoint)
                return;

            _settingsService.OllamaEndpoint = endpoint;
            _settingsService.SaveSettings();
            
            // Update HTTP client with new endpoint
            InitializeHttpClient();
            
            _logger?.LogInfoAsync($"Endpoint changed to: {endpoint}", "OllamaService");
        }

        /// <summary>
        /// Sets the connection manager reference (called by orchestrator)
        /// </summary>
        public void SetConnectionManager(IOllamaConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        #endregion

        #region AI Response Validation

        /// <summary>
        /// Validates the raw response from Ollama AI
        /// </summary>
        private AIResponseValidationResult ValidateAIResponse(OllamaResponse response)
        {
            if (response == null)
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Null response from AI",
                    ErrorType = ValidationErrorType.NullResponse
                };
            }

            if (string.IsNullOrWhiteSpace(response.Response))
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Empty response from AI",
                    ErrorType = ValidationErrorType.EmptyResponse
                };
            }

            // Check for response length limits
            if (response.Response.Length > 5000) // Reasonable limit for code suggestions
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "AI response exceeds maximum length",
                    ErrorType = ValidationErrorType.ResponseTooLong
                };
            }

            // Check for common AI artifacts that indicate malformed responses
            var responseText = response.Response.Trim();
            var malformedIndicators = new[]
            {
                "I'm sorry, I can't",
                "I cannot",
                "I don't understand",
                "Error:",
                "Exception:",
                "<error>",
                "[ERROR]",
                "FAILED",
                "NULL",
                "undefined"
            };

            foreach (var indicator in malformedIndicators)
            {
                if (responseText.Contains(indicator))
                {
                    return new AIResponseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"AI response contains error indicator: {indicator}",
                        ErrorType = ValidationErrorType.ContainsErrorIndicators
                    };
                }
            }

            // Check for incomplete JSON or malformed structured data
            if (responseText.StartsWith("{") && !responseText.EndsWith("}"))
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "AI response appears to be incomplete JSON",
                    ErrorType = ValidationErrorType.IncompleteStructuredData
                };
            }

            //// Check for excessive repetition (indicates model stuck in loop)
            //if (HasExcessiveRepetition(responseText))
            //{
            //    return new AIResponseValidationResult
            //    {
            //        IsValid = false,
            //        ErrorMessage = "AI response contains excessive repetition",
            //        ErrorType = ValidationErrorType.ExcessiveRepetition
            //    };
            //}

            return new AIResponseValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates a parsed code suggestion
        /// </summary>
        private AIResponseValidationResult ValidateCodeSuggestion(CodeSuggestion suggestion, CodeContext codeContext)
        {
            if (suggestion == null)
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Null code suggestion",
                    ErrorType = ValidationErrorType.NullResponse
                };
            }

            if (string.IsNullOrWhiteSpace(suggestion.Text))
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Empty code suggestion",
                    ErrorType = ValidationErrorType.EmptyResponse
                };
            }

            // Check for suspicious content that shouldn't be in code
            var suspiciousPatterns = new[]
            {
                @"\b(password|secret|key|token)\s*=\s*[""']\w+[""']", // Hardcoded secrets
                @"\b(admin|root)\s*[""']?", // Admin credentials
                @"[""'][^""']*\.(exe|bat|cmd|sh)[""']", // Executable references
                @"\b(rm|del|delete|drop)\s+\*", // Dangerous operations
                @"\beval\s*\(", // Code injection risks
            };

            foreach (var pattern in suspiciousPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(suggestion.Text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return new AIResponseValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Code suggestion contains potentially unsafe content",
                        ErrorType = ValidationErrorType.UnsafeContent
                    };
                }
            }

            // Language-specific validation
            if (!string.IsNullOrEmpty(codeContext.Language))
            {
                var languageValidation = ValidateLanguageSpecificSyntax(suggestion.Text, codeContext.Language);
                if (!languageValidation.IsValid)
                {
                    return languageValidation;
                }
            }

            // Check suggestion relevance to context
            if (!IsSuggestionRelevant(suggestion, codeContext))
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Code suggestion is not relevant to the current context",
                    ErrorType = ValidationErrorType.IrrelevantContent
                };
            }

            return new AIResponseValidationResult { IsValid = true };
        }

        /// <summary>
        /// Validates language-specific syntax
        /// </summary>
        private AIResponseValidationResult ValidateLanguageSpecificSyntax(string code, string language)
        {
            var lang = language.ToLowerInvariant();
            
            switch (lang)
            {
                case "csharp":
                    return ValidateCSharpSyntax(code);
                case "javascript":
                case "typescript":
                    return ValidateJavaScriptSyntax(code);
                case "python":
                    return ValidatePythonSyntax(code);
                case "cpp":
                case "c":
                    return ValidateCppSyntax(code);
                default:
                    return new AIResponseValidationResult { IsValid = true }; // Skip validation for unknown languages
            }
        }

        private AIResponseValidationResult ValidateCSharpSyntax(string code)
        {
            // Basic C# syntax validation
            var issues = new List<string>();
            
            // Check for unmatched braces
            var braceCount = code.Count(c => c == '{') - code.Count(c => c == '}');
            if (braceCount != 0)
                issues.Add("Unmatched braces");
            
            // Check for unmatched parentheses
            var parenCount = code.Count(c => c == '(') - code.Count(c => c == ')');
            if (parenCount != 0)
                issues.Add("Unmatched parentheses");
            
            // Check for incomplete statements (simple heuristic)
            var lines = code.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && 
                    !trimmed.EndsWith(";") && 
                    !trimmed.EndsWith("{") && 
                    !trimmed.EndsWith("}") &&
                    !trimmed.StartsWith("//") &&
                    !trimmed.StartsWith("/*") &&
                    !trimmed.StartsWith("*") &&
                    !trimmed.Contains("=>"))
                {
                    // This might be an incomplete statement
                    if (trimmed.Contains(" ") && !trimmed.StartsWith("using") && !trimmed.StartsWith("namespace"))
                    {
                        issues.Add($"Potentially incomplete statement: {trimmed.Substring(0, Math.Min(30, trimmed.Length))}...");
                        break; // Only report first issue
                    }
                }
            }
            
            if (issues.Any())
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"C# syntax issues: {string.Join(", ", issues)}",
                    ErrorType = ValidationErrorType.SyntaxError
                };
            }
            
            return new AIResponseValidationResult { IsValid = true };
        }

        private AIResponseValidationResult ValidateJavaScriptSyntax(string code)
        {
            // Basic JavaScript syntax validation
            var issues = new List<string>();
            
            // Check for unmatched braces and parentheses
            var braceCount = code.Count(c => c == '{') - code.Count(c => c == '}');
            if (braceCount != 0)
                issues.Add("Unmatched braces");
                
            var parenCount = code.Count(c => c == '(') - code.Count(c => c == ')');
            if (parenCount != 0)
                issues.Add("Unmatched parentheses");
            
            if (issues.Any())
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"JavaScript syntax issues: {string.Join(", ", issues)}",
                    ErrorType = ValidationErrorType.SyntaxError
                };
            }
            
            return new AIResponseValidationResult { IsValid = true };
        }

        private AIResponseValidationResult ValidatePythonSyntax(string code)
        {
            // Basic Python syntax validation
            var issues = new List<string>();
            
            // Check for unmatched parentheses and brackets
            var parenCount = code.Count(c => c == '(') - code.Count(c => c == ')');
            if (parenCount != 0)
                issues.Add("Unmatched parentheses");
                
            var bracketCount = code.Count(c => c == '[') - code.Count(c => c == ']');
            if (bracketCount != 0)
                issues.Add("Unmatched brackets");
            
            // Check for mixed indentation (basic check)
            var lines = code.Split('\n');
            var hasSpaces = lines.Any(l => l.StartsWith("    "));
            var hasTabs = lines.Any(l => l.StartsWith("\t"));
            if (hasSpaces && hasTabs)
                issues.Add("Mixed indentation (spaces and tabs)");
            
            if (issues.Any())
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Python syntax issues: {string.Join(", ", issues)}",
                    ErrorType = ValidationErrorType.SyntaxError
                };
            }
            
            return new AIResponseValidationResult { IsValid = true };
        }

        private AIResponseValidationResult ValidateCppSyntax(string code)
        {
            // Basic C++ syntax validation
            var issues = new List<string>();
            
            // Check for unmatched braces and parentheses
            var braceCount = code.Count(c => c == '{') - code.Count(c => c == '}');
            if (braceCount != 0)
                issues.Add("Unmatched braces");
                
            var parenCount = code.Count(c => c == '(') - code.Count(c => c == ')');
            if (parenCount != 0)
                issues.Add("Unmatched parentheses");
            
            if (issues.Any())
            {
                return new AIResponseValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"C++ syntax issues: {string.Join(", ", issues)}",
                    ErrorType = ValidationErrorType.SyntaxError
                };
            }
            
            return new AIResponseValidationResult { IsValid = true };
        }

        /// <summary>
        /// Checks if a suggestion is relevant to the current context
        /// </summary>
        private bool IsSuggestionRelevant(CodeSuggestion suggestion, CodeContext codeContext)
        {
            if (string.IsNullOrEmpty(suggestion.Text) || codeContext == null)
                return false;

            var suggestionText = suggestion.Text.ToLowerInvariant();
            
            // Check if suggestion contains any words from the current context
            var contextWords = ExtractRelevantWords(codeContext.CurrentLine + " " + string.Join(" ", codeContext.PrecedingLines));
            var suggestionWords = ExtractRelevantWords(suggestionText);
            
            var commonWords = contextWords.Intersect(suggestionWords).Count();
            var relevanceRatio = contextWords.Any() ? (double)commonWords / contextWords.Count() : 0;
            
            // At least 10% word overlap or contains language-specific keywords
            return relevanceRatio >= 0.1 || ContainsLanguageKeywords(suggestionText, codeContext.Language);
        }

        private HashSet<string> ExtractRelevantWords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new HashSet<string>();
                
            var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\b[a-zA-Z_][a-zA-Z0-9_]*\b")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Where(w => w.Length > 2) // Ignore very short words
                .ToHashSet();
                
            return words;
        }

        private bool ContainsLanguageKeywords(string text, string language)
        {
            var keywords = GetLanguageKeywords(language?.ToLowerInvariant());
            return keywords.Any(keyword => text.Contains(keyword));
        }

        private string[] GetLanguageKeywords(string language)
        {
            switch (language)
            {
                case "csharp":
                    return new[] { "class", "interface", "public", "private", "void", "string", "int", "bool", "var", "new", "return", "if", "else", "for", "while", "foreach" };
                case "javascript":
                    return new[] { "function", "var", "let", "const", "return", "if", "else", "for", "while", "class", "new", "this", "async", "await" };
                case "python":
                    return new[] { "def", "class", "return", "if", "else", "elif", "for", "while", "import", "from", "as", "with", "try", "except" };
                case "cpp":
                case "c":
                    return new[] { "class", "struct", "public", "private", "void", "int", "char", "bool", "return", "if", "else", "for", "while", "include" };
                default:
                    return new string[0];
            }
        }

        /// <summary>
        /// Attempts to correct a suggestion that failed validation
        /// </summary>
        private CodeSuggestion AttemptSuggestionCorrection(CodeSuggestion suggestion, CodeContext codeContext, AIResponseValidationResult validationResult)
        {
            if (suggestion == null || string.IsNullOrEmpty(suggestion.Text))
                return null;

            var correctedText = suggestion.Text;
            
            switch (validationResult.ErrorType)
            {
                case ValidationErrorType.SyntaxError:
                    correctedText = AttemptSyntaxCorrection(correctedText, codeContext.Language);
                    break;
                    
                case ValidationErrorType.UnsafeContent:
                    // Remove unsafe content
                    correctedText = RemoveUnsafeContent(correctedText);
                    break;
                    
                case ValidationErrorType.IrrelevantContent:
                    // Try to extract relevant parts
                    correctedText = ExtractRelevantContent(correctedText, codeContext);
                    break;
                    
                default:
                    return null; // Can't correct this type of error
            }
            
            if (string.IsNullOrEmpty(correctedText) || correctedText == suggestion.Text)
                return null; // No correction possible or no change made
            
            return new CodeSuggestion
            {
                Text = correctedText,
                InsertionPoint = suggestion.InsertionPoint,
                Confidence = Math.Max(0.1, suggestion.Confidence - 0.2), // Lower confidence for corrected suggestions
                Type = suggestion.Type,
                Description = "AI suggestion (auto-corrected)",
                Priority = Math.Max(1, suggestion.Priority - 20),
                SourceContext = suggestion.SourceContext
            };
        }

        private string AttemptSyntaxCorrection(string code, string language)
        {
            // Basic syntax corrections
            var corrected = code;
            
            // Try to balance braces (simple approach)
            var openBraces = corrected.Count(c => c == '{');
            var closeBraces = corrected.Count(c => c == '}');
            
            if (openBraces > closeBraces)
            {
                corrected += new string('}', openBraces - closeBraces);
            }
            else if (closeBraces > openBraces)
            {
                corrected = new string('{', closeBraces - openBraces) + corrected;
            }
            
            // Try to balance parentheses (simple approach)
            var openParens = corrected.Count(c => c == '(');
            var closeParens = corrected.Count(c => c == ')');
            
            if (openParens > closeParens)
            {
                corrected += new string(')', openParens - closeParens);
            }
            else if (closeParens > openParens)
            {
                corrected = new string('(', closeParens - openParens) + corrected;
            }
            
            return corrected;
        }

        private string RemoveUnsafeContent(string code)
        {
            // Remove lines that contain potentially unsafe content
            var lines = code.Split('\n');
            var safelines = new List<string>();
            
            var unsafePatterns = new[]
            {
                @"\b(password|secret|key|token)\s*=\s*[""']\w+[""']",
                @"[""'][^""']*\.(exe|bat|cmd|sh)[""']",
                @"\b(rm|del|delete|drop)\s+\*",
                @"\beval\s*\("
            };
            
            foreach (var line in lines)
            {
                var isUnsafe = false;
                foreach (var pattern in unsafePatterns)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        isUnsafe = true;
                        break;
                    }
                }
                
                if (!isUnsafe)
                {
                    safelines.Add(line);
                }
            }
            
            return string.Join("\n", safelines);
        }

        private string ExtractRelevantContent(string code, CodeContext codeContext)
        {
            // Extract lines that seem most relevant to the current context
            var lines = code.Split('\n');
            var contextWords = ExtractRelevantWords(codeContext.CurrentLine + " " + string.Join(" ", codeContext.PrecedingLines));
            
            var relevantLines = new List<(string line, int relevance)>();
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var lineWords = ExtractRelevantWords(line);
                var commonWords = contextWords.Intersect(lineWords).Count();
                
                if (commonWords > 0 || ContainsLanguageKeywords(line.ToLowerInvariant(), codeContext.Language))
                {
                    relevantLines.Add((line, commonWords));
                }
            }
            
            // Return the most relevant lines
            return string.Join("\n", relevantLines.OrderByDescending(x => x.relevance).Take(5).Select(x => x.line));
        }

        /// <summary>
        /// Generates a fallback suggestion when AI response is invalid
        /// </summary>
        private CodeSuggestion GenerateFallbackSuggestion(CodeContext codeContext, ValidationErrorType errorType)
        {
            // Generate basic fallback suggestions based on context
            var fallbackText = GenerateBasicFallback(codeContext);
            
            if (string.IsNullOrEmpty(fallbackText))
                return null;
            
            return new CodeSuggestion
            {
                Text = fallbackText,
                InsertionPoint = codeContext.CaretPosition,
                Confidence = 0.3, // Low confidence for fallback
                Type = SuggestionType.General,
                Description = $"Fallback suggestion ({errorType})",
                Priority = 10, // Low priority
                SourceContext = "Fallback"
            };
        }

        private string GenerateBasicFallback(CodeContext codeContext)
        {
            if (codeContext == null || string.IsNullOrEmpty(codeContext.CurrentLine))
                return null;
            
            var currentLine = codeContext.CurrentLine.Trim();
            var language = codeContext.Language?.ToLowerInvariant();
            
            // Basic language-specific fallbacks
            switch (language)
            {
                case "csharp":
                    if (currentLine.EndsWith("{"))
                        return "    // TODO: Implement\n}";
                    if (currentLine.Contains("if (") && !currentLine.Contains(")"))
                        return ")\n{\n    // TODO: Implement\n}";
                    if (currentLine.EndsWith(";"))
                        return "";
                    return ";";
                        
                case "javascript":
                    if (currentLine.EndsWith("{"))
                        return "    // TODO: Implement\n}";
                    if (currentLine.Contains("function") && !currentLine.Contains("{"))
                        return " {\n    // TODO: Implement\n}";
                    return "";
                        
                case "python":
                    if (currentLine.EndsWith(":"))
                        return "    # TODO: Implement\n    pass";
                    return "";
                        
                default:
                    return null;
            }
        }

        #endregion

        #region Private Methods

        private void InitializeHttpClient()
        {
            lock (_lockObject)
            {
                _httpClient?.Dispose();
                
                var config = new OllamaHttpClientConfig
                {
                    TimeoutMs = _settingsService.OllamaTimeout,
                    HealthCheckTimeoutMs = _settingsService.OllamaTimeout / 6, // Shorter timeout for health checks
                    MaxConcurrentRequests = _settingsService.MaxConcurrentRequests,
                    MaxConnectionsPerServer = 10,
                    MaxRetryAttempts = _settingsService.MaxRetryAttempts,
                    BaseRetryDelayMs = 1000,
                    MaxRetryDelayMs = 10000,
                    RetryBackoffMultiplier = 2.0
                };
                
                _httpClient = new OllamaHttpClient(_settingsService.OllamaEndpoint, config);
            }
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.SettingName == nameof(ISettingsService.OllamaEndpoint) ||
                e.SettingName == nameof(ISettingsService.OllamaTimeout))
            {
                InitializeHttpClient();
            }
        }

        private string BuildEnhancedPrompt(string prompt, string context, List<CursorHistoryEntry> history)
        {
            var promptBuilder = new StringBuilder();
            
            // Add system context
            promptBuilder.AppendLine("You are a code completion assistant. Provide concise, relevant code suggestions.");
            promptBuilder.AppendLine();

            // Add cursor history if available
            if (history?.Any() == true)
            {
                promptBuilder.AppendLine("Recent cursor history:");
                foreach (var entry in history.Take(3))
                {
                    promptBuilder.AppendLine($"- {entry.FilePath}:{entry.LineNumber}: {entry.ContextSnippet}");
                }
                promptBuilder.AppendLine();
            }

            // Add current context
            if (!string.IsNullOrWhiteSpace(context))
            {
                promptBuilder.AppendLine("Current code context:");
                promptBuilder.AppendLine(context);
                promptBuilder.AppendLine();
            }

            // Add the actual prompt
            promptBuilder.AppendLine("Request:");
            promptBuilder.AppendLine(prompt);

            return promptBuilder.ToString();
        }

        private string BuildCodeCompletionPrompt(CodeContext codeContext)
        {
            var promptBuilder = new StringBuilder();
            
            // System message for code completion
            promptBuilder.AppendLine("You are a professional code completion assistant. Your task is to complete the code at the cursor position.");
            promptBuilder.AppendLine("Rules:");
            promptBuilder.AppendLine("- Provide ONLY the code completion, no explanations or comments");
            promptBuilder.AppendLine("- Maintain proper indentation and code style");
            promptBuilder.AppendLine("- Complete syntax must be valid for the target language");
            promptBuilder.AppendLine("- Prefer concise, readable solutions");
            promptBuilder.AppendLine();
            
            // Context information
            promptBuilder.AppendLine($"Language: {codeContext.Language}");
            promptBuilder.AppendLine($"File: {System.IO.Path.GetFileName(codeContext.FileName)}");
            
            if (!string.IsNullOrWhiteSpace(codeContext.CurrentScope))
            {
                promptBuilder.AppendLine($"Current scope: {codeContext.CurrentScope}");
            }

            // Add indentation info for consistency
            if (codeContext.Indentation != null)
            {
                var indentType = codeContext.Indentation.UsesSpaces ? "spaces" : "tabs";
                var indentSize = codeContext.Indentation.Level;
                promptBuilder.AppendLine($"Indentation: {indentSize} {indentType}");
            }

            promptBuilder.AppendLine();
            
            // Add cursor history context if available
            if (codeContext.CursorHistory?.Any() == true)
            {
                promptBuilder.AppendLine("Recent editing context:");
                foreach (var entry in codeContext.CursorHistory.Take(2))
                {
                    promptBuilder.AppendLine($"- {System.IO.Path.GetFileName(entry.FilePath)}:{entry.LineNumber} ({entry.ContextSnippet})");
                }
                promptBuilder.AppendLine();
            }

            promptBuilder.AppendLine("Code context:");
            promptBuilder.AppendLine("```" + codeContext.Language.ToLowerInvariant());
            
            // Add line numbers for better context
            var startLine = Math.Max(1, codeContext.LineNumber - codeContext.PrecedingLines.Length);
            
            // Add preceding lines with line numbers
            for (int i = 0; i < codeContext.PrecedingLines.Length; i++)
            {
                var lineNum = startLine + i;
                promptBuilder.AppendLine($"{lineNum:D3}: {codeContext.PrecedingLines[i]}");
            }
            
            // Add current line with cursor marker
            var currentLine = codeContext.CurrentLine;
            var caretPos = Math.Min(codeContext.CaretPosition, currentLine.Length);
            var beforeCursor = currentLine.Substring(0, caretPos);
            var afterCursor = currentLine.Substring(caretPos);
            var markedLine = beforeCursor + "<|CURSOR|>" + afterCursor;
            
            promptBuilder.AppendLine($"{codeContext.LineNumber:D3}: {markedLine}");
            
            // Add following lines with line numbers
            for (int i = 0; i < codeContext.FollowingLines.Length; i++)
            {
                var lineNum = codeContext.LineNumber + i + 1;
                promptBuilder.AppendLine($"{lineNum:D3}: {codeContext.FollowingLines[i]}");
            }
            
            promptBuilder.AppendLine("```");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Complete the code at the <|CURSOR|> position. Provide only the completion text:");

            return promptBuilder.ToString();
        }

        private string BuildJumpAnalysisPrompt(CodeContext codeContext)
        {
            var promptBuilder = new StringBuilder();
            
            promptBuilder.AppendLine("Analyze the code and suggest the next logical cursor position.");
            promptBuilder.AppendLine($"Language: {codeContext.Language}");
            promptBuilder.AppendLine($"Current line: {codeContext.LineNumber}");
            promptBuilder.AppendLine($"Current scope: {codeContext.CurrentScope}");
            promptBuilder.AppendLine();
            
            promptBuilder.AppendLine("Code context:");
            
            // Add code context
            for (int i = 0; i < codeContext.PrecedingLines.Length; i++)
            {
                var lineNum = codeContext.LineNumber - codeContext.PrecedingLines.Length + i;
                promptBuilder.AppendLine($"{lineNum}: {codeContext.PrecedingLines[i]}");
            }
            
            promptBuilder.AppendLine($"{codeContext.LineNumber}: {codeContext.CurrentLine} [CURSOR]");
            
            for (int i = 0; i < codeContext.FollowingLines.Length; i++)
            {
                var lineNum = codeContext.LineNumber + i + 1;
                promptBuilder.AppendLine($"{lineNum}: {codeContext.FollowingLines[i]}");
            }

            promptBuilder.AppendLine();
            promptBuilder.AppendLine("Respond with: LINE_NUMBER|COLUMN|REASON");
            promptBuilder.AppendLine("Example: 15|4|Jump to method body");

            return promptBuilder.ToString();
        }

        private string BuildContextString(CodeContext codeContext)
        {
            var contextBuilder = new StringBuilder();
            
            contextBuilder.AppendLine($"File: {codeContext.FileName}");
            contextBuilder.AppendLine($"Language: {codeContext.Language}");
            contextBuilder.AppendLine($"Line: {codeContext.LineNumber}");
            contextBuilder.AppendLine($"Scope: {codeContext.CurrentScope}");
            
            if (codeContext.CursorHistory?.Any() == true)
            {
                contextBuilder.AppendLine("History:");
                foreach (var entry in codeContext.CursorHistory.Take(3))
                {
                    contextBuilder.AppendLine($"  {entry.FilePath}:{entry.LineNumber} ({entry.ChangeType})");
                }
            }

            return contextBuilder.ToString();
        }

        private OllamaRequest CreateCompletionRequest(string prompt, string context = null)
        {
            var modelName = _settingsService.OllamaModel.ToLowerInvariant();
            
            // Model-specific parameter optimization
            var options = new OllamaOptions();
            
            if (IsCodeModel(modelName))
            {
                // Code-specialized models: more focused, deterministic
                options.Temperature = 0.1;
                options.TopK = 5;
                options.TopP = 0.8;
                options.NumPredict = 150;
                options.Stop = new[] { "\n\n", "```", "---", "// ", "/* " };
            }
            else if (modelName.Contains("instruct") || modelName.Contains("chat"))
            {
                // Instruction-tuned models: slightly more creative but still focused
                options.Temperature = 0.3;
                options.TopK = 15;
                options.TopP = 0.9;
                options.NumPredict = 200;
                options.Stop = new[] { "\n\n", "```", "---", "Human:", "Assistant:" };
            }
            else
            {
                // General models: balanced parameters
                options.Temperature = 0.2;
                options.TopK = 10;
                options.TopP = 0.9;
                options.NumPredict = 180;
                options.Stop = new[] { "\n\n", "```", "---" };
            }
            
            // Adjust parameters based on model size (if detectable)
            if (modelName.Contains("70b") || modelName.Contains("34b"))
            {
                // Larger models can handle more tokens and need less randomness
                options.Temperature *= 0.8;
                options.NumPredict = (int)(options.NumPredict * 1.3);
            }
            else if (modelName.Contains("1b") || modelName.Contains("3b"))
            {
                // Smaller models need more constraint
                options.Temperature *= 0.9;
                options.NumPredict = (int)(options.NumPredict * 0.8);
                options.TopK = Math.Max(3, (byte)options.TopK - 2);
            }
            
            return new OllamaRequest
            {
                Model = _settingsService.OllamaModel,
                Prompt = prompt,
                Context = context,
                Stream = false,
                Options = options
            };
        }

        private CodeSuggestion ParseCodeSuggestion(string response, CodeContext codeContext)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new CodeSuggestion();

            // Clean up the response
            var cleanedResponse = CleanAIResponse(response);
            
            if (string.IsNullOrWhiteSpace(cleanedResponse))
                return new CodeSuggestion();

            // Calculate confidence based on response quality
            var confidence = CalculateSuggestionConfidence(cleanedResponse, codeContext);

            return new CodeSuggestion
            {
                Text = cleanedResponse,
                InsertionPoint = codeContext.CaretPosition,
                Confidence = confidence,
                Type = DetermineSuggestionType(cleanedResponse, codeContext),
                Description = "AI-generated code completion",
                Priority = (int)(confidence * 100),
                SourceContext = codeContext.CurrentScope
            };
        }

        private JumpRecommendation ParseJumpRecommendation(string response, CodeContext codeContext)
        {
            if (string.IsNullOrWhiteSpace(response))
                return new JumpRecommendation { Direction = JumpDirection.None };

            // Try to parse the structured response
            var lines = response.Split('\n');
            
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 3)
                {
                    if (int.TryParse(parts[0].Trim(), out var lineNumber) &&
                        int.TryParse(parts[1].Trim(), out var column))
                    {
                        var reason = parts[2].Trim();
                        var direction = lineNumber > codeContext.LineNumber ? JumpDirection.Down : JumpDirection.Up;
                        
                        return new JumpRecommendation
                        {
                            TargetLine = lineNumber,
                            TargetColumn = column,
                            Direction = direction,
                            Reason = reason,
                            Confidence = 0.8,
                            Type = JumpType.NextLogicalPosition
                        };
                    }
                }
            }

            // Fallback: try to extract line number from natural language
            var match = System.Text.RegularExpressions.Regex.Match(response, @"line\s+(\d+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success && int.TryParse(match.Groups[1].Value, out var extractedLine))
            {
                var direction = extractedLine > codeContext.LineNumber ? JumpDirection.Down : JumpDirection.Up;
                
                return new JumpRecommendation
                {
                    TargetLine = extractedLine,
                    TargetColumn = 0,
                    Direction = direction,
                    Reason = "AI suggested position",
                    Confidence = 0.6,
                    Type = JumpType.NextLogicalPosition
                };
            }

            return new JumpRecommendation { Direction = JumpDirection.None };
        }

        private string CleanAIResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            // Remove common AI response prefixes/suffixes
            var cleaned = response.Trim();
            
            // Remove code block markers
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^```\w*\n?", "", 
                System.Text.RegularExpressions.RegexOptions.Multiline);
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\n?```$", "", 
                System.Text.RegularExpressions.RegexOptions.Multiline);
            
            // Remove explanatory text that might come before/after code
            var lines = cleaned.Split('\n');
            var codeLines = new List<string>();
            var inCode = false;
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip empty lines at the beginning
                if (!inCode && string.IsNullOrWhiteSpace(trimmedLine))
                    continue;
                
                // If we find code-like content, start collecting
                if (!inCode && (line.Contains("{") || line.Contains("}") || 
                               line.Contains(";") || line.StartsWith("    ") || line.StartsWith("\t")))
                {
                    inCode = true;
                }
                
                if (inCode)
                {
                    codeLines.Add(line);
                }
            }
            
            return string.Join("\n", codeLines).Trim();
        }

        private double CalculateSuggestionConfidence(string suggestion, CodeContext codeContext)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
                return 0.0;

            double confidence = 0.4; // Base confidence
            
            // Language-specific syntax check (0.0 to 0.25)
            if (ContainsLanguageSpecificSyntax(suggestion, codeContext.Language))
            {
                confidence += 0.2;
                
                // Bonus for very language-specific constructs
                var languageBonus = GetLanguageSpecificBonus(suggestion, codeContext.Language);
                confidence += languageBonus;
            }
            
            // Indentation consistency (0.0 to 0.15)
            if (RespectsIndentation(suggestion, codeContext.Indentation))
            {
                confidence += 0.15;
            }
            else if (HasInconsistentIndentation(suggestion))
            {
                confidence -= 0.1;
            }
            
            // Length appropriateness (0.0 to 0.1)
            var lengthScore = CalculateLengthScore(suggestion);
            confidence += lengthScore;
            
            // Syntax validity (0.0 to 0.15)
            var syntaxScore = CalculateSyntaxScore(suggestion, codeContext.Language);
            confidence += syntaxScore;
            
            // Context relevance (0.0 to 0.1)
            var contextScore = CalculateContextRelevance(suggestion, codeContext);
            confidence += contextScore;
            
            // Completeness check (0.0 to 0.05)
            if (LooksComplete(suggestion, codeContext))
                confidence += 0.05;
            
            // Penalty for common AI artifacts (-0.1 to 0.0)
            if (ContainsAIArtifacts(suggestion))
                confidence -= 0.1;
            
            return Math.Max(0.0, Math.Min(1.0, confidence));
        }

        private double GetLanguageSpecificBonus(string suggestion, string language)
        {
            var languageLower = language.ToLowerInvariant();
            switch (languageLower)
            {
                case "csharp":
                    if (suggestion.Contains("async ") || suggestion.Contains("await "))
                        return 0.05;
                    if (suggestion.Contains("?.") || suggestion.Contains("??"))
                        return 0.03;
                    break;
                case "javascript":
                    if (suggestion.Contains("=>") || suggestion.Contains("async"))
                        return 0.05;
                    break;
                case "python":
                    if (suggestion.Contains("with ") || suggestion.Contains("lambda"))
                        return 0.05;
                    break;
                case "cpp":
                    if (suggestion.Contains("::") || suggestion.Contains("auto"))
                        return 0.05;
                    break;
            }
            return 0.0;
        }

        private double CalculateLengthScore(string suggestion)
        {
            var length = suggestion.Trim().Length;
            if (length >= 1 && length <= 10)
                return 0.05;      // Very short, likely incomplete
            if (length >= 11 && length <= 50)
                return 0.1;       // Good length for most completions
            if (length >= 51 && length <= 150)
                return 0.08;      // Reasonable length
            if (length >= 151 && length <= 300)
                return 0.05;      // Getting long
            return 0.0;           // Too short or too long
        }

        private double CalculateSyntaxScore(string suggestion, string language)
        {
            var score = 0.0;
            
            // Check for balanced brackets/braces/parentheses
            if (HasBalancedDelimiters(suggestion))
                score += 0.05;
            
            // Check for proper statement termination
            if (HasProperStatementTermination(suggestion, language))
                score += 0.05;
            
            // Check for valid identifiers
            if (HasValidIdentifiers(suggestion, language))
                score += 0.05;
            
            return score;
        }

        private double CalculateContextRelevance(string suggestion, CodeContext codeContext)
        {
            var score = 0.0;
            
            // Check if suggestion uses variables from context
            if (UsesContextVariables(suggestion, codeContext))
                score += 0.05;
            
            // Check if suggestion matches the current scope style
            if (MatchesScopeStyle(suggestion, codeContext))
                score += 0.05;
            
            return score;
        }

        private bool HasBalancedDelimiters(string text)
        {
            var stack = new Stack<char>();
            var pairs = new Dictionary<char, char> { { ')', '(' }, { '}', '{' }, { ']', '[' } };
            
            foreach (var ch in text)
            {
                if (ch == '(' || ch == '{' || ch == '[')
                {
                    stack.Push(ch);
                }
                else if (pairs.ContainsKey(ch))
                {
                    if (stack.Count == 0 || stack.Pop() != pairs[ch])
                        return false;
                }
            }
            
            return stack.Count == 0;
        }

        private bool HasProperStatementTermination(string suggestion, string language)
        {
            var trimmed = suggestion.Trim();
            return language.ToLowerInvariant() switch
            {
                "csharp" or "cpp" or "c" or "java" => trimmed.EndsWith(";") || trimmed.EndsWith("}") || trimmed.EndsWith("{"),
                "python" => !trimmed.EndsWith(";"), // Python doesn't need semicolons
                "javascript" => true, // JavaScript is flexible with semicolons
                _ => true
            };
        }

        private bool HasValidIdentifiers(string suggestion, string language)
        {
            // Simple check for valid identifier patterns
            var identifierPattern = language.ToLowerInvariant() switch
            {
                "csharp" => @"\b[a-zA-Z_][a-zA-Z0-9_]*\b",
                "python" => @"\b[a-zA-Z_][a-zA-Z0-9_]*\b",
                "javascript" => @"\b[a-zA-Z_$][a-zA-Z0-9_$]*\b",
                _ => @"\b[a-zA-Z_][a-zA-Z0-9_]*\b"
            };
            
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(suggestion, identifierPattern);
            }
            catch
            {
                return true; // If regex fails, don't penalize
            }
        }

        private bool LooksComplete(string suggestion, CodeContext codeContext)
        {
            var trimmed = suggestion.Trim();
            
            // Check if it looks like a complete statement or expression
            return trimmed.EndsWith(";") || 
                   trimmed.EndsWith("}") || 
                   trimmed.EndsWith(")") ||
                   (codeContext.Language.ToLowerInvariant() == "python" && !trimmed.EndsWith("\\"));
        }

        private bool ContainsAIArtifacts(string suggestion)
        {
            var artifacts = new[] 
            { 
                "I think", "I believe", "Here's", "This is", "You can", "Let me",
                "```", "```csharp", "```python", "```javascript",
                "Note:", "Please", "Here is the", "The code should"
            };
            
            return artifacts.Any(artifact => suggestion.Contains(artifact));
        }

        private bool HasInconsistentIndentation(string suggestion)
        {
            var lines = suggestion.Split('\n');
            if (lines.Length <= 1) return false;
            
            var hasSpaces = lines.Any(l => l.StartsWith("    "));
            var hasTabs = lines.Any(l => l.StartsWith("\t"));
            
            return hasSpaces && hasTabs; // Mixed indentation is bad
        }

        private bool UsesContextVariables(string suggestion, CodeContext codeContext)
        {
            // Simple heuristic: check if suggestion contains words from context
            if (string.IsNullOrWhiteSpace(codeContext.CurrentLine))
                return false;
            
            var contextWords = System.Text.RegularExpressions.Regex.Matches(codeContext.CurrentLine, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .Where(w => w.Length > 2)
                .ToList();
            
            return contextWords.Any(word => suggestion.Contains(word));
        }

        private bool MatchesScopeStyle(string suggestion, CodeContext codeContext)
        {
            // Check if suggestion matches the naming convention of the current scope
            if (string.IsNullOrWhiteSpace(codeContext.CurrentScope))
                return true;
            
            // Simple check: if scope uses PascalCase/camelCase, suggestion should too
            var scopeHasPascalCase = System.Text.RegularExpressions.Regex.IsMatch(codeContext.CurrentScope, @"\b[A-Z][a-z]+[A-Z]");
            var suggestionHasPascalCase = System.Text.RegularExpressions.Regex.IsMatch(suggestion, @"\b[A-Z][a-z]+[A-Z]");
            
            return !scopeHasPascalCase || suggestionHasPascalCase;
        }

        private SuggestionType DetermineSuggestionType(string suggestion, CodeContext codeContext)
        {
            var trimmed = suggestion.Trim();
            
            if (trimmed.Contains("(") && trimmed.Contains(")"))
                return SuggestionType.Method;
            
            if (trimmed.StartsWith("//") || trimmed.StartsWith("/*"))
                return SuggestionType.Comment;
            
            if (trimmed.Contains("import ") || trimmed.Contains("using ") || trimmed.Contains("#include"))
                return SuggestionType.Import;
            
            if (trimmed.Contains("class ") || trimmed.Contains("interface ") || trimmed.Contains("struct "))
                return SuggestionType.Type;
            
            return SuggestionType.General;
        }

        private bool ContainsLanguageSpecificSyntax(string suggestion, string language)
        {
            return language.ToLowerInvariant() switch
            {
                "csharp" => suggestion.Contains(";") || suggestion.Contains("var ") || suggestion.Contains("public "),
                "javascript" => suggestion.Contains("function") || suggestion.Contains("=>") || suggestion.Contains("const "),
                "python" => suggestion.Contains(":") && !suggestion.Contains(";"),
                "cpp" or "c" => suggestion.Contains(";") || suggestion.Contains("#include") || suggestion.Contains("::"),
                _ => true
            };
        }

        private bool RespectsIndentation(string suggestion, IndentationInfo indentation)
        {
            if (indentation == null || string.IsNullOrWhiteSpace(suggestion))
                return true;

            var lines = suggestion.Split('\n');
            if (lines.Length <= 1)
                return true;

            // Check if the suggestion uses consistent indentation
            var usesSpaces = suggestion.Contains("    ");
            var usesTabs = suggestion.Contains("\t");
            
            return (indentation.UsesSpaces && usesSpaces && !usesTabs) ||
                   (!indentation.UsesSpaces && usesTabs && !usesSpaces);
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private int EstimateContextSize(string modelName)
        {
            var name = modelName.ToLowerInvariant();
            
            return name switch
            {
                var n when n.Contains("codellama") => 4096,
                var n when n.Contains("deepseek") => 16384,
                var n when n.Contains("starcoder") => 8192,
                var n when n.Contains("codegeex") => 8192,
                _ => 2048
            };
        }

        private bool IsCodeModel(string modelName)
        {
            var name = modelName.ToLowerInvariant();
            return name.Contains("code") || name.Contains("coder") || 
                   name.Contains("codellama") || name.Contains("starcoder") ||
                   name.Contains("deepseek") || name.Contains("codegeex");
        }

        private string EstimateParameterCount(string modelName)
        {
            var name = modelName.ToLowerInvariant();
            
            if (name.Contains("7b")) return "7B";
            if (name.Contains("13b")) return "13B";
            if (name.Contains("34b")) return "34B";
            if (name.Contains("70b")) return "70B";
            if (name.Contains("1b")) return "1B";
            if (name.Contains("3b")) return "3B";
            
            return "unknown";
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            
            if (_settingsService != null)
            {
                _settingsService.SettingsChanged -= OnSettingsChanged;
            }
            
            _httpClient?.Dispose();
        }

        /// <summary>
        /// Gets a request identifier for rate limiting (based on file path or anonymous)
        /// </summary>
        private string GetRequestIdentifier(CodeContext codeContext)
        {
            if (!string.IsNullOrEmpty(codeContext?.FileName))
            {
                // Use sanitized file path as identifier
                return _securityValidator.SanitizeFilePath(codeContext.FileName);
            }
            
            return "anonymous";
        }

        #endregion
    }

    #region Validation Classes

    /// <summary>
    /// Result of AI response validation
    /// </summary>
    public class AIResponseValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public ValidationErrorType ErrorType { get; set; }
    }

    /// <summary>
    /// Types of validation errors
    /// </summary>
    public enum ValidationErrorType
    {
        None,
        NullResponse,
        EmptyResponse,
        ResponseTooLong,
        ContainsErrorIndicators,
        IncompleteStructuredData,
        ExcessiveRepetition,
        SyntaxError,
        UnsafeContent,
        IrrelevantContent
    }

    #endregion
}
