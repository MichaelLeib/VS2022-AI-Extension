using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace OllamaAssistant.UI.Components
{
    /// <summary>
    /// Visual confidence indicator for AI suggestions
    /// </summary>
    public class ConfidenceIndicator : UserControl
    {
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _percentageText;
        private readonly Ellipse _statusLight;
        private double _confidence;

        public ConfidenceIndicator()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            var grid = new Grid
            {
                Width = 120,
                Height = 24
            };

            // Add column definitions
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            // Status light
            _statusLight = new Ellipse
            {
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                StrokeThickness = 1
            };
            Grid.SetColumn(_statusLight, 0);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
                Minimum = 0,
                Maximum = 100
            };
            Grid.SetColumn(_progressBar, 1);

            // Percentage text
            _percentageText = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
            };
            Grid.SetColumn(_percentageText, 2);

            // Add children to grid
            grid.Children.Add(_statusLight);
            grid.Children.Add(_progressBar);
            grid.Children.Add(_percentageText);

            // Set grid as content
            Content = grid;

            // Apply initial styling
            ApplyStyling();
        }

        /// <summary>
        /// Gets or sets the confidence value (0.0 to 1.0)
        /// </summary>
        public double Confidence
        {
            get => _confidence;
            set
            {
                _confidence = Math.Max(0, Math.Min(1, value));
                UpdateVisual();
            }
        }

        /// <summary>
        /// Gets or sets whether to animate changes
        /// </summary>
        public bool AnimateChanges { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to show percentage text
        /// </summary>
        public bool ShowPercentage { get; set; } = true;

        private void UpdateVisual()
        {
            var percentage = _confidence * 100;
            
            // Update percentage text
            if (ShowPercentage)
            {
                _percentageText.Text = $"{percentage:F0}%";
                _percentageText.Visibility = Visibility.Visible;
            }
            else
            {
                _percentageText.Visibility = Visibility.Collapsed;
            }

            // Update progress bar
            if (AnimateChanges)
            {
                var animation = new DoubleAnimation
                {
                    To = percentage,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                _progressBar.BeginAnimation(ProgressBar.ValueProperty, animation);
            }
            else
            {
                _progressBar.Value = percentage;
            }

            // Update colors based on confidence level
            UpdateColors();

            // Update status light
            UpdateStatusLight();
        }

        private void UpdateColors()
        {
            Color barColor;
            Color lightColor;

            if (_confidence >= 0.8)
            {
                // High confidence - Green
                barColor = Color.FromRgb(0, 200, 0);
                lightColor = Color.FromRgb(0, 255, 0);
            }
            else if (_confidence >= 0.6)
            {
                // Medium confidence - Yellow/Orange
                barColor = Color.FromRgb(255, 165, 0);
                lightColor = Color.FromRgb(255, 200, 0);
            }
            else if (_confidence >= 0.4)
            {
                // Low confidence - Orange
                barColor = Color.FromRgb(255, 100, 0);
                lightColor = Color.FromRgb(255, 150, 0);
            }
            else
            {
                // Very low confidence - Red
                barColor = Color.FromRgb(200, 0, 0);
                lightColor = Color.FromRgb(255, 0, 0);
            }

            // Apply progress bar color
            _progressBar.Foreground = new SolidColorBrush(barColor);

            // Apply status light color with animation
            if (AnimateChanges)
            {
                var colorAnimation = new ColorAnimation
                {
                    To = lightColor,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
                };
                
                var brush = new SolidColorBrush();
                brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
                _statusLight.Fill = brush;
            }
            else
            {
                _statusLight.Fill = new SolidColorBrush(lightColor);
            }
        }

        private void UpdateStatusLight()
        {
            // Add pulsing animation for very high confidence
            if (_confidence >= 0.9 && AnimateChanges)
            {
                var pulseAnimation = new DoubleAnimation
                {
                    From = 0.7,
                    To = 1.0,
                    Duration = TimeSpan.FromSeconds(1),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                
                _statusLight.BeginAnimation(OpacityProperty, pulseAnimation);
            }
            else
            {
                _statusLight.BeginAnimation(OpacityProperty, null);
                _statusLight.Opacity = 1.0;
            }
        }

        private void ApplyStyling()
        {
            // Style the progress bar
            var progressBarStyle = new Style(typeof(ProgressBar));
            
            // Remove default chrome
            progressBarStyle.Setters.Add(new Setter(ProgressBar.TemplateProperty, CreateProgressBarTemplate()));
            
            _progressBar.Style = progressBarStyle;

            // Set background
            Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0));
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            BorderThickness = new Thickness(1);
        }

        private ControlTemplate CreateProgressBarTemplate()
        {
            var template = new ControlTemplate(typeof(ProgressBar));
            
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 30, 30)));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            
            var grid = new FrameworkElementFactory(typeof(Grid));
            
            var indicator = new FrameworkElementFactory(typeof(Rectangle));
            indicator.SetValue(Rectangle.RadiusXProperty, 5.0);
            indicator.SetValue(Rectangle.RadiusYProperty, 5.0);
            indicator.SetValue(Rectangle.FillProperty, new TemplateBindingExtension(ProgressBar.ForegroundProperty));
            indicator.SetValue(Rectangle.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            indicator.SetValue(Rectangle.MarginProperty, new Thickness(1));
            
            // Bind width to progress
            var widthBinding = new System.Windows.Data.Binding
            {
                Path = new PropertyPath("Value"),
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
                Converter = new ProgressToWidthConverter()
            };
            indicator.SetBinding(Rectangle.WidthProperty, widthBinding);
            
            grid.AppendChild(indicator);
            border.AppendChild(grid);
            
            template.VisualTree = border;
            return template;
        }
    }

    /// <summary>
    /// Converts progress value to width
    /// </summary>
    internal class ProgressToWidthConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double progress && parameter is double maxWidth)
            {
                return (progress / 100.0) * maxWidth;
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Compact confidence indicator for inline display
    /// </summary>
    public class CompactConfidenceIndicator : UserControl
    {
        private readonly Rectangle _bar;
        private readonly Border _container;
        
        public CompactConfidenceIndicator()
        {
            Width = 40;
            Height = 4;
            
            _container = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(0.5)
            };
            
            _bar = new Rectangle
            {
                RadiusX = 2,
                RadiusY = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0.5)
            };
            
            _container.Child = _bar;
            Content = _container;
        }
        
        public double Confidence
        {
            set
            {
                var confidence = Math.Max(0, Math.Min(1, value));
                _bar.Width = (Width - 1) * confidence;
                
                // Update color
                if (confidence >= 0.8)
                    _bar.Fill = new SolidColorBrush(Color.FromRgb(0, 200, 0));
                else if (confidence >= 0.6)
                    _bar.Fill = new SolidColorBrush(Color.FromRgb(255, 165, 0));
                else
                    _bar.Fill = new SolidColorBrush(Color.FromRgb(200, 0, 0));
            }
        }
    }
}