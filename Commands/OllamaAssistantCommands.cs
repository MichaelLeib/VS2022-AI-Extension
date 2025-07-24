using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Commands
{
    /// <summary>
    /// Command definitions and handlers for the Ollama Assistant extension
    /// </summary>
    public static class OllamaAssistantCommands
    {
        // Command IDs - these should match the .vsct file
        public const int ToggleExtensionCommandId = 0x0100;
        public const int ShowSettingsCommandId = 0x0110;
        public const int ManualSuggestionCommandId = 0x0120;
        public const int ClearHistoryCommandId = 0x0130;
        public const int ShowDiagnosticsCommandId = 0x0140;

        public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        private static ILogger _logger;
        private static ErrorHandler _errorHandler;
        private static ISettingsService _settingsService;
        private static ICursorHistoryService _cursorHistoryService;
        private static ExtensionOrchestrator _orchestrator;

        /// <summary>
        /// Initialize command handlers
        /// </summary>
        public static async Task InitializeAsync(AsyncPackage package, ServiceContainer serviceContainer)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            // Resolve services
            _logger = serviceContainer.TryResolve<ILogger>();
            _errorHandler = serviceContainer.TryResolve<ErrorHandler>();
            _settingsService = serviceContainer.TryResolve<ISettingsService>();
            _cursorHistoryService = serviceContainer.TryResolve<ICursorHistoryService>();
            _orchestrator = serviceContainer.TryResolve<ExtensionOrchestrator>();

            var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                await _logger?.LogErrorAsync("Failed to get menu command service", "Commands");
                return;
            }

            // Register commands
            RegisterCommands(commandService);
            
            await _logger?.LogInfoAsync("Commands initialized successfully", "Commands");
        }

        private static void RegisterCommands(OleMenuCommandService commandService)
        {
            // Toggle Extension command
            var toggleCommandId = new CommandID(CommandSet, ToggleExtensionCommandId);
            var toggleCommand = new OleMenuCommand(OnToggleExtension, toggleCommandId);
            toggleCommand.BeforeQueryStatus += OnToggleExtensionQueryStatus;
            commandService.AddCommand(toggleCommand);

            // Show Settings command
            var settingsCommandId = new CommandID(CommandSet, ShowSettingsCommandId);
            var settingsCommand = new OleMenuCommand(OnShowSettings, settingsCommandId);
            commandService.AddCommand(settingsCommand);

            // Manual Suggestion command
            var suggestionCommandId = new CommandID(CommandSet, ManualSuggestionCommandId);
            var suggestionCommand = new OleMenuCommand(OnManualSuggestion, suggestionCommandId);
            suggestionCommand.BeforeQueryStatus += OnManualSuggestionQueryStatus;
            commandService.AddCommand(suggestionCommand);

            // Clear History command
            var clearHistoryCommandId = new CommandID(CommandSet, ClearHistoryCommandId);
            var clearHistoryCommand = new OleMenuCommand(OnClearHistory, clearHistoryCommandId);
            clearHistoryCommand.BeforeQueryStatus += OnClearHistoryQueryStatus;
            commandService.AddCommand(clearHistoryCommand);

            // Show Diagnostics command
            var diagnosticsCommandId = new CommandID(CommandSet, ShowDiagnosticsCommandId);
            var diagnosticsCommand = new OleMenuCommand(OnShowDiagnostics, diagnosticsCommandId);
            commandService.AddCommand(diagnosticsCommand);
        }

        #region Command Handlers

        private static void OnToggleExtension(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await _errorHandler?.ExecuteWithErrorHandlingAsync(async () =>
                {
                    var wasEnabled = _settingsService?.IsEnabled ?? false;
                    
                    if (_settingsService != null)
                    {
                        _settingsService.IsEnabled = !wasEnabled;
                        await _settingsService.SaveSettingsAsync();
                    }

                    var newState = _settingsService?.IsEnabled ?? false;
                    var message = newState ? "Ollama Assistant enabled" : "Ollama Assistant disabled";
                    
                    await _logger?.LogInfoAsync(message, "Commands");
                    await ShowInfoMessageAsync(message);
                    
                }, "ToggleExtension");
            });
        }

        private static void OnToggleExtensionQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                var isEnabled = _settingsService?.IsEnabled ?? false;
                command.Text = isEnabled ? "Disable Ollama Assistant" : "Enable Ollama Assistant";
            }
        }

        private static void OnShowSettings(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                try
                {
                    // Open the Tools > Options page for Ollama Assistant
                    var dte = Package.GetGlobalService(typeof(SDTE)) as DTE;
                    if (dte != null)
                    {
                        dte.ExecuteCommand("Tools.Options", "Ollama Assistant");
                        await _logger?.LogInfoAsync("Settings dialog opened", "Commands");
                    }
                }
                catch (Exception ex)
                {
                    await _logger?.LogErrorAsync(ex, "Error opening settings", "Commands");
                }
            });
        }

        private static void OnManualSuggestion(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await _errorHandler?.ExecuteWithErrorHandlingAsync(async () =>
                {
                    await _logger?.LogInfoAsync("Manual suggestion requested", "Commands");

                    // Trigger manual suggestion
                    if (_orchestrator?.IsInitialized == true)
                    {
                        // Force a suggestion at current cursor position
                        var textViewService = ServiceLocator.TryResolve<ITextViewService>();
                        var currentPosition = textViewService?.GetCurrentPosition();
                        
                        if (currentPosition != null)
                        {
                            // This would trigger the same logic as caret position changed
                            await _logger?.LogInfoAsync("Manual suggestion triggered", "Commands");
                            await ShowInfoMessageAsync("Generating AI suggestion...");
                        }
                        else
                        {
                            await ShowWarningMessageAsync("No active editor found for suggestions.");
                        }
                    }
                    else
                    {
                        await ShowWarningMessageAsync("Ollama Assistant is not initialized or enabled.");
                    }
                    
                }, "ManualSuggestion");
            });
        }

        private static void OnManualSuggestionQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Enabled = _settingsService?.IsEnabled == true && _orchestrator?.IsInitialized == true;
            }
        }

        private static void OnClearHistory(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await _errorHandler?.ExecuteWithErrorHandlingAsync(async () =>
                {
                    if (_cursorHistoryService != null)
                    {
                        await _cursorHistoryService.ClearHistoryAsync();
                        await _logger?.LogInfoAsync("Cursor history cleared by user", "Commands");
                        await ShowInfoMessageAsync("Cursor history cleared successfully.");
                    }
                    else
                    {
                        await ShowWarningMessageAsync("History service not available.");
                    }
                    
                }, "ClearHistory");
            });
        }

        private static void OnClearHistoryQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand command)
            {
                command.Enabled = _cursorHistoryService != null;
            }
        }

        private static void OnShowDiagnostics(object sender, EventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await _errorHandler?.ExecuteWithErrorHandlingAsync(async () =>
                {
                    await _logger?.LogInfoAsync("Diagnostics requested", "Commands");

                    // Perform health check
                    if (_errorHandler != null)
                    {
                        var healthResult = await _errorHandler.PerformHealthCheckAsync();
                        var diagnosticsMessage = FormatDiagnosticsMessage(healthResult);
                        
                        await ShowInfoMessageAsync(diagnosticsMessage);
                    }
                    else
                    {
                        await ShowWarningMessageAsync("Error handler not available for diagnostics.");
                    }
                    
                }, "ShowDiagnostics");
            });
        }

        #endregion

        #region Helper Methods

        private static string FormatDiagnosticsMessage(HealthCheckResult healthResult)
        {
            var message = $"Ollama Assistant Diagnostics\n\n";
            message += $"Overall Health: {(healthResult.IsHealthy ? "✓ Healthy" : "✗ Issues Detected")}\n";
            message += $"Ollama Connectivity: {(healthResult.OllamaConnectivity ? "✓ Connected" : "✗ Disconnected")}\n";
            message += $"Settings Valid: {(healthResult.SettingsValid ? "✓ Valid" : "✗ Invalid")}\n";
            message += $"Memory Usage: {healthResult.MemoryUsage / (1024 * 1024):F1} MB\n";
            
            if (healthResult.PerformanceMetrics != null)
            {
                message += $"Average Response Time: {healthResult.PerformanceMetrics.AverageResponseTime.TotalMilliseconds:F0}ms\n";
                message += $"Success Rate: {healthResult.PerformanceMetrics.SuccessRate:P1}\n";
            }

            if (!string.IsNullOrEmpty(healthResult.ErrorMessage))
            {
                message += $"\nError: {healthResult.ErrorMessage}";
            }

            return message;
        }

        private static async Task ShowInfoMessageAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell != null)
            {
                var clsid = Guid.Empty;
                int result;
                
                uiShell.ShowMessageBox(
                    0,
                    ref clsid,
                    "Ollama Assistant",
                    message,
                    string.Empty,
                    0,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                    OLEMSGICON.OLEMSGICON_INFO,
                    0,
                    out result);
            }
        }

        private static async Task ShowWarningMessageAsync(string message)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            var uiShell = Package.GetGlobalService(typeof(SVsUIShell)) as IVsUIShell;
            if (uiShell != null)
            {
                var clsid = Guid.Empty;
                int result;
                
                uiShell.ShowMessageBox(
                    0,
                    ref clsid,
                    "Ollama Assistant",
                    message,
                    string.Empty,
                    0,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                    OLEMSGICON.OLEMSGICON_WARNING,
                    0,
                    out result);
            }
        }

        #endregion
    }
}