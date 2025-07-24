using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Commands;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using Task = System.Threading.Tasks.Task;

namespace OllamaAssistant
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(OllamaAssistantPackage.PackageGuidString)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(UI.OptionPages.GeneralOptionsPage), "Ollama Assistant", "General", 0, 0, true)]
    [ProvideOptionPage(typeof(UI.OptionPages.AdvancedOptionsPage), "Ollama Assistant", "Advanced", 0, 1, true)]
    public sealed class OllamaAssistantPackage : AsyncPackage
    {
        public const string PackageGuidString = "d7c3f7b1-9b4e-4f5a-8c7d-2e1f3a4b5c6d";

        private ServiceContainer _serviceContainer;
        private ExtensionOrchestrator _orchestrator;
        private ILogger _logger;

        #region Package Members

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            
            try
            {
                // Report initialization progress
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Setting up services...", 0, 5));

                // Initialize service container
                _serviceContainer = new ServiceContainer();
                ServiceLocator.Initialize(_serviceContainer);

                // Register all services
                await RegisterServicesAsync();
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Services registered", 1, 5));

                // Initialize core infrastructure
                await InitializeCoreInfrastructureAsync();
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Core infrastructure ready", 2, 5));

                // Initialize orchestrator
                await InitializeOrchestratorAsync();
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Orchestrator initialized", 3, 5));

                // Initialize commands
                await InitializeCommandsAsync();
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Commands registered", 4, 5));

                // Final initialization
                await FinalizeInitializationAsync();
                progress?.Report(new ServiceProgressData("Initializing Ollama Assistant", "Initialization complete", 5, 5));

                await _logger?.LogInfoAsync("Ollama Assistant package initialized successfully", "Package");
            }
            catch (Exception ex)
            {
                // Fallback logging if logger not available
                System.Diagnostics.Debug.WriteLine($"Failed to initialize Ollama Assistant: {ex.Message}");
                
                // Try to log with basic VS Activity Log
                if (await GetServiceAsync(typeof(SVsActivityLog)) is IVsActivityLog log)
                {
                    log.LogEntry((uint)__ACTIVITYLOG_ENTRYTYPE.ALE_ERROR,
                        "OllamaAssistant",
                        $"Initialization failed: {ex.Message}");
                }
                throw;
            }
        }

        private async Task RegisterServicesAsync()
        {
            // Register settings service first (other services depend on it)
            var settingsService = new SettingsService(this);
            _serviceContainer.RegisterSingleton<ISettingsService, SettingsService>(settingsService);

            // Register logging and error handling
            var logger = new Logger(settingsService, this);
            _serviceContainer.RegisterSingleton<ILogger, Logger>(logger);
            _logger = logger; // Keep reference for disposal

            var errorHandler = new ErrorHandler(logger, settingsService);
            _serviceContainer.RegisterSingleton<ErrorHandler, ErrorHandler>(errorHandler);

            // Register core services
            var cursorHistoryService = new CursorHistoryService(settingsService.CursorHistoryMemoryDepth);
            _serviceContainer.RegisterSingleton<ICursorHistoryService, CursorHistoryService>(cursorHistoryService);

            var textViewService = new TextViewService();
            _serviceContainer.RegisterSingleton<ITextViewService, TextViewService>(textViewService);

            var contextCaptureService = new ContextCaptureService(cursorHistoryService, textViewService);
            _serviceContainer.RegisterSingleton<IContextCaptureService, ContextCaptureService>(contextCaptureService);

            var ollamaService = new OllamaService(settingsService, logger, errorHandler);
            _serviceContainer.RegisterSingleton<IOllamaService, OllamaService>(ollamaService);

            var suggestionEngine = new SuggestionEngine(settingsService, contextCaptureService);
            _serviceContainer.RegisterSingleton<ISuggestionEngine, SuggestionEngine>(suggestionEngine);

            // Register UI services
            var intelliSenseIntegration = new IntelliSenseIntegration(null); // Completion broker will be set later
            _serviceContainer.RegisterSingleton<IIntelliSenseIntegration, IntelliSenseIntegration>(intelliSenseIntegration);

            var jumpNotificationService = new JumpNotificationService(textViewService);
            _serviceContainer.RegisterSingleton<IJumpNotificationService, JumpNotificationService>(jumpNotificationService);

            // Register VS integration services
            var outputWindowService = new VSOutputWindowService();
            _serviceContainer.RegisterSingleton<IVSOutputWindowService, VSOutputWindowService>(outputWindowService);
            await outputWindowService.InitializeAsync();

            var statusBarService = new VSStatusBarService();
            _serviceContainer.RegisterSingleton<IVSStatusBarService, VSStatusBarService>(statusBarService);
            await statusBarService.InitializeAsync();

            var documentTrackingService = new VSDocumentTrackingService();
            _serviceContainer.RegisterSingleton<IVSDocumentTrackingService, VSDocumentTrackingService>(documentTrackingService);
            await documentTrackingService.InitializeAsync();

            var vsServiceProvider = new VSServiceProvider();
            _serviceContainer.RegisterSingleton<IVSServiceProvider, VSServiceProvider>(vsServiceProvider);
            await vsServiceProvider.InitializeAsync();

            var settingsPersistenceService = new VSSettingsPersistenceService();
            _serviceContainer.RegisterSingleton<IVSSettingsPersistenceService, VSSettingsPersistenceService>(settingsPersistenceService);
            await settingsPersistenceService.InitializeAsync();

            await _logger.LogInfoAsync("All services registered successfully", "Package");
        }

        private async Task InitializeCoreInfrastructureAsync()
        {
            // Initialize the service container
            await _serviceContainer.InitializeServicesAsync();
            
            await _logger.LogInfoAsync("Core infrastructure initialized", "Package");
        }

        private async Task InitializeOrchestratorAsync()
        {
            // Create and initialize the main orchestrator
            _orchestrator = new ExtensionOrchestrator(_serviceContainer);
            _serviceContainer.RegisterSingleton<ExtensionOrchestrator, ExtensionOrchestrator>(_orchestrator);

            // Initialize the orchestrator
            await _orchestrator.InitializeAsync(DisposalToken);
            
            await _logger.LogInfoAsync("Extension orchestrator initialized", "Package");
        }

        private async Task InitializeCommandsAsync()
        {
            // Initialize command handlers
            await OllamaAssistantCommands.InitializeAsync(this, _serviceContainer);
            
            await _logger.LogInfoAsync("Commands initialized", "Package");
        }

        private async Task FinalizeInitializationAsync()
        {
            // Perform any final initialization steps
            var errorHandler = _serviceContainer.Resolve<ErrorHandler>();
            var healthResult = await errorHandler.PerformHealthCheckAsync();
            
            await _logger.LogInfoAsync($"Initial health check completed. Healthy: {healthResult.IsHealthy}", "Package");
            
            // Log configuration summary
            var settingsService = _serviceContainer.Resolve<ISettingsService>();
            await _logger.LogInfoAsync($"Extension enabled: {settingsService.IsEnabled}", "Package");
            await _logger.LogInfoAsync($"Code predictions enabled: {settingsService.CodePredictionEnabled}", "Package");
            await _logger.LogInfoAsync($"Jump recommendations enabled: {settingsService.JumpRecommendationsEnabled}", "Package");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _logger?.LogInfoAsync("Disposing Ollama Assistant package", "Package").Wait(5000);
                    
                    // Dispose orchestrator first
                    _orchestrator?.Dispose();
                    
                    // Cleanup service locator
                    ServiceLocator.Cleanup();
                    
                    // Dispose service container (this will dispose all registered services)
                    _serviceContainer?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing OllamaAssistantPackage: {ex.Message}");
                }
            }
            
            base.Dispose(disposing);
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Get the service container for this package
        /// </summary>
        public ServiceContainer ServiceContainer => _serviceContainer;

        /// <summary>
        /// Get the extension orchestrator
        /// </summary>
        public ExtensionOrchestrator Orchestrator => _orchestrator;

        #endregion
    }
}

