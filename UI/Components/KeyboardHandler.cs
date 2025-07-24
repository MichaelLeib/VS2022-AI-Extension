using System;
using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.UI.Components
{
    /// <summary>
    /// Handles keyboard input for Ollama suggestions and jump navigation
    /// </summary>
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class OllamaKeyboardHandlerProvider : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService { get; set; }

        [Import]
        internal IAsyncCompletionBroker CompletionBroker { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            var textView = AdapterService.GetWpfTextView(textViewAdapter);
            if (textView == null)
                return;

            var services = ServiceLocator.Current;
            if (services == null)
                return;

            var settingsService = services.Resolve<ISettingsService>();
            var jumpService = services.Resolve<IJumpNotificationService>();
            var intelliSenseService = services.Resolve<IIntelliSenseIntegration>();
            var logger = services.Resolve<ILogger>();

            // Create and attach the keyboard handler
            var handler = new OllamaKeyboardHandler(
                textViewAdapter,
                textView,
                settingsService,
                jumpService,
                intelliSenseService,
                CompletionBroker,
                logger);

            // Add command filter to intercept keyboard input
            textViewAdapter.AddCommandFilter(handler, out var nextFilter);
            handler.NextCommandTarget = nextFilter;

            // Store handler in text view properties for cleanup
            textView.Properties.AddProperty(typeof(OllamaKeyboardHandler), handler);
        }
    }

    /// <summary>
    /// Keyboard command handler for Ollama features
    /// </summary>
    internal class OllamaKeyboardHandler : IOleCommandTarget
    {
        private readonly IVsTextView _textViewAdapter;
        private readonly IWpfTextView _textView;
        private readonly ISettingsService _settingsService;
        private readonly IJumpNotificationService _jumpService;
        private readonly IIntelliSenseIntegration _intelliSenseService;
        private readonly IAsyncCompletionBroker _completionBroker;
        private readonly ILogger _logger;
        
        public IOleCommandTarget NextCommandTarget { get; set; }

        private static readonly Guid VSStd2KCmdGuid = VSConstants.VSStd2K;
        private static readonly Guid VSStdCmdGuid = VSConstants.GUID_VSStandardCommandSet97;

        public OllamaKeyboardHandler(
            IVsTextView textViewAdapter,
            IWpfTextView textView,
            ISettingsService settingsService,
            IJumpNotificationService jumpService,
            IIntelliSenseIntegration intelliSenseService,
            IAsyncCompletionBroker completionBroker,
            ILogger logger)
        {
            _textViewAdapter = textViewAdapter ?? throw new ArgumentNullException(nameof(textViewAdapter));
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _settingsService = settingsService;
            _jumpService = jumpService;
            _intelliSenseService = intelliSenseService;
            _completionBroker = completionBroker;
            _logger = logger;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return NextCommandTarget?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? 
                   (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            try
            {
                // Check if we should handle this command
                if (ShouldHandleCommand(pguidCmdGroup, nCmdID, out var key))
                {
                    var handled = HandleKeyPress(key);
                    if (handled)
                    {
                        return VSConstants.S_OK;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error handling keyboard command", "KeyboardHandler").Wait();
            }

            // Pass to next handler
            return NextCommandTarget?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? 
                   VSConstants.E_FAIL;
        }

        private bool ShouldHandleCommand(Guid cmdGroup, uint cmdId, out Key key)
        {
            key = Key.None;

            // Check for standard keyboard commands
            if (cmdGroup == VSStd2KCmdGuid)
            {
                switch ((VSConstants.VSStd2KCmdID)cmdId)
                {
                    case VSConstants.VSStd2KCmdID.TAB:
                        key = Key.Tab;
                        return true;
                    case VSConstants.VSStd2KCmdID.RETURN:
                        key = Key.Enter;
                        return true;
                    case VSConstants.VSStd2KCmdID.ESCAPE:
                        key = Key.Escape;
                        return true;
                    case VSConstants.VSStd2KCmdID.UP:
                        key = Key.Up;
                        return true;
                    case VSConstants.VSStd2KCmdID.DOWN:
                        key = Key.Down;
                        return true;
                }
            }
            else if (cmdGroup == VSStdCmdGuid)
            {
                switch ((VSConstants.VSStd97CmdID)cmdId)
                {
                    case VSConstants.VSStd97CmdID.Undo:
                        key = Key.Z; // Ctrl+Z
                        return false; // Don't handle undo
                }
            }

            return false;
        }

        private bool HandleKeyPress(Key key)
        {
            // Handle jump navigation
            if (HandleJumpNavigation(key))
                return true;

            // Handle completion interactions
            if (HandleCompletionInteraction(key))
                return true;

            // Handle explicit suggestion invocation
            if (HandleSuggestionInvocation(key))
                return true;

            return false;
        }

        private bool HandleJumpNavigation(Key key)
        {
            try
            {
                // Check if jump notification is visible and key matches jump key
                if (_jumpService?.IsNotificationVisible == true)
                {
                    var jumpKey = _settingsService?.JumpKey ?? Keys.Tab;
                    
                    if (KeyMatchesJumpKey(key, jumpKey))
                    {
                        // Execute jump
                        _logger?.LogInfoAsync("Executing jump via keyboard", "KeyboardHandler").Wait();
                        
                        // The jump service will handle the actual jump execution
                        // through its internal event handlers
                        return true;
                    }
                    else if (key == Key.Escape)
                    {
                        // Dismiss jump notification
                        _jumpService.HideJumpNotification();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error handling jump navigation", "KeyboardHandler").Wait();
            }

            return false;
        }

        private bool HandleCompletionInteraction(Key key)
        {
            try
            {
                // Check if there's an active completion session
                var session = _completionBroker?.GetSession(_textView);
                if (session?.IsStarted == true)
                {
                    // Check if the selected item is an Ollama suggestion
                    var selectedItem = session.SelectedCompletionItem;
                    if (selectedItem?.Properties.ContainsProperty(typeof(CodeSuggestion)) == true)
                    {
                        switch (key)
                        {
                            case Key.Tab:
                                // Accept the suggestion
                                _logger?.LogInfoAsync("Accepting Ollama suggestion via Tab", "KeyboardHandler").Wait();
                                session.Commit();
                                return true;

                            case Key.Enter:
                                // Accept and insert new line
                                _logger?.LogInfoAsync("Accepting Ollama suggestion via Enter", "KeyboardHandler").Wait();
                                session.Commit();
                                // Let Enter key continue to insert newline
                                return false;

                            case Key.Escape:
                                // Dismiss suggestion
                                _logger?.LogInfoAsync("Dismissing Ollama suggestion via Escape", "KeyboardHandler").Wait();
                                session.Dismiss();
                                _textView.HideInlinePreview();
                                return true;

                            case Key.Up:
                            case Key.Down:
                                // Allow navigation through suggestions
                                return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error handling completion interaction", "KeyboardHandler").Wait();
            }

            return false;
        }

        private bool HandleSuggestionInvocation(Key key)
        {
            try
            {
                // Check for Ctrl+Space to explicitly invoke suggestions
                if (key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (_settingsService?.IsEnabled == true && 
                        _settingsService?.CodePredictionEnabled == true)
                    {
                        _logger?.LogInfoAsync("Manually invoking Ollama suggestions", "KeyboardHandler").Wait();
                        
                        // Trigger completion
                        var caretPosition = _textView.Caret.Position.BufferPosition;
                        _completionBroker?.TriggerCompletion(_textView);
                        
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error handling suggestion invocation", "KeyboardHandler").Wait();
            }

            return false;
        }

        private bool KeyMatchesJumpKey(Key pressedKey, Keys configuredKey)
        {
            // Simple key matching - in real implementation would handle modifiers
            return pressedKey.ToString() == configuredKey.ToString();
        }
    }

    /// <summary>
    /// Extension methods for keyboard handling
    /// </summary>
    internal static class KeyboardExtensions
    {
        public static bool IsCompletionActive(this IWpfTextView textView)
        {
            if (textView == null)
                return false;

            var broker = textView.Properties.TryGetProperty(
                typeof(IAsyncCompletionBroker),
                out IAsyncCompletionBroker completionBroker) ? completionBroker : null;

            return broker?.GetSession(textView)?.IsStarted == true;
        }

        public static void CleanupKeyboardHandler(this IWpfTextView textView)
        {
            if (textView?.Properties.ContainsProperty(typeof(OllamaKeyboardHandler)) == true)
            {
                textView.Properties.RemoveProperty(typeof(OllamaKeyboardHandler));
            }
        }
    }
}