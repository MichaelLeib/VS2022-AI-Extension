using System;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// MEF listener for text view creation events
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public class TextViewCreationListener : IWpfTextViewCreationListener
    {
        private readonly ILogger _logger;
        private readonly ExtensionOrchestrator _orchestrator;
        private readonly ITextViewService _textViewService;

        [ImportingConstructor]
        public TextViewCreationListener(
            [Import(AllowDefault = true)] ILogger logger,
            [Import(AllowDefault = true)] ExtensionOrchestrator orchestrator,
            [Import(AllowDefault = true)] ITextViewService textViewService)
        {
            _logger = logger;
            _orchestrator = orchestrator;
            _textViewService = textViewService;
        }

        /// <summary>
        /// Called when a new WPF text view is created
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                if (textView == null)
                    return;

                // Log the creation
                _logger?.LogInfoAsync($"Text view created for content type: {textView.TextBuffer.ContentType.DisplayName}", "TextViewCreation").ConfigureAwait(false);

                // Set this as the active text view in the service
                if (_textViewService is TextViewService textViewService)
                {
                    textViewService.SetActiveTextView(textView);
                }

                // Notify the orchestrator about the new text view
                _orchestrator?.OnTextViewCreated(textView);

                // Subscribe to view lifecycle events
                textView.Closed += OnTextViewClosed;
                textView.GotAggregateFocus += OnTextViewGotFocus;
                textView.LostAggregateFocus += OnTextViewLostFocus;

                // Subscribe to buffer change events
                textView.TextBuffer.Changed += OnTextBufferChanged;
                textView.Caret.PositionChanged += OnCaretPositionChanged;

                _logger?.LogInfoAsync("Text view event subscriptions completed", "TextViewCreation").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in TextViewCreated", "TextViewCreation").ConfigureAwait(false);
            }
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            try
            {
                if (sender is IWpfTextView textView)
                {
                    _logger?.LogInfoAsync("Text view closed", "TextViewLifecycle").ConfigureAwait(false);

                    // Unsubscribe from events
                    textView.Closed -= OnTextViewClosed;
                    textView.GotAggregateFocus -= OnTextViewGotFocus;
                    textView.LostAggregateFocus -= OnTextViewLostFocus;
                    textView.TextBuffer.Changed -= OnTextBufferChanged;
                    textView.Caret.PositionChanged -= OnCaretPositionChanged;

                    // Notify the orchestrator
                    _orchestrator?.OnTextViewClosed(textView);

                    // Clear from text view service if it's the active one
                    if (_textViewService is TextViewService textViewService)
                    {
                        var activeView = textViewService.GetActiveTextView();
                        if (activeView == textView)
                        {
                            textViewService.SetActiveTextView(null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewClosed", "TextViewLifecycle").ConfigureAwait(false);
            }
        }

        private void OnTextViewGotFocus(object sender, EventArgs e)
        {
            try
            {
                if (sender is IWpfTextView textView)
                {
                    _logger?.LogDebugAsync("Text view got focus", "TextViewFocus").ConfigureAwait(false);

                    // Update the active text view
                    if (_textViewService is TextViewService textViewService)
                    {
                        textViewService.SetActiveTextView(textView);
                    }

                    // Notify the orchestrator
                    _orchestrator?.OnTextViewFocused(textView);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewGotFocus", "TextViewFocus").ConfigureAwait(false);
            }
        }

        private void OnTextViewLostFocus(object sender, EventArgs e)
        {
            try
            {
                if (sender is IWpfTextView textView)
                {
                    _logger?.LogDebugAsync("Text view lost focus", "TextViewFocus").ConfigureAwait(false);

                    // Notify the orchestrator
                    _orchestrator?.OnTextViewLostFocus(textView);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextViewLostFocus", "TextViewFocus").ConfigureAwait(false);
            }
        }

        private void OnTextBufferChanged(object sender, Microsoft.VisualStudio.Text.TextContentChangedEventArgs e)
        {
            try
            {
                // Forward to orchestrator for processing
                _orchestrator?.OnTextBufferChanged(sender, e);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnTextBufferChanged", "TextBufferChange").ConfigureAwait(false);
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            try
            {
                // Forward to orchestrator for processing
                _orchestrator?.OnCaretPositionChanged(sender, e);
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error in OnCaretPositionChanged", "CaretPositionChange").ConfigureAwait(false);
            }
        }
    }
}
