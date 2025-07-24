using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for validating and sanitizing user settings
    /// </summary>
    [Export(typeof(ISettingsValidationService))]
    public class SettingsValidationService : ISettingsValidationService
    {
        private readonly SecurityValidator _securityValidator;
        private readonly ILogger _logger;
        private readonly Dictionary<string, SettingValidator> _validators;

        public SettingsValidationService(ILogger logger = null)
        {
            _logger = logger;
            _securityValidator = new SecurityValidator();
            _validators = InitializeValidators();
        }

        /// <summary>
        /// Validates all settings before they are saved
        /// </summary>
        public ValidationResult ValidateSettings(ISettingsService settings)
        {
            if (settings == null)
                return ValidationResult.Invalid("Settings cannot be null");

            var issues = new List<string>();

            try
            {
                // Validate Ollama endpoint
                var endpointResult = ValidateOllamaEndpoint(settings.OllamaEndpoint);
                if (!endpointResult.IsValid)
                    issues.AddRange(endpointResult.SecurityIssues);

                // Validate Ollama model
                var modelResult = ValidateOllamaModel(settings.OllamaModel);
                if (!modelResult.IsValid)
                    issues.AddRange(modelResult.SecurityIssues);

                // Validate numeric settings
                ValidateNumericSetting("OllamaTimeout", settings.OllamaTimeout, 1000, 300000, issues);
                ValidateNumericSetting("MaxRequestSizeKB", settings.MaxRequestSizeKB, 1, 1024, issues);
                ValidateNumericSetting("MaxConcurrentRequests", settings.MaxConcurrentRequests, 1, 10, issues);
                ValidateNumericSetting("MaxRetryAttempts", settings.MaxRetryAttempts, 0, 10, issues);
                ValidateNumericSetting("SurroundingLinesUp", settings.SurroundingLinesUp, 0, 100, issues);
                ValidateNumericSetting("SurroundingLinesDown", settings.SurroundingLinesDown, 0, 100, issues);
                ValidateNumericSetting("CursorHistoryDepth", settings.CursorHistoryDepth, 0, 50, issues);
                ValidateNumericSetting("CacheTTLSeconds", settings.CacheTTLSeconds, 0, 3600, issues);
                ValidateNumericSetting("DebounceDelayMs", settings.DebounceDelayMs, 50, 5000, issues);

                // Validate boolean settings (no specific validation needed, just type safety)
                if (settings.EnablePredictiveText == null)
                    issues.Add("EnablePredictiveText cannot be null");
                if (settings.EnableJumpSuggestions == null)
                    issues.Add("EnableJumpSuggestions cannot be null");
                if (settings.ShowConfidenceIndicators == null)
                    issues.Add("ShowConfidenceIndicators cannot be null");
                if (settings.FilterSensitiveData == null)
                    issues.Add("FilterSensitiveData cannot be null");
                if (settings.EnableVerboseLogging == null)
                    issues.Add("EnableVerboseLogging cannot be null");

                // Validate key bindings
                var jumpKeyResult = ValidateKeyBinding(settings.JumpSuggestionKey);
                if (!jumpKeyResult.IsValid)
                    issues.AddRange(jumpKeyResult.SecurityIssues);

                if (issues.Any())
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Settings validation failed",
                        SecurityIssues = issues
                    };
                }

                return ValidationResult.Valid();
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error validating settings", "SettingsValidation").Wait();
                return ValidationResult.Invalid($"Settings validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates an individual setting
        /// </summary>
        public ValidationResult ValidateSetting(string settingName, object value)
        {
            if (string.IsNullOrEmpty(settingName))
                return ValidationResult.Invalid("Setting name cannot be empty");

            try
            {
                // Use specific validator if available
                if (_validators.TryGetValue(settingName.ToLowerInvariant(), out var validator))
                {
                    return validator.Validate(value);
                }

                // Fall back to security validator
                return _securityValidator.ValidateSettingsValue(settingName, value);
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid($"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanitizes a setting value to make it safe
        /// </summary>
        public object SanitizeSetting(string settingName, object value)
        {
            if (string.IsNullOrEmpty(settingName) || value == null)
                return value;

            try
            {
                // Handle string values
                if (value is string stringValue)
                {
                    // Sanitize paths
                    if (settingName.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                        settingName.Contains("directory", StringComparison.OrdinalIgnoreCase))
                    {
                        return _securityValidator.SanitizeFilePath(stringValue);
                    }

                    // Sanitize general input
                    return _securityValidator.SanitizeInput(stringValue);
                }

                // Handle numeric values - clamp to valid ranges
                if (_securityValidator.ValidateSettingsValue(settingName, value) is ValidationResult result && !result.IsValid)
                {
                    if (value is int intValue)
                    {
                        return ClampNumericValue(settingName, intValue);
                    }
                    else if (value is double doubleValue)
                    {
                        return ClampNumericValue(settingName, (int)doubleValue);
                    }
                }

                return value;
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, $"Error sanitizing setting {settingName}", "SettingsValidation").Wait();
                return value; // Return original value if sanitization fails
            }
        }

        #region Private Methods

        private Dictionary<string, SettingValidator> InitializeValidators()
        {
            return new Dictionary<string, SettingValidator>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollamaendpoint"] = new SettingValidator
                {
                    Name = "OllamaEndpoint",
                    Validate = value => ValidateOllamaEndpoint(value as string)
                },
                ["ollamamodel"] = new SettingValidator
                {
                    Name = "OllamaModel",
                    Validate = value => ValidateOllamaModel(value as string)
                },
                ["jumpsuggestionkey"] = new SettingValidator
                {
                    Name = "JumpSuggestionKey",
                    Validate = value => ValidateKeyBinding(value as string)
                }
            };
        }

        private ValidationResult ValidateOllamaEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return ValidationResult.Invalid("Ollama endpoint cannot be empty");

            try
            {
                var uri = new Uri(endpoint);

                // Check for valid schemes
                if (uri.Scheme != "http" && uri.Scheme != "https")
                    return ValidationResult.Invalid("Ollama endpoint must use HTTP or HTTPS");

                // Validate using security validator
                if (!_securityValidator.IsFilePathSafe(endpoint))
                    return ValidationResult.Invalid("Ollama endpoint contains unsafe characters");

                // Check for reasonable port range
                if (uri.Port > 0 && (uri.Port < 1 || uri.Port > 65535))
                    return ValidationResult.Invalid("Invalid port number");

                // Warn about non-HTTPS in production
                if (uri.Scheme == "http" && !IsLocalEndpoint(uri))
                {
                    _logger?.LogWarningAsync("Using HTTP for non-local Ollama endpoint is not recommended", "SettingsValidation").Wait();
                }

                return ValidationResult.Valid();
            }
            catch (UriFormatException)
            {
                return ValidationResult.Invalid("Invalid URL format for Ollama endpoint");
            }
            catch (Exception ex)
            {
                return ValidationResult.Invalid($"Error validating Ollama endpoint: {ex.Message}");
            }
        }

        private ValidationResult ValidateOllamaModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                return ValidationResult.Invalid("Ollama model cannot be empty");

            // Check for valid model name pattern
            var modelPattern = new Regex(@"^[a-zA-Z0-9_\-\.:]+$");
            if (!modelPattern.IsMatch(model))
                return ValidationResult.Invalid("Ollama model name contains invalid characters");

            // Check for script injection
            if (model.Contains("<") || model.Contains(">") || model.Contains("script", StringComparison.OrdinalIgnoreCase))
                return ValidationResult.Invalid("Ollama model name contains potentially dangerous content");

            // Validate length
            if (model.Length > 100)
                return ValidationResult.Invalid("Ollama model name is too long");

            return ValidationResult.Valid();
        }

        private ValidationResult ValidateKeyBinding(string keyBinding)
        {
            if (string.IsNullOrWhiteSpace(keyBinding))
                return ValidationResult.Valid(); // Empty key binding is allowed (disables feature)

            // Check for valid key names
            var validKeys = new[]
            {
                "Tab", "Enter", "Space", "Escape", "F1", "F2", "F3", "F4", "F5", 
                "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Insert", "Delete",
                "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right"
            };

            var validModifiers = new[] { "Ctrl", "Shift", "Alt", "Cmd" };

            // Parse key binding (e.g., "Ctrl+Shift+Tab")
            var parts = keyBinding.Split('+').Select(p => p.Trim()).ToList();
            
            if (parts.Count == 0 || parts.Count > 4)
                return ValidationResult.Invalid("Invalid key binding format");

            // Last part should be the key
            var key = parts.Last();
            if (!validKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                return ValidationResult.Invalid($"Invalid key: {key}");

            // All other parts should be modifiers
            var modifiers = parts.Take(parts.Count - 1);
            foreach (var modifier in modifiers)
            {
                if (!validModifiers.Contains(modifier, StringComparer.OrdinalIgnoreCase))
                    return ValidationResult.Invalid($"Invalid modifier: {modifier}");
            }

            return ValidationResult.Valid();
        }

        private void ValidateNumericSetting(string name, int value, int min, int max, List<string> issues)
        {
            if (value < min || value > max)
            {
                issues.Add($"{name} must be between {min} and {max} (current: {value})");
            }
        }

        private bool IsLocalEndpoint(Uri uri)
        {
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("127.0.0.1") ||
                   uri.Host.StartsWith("192.168.") ||
                   uri.Host.StartsWith("10.") ||
                   uri.Host.StartsWith("172.");
        }

        private int ClampNumericValue(string settingName, int value)
        {
            var ranges = new Dictionary<string, (int min, int max)>(StringComparer.OrdinalIgnoreCase)
            {
                ["ollamatimeout"] = (1000, 300000),
                ["maxrequestsizekb"] = (1, 1024),
                ["maxconcurrentrequests"] = (1, 10),
                ["maxretryattempts"] = (0, 10),
                ["surroundinglinesup"] = (0, 100),
                ["surroundinglinesdown"] = (0, 100),
                ["cursorhistorydepth"] = (0, 50),
                ["cachettlseconds"] = (0, 3600),
                ["debouncedelayms"] = (50, 5000)
            };

            if (ranges.TryGetValue(settingName.ToLowerInvariant(), out var range))
            {
                return Math.Max(range.min, Math.Min(range.max, value));
            }

            return value;
        }

        #endregion

        private class SettingValidator
        {
            public string Name { get; set; }
            public Func<object, ValidationResult> Validate { get; set; }
        }
    }
}