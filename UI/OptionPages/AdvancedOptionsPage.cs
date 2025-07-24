using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace OllamaAssistant.UI.OptionPages
{
    /// <summary>
    /// Advanced options page for the Ollama Assistant extension
    /// </summary>
    [Guid("B1C2D3E4-F5G6-7890-BCDE-FG2345678901")]
    [ComVisible(true)]
    public class AdvancedOptionsPage : DialogPage
    {
        #region AI Behavior Settings

        [Category("AI Behavior")]
        [DisplayName("Include File Context")]
        [Description("Include file path and language in AI prompts")]
        [DefaultValue(true)]
        public bool IncludeFileContext { get; set; } = true;

        [Category("AI Behavior")]
        [DisplayName("Include Cursor History")]
        [Description("Include cursor position history in AI prompts for better context")]
        [DefaultValue(true)]
        public bool IncludeCursorHistory { get; set; } = true;

        [Category("AI Behavior")]
        [DisplayName("Smart Context Selection")]
        [Description("Intelligently select relevant history entries based on file relationships")]
        [DefaultValue(true)]
        public bool SmartContextSelection { get; set; } = true;

        [Category("AI Behavior")]
        [DisplayName("Max Prompt Length")]
        [Description("Maximum characters to send in a single prompt (1000-10000)")]
        [DefaultValue(4000)]
        public int MaxPromptLength { get; set; } = 4000;

        #endregion

        #region Jump Behavior Settings

        [Category("Jump Behavior")]
        [DisplayName("Auto-hide Jump Notifications")]
        [Description("Automatically hide jump notifications after timeout")]
        [DefaultValue(true)]
        public bool AutoHideJumpNotifications { get; set; } = true;

        [Category("Jump Behavior")]
        [DisplayName("Jump Notification Timeout (ms)")]
        [Description("Time before jump notifications auto-hide (1000-10000)")]
        [DefaultValue(5000)]
        public int JumpNotificationTimeout { get; set; } = 5000;

        [Category("Jump Behavior")]
        [DisplayName("Show Jump Preview")]
        [Description("Show a preview of the target location in jump notifications")]
        [DefaultValue(true)]
        public bool ShowJumpPreview { get; set; } = true;

        [Category("Jump Behavior")]
        [DisplayName("Animate Jump Notifications")]
        [Description("Use animations for jump notification appearance")]
        [DefaultValue(true)]
        public bool AnimateJumpNotifications { get; set; } = true;

        [Category("Jump Notifications")]
        [DisplayName("Notification Opacity")]
        [Description("Opacity percentage for jump notifications (10-100)")]
        [DefaultValue(80)]
        public int NotificationOpacity { get; set; } = 80;

        #endregion

        #region Suggestion Behavior Settings

        [Category("Suggestion Behavior")]
        [DisplayName("Max Suggestions")]
        [Description("Maximum number of suggestions to show at once (1-10)")]
        [DefaultValue(5)]
        public int MaxSuggestions { get; set; } = 5;

        [Category("Suggestion Behavior")]
        [DisplayName("Filter Duplicates")]
        [Description("Remove duplicate suggestions from the list")]
        [DefaultValue(true)]
        public bool FilterDuplicates { get; set; } = true;

        [Category("Suggestion Behavior")]
        [DisplayName("Adapt to Code Style")]
        [Description("Automatically adapt suggestions to match current code style")]
        [DefaultValue(true)]
        public bool AdaptToCodeStyle { get; set; } = true;

        [Category("Suggestion Behavior")]
        [DisplayName("Show Inline Preview")]
        [Description("Show inline preview of suggestions in the editor")]
        [DefaultValue(true)]
        public bool ShowInlinePreview { get; set; } = true;

        #endregion

        #region Cache Settings

        [Category("Caching")]
        [DisplayName("Enable Suggestion Cache")]
        [Description("Cache recent suggestions to improve performance")]
        [DefaultValue(true)]
        public bool EnableSuggestionCache { get; set; } = true;

        [Category("Caching")]
        [DisplayName("Cache Size")]
        [Description("Number of suggestions to keep in cache (10-100)")]
        [DefaultValue(50)]
        public int CacheSize { get; set; } = 50;

        [Category("Caching")]
        [DisplayName("Cache Expiration (minutes)")]
        [Description("Time before cached suggestions expire (1-60)")]
        [DefaultValue(10)]
        public int CacheExpirationMinutes { get; set; } = 10;

        #endregion

        #region Performance Settings

        [Category("Performance")]
        [DisplayName("Enable Caching")]
        [Description("Enable caching to improve performance")]
        [DefaultValue(true)]
        public bool EnableCaching { get; set; } = true;

        [Category("Performance")]
        [DisplayName("Request Debounce Delay (ms)")]
        [Description("Delay between keystrokes before sending AI request (100-2000)")]
        [DefaultValue(300)]
        public int RequestDebounceDelay { get; set; } = 300;

        [Category("Performance")]
        [DisplayName("Max Concurrent Requests")]
        [Description("Maximum number of concurrent AI requests (1-5)")]
        [DefaultValue(2)]
        public int MaxConcurrentRequests { get; set; } = 2;

        #endregion

        #region Security Settings

        [Category("Security")]
        [DisplayName("Validate SSL Certificates")]
        [Description("Validate SSL certificates when connecting to Ollama")]
        [DefaultValue(true)]
        public bool ValidateSSLCertificates { get; set; } = true;

        [Category("Security")]
        [DisplayName("Max Request Size (KB)")]
        [Description("Maximum size of a single request to prevent abuse (1-50)")]
        [DefaultValue(10)]
        public int MaxRequestSizeKB { get; set; } = 10;

        [Category("Security")]
        [DisplayName("Filter Sensitive Data")]
        [Description("Automatically filter potentially sensitive data from requests")]
        [DefaultValue(true)]
        public bool FilterSensitiveData { get; set; } = true;

        #endregion

        #region Experimental Features

        [Category("Experimental")]
        [DisplayName("Enable Multi-file Context")]
        [Description("EXPERIMENTAL: Analyze context across multiple related files")]
        [DefaultValue(false)]
        public bool EnableMultiFileContext { get; set; } = false;

        [Category("Experimental")]
        [DisplayName("Enable Semantic Analysis")]
        [Description("EXPERIMENTAL: Use semantic analysis for better suggestions")]
        [DefaultValue(false)]
        public bool EnableSemanticAnalysis { get; set; } = false;

        [Category("Experimental")]
        [DisplayName("Enable Pattern Learning")]
        [Description("EXPERIMENTAL: Learn from accepted suggestions to improve future ones")]
        [DefaultValue(false)]
        public bool EnablePatternLearning { get; set; } = false;

        #endregion

        #region Validation

        protected override void OnApply(PageApplyEventArgs e)
        {
            // Validate numeric ranges
            MaxPromptLength = Math.Max(1000, Math.Min(10000, MaxPromptLength));
            JumpNotificationTimeout = Math.Max(1000, Math.Min(10000, JumpNotificationTimeout));
            MaxSuggestions = Math.Max(1, Math.Min(10, MaxSuggestions));
            CacheSize = Math.Max(10, Math.Min(100, CacheSize));
            CacheExpirationMinutes = Math.Max(1, Math.Min(60, CacheExpirationMinutes));
            NotificationOpacity = Math.Max(10, Math.Min(100, NotificationOpacity));
            RequestDebounceDelay = Math.Max(100, Math.Min(2000, RequestDebounceDelay));
            MaxConcurrentRequests = Math.Max(1, Math.Min(5, MaxConcurrentRequests));
            MaxRequestSizeKB = Math.Max(1, Math.Min(50, MaxRequestSizeKB));

            base.OnApply(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Notify that settings have changed
            NotifySettingsChanged();
            base.OnClosed(e);
        }

        private void NotifySettingsChanged()
        {
            // This could be used to notify services that settings have changed
            // Implementation would depend on the service architecture
        }

        #endregion
    }
}