using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// MEF listener for text buffer factory events
    /// </summary>
    [Export(typeof(ITextBufferListener))]
    [ContentType("text")]
    public class TextBufferFactoryListener : ITextBufferListener
    {
        private readonly ILogger _logger;
        private readonly ExtensionOrchestrator _orchestrator;
        private readonly ICursorHistoryService _cursorHistoryService;

        [ImportingConstructor]
        public TextBufferFactoryListener(
            [Import(AllowDefault = true)] ILogger logger,
            [Import(AllowDefault = true)] ExtensionOrchestrator orchestrator,
            [Import(AllowDefault = true)] ICursorHistoryService cursorHistoryService)
        {
            _logger = logger;
            _orchestrator = orchestrator;
            _cursorHistoryService = cursorHistoryService;
        }

        /// <summary>
        /// Called when a text buffer is created
        /// </summary>
        public void TextBufferCreated(ITextBuffer textBuffer)
        {
            try
            {
                if (textBuffer == null)
                    return;

                _logger?.LogDebugAsync($"Text buffer created for content type: {textBuffer.ContentType.DisplayName}", "TextBufferCreation").ConfigureAwait(false);

                // Subscribe to buffer events
                textBuffer.Changed += OnTextBufferChanged;
                textBuffer.PostChanged += OnTextBufferPostChanged;
                textBuffer.ChangedLowPriority += OnTextBufferChangedLowPriority;

                // Notify orchestrator
                _orchestrator?.OnTextBufferCreated(textBuffer);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in TextBufferCreated", "TextBufferCreation").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Called when a text buffer is disposed
        /// </summary>
        public void TextBufferDisposed(ITextBuffer textBuffer)
        {
            try
            {
                if (textBuffer == null)
                    return;

                _logger?.LogDebugAsync("Text buffer disposed", "TextBufferLifecycle").ConfigureAwait(false);

                // Unsubscribe from buffer events
                textBuffer.Changed -= OnTextBufferChanged;
                textBuffer.PostChanged -= OnTextBufferPostChanged;
                textBuffer.ChangedLowPriority -= OnTextBufferChangedLowPriority;

                // Notify orchestrator
                _orchestrator?.OnTextBufferDisposed(textBuffer);

                // Clear any history related to this buffer
                var filePath = GetFilePathFromTextBuffer(textBuffer);
                if (!string.IsNullOrEmpty(filePath))
                {
                    _cursorHistoryService?.ClearHistoryForFileAsync(filePath).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in TextBufferDisposed", "TextBufferLifecycle").ConfigureAwait(false);
            }
        }

        private void OnTextBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                _logger?.LogDebugAsync($"Text buffer changed: {e.Changes.Count} changes", "TextBufferChange").ConfigureAwait(false);

                // Process changes for cursor history and AI analysis
                foreach (var change in e.Changes)
                {
                    ProcessTextChange(sender as ITextBuffer, change, e.Before, e.After);
                }

                // Notify orchestrator
                _orchestrator?.OnTextBufferChanged(sender, e);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferChanged", "TextBufferChange").ConfigureAwait(false);
            }
        }

        private void OnTextBufferPostChanged(object sender, EventArgs e)
        {
            try
            {
                // Post-change processing - can be used for cleanup or finalization
                _orchestrator?.OnTextBufferPostChanged(sender as ITextBuffer);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferPostChanged", "TextBufferChange").ConfigureAwait(false);
            }
        }

        private void OnTextBufferChangedLowPriority(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                // Low priority processing - can be used for background analysis
                _orchestrator?.OnTextBufferChangedLowPriority(sender, e);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferChangedLowPriority", "TextBufferChange").ConfigureAwait(false);
            }
        }

        private void ProcessTextChange(ITextBuffer textBuffer, ITextChange change, ITextSnapshot before, ITextSnapshot after)
        {
            try
            {
                var filePath = GetFilePathFromTextBuffer(textBuffer);
                if (string.IsNullOrEmpty(filePath))
                    return;

                // Determine the type of change
                var changeType = DetermineChangeType(change);

                // Update cursor history with the change context
                var changeInfo = new
                {
                    FilePath = filePath,
                    ChangeType = changeType,
                    Position = change.OldPosition,
                    OldText = change.OldText,
                    NewText = change.NewText,
                    LineNumber = before.GetLineFromPosition(change.OldPosition).LineNumber + 1
                };

                _logger?.LogDebugAsync($"Processed text change: {changeType} at {changeInfo.LineNumber}:{change.OldPosition}", "TextChange").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error processing text change", "TextChange").ConfigureAwait(false);
            }
        }

        private string DetermineChangeType(ITextChange change)
        {
            if (string.IsNullOrEmpty(change.OldText) && !string.IsNullOrEmpty(change.NewText))
            {
                return "insertion";
            }
            else if (!string.IsNullOrEmpty(change.OldText) && string.IsNullOrEmpty(change.NewText))
            {
                return "deletion";
            }
            else if (!string.IsNullOrEmpty(change.OldText) && !string.IsNullOrEmpty(change.NewText))
            {
                return "replacement";
            }
            else
            {
                return "unknown";
            }
        }

        private string GetFilePathFromTextBuffer(ITextBuffer textBuffer)
        {
            try
            {
                if (textBuffer?.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document) == true)
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
    }

}