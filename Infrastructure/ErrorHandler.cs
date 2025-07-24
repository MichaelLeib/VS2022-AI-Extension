using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Centralized error handling system for the Ollama Assistant extension
    /// </summary>
    public class ErrorHandler
    {
        private readonly ILogger _logger;
        private readonly ISettingsService _settingsService;
        private readonly Dictionary<Type, ErrorRecoveryStrategy> _recoveryStrategies;
        private readonly object _lockObject = new object();

        public ErrorHandler(ILogger logger, ISettingsService settingsService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settingsService = settingsService;
            _recoveryStrategies = new Dictionary<Type, ErrorRecoveryStrategy>();

            InitializeRecoveryStrategies();
        }

        #region Public Methods

        /// <summary>
        /// Handles an exception with appropriate recovery strategy
        /// </summary>
        public async Task<bool> HandleExceptionAsync(Exception exception, string context = null, object additionalData = null)
        {
            if (exception == null)
                return true;

            try
            {
                // Log the exception
                await _logger.LogErrorAsync(exception, context, additionalData);

                // Determine recovery strategy
                var strategy = GetRecoveryStrategy(exception);
                
                // Execute recovery
                var recovered = await ExecuteRecoveryStrategyAsync(exception, strategy, context, additionalData);

                // Log recovery result
                if (recovered)
                {
                    await _logger.LogInfoAsync($"Successfully recovered from {exception.GetType().Name}", context);
                }
                else
                {
                    await _logger.LogWarningAsync($"Failed to recover from {exception.GetType().Name}", context);
                }

                return recovered;
            }
            catch (Exception handlerException)
            {
                // Prevent infinite recursion - log to debug output only
                Debug.WriteLine($"Error in ErrorHandler.HandleExceptionAsync: {handlerException.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles an exception and provides user feedback if necessary
        /// </summary>
        public async Task<bool> HandleExceptionWithUserFeedbackAsync(Exception exception, string userMessage = null, string context = null)
        {
            var recovered = await HandleExceptionAsync(exception, context);

            if (!recovered && !string.IsNullOrEmpty(userMessage))
            {
                await ShowUserErrorMessageAsync(userMessage, exception);
            }

            return recovered;
        }

        /// <summary>
        /// Executes an action with error handling
        /// </summary>
        public async Task<T> ExecuteWithErrorHandlingAsync<T>(Func<Task<T>> action, string context = null, T defaultValue = default(T))
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Executes an action with error handling (void return)
        /// </summary>
        public async Task ExecuteWithErrorHandlingAsync(Func<Task> action, string context = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, context);
            }
        }

        /// <summary>
        /// Registers a custom recovery strategy for a specific exception type
        /// </summary>
        public void RegisterRecoveryStrategy<T>(ErrorRecoveryStrategy strategy) where T : Exception
        {
            lock (_lockObject)
            {
                _recoveryStrategies[typeof(T)] = strategy;
            }
        }

        /// <summary>
        /// Checks if the extension is in a healthy state
        /// </summary>
        public async Task<HealthCheckResult> PerformHealthCheckAsync()
        {
            var result = new HealthCheckResult();

            try
            {
                // Check Ollama connectivity
                result.OllamaConnectivity = await CheckOllamaConnectivityAsync();

                // Check settings validity
                result.SettingsValid = CheckSettingsValidity();

                // Check memory usage
                result.MemoryUsage = GetMemoryUsage();

                // Check performance metrics
                result.PerformanceMetrics = await GetPerformanceMetricsAsync();

                // Overall health
                result.IsHealthy = result.OllamaConnectivity && result.SettingsValid && 
                                 result.MemoryUsage < 100 * 1024 * 1024; // 100MB threshold

                await _logger.LogInfoAsync($"Health check completed. Healthy: {result.IsHealthy}");

                return result;
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ex, "HealthCheck");
                result.IsHealthy = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        #endregion

        #region Private Methods

        private void InitializeRecoveryStrategies()
        {
            // Ollama connection errors
            RegisterRecoveryStrategy<OllamaConnectionException>(new ErrorRecoveryStrategy
            {
                RetryCount = 3,
                RetryDelay = TimeSpan.FromSeconds(2),
                FallbackAction = async (ex, context, data) =>
                {
                    await _logger.LogWarningAsync("Ollama connection failed, operating in offline mode", context);
                    return true; // Continue without Ollama
                }
            });

            // Ollama model errors
            RegisterRecoveryStrategy<OllamaModelException>(new ErrorRecoveryStrategy
            {
                RetryCount = 1,
                RetryDelay = TimeSpan.FromSeconds(1),
                FallbackAction = async (ex, context, data) =>
                {
                    await _logger.LogWarningAsync("Ollama model error, disabling AI features temporarily", context);
                    return true;
                }
            });

            // Context capture errors
            RegisterRecoveryStrategy<ContextCaptureException>(new ErrorRecoveryStrategy
            {
                RetryCount = 2,
                RetryDelay = TimeSpan.FromMilliseconds(500),
                FallbackAction = async (ex, context, data) =>
                {
                    await _logger.LogWarningAsync("Context capture failed, using basic context", context);
                    return true;
                }
            });

            // Suggestion processing errors
            RegisterRecoveryStrategy<SuggestionProcessingException>(new ErrorRecoveryStrategy
            {
                RetryCount = 1,
                RetryDelay = TimeSpan.FromMilliseconds(100),
                FallbackAction = async (ex, context, data) =>
                {
                    await _logger.LogWarningAsync("Suggestion processing failed, skipping suggestion", context);
                    return true;
                }
            });

            // General exceptions
            RegisterRecoveryStrategy<Exception>(new ErrorRecoveryStrategy
            {
                RetryCount = 1,
                RetryDelay = TimeSpan.FromMilliseconds(100),
                FallbackAction = async (ex, context, data) =>
                {
                    await _logger.LogErrorAsync(ex, $"Unhandled exception in {context}", data);
                    return false; // Don't continue for unhandled exceptions
                }
            });
        }

        private ErrorRecoveryStrategy GetRecoveryStrategy(Exception exception)
        {
            lock (_lockObject)
            {
                var exceptionType = exception.GetType();

                // Try exact match first
                if (_recoveryStrategies.TryGetValue(exceptionType, out var strategy))
                {
                    return strategy;
                }

                // Try base types
                var currentType = exceptionType.BaseType;
                while (currentType != null)
                {
                    if (_recoveryStrategies.TryGetValue(currentType, out strategy))
                    {
                        return strategy;
                    }
                    currentType = currentType.BaseType;
                }

                // Fallback to general exception strategy
                return _recoveryStrategies.GetValueOrDefault(typeof(Exception), new ErrorRecoveryStrategy());
            }
        }

        private async Task<bool> ExecuteRecoveryStrategyAsync(Exception exception, ErrorRecoveryStrategy strategy, string context, object additionalData)
        {
            for (int attempt = 0; attempt <= strategy.RetryCount; attempt++)
            {
                if (attempt > 0)
                {
                    await _logger.LogInfoAsync($"Retry attempt {attempt} for {exception.GetType().Name}", context);
                    
                    if (strategy.RetryDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(strategy.RetryDelay);
                    }
                }

                // Execute fallback action
                if (strategy.FallbackAction != null)
                {
                    try
                    {
                        var result = await strategy.FallbackAction(exception, context, additionalData);
                        if (result)
                        {
                            return true;
                        }
                    }
                    catch (Exception fallbackException)
                    {
                        await _logger.LogErrorAsync(fallbackException, "Error in recovery strategy fallback", context);
                    }
                }
            }

            return false;
        }

        private async Task ShowUserErrorMessageAsync(string message, Exception exception)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
                if (uiShell != null)
                {
                    var clsid = Guid.Empty;
                    int result;

                    var detailedMessage = _settingsService?.EnableVerboseLogging == true
                        ? $"{message}\n\nDetails: {exception?.Message}"
                        : message;

                    uiShell.ShowMessageBox(
                        0,
                        ref clsid,
                        "Ollama Assistant",
                        detailedMessage,
                        string.Empty,
                        0,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        OLEMSGICON.OLEMSGICON_WARNING,
                        0,
                        out result);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing user error message: {ex.Message}");
            }
        }

        private async Task<bool> CheckOllamaConnectivityAsync()
        {
            try
            {
                // This would normally check actual Ollama connectivity
                // For now, we'll return true as a placeholder
                return await Task.FromResult(true);
            }
            catch
            {
                return false;
            }
        }

        private bool CheckSettingsValidity()
        {
            try
            {
                return _settingsService?.ValidateSettings() ?? false;
            }
            catch
            {
                return false;
            }
        }

        private long GetMemoryUsage()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                return process.WorkingSet64;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<PerformanceMetrics> GetPerformanceMetricsAsync()
        {
            try
            {
                return await Task.FromResult(new PerformanceMetrics
                {
                    AverageResponseTime = TimeSpan.FromMilliseconds(500), // Placeholder
                    SuccessRate = 0.95, // Placeholder
                    ErrorCount = 0 // Placeholder
                });
            }
            catch
            {
                return new PerformanceMetrics();
            }
        }

        #endregion
    }

    /// <summary>
    /// Strategy for recovering from specific types of errors
    /// </summary>
    public class ErrorRecoveryStrategy
    {
        /// <summary>
        /// Number of times to retry the operation
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// Delay between retry attempts
        /// </summary>
        public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Fallback action to execute for recovery
        /// </summary>
        public Func<Exception, string, object, Task<bool>> FallbackAction { get; set; }
    }

    /// <summary>
    /// Result of a health check operation
    /// </summary>
    public class HealthCheckResult
    {
        public bool IsHealthy { get; set; }
        public bool OllamaConnectivity { get; set; }
        public bool SettingsValid { get; set; }
        public long MemoryUsage { get; set; }
        public PerformanceMetrics PerformanceMetrics { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Performance metrics for the extension
    /// </summary>
    public class PerformanceMetrics
    {
        public TimeSpan AverageResponseTime { get; set; }
        public double SuccessRate { get; set; }
        public int ErrorCount { get; set; }
    }

    /// <summary>
    /// Extension methods for safe execution
    /// </summary>
    public static class SafeExecutionExtensions
    {
        /// <summary>
        /// Executes an action safely with error handling
        /// </summary>
        public static async Task SafeExecuteAsync(this Task task, ErrorHandler errorHandler, string context = null)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                await errorHandler.HandleExceptionAsync(ex, context);
            }
        }

        /// <summary>
        /// Executes a function safely with error handling
        /// </summary>
        public static async Task<T> SafeExecuteAsync<T>(this Task<T> task, ErrorHandler errorHandler, string context = null, T defaultValue = default(T))
        {
            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                await errorHandler.HandleExceptionAsync(ex, context);
                return defaultValue;
            }
        }
    }
}