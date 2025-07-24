using System;
using System.Threading.Tasks;
using OllamaAssistant.Models;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for displaying cursor jump notifications
    /// </summary>
    public interface IJumpNotificationService
    {
        /// <summary>
        /// Shows a jump notification to the user
        /// </summary>
        /// <param name="recommendation">The jump recommendation to display</param>
        Task ShowJumpNotificationAsync(JumpRecommendation recommendation);

        /// <summary>
        /// Hides the current jump notification
        /// </summary>
        void HideJumpNotification();

        /// <summary>
        /// Checks if a jump notification is currently visible
        /// </summary>
        bool IsNotificationVisible { get; }

        /// <summary>
        /// Updates the current notification with new information
        /// </summary>
        /// <param name="recommendation">The updated recommendation</param>
        Task UpdateNotificationAsync(JumpRecommendation recommendation);

        /// <summary>
        /// Fired when a jump is executed by the user
        /// </summary>
        event EventHandler<JumpExecutedEventArgs> JumpExecuted;

        /// <summary>
        /// Fired when a jump notification is dismissed
        /// </summary>
        event EventHandler<JumpDismissedEventArgs> JumpDismissed;

        /// <summary>
        /// Configures the appearance and behavior of notifications
        /// </summary>
        /// <param name="options">Display options for notifications</param>
        void ConfigureNotifications(JumpNotificationOptions options);

        /// <summary>
        /// Gets the key binding for executing jumps
        /// </summary>
        string JumpKeyBinding { get; }

        /// <summary>
        /// Sets the key binding for executing jumps
        /// </summary>
        /// <param name="keyBinding">The key combination (e.g., "Tab", "Ctrl+J")</param>
        void SetJumpKeyBinding(string keyBinding);
    }

    /// <summary>
    /// Event args when a jump is executed
    /// </summary>
    public class JumpExecutedEventArgs : EventArgs
    {
        /// <summary>
        /// The recommendation that was executed
        /// </summary>
        public JumpRecommendation Recommendation { get; set; }

        /// <summary>
        /// The source position before the jump
        /// </summary>
        public CursorPosition SourcePosition { get; set; }

        /// <summary>
        /// Whether the jump was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if the jump failed
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Event args when a jump notification is dismissed
    /// </summary>
    public class JumpDismissedEventArgs : EventArgs
    {
        /// <summary>
        /// The recommendation that was dismissed
        /// </summary>
        public JumpRecommendation Recommendation { get; set; }

        /// <summary>
        /// Why the notification was dismissed
        /// </summary>
        public JumpDismissalReason Reason { get; set; }

        /// <summary>
        /// How long the notification was visible (in milliseconds)
        /// </summary>
        public int DisplayDuration { get; set; }
    }

    /// <summary>
    /// Reasons why a jump notification might be dismissed
    /// </summary>
    public enum JumpDismissalReason
    {
        /// <summary>
        /// User explicitly dismissed the notification
        /// </summary>
        UserDismissed,

        /// <summary>
        /// User moved the cursor manually
        /// </summary>
        CursorMoved,

        /// <summary>
        /// Notification timed out
        /// </summary>
        Timeout,

        /// <summary>
        /// File or context changed
        /// </summary>
        ContextChanged,

        /// <summary>
        /// Replaced by a new notification
        /// </summary>
        Replaced
    }

    /// <summary>
    /// Options for jump notification display
    /// </summary>
    public class JumpNotificationOptions
    {
        /// <summary>
        /// Position of notifications for upward jumps
        /// </summary>
        public NotificationPosition UpwardJumpPosition { get; set; } = NotificationPosition.Bottom;

        /// <summary>
        /// Position of notifications for downward jumps
        /// </summary>
        public NotificationPosition DownwardJumpPosition { get; set; } = NotificationPosition.Top;

        /// <summary>
        /// Opacity of the notification (0.0 to 1.0)
        /// </summary>
        public double Opacity { get; set; } = 0.8;

        /// <summary>
        /// Auto-hide timeout in milliseconds (0 = no auto-hide)
        /// </summary>
        public int AutoHideTimeout { get; set; } = 5000;

        /// <summary>
        /// Whether to show a preview of the target location
        /// </summary>
        public bool ShowTargetPreview { get; set; } = true;

        /// <summary>
        /// Whether to animate the notification appearance
        /// </summary>
        public bool AnimateAppearance { get; set; } = true;
    }

    /// <summary>
    /// Positions where notifications can appear
    /// </summary>
    public enum NotificationPosition
    {
        Top,
        Bottom,
        Left,
        Right,
        Center
    }

    /// <summary>
    /// Represents a cursor position for jump notifications
    /// </summary>
    public class JumpCursorPosition
    {
        public string FilePath { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
    }
}