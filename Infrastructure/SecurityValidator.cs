using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OllamaAssistant.Models;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Security validator for input validation and sensitive data filtering
    /// </summary>
    public class SecurityValidator
    {
        private readonly int _maxRequestSizeBytes;
        private readonly HashSet<string> _sensitivePatterns;
        private readonly HashSet<string> _dangerousCommands;
        private readonly Regex _credentialPattern;
        private readonly Regex _emailPattern;
        private readonly Regex _ipAddressPattern;
        private readonly Regex _urlPattern;
        private readonly Regex _pathTraversalPattern;
        private readonly Regex _environmentVariablePattern;
        private readonly Dictionary<string, (double min, double max)> _settingsRanges;

        public SecurityValidator(int maxRequestSizeKB = 100)
        {
            _maxRequestSizeBytes = maxRequestSizeKB * 1024;
            _sensitivePatterns = InitializeSensitivePatterns();
            _dangerousCommands = InitializeDangerousCommands();
            _settingsRanges = InitializeSettingsRanges();
            
            // Compile regex patterns for performance
            _credentialPattern = new Regex(@"(password|pwd|secret|token|key|apikey|api_key|auth|credential|private_key|access_key|secret_key)\s*[=:]\s*[""']?[\w\-\.@!#$%^&*()]+[""']?", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _emailPattern = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", 
                RegexOptions.Compiled);
            _ipAddressPattern = new Regex(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b", 
                RegexOptions.Compiled);
            _urlPattern = new Regex(@"https?://[^\s]+", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _pathTraversalPattern = new Regex(@"(\.\./|\.\\|\.\.\\|%2e%2e%2f|%2e%2e/|\.\.%2f|%2e%2e%5c)", 
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _environmentVariablePattern = new Regex(@"(\$\{[^}]+\}|%[^%]+%|\$[A-Z_][A-Z0-9_]*)", 
                RegexOptions.Compiled);
        }

        #region Public Methods

        /// <summary>
        /// Validates that a code context is safe to send to AI
        /// </summary>
        public SecurityValidationResult ValidateCodeContext(CodeContext context)
        {
            if (context == null)
                return SecurityValidationResult.Invalid("Context cannot be null");

            var result = new SecurityValidationResult { IsValid = true };
            var issues = new List<string>();

            try
            {
                // Check for sensitive data in current line
                if (!string.IsNullOrEmpty(context.CurrentLine))
                {
                    var sensitiveIssues = FindSensitiveData(context.CurrentLine);
                    issues.AddRange(sensitiveIssues);
                }

                // Check preceding lines
                if (context.PrecedingLines != null)
                {
                    foreach (var line in context.PrecedingLines)
                    {
                        var sensitiveIssues = FindSensitiveData(line);
                        issues.AddRange(sensitiveIssues);
                    }
                }

                // Check following lines
                if (context.FollowingLines != null)
                {
                    foreach (var line in context.FollowingLines)
                    {
                        var sensitiveIssues = FindSensitiveData(line);
                        issues.AddRange(sensitiveIssues);
                    }
                }

                // Check file path for sensitive information
                if (!string.IsNullOrEmpty(context.FileName))
                {
                    if (ContainsSensitiveFilePath(context.FileName))
                    {
                        issues.Add("File path may contain sensitive information");
                    }
                }

                // Check selected text
                if (!string.IsNullOrEmpty(context.SelectedText))
                {
                    var sensitiveIssues = FindSensitiveData(context.SelectedText);
                    issues.AddRange(sensitiveIssues);
                }

                if (issues.Any())
                {
                    result.IsValid = false;
                    result.ErrorMessage = string.Join("; ", issues);
                    result.SecurityIssues = issues;
                }

                return result;
            }
            catch (Exception ex)
            {
                return SecurityValidationResult.Invalid($"Validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanitizes input by removing or masking sensitive data
        /// </summary>
        public string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sanitized = input;

            try
            {
                // Replace credentials with placeholders
                sanitized = _credentialPattern.Replace(sanitized, match =>
                {
                    var parts = match.Value.Split(new[] { '=', ':' }, 2);
                    if (parts.Length == 2)
                    {
                        return $"{parts[0].Trim()}={new string('*', Math.Min(8, parts[1].Trim().Length))}";
                    }
                    return match.Value;
                });

                // Replace email addresses
                sanitized = _emailPattern.Replace(sanitized, "user@example.com");

                // Replace IP addresses
                sanitized = _ipAddressPattern.Replace(sanitized, "192.168.1.1");

                // Replace URLs (but keep structure for context)
                sanitized = _urlPattern.Replace(sanitized, match =>
                {
                    var uri = new Uri(match.Value);
                    return $"{uri.Scheme}://example.com{uri.AbsolutePath}";
                });

                // Remove other sensitive patterns
                foreach (var pattern in _sensitivePatterns)
                {
                    var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                    sanitized = regex.Replace(sanitized, "[REDACTED]");
                }

                return sanitized;
            }
            catch (Exception)
            {
                // If sanitization fails, return safe fallback
                return "[CONTENT_SANITIZED]";
            }
        }

        /// <summary>
        /// Validates that a request size is within limits
        /// </summary>
        public bool IsRequestSizeValid(string request)
        {
            if (string.IsNullOrEmpty(request))
                return true;

            var sizeInBytes = System.Text.Encoding.UTF8.GetByteCount(request);
            return sizeInBytes <= _maxRequestSizeBytes;
        }

        /// <summary>
        /// Validates that a suggestion doesn't contain dangerous content
        /// </summary>
        public SecurityValidationResult ValidateSuggestion(CodeSuggestion suggestion)
        {
            if (suggestion == null)
                return SecurityValidationResult.Invalid("Suggestion cannot be null");

            var result = new SecurityValidationResult { IsValid = true };
            var issues = new List<string>();

            try
            {
                // Check suggestion text for dangerous commands
                if (!string.IsNullOrEmpty(suggestion.Text))
                {
                    var dangerousCommands = FindDangerousCommands(suggestion.Text);
                    if (dangerousCommands.Any())
                    {
                        issues.Add($"Contains potentially dangerous commands: {string.Join(", ", dangerousCommands)}");
                    }

                    // Check for file system operations
                    if (ContainsFileSystemOperations(suggestion.Text))
                    {
                        issues.Add("Contains file system operations");
                    }

                    // Check for network operations
                    if (ContainsNetworkOperations(suggestion.Text))
                    {
                        issues.Add("Contains network operations");
                    }
                }

                if (issues.Any())
                {
                    result.IsValid = false;
                    result.ErrorMessage = string.Join("; ", issues);
                    result.SecurityIssues = issues;
                }

                return result;
            }
            catch (Exception ex)
            {
                return SecurityValidationResult.Invalid($"Suggestion validation error: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates that a file path is safe to include in context
        /// </summary>
        public bool IsFilePathSafe(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return true;

            try
            {
                // Check for path traversal attempts
                if (_pathTraversalPattern.IsMatch(filePath))
                    return false;

                // Check for absolute paths pointing to system directories
                if (IsSystemPath(filePath))
                    return false;

                // Check for environment variables in path
                if (_environmentVariablePattern.IsMatch(filePath))
                    return false;

                // Check for sensitive directory names
                var sensitiveDirectories = new[]
                {
                    ".git", ".svn", "node_modules", "packages", "bin", "obj",
                    "credentials", "secrets", "keys", "certs", "certificates",
                    "private", "confidential", ".ssh", ".aws", ".azure",
                    "temp", "tmp", "cache", "backup", "config"
                };

                var pathParts = filePath.ToLowerInvariant().Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                
                return !pathParts.Any(part => sensitiveDirectories.Contains(part));
            }
            catch (Exception)
            {
                // If validation fails, assume unsafe
                return false;
            }
        }

        /// <summary>
        /// Sanitizes a file path to prevent directory traversal and exposure of sensitive paths
        /// </summary>
        public string SanitizeFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return filePath;

            try
            {
                // Remove path traversal patterns
                var sanitized = _pathTraversalPattern.Replace(filePath, "");

                // Remove environment variables
                sanitized = _environmentVariablePattern.Replace(sanitized, "[VAR]");

                // If it's an absolute path, make it relative to a safe location
                if (Path.IsPathRooted(sanitized))
                {
                    // Get just the file name for absolute paths
                    sanitized = Path.GetFileName(sanitized);
                }

                // Replace sensitive directory names
                var sensitiveDirectories = new Dictionary<string, string>
                {
                    { ".git", "[git]" },
                    { ".svn", "[svn]" },
                    { "credentials", "[creds]" },
                    { "secrets", "[secrets]" },
                    { "private", "[private]" },
                    { ".ssh", "[ssh]" },
                    { ".aws", "[aws]" },
                    { ".azure", "[azure]" }
                };

                foreach (var kvp in sensitiveDirectories)
                {
                    sanitized = sanitized.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
                }

                return sanitized;
            }
            catch (Exception)
            {
                // If sanitization fails, return safe fallback
                return "[PATH_SANITIZED]";
            }
        }

        /// <summary>
        /// Validates settings values against acceptable ranges
        /// </summary>
        public SecurityValidationResult ValidateSettingsValue(string settingName, object value)
        {
            if (string.IsNullOrEmpty(settingName) || value == null)
                return SecurityValidationResult.Invalid("Setting name and value cannot be null");

            try
            {
                // Check numeric settings ranges
                if (_settingsRanges.TryGetValue(settingName.ToLowerInvariant(), out var range))
                {
                    if (value is int intValue)
                    {
                        if (intValue < range.min || intValue > range.max)
                        {
                            return SecurityValidationResult.Invalid($"{settingName} must be between {range.min} and {range.max}");
                        }
                    }
                    else if (value is double doubleValue)
                    {
                        if (doubleValue < range.min || doubleValue > range.max)
                        {
                            return SecurityValidationResult.Invalid($"{settingName} must be between {range.min} and {range.max}");
                        }
                    }
                }

                // Check string settings for dangerous content
                if (value is string stringValue)
                {
                    // Check for script injection attempts
                    if (ContainsScriptInjection(stringValue))
                    {
                        return SecurityValidationResult.Invalid($"{settingName} contains potentially dangerous script content");
                    }

                    // Check for path traversal in path settings
                    if (settingName.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                        settingName.Contains("directory", StringComparison.OrdinalIgnoreCase) ||
                        settingName.Contains("folder", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsFilePathSafe(stringValue))
                        {
                            return SecurityValidationResult.Invalid($"{settingName} contains unsafe path");
                        }
                    }

                    // Check for URLs in endpoint settings
                    if (settingName.Contains("endpoint", StringComparison.OrdinalIgnoreCase) ||
                        settingName.Contains("url", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsUrlSafe(stringValue))
                        {
                            return SecurityValidationResult.Invalid($"{settingName} contains unsafe URL");
                        }
                    }
                }

                return SecurityValidationResult.Valid();
            }
            catch (Exception ex)
            {
                return SecurityValidationResult.Invalid($"Settings validation error: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Gets the maximum allowed request size in bytes
        /// </summary>
        public int GetMaxRequestSizeBytes()
        {
            return _maxRequestSizeBytes;
        }

        /// <summary>
        /// Validates the total size of a code context before sending
        /// </summary>
        public SecurityValidationResult ValidateContextSize(CodeContext context)
        {
            if (context == null)
                return SecurityValidationResult.Valid();

            try
            {
                var totalSize = 0;
                
                // Calculate size of all text content
                if (!string.IsNullOrEmpty(context.CurrentLine))
                    totalSize += System.Text.Encoding.UTF8.GetByteCount(context.CurrentLine);
                
                if (context.PrecedingLines != null)
                    totalSize += context.PrecedingLines.Sum(line => System.Text.Encoding.UTF8.GetByteCount(line ?? ""));
                
                if (context.FollowingLines != null)
                    totalSize += context.FollowingLines.Sum(line => System.Text.Encoding.UTF8.GetByteCount(line ?? ""));
                
                if (!string.IsNullOrEmpty(context.SelectedText))
                    totalSize += System.Text.Encoding.UTF8.GetByteCount(context.SelectedText);

                if (totalSize > _maxRequestSizeBytes)
                {
                    var sizeInKB = totalSize / 1024.0;
                    var maxSizeInKB = _maxRequestSizeBytes / 1024.0;
                    return SecurityValidationResult.Invalid($"Context size ({sizeInKB:F1}KB) exceeds maximum allowed size ({maxSizeInKB}KB)");
                }

                return SecurityValidationResult.Valid();
            }
            catch (Exception ex)
            {
                return SecurityValidationResult.Invalid($"Context size validation error: {ex.Message}");
            }
        }

        #region Private Methods

        private HashSet<string> InitializeSensitivePatterns()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"\b(?:SSN|Social.Security)\s*:?\s*\d{3}-?\d{2}-?\d{4}\b",
                @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", // Credit card numbers
                @"\b[A-Z0-9]{20,}\b", // API keys and tokens (common pattern)
                @"\b[a-f0-9]{32}\b", // MD5 hashes that might be passwords
                @"\b[a-f0-9]{40}\b", // SHA1 hashes that might be passwords
                @"\b[a-f0-9]{64}\b", // SHA256 hashes that might be passwords
                @"-----BEGIN [A-Z ]+-----.*?-----END [A-Z ]+-----", // Certificates and keys
                @"\$\{[^}]*PASSWORD[^}]*\}", // Environment variable passwords
                @"Bearer\s+[A-Za-z0-9\-_\.]+", // Bearer tokens
                @"ssh-rsa\s+[A-Za-z0-9+/=]+", // SSH keys
                @"ssh-ed25519\s+[A-Za-z0-9+/=]+", // SSH ED25519 keys
            };
        }

        private HashSet<string> InitializeDangerousCommands()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System.Diagnostics.Process.Start",
                "ProcessStartInfo",
                "cmd.exe",
                "powershell.exe",
                "bash",
                "sh",
                "eval",
                "exec",
                "Runtime.getRuntime().exec",
                "os.system",
                "subprocess.call",
                "shell_exec",
                "system(",
                "popen(",
                "File.Delete",
                "Directory.Delete",
                "rm -rf",
                "rmdir /s",
                "format c:",
                "shutdown",
                "reboot",
                "halt"
            };
        }

        private List<string> FindSensitiveData(string text)
        {
            var issues = new List<string>();

            if (string.IsNullOrEmpty(text))
                return issues;

            // Check for credential patterns
            if (_credentialPattern.IsMatch(text))
            {
                issues.Add("Contains credential-like patterns");
            }

            // Check for email addresses
            if (_emailPattern.IsMatch(text))
            {
                issues.Add("Contains email addresses");
            }

            // Check for IP addresses
            if (_ipAddressPattern.IsMatch(text))
            {
                issues.Add("Contains IP addresses");
            }

            // Check for URLs
            if (_urlPattern.IsMatch(text))
            {
                issues.Add("Contains URLs");
            }

            // Check for other sensitive patterns
            foreach (var pattern in _sensitivePatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    issues.Add("Contains sensitive data pattern");
                    break; // Don't report multiple pattern matches
                }
            }

            return issues;
        }

        private List<string> FindDangerousCommands(string text)
        {
            var found = new List<string>();

            foreach (var command in _dangerousCommands)
            {
                if (text.Contains(command, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(command);
                }
            }

            return found;
        }

        private bool ContainsSensitiveFilePath(string filePath)
        {
            var sensitiveIndicators = new[]
            {
                "password", "secret", "key", "credential", "token",
                "private", "confidential", "internal", ".env",
                "auth", "oauth", "jwt", "bearer", "access_token",
                "refresh_token", "client_secret", "api_key"
            };

            return sensitiveIndicators.Any(indicator => 
                filePath.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var systemPaths = new[]
            {
                @"C:\Windows",
                @"C:\Program Files",
                @"C:\ProgramData",
                @"C:\Users\All Users",
                @"C:\System",
                "/etc",
                "/usr",
                "/bin",
                "/sbin",
                "/var",
                "/sys",
                "/proc",
                "/root",
                "/home"
            };

            var normalizedPath = path.Replace('/', '\\').ToLowerInvariant();
            
            return systemPaths.Any(sysPath => 
                normalizedPath.StartsWith(sysPath.ToLowerInvariant()) ||
                normalizedPath.Contains($"\\{sysPath.ToLowerInvariant()}\\"));
        }

        private bool ContainsScriptInjection(string value)
        {
            var scriptPatterns = new[]
            {
                "<script",
                "javascript:",
                "onclick=",
                "onerror=",
                "onload=",
                "eval(",
                "expression(",
                "\\x3cscript",
                "&lt;script",
                "%3cscript"
            };

            return scriptPatterns.Any(pattern => 
                value.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsUrlSafe(string url)
        {
            if (string.IsNullOrEmpty(url))
                return true;

            try
            {
                var uri = new Uri(url);
                
                // Check for safe schemes
                var safeSchemes = new[] { "http", "https" };
                if (!safeSchemes.Contains(uri.Scheme.ToLowerInvariant()))
                    return false;

                // Check for local/private network addresses
                if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.StartsWith("127.") ||
                    uri.Host.StartsWith("192.168.") ||
                    uri.Host.StartsWith("10.") ||
                    uri.Host.StartsWith("172.16.") ||
                    uri.Host.StartsWith("172.17.") ||
                    uri.Host.StartsWith("172.18.") ||
                    uri.Host.StartsWith("172.19.") ||
                    uri.Host.StartsWith("172.20.") ||
                    uri.Host.StartsWith("172.21.") ||
                    uri.Host.StartsWith("172.22.") ||
                    uri.Host.StartsWith("172.23.") ||
                    uri.Host.StartsWith("172.24.") ||
                    uri.Host.StartsWith("172.25.") ||
                    uri.Host.StartsWith("172.26.") ||
                    uri.Host.StartsWith("172.27.") ||
                    uri.Host.StartsWith("172.28.") ||
                    uri.Host.StartsWith("172.29.") ||
                    uri.Host.StartsWith("172.30.") ||
                    uri.Host.StartsWith("172.31."))
                {
                    // Local addresses are generally safe for Ollama
                    return true;
                }

                // Check for suspicious ports
                var suspiciousPorts = new[] { 22, 23, 135, 139, 445, 3389 };
                if (suspiciousPorts.Contains(uri.Port))
                    return false;

                return true;
            }
            catch (Exception)
            {
                // If URL parsing fails, assume unsafe
                return false;
            }
        }

        private Dictionary<string, (double min, double max)> InitializeSettingsRanges()
        {
            return new Dictionary<string, (double min, double max)>(StringComparer.OrdinalIgnoreCase)
            {
                { "ollamatimeout", (1000, 300000) }, // 1 second to 5 minutes
                { "maxrequestsizekb", (1, 1024) }, // 1KB to 1MB
                { "maxconcurrentrequests", (1, 10) },
                { "maxretryattempts", (0, 10) },
                { "surroundinglinesup", (0, 100) },
                { "surroundinglinesdown", (0, 100) },
                { "cursorhistorydepth", (0, 50) },
                { "cachettlseconds", (0, 3600) }, // Up to 1 hour
                { "debouncedelayms", (50, 5000) },
                { "temperature", (0.0, 2.0) },
                { "topp", (0.0, 1.0) },
                { "topk", (1, 100) },
                { "numpredict", (1, 1000) }
            };
        }

        private bool ContainsFileSystemOperations(string text)
        {
            var fileOperations = new[]
            {
                "File.", "Directory.", "FileStream", "StreamWriter", "StreamReader",
                "Path.Combine", "File.WriteAllText", "File.ReadAllText", "File.Delete",
                "Directory.CreateDirectory", "Directory.Delete"
            };

            return fileOperations.Any(op => text.Contains(op, StringComparison.OrdinalIgnoreCase));
        }

        private bool ContainsNetworkOperations(string text)
        {
            var networkOperations = new[]
            {
                "HttpClient", "WebRequest", "WebClient", "TcpClient", "UdpClient",
                "Socket", "NetworkStream", "http://", "https://", "ftp://",
                "IPAddress", "DNS.", "Ping"
            };

            return networkOperations.Any(op => text.Contains(op, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }

    /// <summary>
    /// Result of a security validation operation
    /// </summary>
    public class SecurityValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public List<string> SecurityIssues { get; set; }

        public SecurityValidationResult()
        {
            SecurityIssues = new List<string>();
        }

        public static SecurityValidationResult Valid()
        {
            return new SecurityValidationResult { IsValid = true };
        }

        public static SecurityValidationResult Invalid(string errorMessage)
        {
            return new SecurityValidationResult 
            { 
                IsValid = false, 
                ErrorMessage = errorMessage,
                SecurityIssues = new List<string> { errorMessage }
            };
        }
    }
}