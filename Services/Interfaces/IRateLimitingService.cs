using System;
using System.Threading.Tasks;
using OllamaAssistant.Services.Implementation;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Service for implementing rate limiting and abuse prevention
    /// </summary>
    public interface IRateLimitingService
    {
        /// <summary>
        /// Checks if a request is allowed based on rate limits
        /// </summary>
        Task<RateLimitResult> CheckRateLimitAsync(string identifier, RateLimitType type);

        /// <summary>
        /// Records a completed request for tracking
        /// </summary>
        Task RecordRequestAsync(string identifier, RateLimitType type, bool success, TimeSpan duration);

        /// <summary>
        /// Checks if usage quota is available
        /// </summary>
        Task<QuotaResult> CheckUsageQuotaAsync(string identifier, QuotaType type);

        /// <summary>
        /// Records usage against quota
        /// </summary>
        Task RecordUsageAsync(string identifier, QuotaType type, int amount = 1);

        /// <summary>
        /// Checks if circuit breaker allows the request
        /// </summary>
        Task<bool> CheckCircuitBreakerAsync(string identifier);

        /// <summary>
        /// Checks if an identifier is engaging in suspicious activity
        /// </summary>
        Task<bool> IsSuspiciousActivityAsync(string identifier);

        /// <summary>
        /// Gets current rate limiting statistics
        /// </summary>
        Task<RateLimitingStats> GetStatsAsync();
    }

    /// <summary>
    /// Result of a rate limit check
    /// </summary>
    public class RateLimitResult
    {
        public bool IsAllowed { get; set; }
        public int Limit { get; set; }
        public int Remaining { get; set; }
        public DateTime ResetTime { get; set; }
        public TimeSpan RetryAfter { get; set; }
    }

    /// <summary>
    /// Result of a quota check
    /// </summary>
    public class QuotaResult
    {
        public bool IsAllowed { get; set; }
        public int Limit { get; set; }
        public int Used { get; set; }
        public int Remaining { get; set; }
        public DateTime ResetTime { get; set; }
    }

    /// <summary>
    /// Rate limiting statistics
    /// </summary>
    public class RateLimitingStats
    {
        public int TotalRequests { get; set; }
        public int RecentRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public int ActiveRateLimitBuckets { get; set; }
        public int ActiveQuotas { get; set; }
        public int CircuitBreakersOpen { get; set; }
        public int SuspiciousIdentifiers { get; set; }
        public DateTime Timestamp { get; set; }
    }
}