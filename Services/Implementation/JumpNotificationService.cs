using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Services.Implementation
{
    /// <summary>
    /// Implementation of jump notification service for displaying cursor navigation hints
    /// </summary>
    public class JumpNotificationService : IJumpNotificationService
    {
        private readonly ITextViewService _textViewService;
        private JumpNotificationWindow _currentNotification;
        private JumpNotificationOptions _options;
        private System.Threading.Timer _autoHideTimer;
        private readonly object _lockObject = new object();
        private bool _disposed;

        public JumpNotificationService(ITextViewService textViewService)
        {
            _textViewService = textViewService ?? throw new ArgumentNullException(nameof(textViewService));
            _options = new JumpNotificationOptions();
        }

        #region Properties

        public bool IsNotificationVisible
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentNotification != null && _currentNotification.IsVisible;
                }
            }
        }

        public string JumpKeyBinding { get; private set; } = "Tab";

        #endregion

        #region Events

        public event EventHandler<JumpExecutedEventArgs> JumpExecuted;
        public event EventHandler<JumpDismissedEventArgs> JumpDismissed;

        #endregion

        #region Public Methods

        public async Task ShowJumpNotificationAsync(JumpRecommendation recommendation)
        {
            if (recommendation == null || recommendation.Direction == JumpDirection.None)
                return;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Hide any existing notification
                HideJumpNotification();

                // Create and show new notification
                var notification = new JumpNotificationWindow(recommendation, _options);
                
                lock (_lockObject)
                {
                    _currentNotification = notification;
                }

                // Subscribe to notification events
                notification.JumpRequested += OnJumpRequested;
                notification.NotificationDismissed += OnNotificationDismissed;

                // Position and show the notification
                PositionNotification(notification, recommendation);
                notification.Show();

                // Apply animation if enabled
                if (_options.AnimateAppearance)
                {
                    await AnimateNotificationAppearanceAsync(notification);
                }

                // Set up auto-hide timer if configured
                if (_options.AutoHideTimeout > 0)
                {
                    SetupAutoHideTimer(recommendation);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing jump notification: {ex.Message}");
            }
        }

        public void HideJumpNotification()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                lock (_lockObject)
                {
                    if (_currentNotification != null)
                    {
                        // Dispose auto-hide timer
                        _autoHideTimer?.Dispose();
                        _autoHideTimer = null;

                        // Hide and dispose notification
                        _currentNotification.Hide();
                        _currentNotification.Dispose();
                        _currentNotification = null;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding jump notification: {ex.Message}");
            }
        }

        public async Task UpdateNotificationAsync(JumpRecommendation recommendation)
        {
            if (recommendation == null)
            {
                HideJumpNotification();
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                lock (_lockObject)
                {
                    if (_currentNotification != null)
                    {
                        _currentNotification.UpdateRecommendation(recommendation);
                        PositionNotification(_currentNotification, recommendation);
                    }
                    else
                    {
                        // Show new notification if none exists
                        _ = ShowJumpNotificationAsync(recommendation);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating jump notification: {ex.Message}");
            }
        }

        public void ConfigureNotifications(JumpNotificationOptions options)
        {
            _options = options ?? new JumpNotificationOptions();
        }

        public void SetJumpKeyBinding(string keyBinding)
        {
            JumpKeyBinding = keyBinding ?? "Tab";
            
            // Update current notification if it exists
            lock (_lockObject)
            {
                _currentNotification?.UpdateKeyBinding(JumpKeyBinding);
            }
        }

        #endregion

        #region Private Methods

        private void PositionNotification(JumpNotificationWindow notification, JumpRecommendation recommendation)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var textView = _textViewService.GetActiveTextView();
                if (textView == null)
                    return;

                // Get text view bounds
                var textViewBounds = GetTextViewBounds(textView);
                if (!textViewBounds.HasValue)
                    return;

                var bounds = textViewBounds.Value;
                
                // Determine position based on jump direction and options
                var position = CalculateNotificationPosition(bounds, recommendation.Direction);
                
                // Apply position
                notification.Left = position.X;
                notification.Top = position.Y;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error positioning notification: {ex.Message}");
            }
        }

        private Rect? GetTextViewBounds(IWpfTextView textView)
        {
            try
            {
                if (textView.VisualElement == null)
                    return null;

                var element = textView.VisualElement;
                var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
                
                // Transform to screen coordinates
                var transform = element.TransformToAncestor(Application.Current.MainWindow);
                return transform.TransformBounds(bounds);
            }
            catch
            {
                return null;
            }
        }

        private Point CalculateNotificationPosition(Rect textViewBounds, JumpDirection direction)
        {
            var notificationWidth = 200; // Estimated notification width
            var notificationHeight = 60; // Estimated notification height
            
            return direction switch
            {
                JumpDirection.Up => _options.UpwardJumpPosition switch
                {
                    NotificationPosition.Bottom => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Bottom - notificationHeight - 10),
                    NotificationPosition.Top => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Top + 10),
                    NotificationPosition.Center => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Top + (textViewBounds.Height - notificationHeight) / 2),
                    _ => new Point(textViewBounds.Left + 10, textViewBounds.Bottom - notificationHeight - 10)
                },

                JumpDirection.Down => _options.DownwardJumpPosition switch
                {
                    NotificationPosition.Top => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Top + 10),
                    NotificationPosition.Bottom => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Bottom - notificationHeight - 10),
                    NotificationPosition.Center => new Point(
                        textViewBounds.Left + (textViewBounds.Width - notificationWidth) / 2,
                        textViewBounds.Top + (textViewBounds.Height - notificationHeight) / 2),
                    _ => new Point(textViewBounds.Left + 10, textViewBounds.Top + 10)
                },

                _ => new Point(textViewBounds.Left + 10, textViewBounds.Top + 10)
            };
        }

        private async Task AnimateNotificationAppearanceAsync(JumpNotificationWindow notification)
        {
            try
            {
                // Fade in animation
                var fadeIn = new DoubleAnimation(0, _options.Opacity, TimeSpan.FromMilliseconds(200));
                notification.BeginAnimation(UIElement.OpacityProperty, fadeIn);

                // Scale animation
                var scaleTransform = new ScaleTransform(0.8, 0.8);
                notification.RenderTransform = scaleTransform;
                
                var scaleX = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(200));
                var scaleY = new DoubleAnimation(0.8, 1.0, TimeSpan.FromMilliseconds(200));
                
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);

                // Wait for animation to complete
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error animating notification: {ex.Message}");
            }
        }

        private void SetupAutoHideTimer(JumpRecommendation recommendation)
        {
            _autoHideTimer?.Dispose();
            
            _autoHideTimer = new System.Threading.Timer(state =>
            {
                try
                {
                    ThreadHelper.JoinableTaskFactory.Run(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        
                        // Fire dismissed event
                        var dismissedArgs = new JumpDismissedEventArgs
                        {
                            Recommendation = recommendation,
                            Reason = JumpDismissalReason.Timeout,
                            DisplayDuration = _options.AutoHideTimeout
                        };

                        JumpDismissed?.Invoke(this, dismissedArgs);
                        
                        // Hide notification
                        HideJumpNotification();
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in auto-hide timer: {ex.Message}");
                }
            }, null, _options.AutoHideTimeout, System.Threading.Timeout.Infinite);
        }

        #endregion

        #region Event Handlers

        private void OnJumpRequested(object sender, JumpRequestedEventArgs e)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    
                    var success = await ExecuteJumpAsync(e.Recommendation);
                    
                    // Fire jump executed event
                    var executedArgs = new JumpExecutedEventArgs
                    {
                        Recommendation = e.Recommendation,
                        SourcePosition = GetCurrentCursorPosition(),
                        Success = success,
                        ErrorMessage = success ? null : "Failed to execute jump"
                    };

                    JumpExecuted?.Invoke(this, executedArgs);
                    
                    // Hide notification after successful jump
                    if (success)
                    {
                        HideJumpNotification();
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling jump request: {ex.Message}");
            }
        }

        private void OnNotificationDismissed(object sender, NotificationDismissedEventArgs e)
        {
            try
            {
                var dismissedArgs = new JumpDismissedEventArgs
                {
                    Recommendation = e.Recommendation,
                    Reason = JumpDismissalReason.UserDismissed,
                    DisplayDuration = e.DisplayDuration
                };

                JumpDismissed?.Invoke(this, dismissedArgs);
                
                // Clean up
                HideJumpNotification();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling notification dismissed: {ex.Message}");
            }
        }

        private async Task<bool> ExecuteJumpAsync(JumpRecommendation recommendation)
        {
            try
            {
                if (recommendation.IsCrossFile && !string.IsNullOrEmpty(recommendation.TargetFilePath))
                {
                    // Cross-file jump would require opening the target file
                    // This is a simplified implementation
                    return false;
                }
                else
                {
                    // Same-file jump
                    await _textViewService.MoveCaretToAsync(recommendation.TargetLine, recommendation.TargetColumn);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error executing jump: {ex.Message}");
                return false;
            }
        }

        private CursorPosition GetCurrentCursorPosition()
        {
            try
            {
                return new CursorPosition
                {
                    FilePath = _textViewService.GetCurrentFilePath(),
                    Line = _textViewService.GetCurrentLineNumber(),
                    Column = _textViewService.GetCurrentColumn()
                };
            }
            catch
            {
                return new CursorPosition();
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
                HideJumpNotification();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing JumpNotificationService: {ex.Message}");
            }
        }

        #endregion
    }

    /// <summary>
    /// Window for displaying jump notifications
    /// </summary>
    internal class JumpNotificationWindow : Window, IDisposable
    {
        private readonly JumpNotificationOptions _options;
        private JumpRecommendation _recommendation;
        private DateTime _showTime;
        private bool _disposed;

        public JumpNotificationWindow(JumpRecommendation recommendation, JumpNotificationOptions options)
        {
            _recommendation = recommendation ?? throw new ArgumentNullException(nameof(recommendation));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _showTime = DateTime.Now;

            InitializeWindow();
            CreateContent();
        }

        public event EventHandler<JumpRequestedEventArgs> JumpRequested;
        public event EventHandler<NotificationDismissedEventArgs> NotificationDismissed;

        private void InitializeWindow()
        {
            // Window properties
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            Opacity = 0; // Start invisible for animation

            // Event handlers
            KeyDown += OnKeyDown;
            MouseLeftButtonDown += OnMouseClick;
            Deactivated += OnDeactivated;
        }

        private void CreateContent()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(230, 45, 45, 48)), // VS Dark theme background
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 122, 204)), // VS Blue
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(4)
            };

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                //Spacing = 4,

            };

            // Direction indicator
            var directionText = new TextBlock
            {
                Text = GetDirectionText(),
                Foreground = Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };

            // Reason text
            var reasonText = new TextBlock
            {
                Text = _recommendation.Reason ?? "Jump to suggested position",
                Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 180
            };

            // Preview text (if available)
            if (_options.ShowTargetPreview && !string.IsNullOrEmpty(_recommendation.TargetPreview))
            {
                var previewText = new TextBlock
                {
                    Text = _recommendation.TargetPreview,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    FontSize = 9,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 180,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                stackPanel.Children.Add(previewText);
            }

            // Key binding hint
            var keyHintText = new TextBlock
            {
                Text = $"Press {GetKeyDisplayName()} to jump",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                FontSize = 9,
                Margin = new Thickness(0, 4, 0, 0)
            };

            stackPanel.Children.Add(directionText);
            stackPanel.Children.Add(reasonText);
            stackPanel.Children.Add(keyHintText);

            border.Child = stackPanel;
            Content = border;
        }

        public void UpdateRecommendation(JumpRecommendation recommendation)
        {
            _recommendation = recommendation ?? throw new ArgumentNullException(nameof(recommendation));
            
            // Recreate content with new recommendation
            CreateContent();
        }

        public void UpdateKeyBinding(string keyBinding)
        {
            // Find and update the key hint text
            if (Content is Border border && border.Child is StackPanel stackPanel)
            {
                foreach (var child in stackPanel.Children)
                {
                    if (child is TextBlock textBlock && textBlock.Text.StartsWith("Press "))
                    {
                        textBlock.Text = $"Press {GetKeyDisplayName(keyBinding)} to jump";
                        break;
                    }
                }
            }
        }

        private string GetDirectionText()
        {
            switch (_recommendation.Direction)
            {
                case JumpDirection.Up:
                    return "↑ Jump Up";
                case JumpDirection.Down:
                    return "↓ Jump Down";
                default:
                    return "→ Jump";
            };
        }

        private string GetKeyDisplayName(string keyBinding = null)
        {
            var key = keyBinding ?? "Tab";
            switch (key)
            {
                case "Tab":
                    return "Tab";
                case "Enter":
                    return "Enter";
                case "Space":
                    return "Space";
                default:
                    return key;
            };
        }

        #region Event Handlers

        private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                // Check if the pressed key matches the jump key binding
                var keyName = e.Key.ToString();
                if (keyName == "Tab" || keyName == GetKeyDisplayName()) // Simplified key matching
                {
                    e.Handled = true;
                    
                    var args = new JumpRequestedEventArgs { Recommendation = _recommendation };
                    JumpRequested?.Invoke(this, args);
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    e.Handled = true;
                    DismissNotification();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnKeyDown: {ex.Message}");
            }
        }

        private void OnMouseClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                e.Handled = true;
                
                var args = new JumpRequestedEventArgs { Recommendation = _recommendation };
                JumpRequested?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnMouseClick: {ex.Message}");
            }
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            try
            {
                DismissNotification();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in OnDeactivated: {ex.Message}");
            }
        }

        private void DismissNotification()
        {
            try
            {
                var displayDuration = (int)(DateTime.Now - _showTime).TotalMilliseconds;
                
                var args = new NotificationDismissedEventArgs
                {
                    Recommendation = _recommendation,
                    DisplayDuration = displayDuration
                };

                NotificationDismissed?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error dismissing notification: {ex.Message}");
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
                // Unsubscribe from events
                KeyDown -= OnKeyDown;
                MouseLeftButtonDown -= OnMouseClick;
                Deactivated -= OnDeactivated;

                // Close window
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing JumpNotificationWindow: {ex.Message}");
            }
        }

        #endregion
    }

    #region Event Args

    internal class JumpRequestedEventArgs : EventArgs
    {
        public JumpRecommendation Recommendation { get; set; }
    }

    internal class NotificationDismissedEventArgs : EventArgs
    {
        public JumpRecommendation Recommendation { get; set; }
        public int DisplayDuration { get; set; }
    }

    #endregion
}