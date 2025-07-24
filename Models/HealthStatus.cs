using System;
using System.Collections.Generic;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Represents the health status of the Ollama service
    /// </summary>
    public class HealthStatus
    {
        /// <summary>
        /// Whether the service is healthy
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// Status message describing the health state
        /// </summary>
        public string StatusMessage { get; set; } = string.Empty;

        /// <summary>
        /// Response time in milliseconds
        /// </summary>
        public long ResponseTimeMs { get; set; }

        /// <summary>
        /// Timestamp of the health check
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Available models
        /// </summary>
        public List<string> AvailableModels { get; set; } = new List<string>();

        /// <summary>
        /// Server version information
        /// </summary>
        public string ServerVersion { get; set; } = string.Empty;

        /// <summary>
        /// Additional health metrics
        /// </summary>
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Creates a healthy status
        /// </summary>
        public static HealthStatus Healthy(string message = "OK")
        {
            return new HealthStatus
            {
                IsHealthy = true,
                StatusMessage = message,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates an unhealthy status
        /// </summary>
        public static HealthStatus Unhealthy(string message)
        {
            return new HealthStatus
            {
                IsHealthy = false,
                StatusMessage = message,
                Timestamp = DateTime.UtcNow
            };
        }
    }
}