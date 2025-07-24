using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Interface for logging operations
    /// </summary>
    public interface ILogger
    {
        Task LogDebugAsync(string message, string context = null, object additionalData = null);
        Task LogInfoAsync(string message, string context = null, object additionalData = null);
        Task LogWarningAsync(string message, string context = null, object additionalData = null);
        Task LogErrorAsync(Exception exception, string context = null, object additionalData = null);
        Task LogErrorAsync(string message, string context = null, object additionalData = null);
        Task LogPerformanceAsync(string operation, TimeSpan duration, string context = null);
        Task<List<LogEntry>> GetRecentLogsAsync(int count = 100);
        Task ClearLogsAsync();
    }

    /// <summary>
    /// Comprehensive logging system for the Ollama Assistant extension
    /// </summary>
    public class Logger : ILogger, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IVsActivityLog _vsActivityLog;
        private readonly ConcurrentQueue<LogEntry> _logBuffer;
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly Timer _flushTimer;
        private readonly string _logFilePath;
        private bool _disposed;

        private const int MaxBufferSize = 1000;
        private const int FlushIntervalMs = 5000;

        public Logger(ISettingsService settingsService, IServiceProvider serviceProvider)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logBuffer = new ConcurrentQueue<LogEntry>();
            _writeSemaphore = new SemaphoreSlim(1, 1);

            // Get VS Activity Log
            if (serviceProvider != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _vsActivityLog = serviceProvider.GetService(typeof(SVsActivityLog)) as IVsActivityLog;
            }

            // Set up log file path
            _logFilePath = GetLogFilePath();

            // Set up flush timer
            _flushTimer = new Timer(FlushLogsCallback, null, FlushIntervalMs, FlushIntervalMs);

            // Subscribe to settings changes
            if (_settingsService != null)
            {
                _settingsService.SettingsChanged += OnSettingsChanged;
            }
        }

        #region Public Methods

        public async Task LogDebugAsync(string message, string context = null, object additionalData = null)
        {
            if (ShouldLog(LogLevel.Debug))
            {
                await LogInternalAsync(LogLevel.Debug, message, context, additionalData);
            }
        }

        public async Task LogInfoAsync(string message, string context = null, object additionalData = null)
        {
            if (ShouldLog(LogLevel.Info))
            {
                await LogInternalAsync(LogLevel.Info, message, context, additionalData);
            }
        }

        public async Task LogWarningAsync(string message, string context = null, object additionalData = null)
        {
            if (ShouldLog(LogLevel.Warning))
            {
                await LogInternalAsync(LogLevel.Warning, message, context, additionalData);
            }
        }

        public async Task LogErrorAsync(Exception exception, string context = null, object additionalData = null)
        {
            if (ShouldLog(LogLevel.Error))
            {
                var message = FormatException(exception);
                await LogInternalAsync(LogLevel.Error, message, context, additionalData, exception);
            }
        }

        public async Task LogErrorAsync(string message, string context = null, object additionalData = null)
        {
            if (ShouldLog(LogLevel.Error))
            {
                await LogInternalAsync(LogLevel.Error, message, context, additionalData);
            }
        }

        public async Task LogPerformanceAsync(string operation, TimeSpan duration, string context = null)
        {
            if (ShouldLog(LogLevel.Performance))
            {
                var message = $"Performance: {operation} took {duration.TotalMilliseconds:F2}ms";
                var performanceData = new PerformanceLogData
                {
                    Operation = operation,
                    Duration = duration,
                    Timestamp = DateTime.Now
                };

                await LogInternalAsync(LogLevel.Performance, message, context, performanceData);
            }
        }

        public async Task<List<LogEntry>> GetRecentLogsAsync(int count = 100)
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                var logs = new List<LogEntry>();
                var tempQueue = new Queue<LogEntry>();

                // Extract logs from buffer
                while (_logBuffer.TryDequeue(out var entry) && logs.Count < count)
                {
                    logs.Add(entry);
                    tempQueue.Enqueue(entry);
                }

                // Put logs back in buffer
                while (tempQueue.Count > 0)
                {
                    _logBuffer.Enqueue(tempQueue.Dequeue());
                }

                // If we need more logs, read from file
                if (logs.Count < count)
                {
                    var fileLogs = await ReadLogsFromFileAsync(count - logs.Count);
                    logs.AddRange(fileLogs);
                }

                logs.Sort((x, y) => y.Timestamp.CompareTo(x.Timestamp));
                return logs.Take(count).ToList();
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        public async Task ClearLogsAsync()
        {
            await _writeSemaphore.WaitAsync();
            try
            {
                // Clear buffer
                while (_logBuffer.TryDequeue(out _)) { }

                // Clear log file
                if (File.Exists(_logFilePath))
                {
                    File.Delete(_logFilePath);
                }

                await LogInfoAsync("Log cleared by user request");
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        #endregion

        #region Private Methods

        private async Task LogInternalAsync(LogLevel level, string message, string context, object additionalData, Exception exception = null)
        {
            var entry = new LogEntry
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Context = context,
                ThreadId = Thread.CurrentThread.ManagedThreadId,
                Exception = exception,
                AdditionalData = additionalData
            };

            // Add correlation ID if available
            entry.CorrelationId = GetCorrelationId();

            // Add to buffer
            _logBuffer.Enqueue(entry);

            // Manage buffer size
            while (_logBuffer.Count > MaxBufferSize)
            {
                _logBuffer.TryDequeue(out _);
            }

            // Log to VS Activity Log for important messages
            if (level >= LogLevel.Warning)
            {
                await LogToVsActivityLogAsync(entry);
            }

            // Log to debug output for development
            if (ShouldLogToDebug())
            {
                LogToDebugOutput(entry);
            }
        }

        private bool ShouldLog(LogLevel level)
        {
            var minimumLevel = GetMinimumLogLevel();
            return level >= minimumLevel;
        }

        private LogLevel GetMinimumLogLevel()
        {
            if (_settingsService?.EnableVerboseLogging == true)
            {
                return LogLevel.Debug;
            }

            return LogLevel.Info;
        }

        private bool ShouldLogToDebug()
        {
            return Debugger.IsAttached || (_settingsService?.EnableVerboseLogging == true);
        }

        private string GetCorrelationId()
        {
            // In a real implementation, this would track request/operation correlation
            return $"OA-{DateTime.Now:yyyyMMdd-HHmmss}-{Thread.CurrentThread.ManagedThreadId}";
        }

        private string FormatException(Exception exception)
        {
            if (exception == null)
                return "Unknown exception";

            var sb = new StringBuilder();
            sb.AppendLine($"Exception: {exception.GetType().Name}");
            sb.AppendLine($"Message: {exception.Message}");

            if (exception.InnerException != null)
            {
                sb.AppendLine($"Inner Exception: {exception.InnerException.GetType().Name}");
                sb.AppendLine($"Inner Message: {exception.InnerException.Message}");
            }

            if (_settingsService?.EnableVerboseLogging == true)
            {
                sb.AppendLine($"Stack Trace: {exception.StackTrace}");
            }

            return sb.ToString();
        }

        private async Task LogToVsActivityLogAsync(LogEntry entry)
        {
            try
            {
                if (_vsActivityLog == null)
                    return;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var entryType = entry.Level switch
                {
                    LogLevel.Error => __ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                    LogLevel.Warning => __ACTIVITYLOG_ENTRYTYPE.ALE_WARNING,
                    _ => __ACTIVITYLOG_ENTRYTYPE.ALE_INFORMATION
                };

                var source = string.IsNullOrEmpty(entry.Context) ? "OllamaAssistant" : $"OllamaAssistant.{entry.Context}";
                var message = FormatLogMessage(entry, false);

                _vsActivityLog.LogEntry((uint)entryType, source, message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging to VS Activity Log: {ex.Message}");
            }
        }

        private void LogToDebugOutput(LogEntry entry)
        {
            try
            {
                var message = FormatLogMessage(entry, true);
                Debug.WriteLine(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging to debug output: {ex.Message}");
            }
        }

        private string FormatLogMessage(LogEntry entry, bool includeTimestamp)
        {
            var sb = new StringBuilder();

            if (includeTimestamp)
            {
                sb.Append($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            }

            sb.Append($"[{entry.Level}] ");

            if (!string.IsNullOrEmpty(entry.Context))
            {
                sb.Append($"[{entry.Context}] ");
            }

            sb.Append(entry.Message);

            if (entry.Exception != null && _settingsService?.EnableVerboseLogging == true)
            {
                sb.AppendLine();
                sb.Append($"Exception Details: {entry.Exception}");
            }

            return sb.ToString();
        }

        private void FlushLogsCallback(object state)
        {
            if (_disposed)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await FlushLogsAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error flushing logs: {ex.Message}");
                }
            });
        }

        private async Task FlushLogsAsync()
        {
            if (_logBuffer.IsEmpty)
                return;

            await _writeSemaphore.WaitAsync();
            try
            {
                var logsToWrite = new List<LogEntry>();
                
                while (_logBuffer.TryDequeue(out var entry))
                {
                    logsToWrite.Add(entry);
                }

                if (logsToWrite.Count > 0)
                {
                    await WriteLogsToFileAsync(logsToWrite);
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        private async Task WriteLogsToFileAsync(List<LogEntry> logs)
        {
            try
            {
                EnsureLogDirectoryExists();

                var json = JsonSerializer.Serialize(logs, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await File.AppendAllTextAsync(_logFilePath, json + Environment.NewLine);

                // Rotate log file if it gets too large
                await RotateLogFileIfNeededAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error writing logs to file: {ex.Message}");
            }
        }

        private async Task<List<LogEntry>> ReadLogsFromFileAsync(int count)
        {
            try
            {
                if (!File.Exists(_logFilePath))
                    return new List<LogEntry>();

                var lines = await File.ReadAllLinesAsync(_logFilePath);
                var logs = new List<LogEntry>();

                foreach (var line in lines.Reverse().Take(count))
                {
                    try
                    {
                        var entries = JsonSerializer.Deserialize<List<LogEntry>>(line, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        });

                        if (entries != null)
                        {
                            logs.AddRange(entries);
                        }
                    }
                    catch
                    {
                        // Skip invalid JSON lines
                        continue;
                    }
                }

                return logs.OrderByDescending(x => x.Timestamp).Take(count).ToList();
            }
            catch
            {
                return new List<LogEntry>();
            }
        }

        private string GetLogFilePath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDirectory = Path.Combine(appDataPath, "OllamaAssistant", "Logs");
            return Path.Combine(logDirectory, $"ollama-assistant-{DateTime.Now:yyyyMMdd}.log");
        }

        private void EnsureLogDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_logFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private async Task RotateLogFileIfNeededAsync()
        {
            try
            {
                if (!File.Exists(_logFilePath))
                    return;

                var fileInfo = new FileInfo(_logFilePath);
                const long maxSizeBytes = 10 * 1024 * 1024; // 10MB

                if (fileInfo.Length > maxSizeBytes)
                {
                    var archivePath = _logFilePath.Replace(".log", $"-{DateTime.Now:HHmmss}.log");
                    File.Move(_logFilePath, archivePath);

                    await LogInfoAsync("Log file rotated", "Logger");

                    // Clean up old log files (keep last 5)
                    await CleanupOldLogFilesAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error rotating log file: {ex.Message}");
            }
        }

        private async Task CleanupOldLogFilesAsync()
        {
            try
            {
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                var logFiles = Directory.GetFiles(logDirectory, "ollama-assistant-*.log")
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .Skip(5)
                    .ToList();

                foreach (var file in logFiles)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up old log files: {ex.Message}");
            }
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            if (e.SettingName == nameof(ISettingsService.EnableVerboseLogging))
            {
                Task.Run(async () =>
                {
                    await LogInfoAsync($"Verbose logging {(_settingsService.EnableVerboseLogging ? "enabled" : "disabled")}", "Settings");
                });
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                // Flush remaining logs
                Task.Run(async () => await FlushLogsAsync()).Wait(5000);

                // Dispose resources
                _flushTimer?.Dispose();
                _writeSemaphore?.Dispose();

                // Unsubscribe from events
                if (_settingsService != null)
                {
                    _settingsService.SettingsChanged -= OnSettingsChanged;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing Logger: {ex.Message}");
            }
        }

        #endregion
    }

    #region Data Models

    /// <summary>
    /// Represents a single log entry
    /// </summary>
    public class LogEntry
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public string Context { get; set; }
        public string CorrelationId { get; set; }
        public int ThreadId { get; set; }
        public Exception Exception { get; set; }
        public object AdditionalData { get; set; }
    }

    /// <summary>
    /// Log levels for categorizing log entries
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Performance = 2,
        Warning = 3,
        Error = 4
    }

    /// <summary>
    /// Performance-specific log data
    /// </summary>
    public class PerformanceLogData
    {
        public string Operation { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }

    #endregion

    /// <summary>
    /// Extension methods for performance logging
    /// </summary>
    public static class PerformanceLoggingExtensions
    {
        /// <summary>
        /// Executes an operation and logs its performance
        /// </summary>
        public static async Task<T> LogPerformanceAsync<T>(this ILogger logger, string operation, Func<Task<T>> func, string context = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await func();
                stopwatch.Stop();
                await logger.LogPerformanceAsync(operation, stopwatch.Elapsed, context);
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await logger.LogErrorAsync(ex, $"Error in {operation}", context);
                throw;
            }
        }

        /// <summary>
        /// Executes an operation and logs its performance (void return)
        /// </summary>
        public static async Task LogPerformanceAsync(this ILogger logger, string operation, Func<Task> func, string context = null)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await func();
                stopwatch.Stop();
                await logger.LogPerformanceAsync(operation, stopwatch.Elapsed, context);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await logger.LogErrorAsync(ex, $"Error in {operation}", context);
                throw;
            }
        }
    }
}