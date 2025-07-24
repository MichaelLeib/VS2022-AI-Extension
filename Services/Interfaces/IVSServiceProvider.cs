using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for accessing Visual Studio services with proper thread marshaling
    /// </summary>
    public interface IVSServiceProvider : IDisposable
    {
        /// <summary>
        /// Gets whether the service provider is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initializes the VS service provider
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Gets the VS UI Shell service
        /// </summary>
        Task<IVsUIShell> GetUIShellAsync();

        /// <summary>
        /// Gets the VS Text Manager service
        /// </summary>
        Task<IVsTextManager> GetTextManagerAsync();

        /// <summary>
        /// Gets the VS Selection Monitor service
        /// </summary>
        Task<IVsMonitorSelection> GetSelectionMonitorAsync();

        /// <summary>
        /// Gets the VS Running Document Table service
        /// </summary>
        Task<IVsRunningDocumentTable> GetRunningDocumentTableAsync();

        /// <summary>
        /// Gets the VS Activity Log service
        /// </summary>
        Task<IVsActivityLog> GetActivityLogAsync();

        /// <summary>
        /// Gets the VS Error List service
        /// </summary>
        Task<IVsErrorList> GetErrorListAsync();

        /// <summary>
        /// Gets the VS Status Bar service
        /// </summary>
        Task<IVsStatusbar> GetStatusBarAsync();

        /// <summary>
        /// Gets the VS Output Window service
        /// </summary>
        Task<IVsOutputWindow> GetOutputWindowAsync();

        /// <summary>
        /// Gets the VS Solution service
        /// </summary>
        Task<IVsSolution> GetSolutionAsync();

        /// <summary>
        /// Gets a service by type
        /// </summary>
        Task<T> GetServiceAsync<T>(Type serviceType) where T : class;

        /// <summary>
        /// Shows a message box
        /// </summary>
        Task<int> ShowMessageBoxAsync(string message, string title, OLEMSGICON icon = OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON buttons = OLEMSGBUTTON.OLEMSGBUTTON_OK);

        /// <summary>
        /// Gets the active text view
        /// </summary>
        Task<IVsTextView> GetActiveTextViewAsync();

        /// <summary>
        /// Gets information about the active document
        /// </summary>
        Task<(string filePath, IVsHierarchy hierarchy, uint itemId)> GetActiveDocumentAsync();

        /// <summary>
        /// Checks if a solution is loaded
        /// </summary>
        Task<bool> IsSolutionLoadedAsync();

        /// <summary>
        /// Gets the solution file path
        /// </summary>
        Task<string> GetSolutionFilePathAsync();

        /// <summary>
        /// Logs an error to the VS activity log
        /// </summary>
        Task LogErrorAsync(string source, string message, Exception exception = null);

        /// <summary>
        /// Logs a warning to the VS activity log
        /// </summary>
        Task LogWarningAsync(string source, string message);

        /// <summary>
        /// Logs an info message to the VS activity log
        /// </summary>
        Task LogInfoAsync(string source, string message);
    }
}