using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for managing Ollama server connection, health monitoring, and graceful degradation
    /// </summary>
    [Export(typeof(IOllamaConnectionManager))]
    public class OllamaConnectionManager : IOllamaConnectionManager, IDisposable
    {
        private readonly IOllamaService _ollamaService;
        private readonly ISettingsService _settingsService;
        private readonly IVSStatusBarService _statusBarService;
        private readonly IVSOutputWindowService _outputWindowService;
        private readonly ILogger _logger;
        
        private Timer _healthCheckTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;
        private bool _isConnected;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private OllamaHealthStatus _lastHealthStatus;
        private bool _offlineMode;
        private DateTime _lastConnectionAttempt = DateTime.MinValue;
        private int _consecutiveFailures;
        private const int MaxConsecutiveFailures = 5;
        private readonly TimeSpan MinRetryInterval = TimeSpan.FromMinutes(1);

        public OllamaConnectionManager(
            IOllamaService ollamaService,
            ISettingsService settingsService,
            IVSStatusBarService statusBarService = null,
            IVSOutputWindowService outputWindowService = null,
            ILogger logger = null)
        {
            _ollamaService = ollamaService ?? throw new ArgumentNullException(nameof(ollamaService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _statusBarService = statusBarService;
            _outputWindowService = outputWindowService;
            _logger = logger;

            // Subscribe to settings changes to restart health monitoring
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        #region Properties

        /// <summary>
        /// Whether the connection to Ollama is currently healthy
        /// </summary>
        public bool IsConnected 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _isConnected;
                }
            } 
            private set 
            { 
                lock (_lockObject)
                {
                    var wasConnected = _isConnected;
                    _isConnected = value;
                    
                    if (wasConnected != value)
                    {
                        ConnectionStatusChanged?.Invoke(this, new ConnectionStatusChangedEventArgs
                        {
                            IsConnected = value,
                            Timestamp = DateTime.UtcNow,
                            HealthStatus = _lastHealthStatus
                        });
                    }
                }
            } 
        }

        /// <summary>
        /// The last health status received from the server
        /// </summary>
        public OllamaHealthStatus LastHealthStatus => _lastHealthStatus;

        /// <summary>
        /// How long ago the last health check was performed
        /// </summary>
        public TimeSpan TimeSinceLastHealthCheck => DateTime.UtcNow - _lastHealthCheck;

        /// <summary>
        /// Whether the extension is currently operating in offline mode
        /// </summary>
        public bool IsOfflineMode 
        { 
            get 
            { 
                lock (_lockObject)
                {
                    return _offlineMode;
                }
            } 
            private set 
            { 
                lock (_lockObject)
                {
                    _offlineMode = value;
                }
            } 
        }

        /// <summary>
        /// Number of consecutive connection failures
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
        /// Fired when the connection status changes
        /// </summary>
        public event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;

        /// <summary>
        /// Fired when a health check completes
        /// </summary>
        public event EventHandler<HealthCheckCompletedEventArgs> HealthCheckCompleted;

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts periodic health monitoring
        /// </summary>
        public async Task StartMonitoringAsync()
        {
            await _logger?.LogInfoAsync("Starting Ollama connection monitoring", "ConnectionManager");

            lock (_lockObject)
            {
                if (_disposed)
                    return;

                // Stop existing timer
                _healthCheckTimer?.Dispose();
                
                // Start new timer - check every 30 seconds
                _healthCheckTimer = new Timer(async _ => await PerformHealthCheckAsync(), 
                    null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
            }
        }

        /// <summary>
        /// Stops health monitoring
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            await _logger?.LogInfoAsync("Stopping Ollama connection monitoring", "ConnectionManager");

            lock (_lockObject)
            {
                _healthCheckTimer?.Dispose();
                _healthCheckTimer = null;
            }
        }

        /// <summary>
        /// Attempts to reconnect to the Ollama server
        /// </summary>
        public async Task<bool> AttemptReconnectAsync(CancellationToken cancellationToken = default)
        {
            await _logger?.LogInfoAsync("Attempting to reconnect to Ollama server", "ConnectionManager");
            await _statusBarService?.ShowAIProcessingAsync("Reconnecting to Ollama...");

            try
            {
                // Reset connection tracking for this attempt
                lock (_lockObject)
                {
                    _lastConnectionAttempt = DateTime.UtcNow;
                }

                // Handle failed reconnection
                lock (_lockObject)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _offlineMode = true;
                    }
                }

                var failureMessage = IsOfflineMode 
                    ? "Failed to reconnect - entering offline mode"
                    : "Failed to reconnect to Ollama";
                
                await _statusBarService?.ShowAIErrorAsync(failureMessage);
                await _logger?.LogWarningAsync($"Failed to reconnect to Ollama after 3 attempts. Consecutive failures: {_consecutiveFailures}", "ConnectionManager");
                
                return false;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error during reconnection attempt", "ConnectionManager");
                await _statusBarService?.ShowAIErrorAsync($"Reconnection failed: {ex.Message}");
                
                // Increment failure count on exception
                lock (_lockObject)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _offlineMode = true;
                    }
                }
                
                return false;
            }
        }

        /// <summary>
        /// Gets the current connection info for display
        /// </summary>
        public async Task<string> GetConnectionInfoAsync()
        {
            try
            {
                if (!IsConnected)
                    return "Disconnected";

                var modelInfo = await _ollamaService.GetModelInfoAsync();
                var endpoint = _settingsService.OllamaEndpoint;
                var host = new Uri(endpoint).Host;
                
                return $"{modelInfo.Name}@{host}";
            }
            catch
            {
                return IsConnected ? "Connected" : "Disconnected";
            }
        }

        /// <summary>
        /// Handles graceful degradation when Ollama is unavailable
        /// </summary>
        public async Task HandleServerUnavailableAsync(string operation = null)
        {
            lock (_lockObject)
            {
                _consecutiveFailures++;
                _lastConnectionAttempt = DateTime.UtcNow;
                
                // Enter offline mode after too many failures
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _offlineMode = true;
                }
            }

            var message = string.IsNullOrEmpty(operation) 
                ? "Ollama server is unavailable" 
                : $"Ollama server unavailable for {operation}";

            if (IsOfflineMode)
            {
                message += " - Operating in offline mode";
                await _logger?.LogWarningAsync($"{message}. Too many consecutive failures ({_consecutiveFailures}), entering offline mode.", "ConnectionManager");
                await _statusBarService?.ShowAIErrorAsync("Offline Mode: AI features disabled");
                await _outputWindowService?.WriteWarningAsync("Extension is now operating in offline mode due to repeated connection failures. AI features will be disabled until connection is restored.", "Connection");
            }
            else
            {
                await _logger?.LogWarningAsync($"{message}. Failure count: {_consecutiveFailures}/{MaxConsecutiveFailures}", "ConnectionManager");
                await _statusBarService?.ShowAIErrorAsync(message);
            }
            
            // Schedule automatic retry if not in offline mode
            if (!IsOfflineMode)
            {
                _ = Task.Run(async () => await ScheduleRetryAsync());
            }
        }

        /// <summary>
        /// Checks if the system should attempt a connection based on retry logic
        /// </summary>
        public bool ShouldAttemptConnection()
        {
            lock (_lockObject)
            {
                if (!_offlineMode)
                    return true;

                // Allow retry attempts in offline mode after the minimum interval
                return DateTime.UtcNow - _lastConnectionAttempt >= MinRetryInterval;
            }
        }

        /// <summary>
        /// Forces exit from offline mode and attempts reconnection
        /// </summary>
        public async Task<bool> ForceReconnectFromOfflineModeAsync(CancellationToken cancellationToken = default)
        {
            await _logger?.LogInfoAsync("Forcing reconnection from offline mode", "ConnectionManager");
            
            lock (_lockObject)
            {
                _offlineMode = false;
                _consecutiveFailures = 0;
            }

            return await AttemptReconnectAsync(cancellationToken);
        }

        /// <summary>
        /// Provides user-friendly error messages based on connection status
        /// </summary>
        public async Task<string> GetUserFriendlyStatusAsync()
        {
            if (IsConnected)
            {
                var connectionInfo = await GetConnectionInfoAsync();
                return $"Connected to {connectionInfo}";
            }
            else if (IsOfflineMode)
            {
                return $"Offline Mode - {_consecutiveFailures} consecutive failures. AI features disabled. Use 'Reconnect' to retry.";
            }
            else if (_lastHealthStatus != null)
            {
                return $"Disconnected: {_lastHealthStatus.Error}";
            }
            else
            {
                return "Not connected to Ollama server";
            }
        }

        #endregion

        #region Private Methods

        private async Task PerformHealthCheckAsync()
        {
            if (_disposed)
                return;

            try
            {
                await _logger?.LogDebugAsync("Performing periodic health check", "ConnectionManager");
                
                _lastHealthCheck = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error during periodic health check", "ConnectionManager");
                
                var errorStatus = new OllamaHealthStatus 
                { 
                    IsAvailable = false, 
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };
                
                await ProcessHealthStatus(errorStatus);
            }
        }

        private async Task ProcessHealthStatus(OllamaHealthStatus healthStatus)
        {
            _lastHealthStatus = healthStatus;
            var wasConnected = IsConnected;
            IsConnected = healthStatus.IsAvailable;

            // Update connection tracking
            lock (_lockObject)
            {
                if (IsConnected)
                {
                    // Reset failure tracking on successful connection
                    if (_consecutiveFailures > 0 || _offlineMode)
                    {
                        var wasOffline = _offlineMode;
                        _consecutiveFailures = 0;
                        _offlineMode = false;
                        
                        if (wasOffline)
                        {
                            _ = Task.Run(async () => await _logger?.LogInfoAsync("Successfully exited offline mode - AI features restored", "ConnectionManager"));
                            _ = Task.Run(async () => await _outputWindowService?.WriteInfoAsync("Connection restored - AI features are now available", "Connection"));
                        }
                    }
                }
                else
                {
                    _consecutiveFailures++;
                    _lastConnectionAttempt = DateTime.UtcNow;
                    
                    // Enter offline mode if too many failures
                    if (_consecutiveFailures >= MaxConsecutiveFailures && !_offlineMode)
                    {
                        _offlineMode = true;
                        _ = Task.Run(async () => await _logger?.LogWarningAsync($"Entering offline mode after {_consecutiveFailures} consecutive failures", "ConnectionManager"));
                        _ = Task.Run(async () => await _outputWindowService?.WriteWarningAsync("Entering offline mode - AI features will be disabled", "Connection"));
                    }
                }
            }

            // Log status changes
            if (wasConnected != IsConnected)
            {
                var statusMessage = IsConnected 
                    ? $"Ollama server is now available (Response time: {healthStatus.ResponseTimeMs}ms)"
                    : $"Ollama server is unavailable: {healthStatus.Error}";

                await _logger?.LogInfoAsync(statusMessage, "ConnectionManager");
                await _outputWindowService?.WriteInfoAsync(statusMessage, "Connection");

                // Update status bar
                var connectionInfo = await GetConnectionInfoAsync();
                await _statusBarService?.ShowConnectionStatusAsync(IsConnected, connectionInfo);
            }

            // Fire event for subscribers
            HealthCheckCompleted?.Invoke(this, new HealthCheckCompletedEventArgs
            {
                HealthStatus = healthStatus,
                PreviouslyConnected = wasConnected,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Schedules an automatic retry attempt
        /// </summary>
        private async Task ScheduleRetryAsync()
        {
            try
            {
                // Wait for retry interval based on failure count
                var retryDelay = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, Math.Max(0, _consecutiveFailures - 1)), 300)); // Max 5 minutes
                await Task.Delay(retryDelay);
                
                if (!IsConnected && !IsOfflineMode)
                {
                    await _logger?.LogDebugAsync($"Attempting automatic reconnection (failure count: {_consecutiveFailures})", "ConnectionManager");
                }
            }
            catch (Exception ex)
            {
                await _logger?.LogErrorAsync(ex, "Error in automatic retry logic", "ConnectionManager");
            }
        }

        private void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            // Restart monitoring if connection settings changed
            if (e.SettingName == nameof(ISettingsService.OllamaEndpoint) ||
                e.SettingName == nameof(ISettingsService.OllamaTimeout))
            {
                Task.Run(async () =>
                {
                    await StopMonitoringAsync();
                    await Task.Delay(1000); // Brief pause
                    await StartMonitoringAsync();
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
                _healthCheckTimer?.Dispose();
                _healthCheckTimer = null;

                if (_settingsService != null)
                {
                    _settingsService.SettingsChanged -= OnSettingsChanged;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error disposing ConnectionManager", "ConnectionManager").Wait();
            }
        }

        #endregion
    }

    #region Event Args Classes

    /// <summary>
    /// Event arguments for connection status changes
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; set; }
        public DateTime Timestamp { get; set; }
        public OllamaHealthStatus HealthStatus { get; set; }
    }

    /// <summary>
    /// Event arguments for health check completion
    /// </summary>
    public class HealthCheckCompletedEventArgs : EventArgs
    {
        public OllamaHealthStatus HealthStatus { get; set; }
        public bool PreviouslyConnected { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}