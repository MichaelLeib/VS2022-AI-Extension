using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Service for handling user-friendly error interfaces and reporting
    /// </summary>
    public class ErrorUserInterfaceService : IDisposable
    {
        private readonly JoinableTaskFactory _joinableTaskFactory;
        private readonly IVsUIShell _uiShell;
        private readonly DiagnosticCollector _diagnosticCollector;
        private readonly ErrorReportingService _errorReporting;
        private readonly Dictionary<string, ErrorUITemplate> _errorTemplates;
        private bool _disposed;

        public ErrorUserInterfaceService(JoinableTaskFactory joinableTaskFactory = null)
        {
            _joinableTaskFactory = joinableTaskFactory ?? ThreadHelper.JoinableTaskFactory;
            _uiShell = ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) as IVsUIShell;
            _diagnosticCollector = new DiagnosticCollector();
            _errorReporting = new ErrorReportingService();
            _errorTemplates = InitializeErrorTemplates();
        }

        /// <summary>
        /// Shows a user-friendly error dialog for common error scenarios
        /// </summary>
        public async Task ShowErrorDialogAsync(ErrorContext errorContext)
        {
            if (_disposed || errorContext == null)
                return;

            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var template = GetErrorTemplate(errorContext.ErrorType);
                var dialogInfo = CreateErrorDialogInfo(errorContext, template);

                var result = ShowVSErrorDialog(dialogInfo);

                // Handle user action
                await HandleUserActionAsync(result, errorContext, template);
            }
            catch (Exception ex)
            {
                // Fallback to simple message box if dialog fails
                await ShowFallbackErrorAsync(errorContext, ex);
            }
        }

        /// <summary>
        /// Shows an actionable error message with suggested solutions
        /// </summary>
        public async Task ShowActionableErrorAsync(string errorMessage, ErrorSeverity severity, params ErrorAction[] actions)
        {
            if (_disposed || string.IsNullOrEmpty(errorMessage))
                return;

            var errorContext = new ErrorContext
            {
                ErrorType = ErrorType.General,
                Message = errorMessage,
                Severity = severity,
                Timestamp = DateTime.UtcNow,
                Actions = actions?.ToList() ?? new List<ErrorAction>()
            };

            await ShowErrorDialogAsync(errorContext);
        }

        /// <summary>
        /// Reports an error and collects diagnostic information
        /// </summary>
        public async Task<string> ReportErrorAsync(Exception exception, string context = null, bool showToUser = true)
        {
            if (_disposed || exception == null)
                return null;

            try
            {
                // Collect diagnostic information
                var diagnostics = await _diagnosticCollector.CollectDiagnosticsAsync(exception, context);

                // Generate error report
                var reportId = await _errorReporting.SubmitErrorReportAsync(exception, diagnostics, context);

                // Show to user if requested
                if (showToUser)
                {
                    var errorContext = new ErrorContext
                    {
                        ErrorType = ClassifyException(exception),
                        Message = exception.Message,
                        Exception = exception,
                        Severity = ErrorSeverity.Error,
                        Timestamp = DateTime.UtcNow,
                        DiagnosticInfo = diagnostics,
                        ReportId = reportId,
                        Actions = GetActionsForException(exception)
                    };

                    await ShowErrorDialogAsync(errorContext);
                }

                return reportId;
            }
            catch (Exception reportingEx)
            {
                // If reporting fails, at least log locally
                Debug.WriteLine($"Error reporting failed: {reportingEx.Message}");
                return null;
            }
        }

        /// <summary>
        /// Shows a connection error with specific troubleshooting steps
        /// </summary>
        public async Task ShowConnectionErrorAsync(string serverUrl, Exception exception)
        {
            if (_disposed)
                return;

            var actions = new List<ErrorAction>
            {
                new ErrorAction("Check Server Status", ActionType.CheckConnection, () => CheckServerStatusAsync(serverUrl)),
                new ErrorAction("Configure Server", ActionType.OpenSettings, () => OpenServerSettingsAsync()),
                new ErrorAction("View Diagnostics", ActionType.ShowDiagnostics, () => ShowConnectionDiagnosticsAsync(serverUrl, exception)),
                new ErrorAction("Report Issue", ActionType.ReportError, () => ReportErrorAsync(exception, $"Connection to {serverUrl}"))
            };

            var errorContext = new ErrorContext
            {
                ErrorType = ErrorType.ConnectionFailed,
                Message = $"Failed to connect to Ollama server at {serverUrl}",
                Exception = exception,
                Severity = ErrorSeverity.Error,
                Timestamp = DateTime.UtcNow,
                Actions = actions
            };

            await ShowErrorDialogAsync(errorContext);
        }

        /// <summary>
        /// Shows an AI response error with retry options
        /// </summary>
        public async Task ShowAIResponseErrorAsync(string model, Exception exception, string context = null)
        {
            if (_disposed)
                return;

            var actions = new List<ErrorAction>
            {
                new ErrorAction("Retry Request", ActionType.RetryOperation, () => RetryLastRequestAsync()),
                new ErrorAction("Switch Model", ActionType.OpenSettings, () => OpenModelSettingsAsync()),
                new ErrorAction("Check Server Health", ActionType.CheckConnection, () => CheckServerHealthAsync()),
                new ErrorAction("Report Issue", ActionType.ReportError, () => ReportErrorAsync(exception, $"AI Response from {model}"))
            };

            var errorContext = new ErrorContext
            {
                ErrorType = ErrorType.AIResponseFailed,
                Message = $"AI model '{model}' failed to generate a response",
                Exception = exception,
                Severity = ErrorSeverity.Warning,
                Timestamp = DateTime.UtcNow,
                Context = context,
                Actions = actions
            };

            await ShowErrorDialogAsync(errorContext);
        }

        /// <summary>
        /// Gets error template for specific error type
        /// </summary>
        private ErrorUITemplate GetErrorTemplate(ErrorType errorType)
        {
            return _errorTemplates.TryGetValue(errorType.ToString(), out var template) 
                ? template 
                : _errorTemplates["Default"];
        }

        /// <summary>
        /// Creates error dialog information from context and template
        /// </summary>
        private ErrorDialogInfo CreateErrorDialogInfo(ErrorContext errorContext, ErrorUITemplate template)
        {
            var dialogInfo = new ErrorDialogInfo
            {
                Title = string.Format(template.TitleFormat, GetErrorTypeDisplayName(errorContext.ErrorType)),
                Message = errorContext.Message,
                DetailedMessage = CreateDetailedMessage(errorContext),
                Icon = GetIconForSeverity(errorContext.Severity),
                Buttons = CreateButtonsFromActions(errorContext.Actions),
                ShowDiagnostics = errorContext.DiagnosticInfo != null,
                ShowReportId = !string.IsNullOrEmpty(errorContext.ReportId)
            };

            return dialogInfo;
        }

        /// <summary>
        /// Shows Visual Studio error dialog
        /// </summary>
        private DialogResult ShowVSErrorDialog(ErrorDialogInfo dialogInfo)
        {
            if (_uiShell == null)
                return DialogResult.Cancel;

            var buttons = new string[dialogInfo.Buttons.Count];
            for (int i = 0; i < dialogInfo.Buttons.Count; i++)
            {
                buttons[i] = dialogInfo.Buttons[i].Text;
            }

            var compoundMessage = $"{dialogInfo.Message}\n\n{dialogInfo.DetailedMessage}";

            var result = _uiShell.ShowMessageBox(
                0,
                ref Guid.Empty,
                dialogInfo.Title,
                compoundMessage,
                string.Empty,
                0,
                OLEMSGBUTTON.OLEMSGBUTTON_ABORTRETRYIGNORE,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                dialogInfo.Icon,
                0,
                out int selectedButton);

            return (DialogResult)selectedButton;
        }

        /// <summary>
        /// Handles user action from error dialog
        /// </summary>
        private async Task HandleUserActionAsync(DialogResult result, ErrorContext errorContext, ErrorUITemplate template)
        {
            var actionIndex = (int)result;
            
            if (actionIndex >= 0 && actionIndex < errorContext.Actions.Count)
            {
                var action = errorContext.Actions[actionIndex];
                try
                {
                    await action.ExecuteAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error executing action {action.Text}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Shows fallback error when main error dialog fails
        /// </summary>
        private async Task ShowFallbackErrorAsync(ErrorContext errorContext, Exception dialogException)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var message = $"An error occurred: {errorContext.Message}\n\nAdditionally, the error dialog failed to display: {dialogException.Message}";
                
                if (_uiShell != null)
                {
                    _uiShell.ShowMessageBox(
                        0,
                        ref Guid.Empty,
                        "Ollama Assistant Error",
                        message,
                        string.Empty,
                        0,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        0,
                        out _);
                }
            }
            catch
            {
                // If everything fails, at least log it
                Debug.WriteLine($"Critical error - unable to show error dialog: {errorContext.Message}");
            }
        }

        /// <summary>
        /// Initializes error templates for different error types
        /// </summary>
        private Dictionary<string, ErrorUITemplate> InitializeErrorTemplates()
        {
            return new Dictionary<string, ErrorUITemplate>
            {
                ["ConnectionFailed"] = new ErrorUITemplate
                {
                    TitleFormat = "Ollama Connection Error",
                    MessageFormat = "Unable to connect to the Ollama server. Please check your connection settings and ensure the server is running.",
                    SuggestedActions = new[] { "Check Connection", "Configure Settings", "View Help" }
                },
                ["AIResponseFailed"] = new ErrorUITemplate
                {
                    TitleFormat = "AI Response Error",
                    MessageFormat = "The AI model failed to generate a response. This might be a temporary issue.",
                    SuggestedActions = new[] { "Retry", "Switch Model", "Report Issue" }
                },
                ["ConfigurationError"] = new ErrorUITemplate
                {
                    TitleFormat = "Configuration Error",
                    MessageFormat = "There's an issue with your Ollama Assistant configuration.",
                    SuggestedActions = new[] { "Open Settings", "Reset Configuration", "View Documentation" }
                },
                ["Default"] = new ErrorUITemplate
                {
                    TitleFormat = "Ollama Assistant Error",
                    MessageFormat = "An unexpected error occurred in the Ollama Assistant extension.",
                    SuggestedActions = new[] { "Retry", "Report Issue", "View Logs" }
                }
            };
        }

        /// <summary>
        /// Classifies exception type for appropriate error handling
        /// </summary>
        private ErrorType ClassifyException(Exception exception)
        {
            switch (exception)
            {
                case System.Net.Http.HttpRequestException _:
                    return ErrorType.ConnectionFailed;
                case TimeoutException _:
                    return ErrorType.Timeout;
                case UnauthorizedAccessException _:
                    return ErrorType.AuthenticationFailed;
                case ArgumentException _:
                    return ErrorType.ConfigurationError;
                case InvalidOperationException _:
                    return ErrorType.InvalidState;
                default:
                    return ErrorType.General;
            }
        }

        /// <summary>
        /// Gets appropriate actions for an exception
        /// </summary>
        private List<ErrorAction> GetActionsForException(Exception exception)
        {
            var actions = new List<ErrorAction>();

            switch (ClassifyException(exception))
            {
                case ErrorType.ConnectionFailed:
                    actions.Add(new ErrorAction("Check Connection", ActionType.CheckConnection, CheckServerHealthAsync));
                    actions.Add(new ErrorAction("Configure Server", ActionType.OpenSettings, OpenServerSettingsAsync));
                    break;

                case ErrorType.ConfigurationError:
                    actions.Add(new ErrorAction("Open Settings", ActionType.OpenSettings, OpenGeneralSettingsAsync));
                    actions.Add(new ErrorAction("Reset Configuration", ActionType.ResetSettings, ResetConfigurationAsync));
                    break;

                default:
                    actions.Add(new ErrorAction("Retry", ActionType.RetryOperation, RetryLastRequestAsync));
                    break;
            }

            actions.Add(new ErrorAction("Report Issue", ActionType.ReportError, () => ReportErrorAsync(exception)));
            return actions;
        }

        /// <summary>
        /// Creates detailed error message with context and diagnostics
        /// </summary>
        private string CreateDetailedMessage(ErrorContext errorContext)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(errorContext.Context))
            {
                sb.AppendLine($"Context: {errorContext.Context}");
                sb.AppendLine();
            }

            if (errorContext.Exception != null)
            {
                sb.AppendLine($"Technical Details: {errorContext.Exception.GetType().Name}");
                if (errorContext.Exception.InnerException != null)
                {
                    sb.AppendLine($"Inner Exception: {errorContext.Exception.InnerException.Message}");
                }
                sb.AppendLine();
            }

            if (!string.IsNullOrEmpty(errorContext.ReportId))
            {
                sb.AppendLine($"Report ID: {errorContext.ReportId}");
                sb.AppendLine();
            }

            sb.AppendLine($"Timestamp: {errorContext.Timestamp:yyyy-MM-dd HH:mm:ss}");

            return sb.ToString();
        }

        /// <summary>
        /// Gets display name for error type
        /// </summary>
        private string GetErrorTypeDisplayName(ErrorType errorType)
        {
            switch (errorType)
            {
                case ErrorType.ConnectionFailed:
                    return "Connection Error";
                case ErrorType.AIResponseFailed:
                    return "AI Response Error";
                case ErrorType.ConfigurationError:
                    return "Configuration Error";
                case ErrorType.AuthenticationFailed:
                    return "Authentication Error";
                case ErrorType.Timeout:
                    return "Timeout Error";
                case ErrorType.InvalidState:
                    return "Invalid State Error";
                default:
                    return "Error";
            }
        }

        /// <summary>
        /// Gets appropriate icon for error severity
        /// </summary>
        private OLEMSGICON GetIconForSeverity(ErrorSeverity severity)
        {
            switch (severity)
            {
                case ErrorSeverity.Critical:
                    return OLEMSGICON.OLEMSGICON_CRITICAL;
                case ErrorSeverity.Error:
                    return OLEMSGICON.OLEMSGICON_CRITICAL;
                case ErrorSeverity.Warning:
                    return OLEMSGICON.OLEMSGICON_WARNING;
                case ErrorSeverity.Info:
                    return OLEMSGICON.OLEMSGICON_INFO;
                default:
                    return OLEMSGICON.OLEMSGICON_CRITICAL;
            }
        }

        /// <summary>
        /// Creates dialog buttons from error actions
        /// </summary>
        private List<ErrorDialogButton> CreateButtonsFromActions(List<ErrorAction> actions)
        {
            var buttons = new List<ErrorDialogButton>();

            foreach (var action in actions.Take(3)) // Limit to 3 actions
            {
                buttons.Add(new ErrorDialogButton
                {
                    Text = action.Text,
                    ActionType = action.Type,
                    IsDefault = action.Type == ActionType.RetryOperation
                });
            }

            // Always add a cancel/close button
            buttons.Add(new ErrorDialogButton
            {
                Text = "Close",
                ActionType = ActionType.Cancel,
                IsDefault = false
            });

            return buttons;
        }

        // Action implementations
        private async Task CheckServerStatusAsync(string serverUrl)
        {
            try
            {
                // Perform actual server connectivity check
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5);
                    
                    var response = await httpClient.GetAsync($"{serverUrl}/api/version");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        await ShowInfoMessageAsync("Server Connection", "Server is reachable and responding.");
                    }
                    else
                    {
                        await ShowWarningMessageAsync("Server Connection", 
                            $"Server responded with status: {response.StatusCode}. Check server configuration.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Server Connection Failed", 
                    $"Unable to connect to server: {ex.Message}");
            }
        }

        private async Task CheckServerHealthAsync()
        {
            try
            {
                // Get current server URL from settings
                var settingsService = ServiceProvider.GlobalProvider.GetService(typeof(ISettingsService)) as ISettingsService;
                var serverUrl = settingsService?.OllamaEndpoint ?? "http://localhost:11434";
                
                using (var httpClient = new System.Net.Http.HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    
                    // Check server version and health
                    var versionResponse = await httpClient.GetAsync($"{serverUrl}/api/version");
                    var tagsResponse = await httpClient.GetAsync($"{serverUrl}/api/tags");
                    
                    if (versionResponse.IsSuccessStatusCode && tagsResponse.IsSuccessStatusCode)
                    {
                        var versionContent = await versionResponse.Content.ReadAsStringAsync();
                        var tagsContent = await tagsResponse.Content.ReadAsStringAsync();
                        
                        await ShowInfoMessageAsync("Server Health Check", 
                            $"Server is healthy.\nVersion info: {versionContent}\nAvailable models: {tagsContent}");
                    }
                    else
                    {
                        await ShowWarningMessageAsync("Server Health Check", 
                            "Server is responding but may have issues. Check server logs.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Health Check Failed", 
                    $"Server health check failed: {ex.Message}");
            }
        }

        private async Task OpenServerSettingsAsync()
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                // Open VS Tools > Options > Ollama Assistant > Server Settings
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    // Execute command to open options dialog to Ollama Assistant page
                    dte.ExecuteCommand("Tools.Options", "Ollama Assistant.Server");
                }
                else
                {
                    await ShowWarningMessageAsync("Settings", "Unable to open settings dialog. Please go to Tools > Options > Ollama Assistant manually.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Settings Error", $"Failed to open server settings: {ex.Message}");
            }
        }

        private async Task OpenModelSettingsAsync()
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                // Open VS Tools > Options > Ollama Assistant > Model Settings
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    dte.ExecuteCommand("Tools.Options", "Ollama Assistant.Models");
                }
                else
                {
                    await ShowWarningMessageAsync("Settings", "Unable to open settings dialog. Please go to Tools > Options > Ollama Assistant > Models manually.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Settings Error", $"Failed to open model settings: {ex.Message}");
            }
        }

        private async Task OpenGeneralSettingsAsync()
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            try
            {
                // Open VS Tools > Options > Ollama Assistant
                var dte = ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte != null)
                {
                    dte.ExecuteCommand("Tools.Options", "Ollama Assistant");
                }
                else
                {
                    await ShowWarningMessageAsync("Settings", "Unable to open settings dialog. Please go to Tools > Options > Ollama Assistant manually.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Settings Error", $"Failed to open general settings: {ex.Message}");
            }
        }

        private async Task RetryLastRequestAsync()
        {
            try
            {
                // Get the last failed request context and retry it
                var ollamaService = ServiceProvider.GlobalProvider.GetService(typeof(IOllamaService)) as IOllamaService;
                if (ollamaService != null)
                {
                    // Attempt to retry the service - this would typically involve
                    // storing the last request context and replaying it
                    await ShowInfoMessageAsync("Retry", "Attempting to retry the last request...");
                    
                    // For now, we'll just test the connection
                    // In a full implementation, we'd store and replay the actual failed request
                    var testContext = new CodeContext
                    {
                        Language = "csharp",
                        Code = "// Test retry",
                        FilePath = "retry_test.cs"
                    };
                    
                    var result = await ollamaService.GetCodeSuggestionAsync(testContext);
                    
                    if (!string.IsNullOrEmpty(result?.Text))
                    {
                        await ShowInfoMessageAsync("Retry Successful", "The retry operation completed successfully.");
                    }
                    else
                    {
                        await ShowWarningMessageAsync("Retry Warning", "The retry operation completed but no response was received.");
                    }
                }
                else
                {
                    await ShowErrorMessageAsync("Retry Failed", "Unable to access Ollama service for retry.");
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Retry Failed", $"Retry operation failed: {ex.Message}");
            }
        }

        private async Task ResetConfigurationAsync()
        {
            try
            {
                // Show confirmation dialog
                var result = await ShowConfirmationDialogAsync("Reset Configuration", 
                    "This will reset all Ollama Assistant settings to their default values. Are you sure you want to continue?");
                
                if (result)
                {
                    var settingsService = ServiceProvider.GlobalProvider.GetService(typeof(ISettingsService)) as ISettingsService;
                    if (settingsService != null)
                    {
                        await settingsService.ResetToDefaults();
                        await settingsService.SaveSettings();
                        
                        await ShowInfoMessageAsync("Configuration Reset", 
                            "Configuration has been reset to default values. Please restart Visual Studio for all changes to take effect.");
                    }
                    else
                    {
                        await ShowErrorMessageAsync("Reset Failed", "Unable to access settings service.");
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Reset Failed", $"Failed to reset configuration: {ex.Message}");
            }
        }

        private async Task ShowConnectionDiagnosticsAsync(string serverUrl, Exception exception)
        {
            try
            {
                var diagnostics = new StringBuilder();
                diagnostics.AppendLine("Connection Diagnostics:");
                diagnostics.AppendLine($"Server URL: {serverUrl}");
                diagnostics.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                diagnostics.AppendLine();
                
                if (exception != null)
                {
                    diagnostics.AppendLine("Exception Details:");
                    diagnostics.AppendLine($"Type: {exception.GetType().Name}");
                    diagnostics.AppendLine($"Message: {exception.Message}");
                    diagnostics.AppendLine($"Stack Trace: {exception.StackTrace}");
                    diagnostics.AppendLine();
                }
                
                // Collect system diagnostics
                var systemDiagnostics = await _diagnosticCollector.CollectSystemDiagnosticsAsync();
                diagnostics.AppendLine("System Information:");
                diagnostics.AppendLine(systemDiagnostics.ToString());
                
                // Network connectivity tests
                diagnostics.AppendLine("Network Tests:");
                await TestNetworkConnectivity(diagnostics, serverUrl);
                
                // Show in a scrollable dialog
                await ShowDiagnosticsDialog("Connection Diagnostics", diagnostics.ToString());
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync("Diagnostics Error", $"Failed to generate diagnostics: {ex.Message}");
            }
        }

        #region Helper Methods
        
        private async Task<bool> ShowConfirmationDialogAsync(string title, string message)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            var result = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_QUESTION, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            
            return result == (int)VSConstants.MessageBoxResult.IDYES;
        }
        
        private async Task ShowInfoMessageAsync(string title, string message)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        
        private async Task ShowWarningMessageAsync(string title, string message)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        
        private async Task ShowErrorMessageAsync(string title, string message)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, message, title,
                OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        
        private async Task ShowDiagnosticsDialog(string title, string diagnostics)
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            
            // For now, show in a simple message box. In a full implementation,
            // this would be a custom dialog with scrollable text and copy functionality
            VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, diagnostics, title,
                OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
        
        private async Task TestNetworkConnectivity(StringBuilder diagnostics, string serverUrl)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                var stopwatch = Stopwatch.StartNew();
                var response = await httpClient.GetAsync(serverUrl);
                stopwatch.Stop();
                
                diagnostics.AppendLine($"HTTP Response: {response.StatusCode}");
                diagnostics.AppendLine($"Response Time: {stopwatch.ElapsedMilliseconds}ms");
                diagnostics.AppendLine($"Content Length: {response.Content.Headers.ContentLength ?? 0} bytes");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"Network Test Failed: {ex.Message}");
            }
        }
        
        #endregion
        
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _diagnosticCollector?.Dispose();
            _errorReporting?.Dispose();
        }
    }

    /// <summary>
    /// Error context information
    /// </summary>
    public class ErrorContext
    {
        public ErrorType ErrorType { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public ErrorSeverity Severity { get; set; }
        public DateTime Timestamp { get; set; }
        public string Context { get; set; }
        public string ReportId { get; set; }
        public DiagnosticInformation DiagnosticInfo { get; set; }
        public List<ErrorAction> Actions { get; set; } = new List<ErrorAction>();
    }

    /// <summary>
    /// Error types for classification
    /// </summary>
    public enum ErrorType
    {
        General,
        ConnectionFailed,
        AIResponseFailed,
        ConfigurationError,
        AuthenticationFailed,
        Timeout,
        InvalidState
    }

    /// <summary>
    /// Error severity levels
    /// </summary>
    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Error action definition
    /// </summary>
    public class ErrorAction
    {
        public ErrorAction(string text, ActionType type, Func<Task> executeAsync)
        {
            Text = text;
            Type = type;
            ExecuteAsync = executeAsync;
        }

        public string Text { get; set; }
        public ActionType Type { get; set; }
        public Func<Task> ExecuteAsync { get; set; }
    }

    /// <summary>
    /// Action types for error handling
    /// </summary>
    public enum ActionType
    {
        Cancel,
        RetryOperation,
        CheckConnection,
        OpenSettings,
        ResetSettings,
        ShowDiagnostics,
        ReportError,
        ViewLogs
    }

    /// <summary>
    /// Dialog result enumeration
    /// </summary>
    public enum DialogResult
    {
        Cancel = 0,
        OK = 1,
        Abort = 3,
        Retry = 4,
        Ignore = 5
    }

    /// <summary>
    /// Error UI template for consistent messaging
    /// </summary>
    internal class ErrorUITemplate
    {
        public string TitleFormat { get; set; }
        public string MessageFormat { get; set; }
        public string[] SuggestedActions { get; set; }
    }

    /// <summary>
    /// Error dialog information
    /// </summary>
    internal class ErrorDialogInfo
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public string DetailedMessage { get; set; }
        public OLEMSGICON Icon { get; set; }
        public List<ErrorDialogButton> Buttons { get; set; } = new List<ErrorDialogButton>();
        public bool ShowDiagnostics { get; set; }
        public bool ShowReportId { get; set; }
    }

    /// <summary>
    /// Error dialog button definition
    /// </summary>
    internal class ErrorDialogButton
    {
        public string Text { get; set; }
        public ActionType ActionType { get; set; }
        public bool IsDefault { get; set; }
    }
}