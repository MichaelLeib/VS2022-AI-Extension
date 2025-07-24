using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.Shell;

namespace OllamaAssistant.UI.OptionPages
{
    /// <summary>
    /// General options page for the Ollama Assistant extension
    /// </summary>
    [Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890")]
    [ComVisible(true)]
    public class GeneralOptionsPage : DialogPage
    {
        #region Connection Settings

        [Category("Ollama Connection")]
        [DisplayName("Server Endpoint")]
        [Description("The URL of your Ollama server (e.g., http://localhost:11434)")]
        [DefaultValue("http://localhost:11434")]
        public string OllamaEndpoint { get; set; } = "http://localhost:11434";

        [Category("Ollama Connection")]
        [DisplayName("Model Name")]
        [Description("The Ollama model to use for code completion (e.g., codellama, deepseek-coder)")]
        [DefaultValue("codellama")]
        public string OllamaModel { get; set; } = "codellama";

        [Category("Ollama Connection")]
        [DisplayName("Request Timeout (ms)")]
        [Description("Timeout for Ollama API requests in milliseconds (5000-300000)")]
        [DefaultValue(30000)]
        public int OllamaTimeout { get; set; } = 30000;

        #endregion

        #region Context Settings

        [Category("Context Settings")]
        [DisplayName("Lines Above Cursor")]
        [Description("Number of lines to capture above the cursor position (0-50)")]
        [DefaultValue(3)]
        public int SurroundingLinesUp { get; set; } = 3;

        [Category("Context Settings")]
        [DisplayName("Lines Below Cursor")]
        [Description("Number of lines to capture below the cursor position (0-50)")]
        [DefaultValue(2)]
        public int SurroundingLinesDown { get; set; } = 2;

        [Category("Context Settings")]
        [DisplayName("Cursor History Depth")]
        [Description("Number of cursor positions to remember across files (1-10)")]
        [DefaultValue(3)]
        public int CursorHistoryMemoryDepth { get; set; } = 3;

        #endregion

        #region Feature Settings

        [Category("Features")]
        [DisplayName("Enable Extension")]
        [Description("Enable or disable the Ollama Assistant extension completely")]
        [DefaultValue(true)]
        public bool IsEnabled { get; set; } = true;

        [Category("Features")]
        [DisplayName("Enable Code Predictions")]
        [Description("Show AI-powered code completion suggestions")]
        [DefaultValue(true)]
        public bool CodePredictionEnabled { get; set; } = true;

        [Category("Features")]
        [DisplayName("Enable Jump Recommendations")]
        [Description("Show cursor navigation suggestions")]
        [DefaultValue(true)]
        public bool JumpRecommendationsEnabled { get; set; } = true;

        [Category("Features")]
        [DisplayName("Jump Key")]
        [Description("Key to execute jump recommendations")]
        [DefaultValue(Keys.Tab)]
        [TypeConverter(typeof(KeysConverter))]
        public Keys JumpKey { get; set; } = Keys.Tab;

        #endregion

        #region Display Settings

        [Category("Display")]
        [DisplayName("Show Confidence Scores")]
        [Description("Display confidence scores for AI suggestions")]
        [DefaultValue(false)]
        public bool ShowConfidenceScores { get; set; } = false;

        [Category("Display")]
        [DisplayName("Minimum Confidence")]
        [Description("Minimum confidence threshold for showing suggestions (0.0-1.0)")]
        [DefaultValue(0.7)]
        [TypeConverter(typeof(DoubleConverter))]
        public double MinimumConfidenceThreshold { get; set; } = 0.7;

        #endregion

        #region Performance Settings

        [Category("Performance")]
        [DisplayName("Typing Debounce Delay (ms)")]
        [Description("Delay before requesting suggestions while typing (100-2000)")]
        [DefaultValue(500)]
        public int TypingDebounceDelay { get; set; } = 500;

        #endregion

        #region Debugging Settings

        [Category("Debugging")]
        [DisplayName("Enable Verbose Logging")]
        [Description("Enable detailed logging for troubleshooting")]
        [DefaultValue(false)]
        public bool EnableVerboseLogging { get; set; } = false;

        #endregion

        #region Validation

        protected override void OnApply(PageApplyEventArgs e)
        {
            // Validate Ollama endpoint
            if (string.IsNullOrWhiteSpace(OllamaEndpoint))
            {
                e.ApplyBehavior = ApplyKind.Cancel;
                MessageBox.Show("Ollama endpoint cannot be empty.", "Validation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!Uri.TryCreate(OllamaEndpoint, UriKind.Absolute, out var uri) || 
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                e.ApplyBehavior = ApplyKind.Cancel;
                MessageBox.Show("Invalid Ollama endpoint URL. Please enter a valid HTTP/HTTPS URL.", 
                    "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validate model name
            if (string.IsNullOrWhiteSpace(OllamaModel))
            {
                e.ApplyBehavior = ApplyKind.Cancel;
                MessageBox.Show("Model name cannot be empty.", "Validation Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Validate numeric ranges
            SurroundingLinesUp = Math.Max(0, Math.Min(50, SurroundingLinesUp));
            SurroundingLinesDown = Math.Max(0, Math.Min(50, SurroundingLinesDown));
            CursorHistoryMemoryDepth = Math.Max(1, Math.Min(10, CursorHistoryMemoryDepth));
            OllamaTimeout = Math.Max(5000, Math.Min(300000, OllamaTimeout));
            MinimumConfidenceThreshold = Math.Max(0.0, Math.Min(1.0, MinimumConfidenceThreshold));
            TypingDebounceDelay = Math.Max(100, Math.Min(2000, TypingDebounceDelay));

            base.OnApply(e);
        }

        #endregion
    }

    /// <summary>
    /// Custom type converter for double values with validation
    /// </summary>
    public class DoubleConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, 
            System.Globalization.CultureInfo culture, object value)
        {
            if (value is string stringValue)
            {
                if (double.TryParse(stringValue, out var result))
                {
                    return Math.Max(0.0, Math.Min(1.0, result));
                }
            }
            return base.ConvertFrom(context, culture, value);
        }
    }
}