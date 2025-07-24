using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for reporting errors and collecting analytics
    /// </summary>
    public class ErrorReportingService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly SecurityValidator _securityValidator;
        private readonly string _reportingEndpoint;
        private readonly string _localReportDirectory;
        private bool _disposed;

        public ErrorReportingService(string reportingEndpoint = null)
        {
            _httpClient = new HttpClient();
            _securityValidator = new SecurityValidator();
            _reportingEndpoint = reportingEndpoint ?? "https://api.example.com/error-reports"; // Placeholder
            _localReportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaAssistant", "ErrorReports");
            
            // Ensure local directory exists
            Directory.CreateDirectory(_localReportDirectory);
        }

        /// <summary>
        /// Submits an error report with diagnostic information
        /// </summary>
        public async Task<string> SubmitErrorReportAsync(Exception exception, DiagnosticInformation diagnostics, string context = null)
        {
            if (_disposed || exception == null)
                return null;

            try
            {
                // Generate unique report ID
                var reportId = GenerateReportId();

                // Create error report
                var errorReport = CreateErrorReport(exception, diagnostics, context, reportId);

                // Sanitize sensitive information
                SanitizeErrorReport(errorReport);

                // Save locally first
                await SaveReportLocallyAsync(errorReport);

                // Attempt to submit online (if configured and user consents)
                if (ShouldSubmitOnline())
                {
                    await SubmitOnlineAsync(errorReport);
                }

                return reportId;
            }
            catch (Exception reportingException)
            {
                // If reporting fails, at least try to log locally
                await LogReportingFailureAsync(exception, reportingException);
                return null;
            }
        }

        /// <summary>
        /// Submits anonymous usage analytics
        /// </summary>
        public async Task SubmitUsageAnalyticsAsync(UsageAnalytics analytics)
        {
            if (_disposed || analytics == null)
                return;

            try
            {
                // Ensure analytics are anonymized
                var anonymizedAnalytics = AnonymizeAnalytics(analytics);

                // Save locally
                await SaveAnalyticsLocallyAsync(anonymizedAnalytics);

                // Submit online if enabled
                if (ShouldSubmitAnalytics())
                {
                    await SubmitAnalyticsOnlineAsync(anonymizedAnalytics);
                }
            }
            catch (Exception ex)
            {
                // Ignore analytics submission failures
                System.Diagnostics.Debug.WriteLine($"Analytics submission failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets local error report statistics
        /// </summary>
        public async Task<ErrorReportStatistics> GetReportStatisticsAsync()
        {
            if (_disposed)
                return new ErrorReportStatistics();

            try
            {
                var stats = new ErrorReportStatistics();
                var files = Directory.GetFiles(_localReportDirectory, "*.json");

                stats.TotalReports = files.Length;
                stats.LastReportTime = DateTime.MinValue;

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime > stats.LastReportTime)
                    {
                        stats.LastReportTime = fileInfo.LastWriteTime;
                    }

                    // Count reports by type (basic analysis)
                    var content = await File.ReadAllTextAsync(file);
                    if (content.Contains("ConnectionFailed"))
                        stats.ConnectionErrors++;
                    else if (content.Contains("AIResponseFailed"))
                        stats.AIResponseErrors++;
                    else
                        stats.GeneralErrors++;
                }

                return stats;
            }
            catch (Exception ex)
            {
                return new ErrorReportStatistics
                {
                    CollectionError = ex.Message
                };
            }
        }

        /// <summary>
        /// Cleans up old error reports to manage disk space
        /// </summary>
        public async Task CleanupOldReportsAsync(TimeSpan retentionPeriod)
        {
            if (_disposed)
                return;

            try
            {
                var cutoffDate = DateTime.UtcNow - retentionPeriod;
                var files = Directory.GetFiles(_localReportDirectory, "*.json");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Continue if individual file deletion fails
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates structured error report
        /// </summary>
        private ErrorReport CreateErrorReport(Exception exception, DiagnosticInformation diagnostics, string context, string reportId)
        {
            return new ErrorReport
            {
                ReportId = reportId,
                Timestamp = DateTime.UtcNow,
                Version = GetExtensionVersion(),
                Context = context,
                Exception = new ErrorReportException
                {
                    Type = exception.GetType().FullName,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    Source = exception.Source,
                    InnerExceptions = CollectInnerExceptions(exception)
                },
                Diagnostics = diagnostics,
                UserAgent = GenerateUserAgent(),
                SessionId = GenerateSessionId()
            };
        }

        /// <summary>
        /// Sanitizes error report to remove sensitive information
        /// </summary>
        private void SanitizeErrorReport(ErrorReport report)
        {
            if (report?.Diagnostics?.SystemInfo != null)
            {
                // Remove or hash sensitive system information
                report.Diagnostics.SystemInfo.MachineName = HashSensitiveString(report.Diagnostics.SystemInfo.MachineName);
                report.Diagnostics.SystemInfo.UserName = HashSensitiveString(report.Diagnostics.SystemInfo.UserName);
                report.Diagnostics.SystemInfo.CurrentDirectory = SanitizePath(report.Diagnostics.SystemInfo.CurrentDirectory);
            }

            if (report?.Diagnostics?.VSInfo?.SolutionInfo != null)
            {
                // Sanitize solution paths
                report.Diagnostics.VSInfo.SolutionInfo.SolutionDirectory = SanitizePath(report.Diagnostics.VSInfo.SolutionInfo.SolutionDirectory);
            }

            if (report?.Diagnostics?.ExtensionInfo != null)
            {
                // Sanitize extension location
                report.Diagnostics.ExtensionInfo.Location = SanitizePath(report.Diagnostics.ExtensionInfo.Location);
            }

            // Remove sensitive information from stack traces
            if (report?.Exception?.StackTrace != null)
            {
                report.Exception.StackTrace = SanitizeStackTrace(report.Exception.StackTrace);
            }
        }

        /// <summary>
        /// Saves error report locally as JSON
        /// </summary>
        private async Task SaveReportLocallyAsync(ErrorReport report)
        {
            try
            {
                var filename = $"error_report_{report.ReportId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                var filepath = Path.Combine(_localReportDirectory, filename);

                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filepath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save report locally: {ex.Message}");
            }
        }

        /// <summary>
        /// Submits error report online
        /// </summary>
        private async Task SubmitOnlineAsync(ErrorReport report)
        {
            try
            {
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var response = await _httpClient.PostAsync(_reportingEndpoint, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Online submission failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Online submission error: {ex.Message}");
            }
        }

        /// <summary>
        /// Anonymizes usage analytics
        /// </summary>
        private UsageAnalytics AnonymizeAnalytics(UsageAnalytics analytics)
        {
            return new UsageAnalytics
            {
                SessionId = HashSensitiveString(analytics.SessionId),
                Timestamp = analytics.Timestamp,
                ExtensionVersion = analytics.ExtensionVersion,
                VSVersion = analytics.VSVersion,
                OSVersion = analytics.OSVersion,
                Features = analytics.Features,
                PerformanceMetrics = analytics.PerformanceMetrics,
                UsagePatterns = analytics.UsagePatterns
                // Remove any personally identifiable information
            };
        }

        /// <summary>
        /// Saves analytics locally
        /// </summary>
        private async Task SaveAnalyticsLocallyAsync(UsageAnalytics analytics)
        {
            try
            {
                var filename = $"analytics_{DateTime.UtcNow:yyyyMMdd}.json";
                var filepath = Path.Combine(_localReportDirectory, filename);

                var json = JsonSerializer.Serialize(analytics, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.WriteAllTextAsync(filepath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save analytics: {ex.Message}");
            }
        }

        /// <summary>
        /// Submits analytics online
        /// </summary>
        private async Task SubmitAnalyticsOnlineAsync(UsageAnalytics analytics)
        {
            try
            {
                var analyticsEndpoint = _reportingEndpoint.Replace("error-reports", "analytics");
                var json = JsonSerializer.Serialize(analytics, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                using var response = await _httpClient.PostAsync(analyticsEndpoint, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"Analytics submission failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Analytics submission error: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs reporting failure for debugging
        /// </summary>
        private async Task LogReportingFailureAsync(Exception originalException, Exception reportingException)
        {
            try
            {
                var failureLog = new
                {
                    Timestamp = DateTime.UtcNow,
                    OriginalException = originalException.Message,
                    ReportingException = reportingException.Message,
                    Note = "Error reporting system failed"
                };

                var filename = $"reporting_failure_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
                var filepath = Path.Combine(_localReportDirectory, filename);

                var json = JsonSerializer.Serialize(failureLog, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(filepath, json);
            }
            catch
            {
                // If this fails too, there's nothing more we can do
                System.Diagnostics.Debug.WriteLine("Critical: Unable to log reporting failure");
            }
        }

        /// <summary>
        /// Generates unique report ID
        /// </summary>
        private string GenerateReportId()
        {
            return Guid.NewGuid().ToString("N")[..12]; // 12 character unique ID
        }

        /// <summary>
        /// Gets extension version
        /// </summary>
        private string GetExtensionVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                return assembly.GetName().Version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Generates user agent string
        /// </summary>
        private string GenerateUserAgent()
        {
            var version = GetExtensionVersion();
            var osVersion = Environment.OSVersion.ToString();
            return $"OllamaAssistant/{version} ({osVersion})";
        }

        /// <summary>
        /// Generates session ID for grouping related reports
        /// </summary>
        private string GenerateSessionId()
        {
            // Use process start time as basis for session ID
            var processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime;
            return HashSensitiveString(processStart.ToString());
        }

        /// <summary>
        /// Collects inner exception details
        /// </summary>
        private List<ErrorReportInnerException> CollectInnerExceptions(Exception exception)
        {
            var innerExceptions = new List<ErrorReportInnerException>();
            var current = exception.InnerException;

            while (current != null)
            {
                innerExceptions.Add(new ErrorReportInnerException
                {
                    Type = current.GetType().FullName,
                    Message = current.Message,
                    StackTrace = SanitizeStackTrace(current.StackTrace)
                });

                current = current.InnerException;
            }

            return innerExceptions;
        }

        /// <summary>
        /// Hashes sensitive strings for anonymization
        /// </summary>
        private string HashSensitiveString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hash)[..8]; // First 8 characters of hash
        }

        /// <summary>
        /// Sanitizes file paths to remove sensitive information
        /// </summary>
        private string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                // Replace user directory with placeholder
                var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile))
                {
                    return path.Replace(userProfile, "[USER_PROFILE]");
                }

                // Replace machine name if present
                return path.Replace(Environment.MachineName, "[MACHINE]");
            }
            catch
            {
                return "[SANITIZED_PATH]";
            }
        }

        /// <summary>
        /// Sanitizes stack traces to remove sensitive paths
        /// </summary>
        private string SanitizeStackTrace(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
                return stackTrace;

            var sanitized = stackTrace;
            
            // Remove file paths from stack trace
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                sanitized = sanitized.Replace(userProfile, "[USER_PROFILE]");
            }

            sanitized = sanitized.Replace(Environment.MachineName, "[MACHINE]");
            
            return sanitized;
        }

        /// <summary>
        /// Determines if reports should be submitted online
        /// </summary>
        private bool ShouldSubmitOnline()
        {
            // In real implementation, this would check user preferences
            // For now, return false to only store locally
            return false;
        }

        /// <summary>
        /// Determines if analytics should be submitted
        /// </summary>
        private bool ShouldSubmitAnalytics()
        {
            // In real implementation, this would check user consent
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _httpClient?.Dispose();
        }
    }

    /// <summary>
    /// Complete error report structure
    /// </summary>
    public class ErrorReport
    {
        public string ReportId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Version { get; set; }
        public string Context { get; set; }
        public ErrorReportException Exception { get; set; }
        public DiagnosticInformation Diagnostics { get; set; }
        public string UserAgent { get; set; }
        public string SessionId { get; set; }
    }

    /// <summary>
    /// Exception information for error report
    /// </summary>
    public class ErrorReportException
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
        public string Source { get; set; }
        public List<ErrorReportInnerException> InnerExceptions { get; set; } = new List<ErrorReportInnerException>();
    }

    /// <summary>
    /// Inner exception information
    /// </summary>
    public class ErrorReportInnerException
    {
        public string Type { get; set; }
        public string Message { get; set; }
        public string StackTrace { get; set; }
    }

    /// <summary>
    /// Usage analytics structure
    /// </summary>
    public class UsageAnalytics
    {
        public string SessionId { get; set; }
        public DateTime Timestamp { get; set; }
        public string ExtensionVersion { get; set; }
        public string VSVersion { get; set; }
        public string OSVersion { get; set; }
        public Dictionary<string, object> Features { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, double> PerformanceMetrics { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, int> UsagePatterns { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Error report statistics
    /// </summary>
    public class ErrorReportStatistics
    {
        public int TotalReports { get; set; }
        public int ConnectionErrors { get; set; }
        public int AIResponseErrors { get; set; }
        public int GeneralErrors { get; set; }
        public DateTime LastReportTime { get; set; }
        public string CollectionError { get; set; }
    }
}