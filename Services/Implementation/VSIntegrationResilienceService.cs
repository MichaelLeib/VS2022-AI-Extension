using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing Visual Studio integration resilience and error recovery
    /// </summary>
    [Export(typeof(IVSIntegrationResilienceService))]
    public class VSIntegrationResilienceService : IVSIntegrationResilienceService
    {
        private readonly IVSServiceProvider _vsServiceProvider;
        private readonly ITextViewService _textViewService;
        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        
        private bool _isVSHealthy = true;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 5;
        private readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(2);

        public VSIntegrationResilienceService(
            IVSServiceProvider vsServiceProvider = null,
            ITextViewService textViewService = null,
            ILogger logger = null)
        {
            _vsServiceProvider = vsServiceProvider;
            _textViewService = textViewService;
            _logger = logger;
        }

        #region Properties

        /// <summary>
        /// Whether VS integration is currently healthy
        /// </summary>
        public bool IsVSHealthy 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _isVSHealthy;
                }
            } 
            private set 
            { 
                lock (_lockObject)
                {
                    var wasHealthy = _isVSHealthy;
                    _isVSHealthy = value;
                    
                    if (wasHealthy != value)
                    {
                        VSHealthStatusChanged?.Invoke(this, new VSHealthStatusChangedEventArgs
                        {
                            IsHealthy = value,
                            ConsecutiveFailures = _consecutiveFailures,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            } 
        }

        /// <summary>
        /// Number of consecutive VS integration failures
        /// </summary>
        public int ConsecutiveFailures 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _consecutiveFailures;
                }
            } 
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when VS health status changes
        /// </summary>
        public event EventHandler<VSHealthStatusChangedEventArgs> VSHealthStatusChanged;

        #endregion

        #region Public Methods

        /// <summary>
        /// Performs a comprehensive health check of VS integration
        /// </summary>
        public async Task<VSIntegrationHealthResult> CheckVSIntegrationHealthAsync()
        {
            try
            {
                await _logger?.LogDebugAsync("Performing VS integration health check", "VSResilience");
                
                var result = new VSIntegrationHealthResult
                {
                    Timestamp = DateTime.UtcNow
                };

                // Check if we're on the UI thread
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Check VS service provider
                if (_vsServiceProvider != null)
                {
                    result.VSServiceProviderHealthy = await _vsServiceProvider.IsVSReadyAsync();
                    if (!result.VSServiceProviderHealthy)
                    {
                        result.Issues.Add("VS Service Provider is not ready");
                    }
                }
                else
                {
                    result.Issues.Add("VS Service Provider is not available");
                }

                // Check text view service
                if (_textViewService != null)
                {
                    try
                    {
                        var activeView = _textViewService.GetActiveTextView();
                        result.TextViewServiceHealthy = activeView != null;
                        if (!result.TextViewServiceHealthy)
                        {
                            result.Issues.Add("No active text view available");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.TextViewServiceHealthy = false;
                        result.Issues.Add($"Text view service error: {ex.Message}");
                    }
                }
                else
                {
                    result.Issues.Add("Text View Service is not available");
                }

                // Check basic editor functionality
                try
                {
                    if (_textViewService != null)
                    {
                        var filePath = _textViewService.GetCurrentFilePath();
                        var language = _textViewService.GetCurrentLanguage();
                        result.EditorFunctionalityHealthy = true; // If no exception, basic functionality works
                    }
                }
                catch (Exception ex)
                {
                    result.EditorFunctionalityHealthy = false;
                    result.Issues.Add($"Editor functionality error: {ex.Message}");
                }

                // Overall health assessment
                result.IsOverallHealthy = result.VSServiceProviderHealthy && 
                                        result.TextViewServiceHealthy && 
                                        result.EditorFunctionalityHealthy;

                // Update health status
                UpdateHealthStatus(result.IsOverallHealthy);

                _lastHealthCheck = DateTime.UtcNow;

                await _logger?.LogDebugAsync($"VS integration health check completed. Healthy: {result.IsOverallHealthy}", "VSResilience");

                return result;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error during VS integration health check", "VSResilience");
                
                UpdateHealthStatus(false);
                
                return new VSIntegrationHealthResult
                {
                    IsOverallHealthy = false,
                    Timestamp = DateTime.UtcNow,
                    Issues = { $"Health check failed: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Attempts to recover from VS integration failures
        /// </summary>
        public async Task<bool> AttemptVSIntegrationRecoveryAsync()
        {
            try
            {
                await _logger?.LogInfoAsync("Attempting VS integration recovery", "VSResilience");

                // Attempt service provider recovery
                bool serviceProviderRecovered = true;
                if (_vsServiceProvider != null)
                {
                    serviceProviderRecovered = await _vsServiceProvider.AttemptServiceRecoveryAsync();
                    if (!serviceProviderRecovered)
                    {
                        await _logger?.LogWarningAsync("VS Service Provider recovery failed", "VSResilience");
                    }
                }

                // Attempt text view service recovery (refresh active view)
                bool textViewRecovered = true;
                if (_textViewService != null)
                {
                    try
                    {
                        // Force refresh of the active text view
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _textViewService.SetActiveTextView(null); // Clear cached view
                        var newView = _textViewService.GetActiveTextView(); // Get fresh view
                        textViewRecovered = newView != null;
                        
                        if (!textViewRecovered)
                        {
                            await _logger?.LogWarningAsync("Text View Service recovery failed - no active view", "VSResilience");
                        }
                    }
                    catch (Exception ex)
                    {
                        textViewRecovered = false;
                        await _logger?.LogErrorAsync(ex, "Text View Service recovery failed", "VSResilience");
                    }
                }

                // Perform health check to verify recovery
                var healthResult = await CheckVSIntegrationHealthAsync();
                var recovered = healthResult.IsOverallHealthy;

                if (recovered)
                {
                    await _logger?.LogInfoAsync("VS integration recovery successful", "VSResilience");
                    lock (_lockObject)
                    {
                        _consecutiveFailures = 0;
                    }
                }
                else
                {
                    await _logger?.LogWarningAsync("VS integration recovery partially failed", "VSResilience");
                }

                return recovered;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "VS integration recovery failed", "VSResilience");
                return false;
            }
        }

        /// <summary>
        /// Reports a VS integration failure
        /// </summary>
        public async Task ReportVSIntegrationFailureAsync(string component, string operation, Exception exception = null)
        {
            var message = $"VS integration failure in {component} during {operation}";
            if (exception != null)
            {
                message += $": {exception.Message}";
            }

            await _logger?.LogWarningAsync(message, "VSResilience");

            UpdateHealthStatus(false);

            // Auto-attempt recovery if too many failures
            lock (_lockObject)
            {
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    // Don't block the calling thread
                    _ = Task.Run(async () =>
                    {
                        await _logger?.LogInfoAsync($"Auto-attempting recovery after {_consecutiveFailures} consecutive failures", "VSResilience");
                        await AttemptVSIntegrationRecoveryAsync();
                    });
                }
            }
        }

        /// <summary>
        /// Executes an operation with VS integration error handling
        /// </summary>
        public async Task<T> ExecuteWithVSErrorHandlingAsync<T>(Func<Task<T>> operation, string component, string operationName, T defaultValue = default(T))
        {
            try
            {
                // Check if we should attempt the operation
                if (!ShouldAttemptVSOperation())
                {
                    await _logger?.LogDebugAsync($"Skipping {component}.{operationName} - VS integration unhealthy", "VSResilience");
                    return defaultValue;
                }

                var result = await operation();
                
                // Report success (helps reset failure count)
                ReportVSOperationSuccess();
                
                return result;
            }
            catch (Exception ex)
            {
                await ReportVSIntegrationFailureAsync(component, operationName, ex);
                return defaultValue;
            }
        }

        /// <summary>
        /// Executes an operation with VS integration error handling (void return)
        /// </summary>
        public async Task ExecuteWithVSErrorHandlingAsync(Func<Task> operation, string component, string operationName)
        {
            try
            {
                // Check if we should attempt the operation
                if (!ShouldAttemptVSOperation())
                {
                    await _logger?.LogDebugAsync($"Skipping {component}.{operationName} - VS integration unhealthy", "VSResilience");
                    return;
                }

                await operation();
                
                // Report success (helps reset failure count)
                ReportVSOperationSuccess();
            }
            catch (Exception ex)
            {
                await ReportVSIntegrationFailureAsync(component, operationName, ex);
            }
        }

        /// <summary>
        /// Determines if a VS operation should be attempted based on current health
        /// </summary>
        public bool ShouldAttemptVSOperation()
        {
            lock (_lockObject)
            {
                // Don't attempt if too many consecutive failures
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    // Allow retry after some time has passed
                    return DateTime.UtcNow - _lastHealthCheck > HealthCheckInterval;
                }

                return _isVSHealthy || _consecutiveFailures < 3; // Allow some failures before stopping
            }
        }

        /// <summary>
        /// Forces a health check if enough time has passed
        /// </summary>
        public async Task<bool> EnsureVSHealthAsync()
        {
            lock (_lockObject)
            {
                if (DateTime.UtcNow - _lastHealthCheck < HealthCheckInterval)
                {
                    return _isVSHealthy;
                }
            }

            var healthResult = await CheckVSIntegrationHealthAsync();
            return healthResult.IsOverallHealthy;
        }

        #endregion

        #region Private Methods

        private void UpdateHealthStatus(bool isHealthy)
        {
            lock (_lockObject)
            {
                if (isHealthy)
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    _consecutiveFailures++;
                }

                IsVSHealthy = isHealthy;
            }
        }

        private void ReportVSOperationSuccess()
        {
            lock (_lockObject)
            {
                if (_consecutiveFailures > 0)
                {
                    _consecutiveFailures = Math.Max(0, _consecutiveFailures - 1);
                    
                    // If we've recovered from failures, update health status
                    if (_consecutiveFailures == 0 && !_isVSHealthy)
                    {
                        IsVSHealthy = true;
                    }
                }
            }
        }

        #endregion
    }

    #region Data Classes

    /// <summary>
    /// Result of VS integration health check
    /// </summary>
    public class VSIntegrationHealthResult
    {
        public bool IsOverallHealthy { get; set; }
        public bool VSServiceProviderHealthy { get; set; }
        public bool TextViewServiceHealthy { get; set; }
        public bool EditorFunctionalityHealthy { get; set; }
        public DateTime Timestamp { get; set; }
        public System.Collections.Generic.List<string> Issues { get; set; } = new System.Collections.Generic.List<string>();
    }

    /// <summary>
    /// Event arguments for VS health status changes
    /// </summary>
    public class VSHealthStatusChangedEventArgs : EventArgs
    {
        public bool IsHealthy { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}