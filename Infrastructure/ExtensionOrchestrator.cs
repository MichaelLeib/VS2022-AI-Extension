using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Infrastructure
{
    /// <summary>
    /// Main orchestrator for the Ollama Assistant extension
    /// Coordinates all components and manages the overall workflow
    /// </summary>
    public class ExtensionOrchestrator : IDisposable, IVsSolutionEvents
    {
        private readonly ServiceContainer _serviceContainer;
        private readonly ILogger _logger;
        private readonly ErrorHandler _errorHandler;
        private readonly ISettingsService _settingsService;
        private readonly ICursorHistoryService _cursorHistoryService;
        private readonly ITextViewService _textViewService;
        private readonly IContextCaptureService _contextCaptureService;
        private readonly IOllamaService _ollamaService;
        private readonly IOllamaConnectionManager _connectionManager;
        private readonly ISuggestionEngine _suggestionEngine;
        private readonly IIntelliSenseIntegration _intelliSenseIntegration;
        private readonly IJumpNotificationService _jumpNotificationService;

        private bool _isInitialized;
        private bool _disposed;
        private readonly object _lockObject = new object();
        
        // Solution event handling
        private IVsSolution _solution;
        private uint _solutionEventsCookie;

        public ExtensionOrchestrator(ServiceContainer serviceContainer)
        {
            _serviceContainer = serviceContainer ?? throw new ArgumentNullException(nameof(serviceContainer));
            
            // Resolve core services
            _logger = _serviceContainer.Resolve<ILogger>();
            _errorHandler = _serviceContainer.Resolve<ErrorHandler>();
            _settingsService = _serviceContainer.Resolve<ISettingsService>();
            _cursorHistoryService = _serviceContainer.Resolve<ICursorHistoryService>();
            _textViewService = _serviceContainer.Resolve<ITextViewService>();
            _contextCaptureService = _serviceContainer.Resolve<IContextCaptureService>();
            _ollamaService = _serviceContainer.Resolve<IOllamaService>();
            _connectionManager = _serviceContainer.Resolve<IOllamaConnectionManager>();
            _suggestionEngine = _serviceContainer.Resolve<ISuggestionEngine>();
            _intelliSenseIntegration = _serviceContainer.Resolve<IIntelliSenseIntegration>();
            _jumpNotificationService = _serviceContainer.Resolve<IJumpNotificationService>();
        }

        #region Properties

        public bool IsInitialized
        {
            get
            {
                lock (_lockObject)
                {
                    return _isInitialized;
                }
            }
        }

        public bool IsEnabled => _settingsService?.IsEnabled ?? false;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialize the orchestrator and wire up all event handlers
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _logger.LogInfoAsync("Initializing Extension Orchestrator", "Orchestrator");

            try
            {
                await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
                {
                    // Initialize service container
                    await _serviceContainer.InitializeServicesAsync();
                    
                    // Setup solution event handling
                    await SetupSolutionEventsAsync();
                    
                    // Wire up event handlers
                    await SetupEventHandlersAsync();
                    
                    // Setup initial state
                    await SetupInitialStateAsync();
                    
                    lock (_lockObject)
                    {
                        _isInitialized = true;
                    }
                    
                    await _logger.LogInfoAsync("Extension Orchestrator initialized successfully", "Orchestrator");
                    
                }, "OrchestratorInitialization");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Failed to initialize Extension Orchestrator", "Orchestrator");
                throw;
            }
        }

        private async Task SetupSolutionEventsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                _solution = Package.GetGlobalService(typeof(SVsSolution)) as IVsSolution;
                if (_solution != null)
                {
                    var hr = _solution.AdviseSolutionEvents(this, out _solutionEventsCookie);
                    if (hr == 0) // S_OK
                    {
                        await _logger.LogInfoAsync("Solution events subscribed successfully", "Orchestrator");
                    }
                    else
                    {
                        await _logger.LogWarningAsync($"Failed to subscribe to solution events. HRESULT: {hr}", "Orchestrator");
                    }
                }
                else
                {
                    await _logger.LogWarningAsync("Could not get IVsSolution service", "Orchestrator");
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error setting up solution events", "Orchestrator");
            }
        }

        private async Task SetupEventHandlersAsync()
        {
            await _logger.LogDebugAsync("Setting up event handlers", "Orchestrator");

            // Text view events
            if (_textViewService != null)
            {
                _textViewService.CaretPositionChanged += OnCaretPositionChanged;
                _textViewService.TextChanged += OnTextChanged;
                _textViewService.TextViewCreated += OnTextViewCreated;
                _textViewService.TextViewClosed += OnTextViewClosed;
            }

            // Settings events
            if (_settingsService != null)
            {
                _settingsService.SettingsChanged += OnSettingsChanged;
            }

            // Jump notification events
            if (_jumpNotificationService != null)
            {
                _jumpNotificationService.JumpExecuted += OnJumpExecuted;
                _jumpNotificationService.JumpDismissed += OnJumpDismissed;
            }

            // IntelliSense events
            if (_intelliSenseIntegration != null)
            {
                _intelliSenseIntegration.SuggestionAccepted += OnSuggestionAccepted;
                _intelliSenseIntegration.SuggestionDismissed += OnSuggestionDismissed;
            }

            // Connection manager events
            if (_connectionManager != null)
            {
                _connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;
                _connectionManager.HealthCheckCompleted += OnHealthCheckCompleted;
            }

            await _logger.LogDebugAsync("Event handlers setup completed", "Orchestrator");
        }

        private async Task SetupInitialStateAsync()
        {
            await _logger.LogDebugAsync("Setting up initial state", "Orchestrator");

            // Perform health check
            if (_errorHandler != null)
            {
                var healthResult = await _errorHandler.PerformHealthCheckAsync();
                await _logger.LogInfoAsync($"Health check completed. Healthy: {healthResult.IsHealthy}", "Orchestrator");
            }

            // Load any cached data or state
            await LoadInitialDataAsync();
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                // Load cursor history if available
                if (_cursorHistoryService != null)
                {
                    // This would load persisted history in a real implementation
                    await _logger.LogDebugAsync("Cursor history service ready", "Orchestrator");
                }

                // Initialize Ollama connection and start monitoring
                if (_ollamaService != null && _connectionManager != null && _settingsService?.IsEnabled == true)
                {
                    await _logger.LogDebugAsync("Initializing Ollama connection monitoring", "Orchestrator");
                    
                    // Wire up the connection manager with the service
                    if (_ollamaService is OllamaService ollamaServiceImpl)
                    {
                        ollamaServiceImpl.SetConnectionManager(_connectionManager);
                    }
                    
                    // Start connection monitoring
                    await _connectionManager.StartMonitoringAsync();
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error loading initial data", "Orchestrator");
            }
        }

        #endregion

        #region Event Handlers

        private async void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            if (!IsEnabled || !IsInitialized)
                return;

            await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
            {
                await _logger.LogDebugAsync($"Caret position changed: Line {e.NewPosition.Line}, Column {e.NewPosition.Column}", "Orchestrator");

                // Record cursor position in history
                var historyEntry = new CursorHistoryEntry
                {
                    FilePath = e.FilePath,
                    Line = e.NewPosition.Line,
                    Column = e.NewPosition.Column,
                    Timestamp = DateTime.Now,
                    Context = await _contextCaptureService.GetContextSnippetAsync(e.NewPosition.Line, e.NewPosition.Column)
                };

                await _cursorHistoryService.AddEntryAsync(historyEntry);

                // Trigger AI suggestions if appropriate
                if (_settingsService.EnableAutoSuggestions)
                {
                    await ProcessAutoSuggestionsAsync(e);
                }

                // Check for jump recommendations
                if (_settingsService.EnableJumpRecommendations)
                {
                    await ProcessJumpRecommendationsAsync(e);
                }

            }, "CaretPositionChanged");
        }

        private async void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsEnabled || !IsInitialized)
                return;

            await _errorHandler.ExecuteWithErrorHandlingAsync(async () =>
            {
                await _logger.LogDebugAsync($"Text changed: {e.Changes?.Count ?? 0} changes", "Orchestrator");

                // Update context for current position
                if (_settingsService.EnableAutoSuggestions)
                {
                    // Debounce rapid typing
                    await Task.Delay(500); // Simple debouncing
                    
                    var currentPosition = _textViewService.GetCurrentPosition();
                    if (currentPosition != null)
                    {
                        await ProcessAutoSuggestionsAsync(new CaretPositionChangedEventArgs
                        {
                            NewPosition = currentPosition,
                            FilePath = _textViewService.GetCurrentFilePath()
                        });
                    }
                }

            }, "TextChanged");
        }

        private async void OnTextViewCreated(object sender, TextViewCreatedEventArgs e)
        {
            await _logger.LogInfoAsync($"Text view created for file: {e.FilePath}", "Orchestrator");
        }

        private async void OnTextViewClosed(object sender, TextViewClosedEventArgs e)
        {
            await _logger.LogInfoAsync($"Text view closed for file: {e.FilePath}", "Orchestrator");
        }

        private async void OnSettingsChanged(object sender, SettingsChangedEventArgs e)
        {
            await _logger.LogInfoAsync($"Settings changed: {e.SettingName}", "Orchestrator");

            // Refresh components based on settings changes
            await RefreshComponentsAsync();
        }

        private async void OnJumpExecuted(object sender, JumpExecutedEventArgs e)
        {
            await _logger.LogInfoAsync($"Jump executed: {e.Success}", "Orchestrator");

            if (e.Success)
            {
                // Record the jump in history
                var historyEntry = new CursorHistoryEntry
                {
                    FilePath = e.Recommendation.TargetFilePath ?? _textViewService.GetCurrentFilePath(),
                    Line = e.Recommendation.TargetLine,
                    Column = e.Recommendation.TargetColumn,
                    Timestamp = DateTime.Now,
                    Context = e.Recommendation.TargetPreview,
                    JumpReason = e.Recommendation.Reason
                };

                await _cursorHistoryService.AddEntryAsync(historyEntry);
            }
        }

        private async void OnJumpDismissed(object sender, JumpDismissedEventArgs e)
        {
            await _logger.LogDebugAsync($"Jump dismissed: {e.Reason}", "Orchestrator");
        }

        private async void OnSuggestionAccepted(object sender, SuggestionAcceptedEventArgs e)
        {
            await _logger.LogInfoAsync("Suggestion accepted", "Orchestrator");

            // Track acceptance for improving future suggestions
            if (e.Suggestion != null)
            {
                await _logger.LogPerformanceAsync("SuggestionAccepted", TimeSpan.FromMilliseconds(e.Suggestion.ProcessingTime));
            }
        }

        private async void OnSuggestionDismissed(object sender, SuggestionDismissedEventArgs e)
        {
            await _logger.LogDebugAsync("Suggestion dismissed", "Orchestrator");
        }

        private async void OnConnectionStatusChanged(object sender, ConnectionStatusChangedEventArgs e)
        {
            var status = e.IsConnected ? "Connected" : "Disconnected";
            await _logger.LogInfoAsync($"Ollama connection status changed: {status}", "Orchestrator");
            
            // Optionally show status in UI
            if (_settingsService?.ShowConnectionNotifications == true)
            {
                // Could show notification here
            }
        }

        private async void OnHealthCheckCompleted(object sender, HealthCheckCompletedEventArgs e)
        {
            var responseTime = e.HealthStatus.ResponseTimeMs;
            var status = e.HealthStatus.IsAvailable ? "healthy" : "unhealthy";
            
            await _logger.LogDebugAsync(
                $"Health check completed: {status} (Response: {responseTime}ms)", 
                "Orchestrator");
            
            // Track performance metrics
            if (e.HealthStatus.IsAvailable)
            {
                await _logger.LogPerformanceAsync("OllamaHealthCheck", TimeSpan.FromMilliseconds(responseTime));
            }
        }

        #endregion

        #region Processing Methods

        private async Task ProcessAutoSuggestionsAsync(CaretPositionChangedEventArgs e)
        {
            try
            {
                // Capture current context
                var context = await _contextCaptureService.CaptureContextAsync(
                    e.FilePath, 
                    e.NewPosition.Line, 
                    e.NewPosition.Column);

                // Get AI suggestions
                var suggestion = await _ollamaService.GetCodeSuggestionAsync(context, CancellationToken.None);

                if (suggestion != null && !string.IsNullOrEmpty(suggestion.CompletionText))
                {
                    // Process and filter suggestions
                    var processedSuggestions = await _suggestionEngine.ProcessSuggestionAsync(suggestion, context);

                    if (processedSuggestions?.Count > 0)
                    {
                        // Show suggestions in IntelliSense
                        await _intelliSenseIntegration.ShowSuggestionsAsync(processedSuggestions);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error processing auto suggestions", "Orchestrator");
            }
        }

        private async Task ProcessJumpRecommendationsAsync(CaretPositionChangedEventArgs e)
        {
            try
            {
                // Capture context for jump analysis
                var context = await _contextCaptureService.CaptureContextAsync(
                    e.FilePath, 
                    e.NewPosition.Line, 
                    e.NewPosition.Column);

                // Analyze for jump opportunities
                var jumpRecommendations = await _suggestionEngine.AnalyzeJumpOpportunitiesAsync(context);

                if (jumpRecommendations?.Count > 0)
                {
                    // Show the best jump recommendation
                    var bestRecommendation = jumpRecommendations[0]; // Assuming sorted by confidence
                    await _jumpNotificationService.ShowJumpNotificationAsync(bestRecommendation);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error processing jump recommendations", "Orchestrator");
            }
        }

        private async Task RefreshComponentsAsync()
        {
            try
            {
                // Update components based on current settings
                if (_jumpNotificationService != null)
                {
                    var options = new JumpNotificationOptions
                    {
                        AutoHideTimeout = _settingsService.JumpNotificationTimeout,
                        ShowTargetPreview = _settingsService.ShowJumpPreview,
                        Opacity = _settingsService.NotificationOpacity / 100.0,
                        AnimateAppearance = _settingsService.AnimateNotifications
                    };
                    
                    _jumpNotificationService.ConfigureNotifications(options);
                }

                // Update key bindings
                if (!string.IsNullOrEmpty(_settingsService.JumpKeyBinding))
                {
                    _jumpNotificationService?.SetJumpKeyBinding(_settingsService.JumpKeyBinding);
                }

                await _logger.LogDebugAsync("Components refreshed with new settings", "Orchestrator");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error refreshing components", "Orchestrator");
            }
        }

        #endregion

        #region Text View Lifecycle Methods

        public void OnTextViewCreated(IWpfTextView textView)
        {
            try
            {
                _logger?.LogInfoAsync($"Text view created in orchestrator", "Orchestrator").ConfigureAwait(false);
                
                // Additional processing for new text views
                if (textView?.TextBuffer != null)
                {
                    var filePath = GetFilePathFromTextView(textView);
                    _logger?.LogDebugAsync($"Text view created for file: {filePath}", "Orchestrator").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewCreated", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextViewClosed(IWpfTextView textView)
        {
            try
            {
                _logger?.LogInfoAsync("Text view closed in orchestrator", "Orchestrator").ConfigureAwait(false);
                
                var filePath = GetFilePathFromTextView(textView);
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Clean up any file-specific data
                    _cursorHistoryService?.CleanupFileDataAsync(filePath).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewClosed", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextViewFocused(IWpfTextView textView)
        {
            try
            {
                _logger?.LogDebugAsync("Text view focused in orchestrator", "Orchestrator").ConfigureAwait(false);
                
                // Update active context when view gets focus
                var filePath = GetFilePathFromTextView(textView);
                if (!string.IsNullOrEmpty(filePath))
                {
                    _logger?.LogDebugAsync($"Active file changed to: {filePath}", "Orchestrator").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewFocused", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextViewLostFocus(IWpfTextView textView)
        {
            try
            {
                _logger?.LogDebugAsync("Text view lost focus in orchestrator", "Orchestrator").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewLostFocus", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextBufferCreated(Microsoft.VisualStudio.Text.ITextBuffer textBuffer)
        {
            try
            {
                _logger?.LogDebugAsync("Text buffer created in orchestrator", "Orchestrator").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferCreated", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextBufferDisposed(Microsoft.VisualStudio.Text.ITextBuffer textBuffer)
        {
            try
            {
                _logger?.LogDebugAsync("Text buffer disposed in orchestrator", "Orchestrator").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferDisposed", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextBufferChanged(object sender, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                if (!IsEnabled || !IsInitialized)
                    return;

                _logger?.LogDebugAsync($"Text buffer changed: {e.Changes.Count} changes", "Orchestrator").ConfigureAwait(false);
                
                // Process the changes for AI analysis
                ProcessTextChangesAsync(sender as Microsoft.VisualStudio.Text.ITextBuffer, e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferChanged", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextBufferPostChanged(Microsoft.VisualStudio.Text.ITextBuffer textBuffer)
        {
            try
            {
                _logger?.LogDebugAsync("Text buffer post-changed in orchestrator", "Orchestrator").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferPostChanged", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnTextBufferChangedLowPriority(object sender, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                _logger?.LogDebugAsync("Text buffer changed low priority in orchestrator", "Orchestrator").ConfigureAwait(false);
                
                // Low priority background processing
                ProcessLowPriorityChangesAsync(sender as Microsoft.VisualStudio.Text.ITextBuffer, e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferChangedLowPriority", "Orchestrator").ConfigureAwait(false);
            }
        }

        public void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                if (!IsEnabled || !IsInitialized)
                    return;

                _logger?.LogDebugAsync("Caret position changed in orchestrator", "Orchestrator").ConfigureAwait(false);
                
                // Process caret position change
                ProcessCaretPositionChangeAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnCaretPositionChanged overload", "Orchestrator").ConfigureAwait(false);
            }
        }

        private async Task ProcessTextChangesAsync(Microsoft.VisualStudio.Text.ITextBuffer textBuffer, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                // Debounce rapid changes
                await Task.Delay(300);
                
                // Process for AI suggestions if enabled
                if (_settingsService?.EnableAutoSuggestions == true)
                {
                    // Get current context and trigger suggestions
                    var filePath = GetFilePathFromTextBuffer(textBuffer);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        await TriggerContextualSuggestionsAsync(filePath, textBuffer);
                    }
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error processing text changes", "Orchestrator");
            }
        }

        private async Task ProcessLowPriorityChangesAsync(Microsoft.VisualStudio.Text.ITextBuffer textBuffer, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                // Background analysis and learning
                if (_settingsService?.EnablePatternLearning == true)
                {
                    // Analyze patterns for future improvements
                    await AnalyzeEditPatternsAsync(textBuffer, e);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error processing low priority changes", "Orchestrator");
            }
        }

        private async Task ProcessCaretPositionChangeAsync(CaretPositionChangedEventArgs e)
        {
            try
            {
                // This handles the MEF-style caret position changes
                await ProcessAutoSuggestionsAsync(new Infrastructure.CaretPositionChangedEventArgs
                {
                    NewPosition = new CursorPosition { Line = e.NewPosition.Line, Column = e.NewPosition.Column },
                    FilePath = e.FilePath
                });
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error processing caret position change", "Orchestrator");
            }
        }

        private async Task TriggerContextualSuggestionsAsync(string filePath, Microsoft.VisualStudio.Text.ITextBuffer textBuffer)
        {
            try
            {
                // Implementation for contextual suggestions based on text changes
                await _logger.LogDebugAsync($"Triggering contextual suggestions for {filePath}", "Orchestrator");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error triggering contextual suggestions", "Orchestrator");
            }
        }

        private async Task AnalyzeEditPatternsAsync(Microsoft.VisualStudio.Text.ITextBuffer textBuffer, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                // Implementation for pattern analysis
                await _logger.LogDebugAsync("Analyzing edit patterns", "Orchestrator");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error analyzing edit patterns", "Orchestrator");
            }
        }

        private string GetFilePathFromTextView(IWpfTextView textView)
        {
            try
            {
                if (textView?.TextBuffer?.Properties.TryGetProperty(typeof(Microsoft.VisualStudio.Text.ITextDocument), out Microsoft.VisualStudio.Text.ITextDocument document) == true)
                {
                    return document.FilePath;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string GetFilePathFromTextBuffer(Microsoft.VisualStudio.Text.ITextBuffer textBuffer)
        {
            try
            {
                if (textBuffer?.Properties.TryGetProperty(typeof(Microsoft.VisualStudio.Text.ITextDocument), out Microsoft.VisualStudio.Text.ITextDocument document) == true)
                {
                    return document.FilePath;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region IVsSolutionEvents Implementation

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            try
            {
                _logger?.LogInfoAsync("Project opened", "SolutionEvents").ConfigureAwait(false);
                
                // Project opened - could initialize project-specific features
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnAfterOpenProject", "SolutionEvents").ConfigureAwait(false);
                return -1; // E_FAIL
            }
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            pfCancel = 0; // Allow closing
            return 0; // S_OK
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            try
            {
                _logger?.LogInfoAsync("Project closing", "SolutionEvents").ConfigureAwait(false);
                
                // Clean up project-specific data
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnBeforeCloseProject", "SolutionEvents").ConfigureAwait(false);
                return -1; // E_FAIL
            }
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return 0; // S_OK
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            pfCancel = 0; // Allow unloading
            return 0; // S_OK
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return 0; // S_OK
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            try
            {
                _logger?.LogInfoAsync("Solution opened", "SolutionEvents").ConfigureAwait(false);
                
                // Solution opened - initialize for new solution
                InitializeForNewSolutionAsync().ConfigureAwait(false);
                
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnAfterOpenSolution", "SolutionEvents").ConfigureAwait(false);
                return -1; // E_FAIL
            }
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            pfCancel = 0; // Allow closing
            return 0; // S_OK
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            try
            {
                _logger?.LogInfoAsync("Solution closing", "SolutionEvents").ConfigureAwait(false);
                
                // Clean up solution-specific data
                CleanupForSolutionCloseAsync().ConfigureAwait(false);
                
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnBeforeCloseSolution", "SolutionEvents").ConfigureAwait(false);
                return -1; // E_FAIL
            }
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            try
            {
                _logger?.LogInfoAsync("Solution closed", "SolutionEvents").ConfigureAwait(false);
                
                // Final cleanup after solution is closed
                FinalCleanupAfterSolutionClosedAsync().ConfigureAwait(false);
                
                return 0; // S_OK
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnAfterCloseSolution", "SolutionEvents").ConfigureAwait(false);
                return -1; // E_FAIL
            }
        }

        private async Task InitializeForNewSolutionAsync()
        {
            try
            {
                await _logger.LogInfoAsync("Initializing for new solution", "SolutionEvents");
                
                // Reset any solution-specific state
                if (_cursorHistoryService != null)
                {
                    // Could load persisted history for this solution
                    await _logger.LogDebugAsync("Cursor history service ready for new solution", "SolutionEvents");
                }
                
                // Check Ollama connectivity for new solution
                if (_ollamaService != null && _settingsService?.IsEnabled == true)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var isAvailable = await _ollamaService.IsAvailableAsync();
                            await _logger.LogInfoAsync($"Ollama connectivity check for new solution: {isAvailable}", "SolutionEvents");
                        }
                        catch (Exception ex)
                        {
                            await _logger.LogWarningAsync($"Ollama connectivity check failed for new solution: {ex.Message}", "SolutionEvents");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error initializing for new solution", "SolutionEvents");
            }
        }

        private async Task CleanupForSolutionCloseAsync()
        {
            try
            {
                await _logger.LogInfoAsync("Cleaning up before solution close", "SolutionEvents");
                
                // Clear cursor history for the closing solution
                if (_cursorHistoryService != null)
                {
                    await _cursorHistoryService.ClearHistoryAsync();
                    await _logger.LogDebugAsync("Cursor history cleared for solution close", "SolutionEvents");
                }
                
                // Cancel any pending AI operations
                // Could implement cancellation logic here
                
                // Clean up any solution-specific cached data
                await _logger.LogDebugAsync("Solution-specific data cleaned up", "SolutionEvents");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error cleaning up for solution close", "SolutionEvents");
            }
        }

        private async Task FinalCleanupAfterSolutionClosedAsync()
        {
            try
            {
                await _logger.LogInfoAsync("Final cleanup after solution closed", "SolutionEvents");
                
                // Final memory cleanup
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                await _logger.LogDebugAsync("Final cleanup completed", "SolutionEvents");
            }
            catch (Exception ex)
            {
                await _logger.LogErrorAsync(ex, "Error in final cleanup", "SolutionEvents");
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
                // Unsubscribe from solution events
                if (_solution != null && _solutionEventsCookie != 0)
                {
                    ThreadHelper.JoinableTaskFactory.Run(async delegate
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        _solution.UnadviseSolutionEvents(_solutionEventsCookie);
                        _solutionEventsCookie = 0;
                        await _logger?.LogInfoAsync("Solution events unsubscribed", "Orchestrator");
                    });
                }

                // Unsubscribe from events
                if (_textViewService != null)
                {
                    _textViewService.CaretPositionChanged -= OnCaretPositionChanged;
                    _textViewService.TextChanged -= OnTextChanged;
                    _textViewService.TextViewCreated -= OnTextViewCreated;
                    _textViewService.TextViewClosed -= OnTextViewClosed;
                }

                if (_settingsService != null)
                {
                    _settingsService.SettingsChanged -= OnSettingsChanged;
                }

                if (_jumpNotificationService != null)
                {
                    _jumpNotificationService.JumpExecuted -= OnJumpExecuted;
                    _jumpNotificationService.JumpDismissed -= OnJumpDismissed;
                }

                if (_intelliSenseIntegration != null)
                {
                    _intelliSenseIntegration.SuggestionAccepted -= OnSuggestionAccepted;
                    _intelliSenseIntegration.SuggestionDismissed -= OnSuggestionDismissed;
                }

                if (_connectionManager != null)
                {
                    _connectionManager.ConnectionStatusChanged -= OnConnectionStatusChanged;
                    _connectionManager.HealthCheckCompleted -= OnHealthCheckCompleted;
                    _connectionManager.StopMonitoringAsync().ConfigureAwait(false);
                }

                _logger?.LogInfoAsync("Extension Orchestrator disposed", "Orchestrator");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing ExtensionOrchestrator: {ex.Message}");
            }
        }

        #endregion
    }

    #region Event Args Classes

    public class CaretPositionChangedEventArgs : EventArgs
    {
        public CursorPosition NewPosition { get; set; }
        public string FilePath { get; set; }
    }

    public class TextChangedEventArgs : EventArgs
    {
        public object Changes { get; set; }
        public string FilePath { get; set; }
    }

    public class TextViewCreatedEventArgs : EventArgs
    {
        public string FilePath { get; set; }
    }

    public class TextViewClosedEventArgs : EventArgs
    {
        public string FilePath { get; set; }
    }

    public class SuggestionAcceptedEventArgs : EventArgs
    {
        public CodeSuggestion Suggestion { get; set; }
        public string AcceptanceMethod { get; set; } // e.g., "Tab", "Enter", "Click"
        public TimeSpan ResponseTime { get; set; }
    }

    public class SuggestionDismissedEventArgs : EventArgs
    {
        public CodeSuggestion Suggestion { get; set; }
        public string Reason { get; set; }
        public TimeSpan DisplayDuration { get; set; }
    }

    #endregion
}