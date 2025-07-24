using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.UI.Components
{
    /// <summary>
    /// Provides inline preview adornments for code suggestions
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class InlinePreviewAdornmentProvider : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("OllamaInlinePreview")]
        [Order(After = PredefinedAdornmentLayers.Selection, Before = PredefinedAdornmentLayers.Text)]
        private AdornmentLayerDefinition editorAdornmentLayer;

        public void TextViewCreated(IWpfTextView textView)
        {
            if (textView == null)
                return;

            var services = ServiceLocator.Current;
            if (services == null)
                return;

            var settingsService = services.Resolve<ISettingsService>();
            var logger = services.Resolve<ILogger>();

            // Only create adornment manager if inline preview is enabled
            if (settingsService?.ShowInlinePreview == true)
            {
                textView.Properties.GetOrCreateSingletonProperty(
                    () => new InlinePreviewAdornmentManager(textView, settingsService, logger));
            }
        }
    }

    /// <summary>
    /// Manages inline preview adornments for a text view
    /// </summary>
    internal class InlinePreviewAdornmentManager
    {
        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private CodeSuggestion _currentSuggestion;
        private SnapshotSpan? _currentSpan;
        private readonly object _lockObject = new object();

        public InlinePreviewAdornmentManager(
            IWpfTextView view,
            ISettingsService settingsService,
            ILogger logger)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _settingsService = settingsService;
            _logger = logger;
            
            _layer = view.GetAdornmentLayer("OllamaInlinePreview");

            // Subscribe to layout changes
            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnTextViewClosed;

            // Subscribe to suggestion events from IntelliSense integration
            var intelliSenseIntegration = ServiceLocator.Current?.Resolve<IIntelliSenseIntegration>();
            if (intelliSenseIntegration != null)
            {
                intelliSenseIntegration.SuggestionAccepted += OnSuggestionAccepted;
                intelliSenseIntegration.SuggestionDismissed += OnSuggestionDismissed;
            }
        }

        public void ShowPreview(CodeSuggestion suggestion, SnapshotSpan span)
        {
            if (suggestion == null || string.IsNullOrWhiteSpace(suggestion.Text))
                return;

            lock (_lockObject)
            {
                _currentSuggestion = suggestion;
                _currentSpan = span;
                
                // Update the adornment on the UI thread
                _view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        CreateOrUpdateAdornment();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogErrorAsync(ex, "Error showing inline preview", "InlinePreview").Wait();
                    }
                }));
            }
        }

        public void HidePreview()
        {
            lock (_lockObject)
            {
                _currentSuggestion = null;
                _currentSpan = null;
                
                _view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _layer.RemoveAllAdornments();
                }));
            }
        }

        private void CreateOrUpdateAdornment()
        {
            _layer.RemoveAllAdornments();

            if (_currentSuggestion == null || !_currentSpan.HasValue)
                return;

            var span = _currentSpan.Value;
            var geometry = _view.TextViewLines.GetMarkerGeometry(span);
            if (geometry == null)
                return;

            var previewElement = CreatePreviewElement(_currentSuggestion);
            if (previewElement == null)
                return;

            // Position the preview at the end of the current text
            Canvas.SetLeft(previewElement, geometry.Bounds.Right);
            Canvas.SetTop(previewElement, geometry.Bounds.Top);

            _layer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                span,
                null,
                previewElement,
                null);
        }

        private UIElement CreatePreviewElement(CodeSuggestion suggestion)
        {
            try
            {
                var textBlock = new TextBlock
                {
                    Text = suggestion.Text,
                    FontFamily = _view.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                    FontSize = _view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize,
                    FontStyle = FontStyles.Italic,
                    Opacity = 0.5,
                    IsHitTestVisible = false
                };

                // Style based on theme (simplified - real implementation would detect VS theme)
                var isDarkTheme = true; // Assume dark theme for now
                textBlock.Foreground = new SolidColorBrush(
                    isDarkTheme ? 
                    Color.FromRgb(150, 150, 150) : 
                    Color.FromRgb(100, 100, 100));

                // Add confidence indicator if enabled
                if (_settingsService?.ShowConfidenceScores == true)
                {
                    var confidenceIndicator = CreateConfidenceIndicator(suggestion.Confidence);
                    
                    var stackPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        IsHitTestVisible = false
                    };
                    
                    stackPanel.Children.Add(textBlock);
                    stackPanel.Children.Add(confidenceIndicator);
                    
                    return stackPanel;
                }

                return textBlock;
            }
            catch (Exception ex)
            {
                _logger?.LogErrorAsync(ex, "Error creating preview element", "InlinePreview").Wait();
                return null;
            }
        }

        private UIElement CreateConfidenceIndicator(double confidence)
        {
            var indicator = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Confidence: {confidence:P0}"
            };

            // Color based on confidence level
            if (confidence >= 0.8)
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(0, 200, 0)); // Green
            }
            else if (confidence >= 0.6)
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // Orange
            }
            else
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(255, 100, 100)); // Red
            }

            return indicator;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // Update adornment position if text view layout changes
            if (_currentSuggestion != null && _currentSpan.HasValue)
            {
                foreach (var line in e.NewOrReformattedLines)
                {
                    if (line.Extent.OverlapsWith(_currentSpan.Value))
                    {
                        CreateOrUpdateAdornment();
                        break;
                    }
                }
            }
        }

        private void OnSuggestionAccepted(object sender, SuggestionAcceptedEventArgs e)
        {
            HidePreview();
        }

        private void OnSuggestionDismissed(object sender, SuggestionDismissedEventArgs e)
        {
            HidePreview();
        }

        private void OnTextViewClosed(object sender, EventArgs e)
        {
            // Clean up
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnTextViewClosed;

            var intelliSenseIntegration = ServiceLocator.Current?.Resolve<IIntelliSenseIntegration>();
            if (intelliSenseIntegration != null)
            {
                intelliSenseIntegration.SuggestionAccepted -= OnSuggestionAccepted;
                intelliSenseIntegration.SuggestionDismissed -= OnSuggestionDismissed;
            }

            _layer.RemoveAllAdornments();
        }
    }

    /// <summary>
    /// Extension methods for inline preview functionality
    /// </summary>
    internal static class InlinePreviewExtensions
    {
        public static void ShowInlinePreview(this IWpfTextView view, CodeSuggestion suggestion, SnapshotSpan span)
        {
            if (view == null || suggestion == null)
                return;

            var manager = view.Properties.TryGetProperty(
                typeof(InlinePreviewAdornmentManager),
                out InlinePreviewAdornmentManager adornmentManager) ? adornmentManager : null;

            manager?.ShowPreview(suggestion, span);
        }

        public static void HideInlinePreview(this IWpfTextView view)
        {
            if (view == null)
                return;

            var manager = view.Properties.TryGetProperty(
                typeof(InlinePreviewAdornmentManager),
                out InlinePreviewAdornmentManager adornmentManager) ? adornmentManager : null;

            manager?.HidePreview();
        }
    }
}