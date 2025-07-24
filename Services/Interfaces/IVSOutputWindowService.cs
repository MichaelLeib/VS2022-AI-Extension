using System;
using System.Threading.Tasks;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for Visual Studio Output Window integration
    /// </summary>
    public interface IVSOutputWindowService : IDisposable
    {
        /// <summary>
        /// Gets or sets whether the output window is enabled
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Initializes the output window service
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Writes a line to the output window
        /// </summary>
        Task WriteLineAsync(string message);

        /// <summary>
        /// Writes text to the output window
        /// </summary>
        Task WriteAsync(string text);

        /// <summary>
        /// Writes an informational message
        /// </summary>
        Task WriteInfoAsync(string message, string source = null);

        /// <summary>
        /// Writes a warning message
        /// </summary>
        Task WriteWarningAsync(string message, string source = null);

        /// <summary>
        /// Writes an error message
        /// </summary>
        Task WriteErrorAsync(string message, string source = null);

        /// <summary>
        /// Writes an exception to the output window
        /// </summary>
        Task WriteExceptionAsync(Exception exception, string context = null);

        /// <summary>
        /// Clears the output window
        /// </summary>
        Task ClearAsync();

        /// <summary>
        /// Shows and activates the output window
        /// </summary>
        Task ShowAsync();
    }
}