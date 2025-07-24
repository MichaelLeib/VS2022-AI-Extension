using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Infrastructure;
using OllamaAssistant.Services.Interfaces;

namespace OllamaAssistant.Tests.TestUtilities
{
    /// <summary>
    /// Base class for all unit tests providing common testing utilities
    /// </summary>
    [TestClass]
    public abstract class BaseTest
    {
        protected Mock<ISettingsService> MockSettingsService { get; private set; }
        protected Mock<ILogger> MockLogger { get; private set; }
        protected Mock<ErrorHandler> MockErrorHandler { get; private set; }
        protected TestScenario TestScenario { get; private set; }
        protected CancellationTokenSource CancellationTokenSource { get; private set; }

        [TestInitialize]
        public virtual void TestInitialize()
        {
            // Create default mocks
            MockSettingsService = MockFactory.CreateMockSettingsService();
            MockLogger = MockFactory.CreateMockLogger();
            MockErrorHandler = MockFactory.CreateMockErrorHandler();

            // Create cancellation token source with reasonable timeout
            CancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Allow derived classes to customize setup
            OnTestInitialize();
        }

        [TestCleanup]
        public virtual void TestCleanup()
        {
            CancellationTokenSource?.Dispose();
            OnTestCleanup();
        }

        /// <summary>
        /// Override in derived classes for custom initialization
        /// </summary>
        protected virtual void OnTestInitialize() { }

        /// <summary>
        /// Override in derived classes for custom cleanup
        /// </summary>
        protected virtual void OnTestCleanup() { }

        #region Test Utilities

        /// <summary>
        /// Creates a test scenario with the specified configuration
        /// </summary>
        protected TestScenario CreateTestScenario()
        {
            return MockFactory.CreateTestScenario()
                .WithMockSettingsService()
                .WithMockLogger()
                .Build();
        }

        /// <summary>
        /// Runs an async test method with proper exception handling
        /// </summary>
        protected async Task RunAsync(Func<Task> testAction)
        {
            try
            {
                await testAction();
            }
            catch (Exception ex)
            {
                // Log the exception for debugging
                TestContext.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Runs an async test method with return value
        /// </summary>
        protected async Task<T> RunAsync<T>(Func<Task<T>> testAction)
        {
            try
            {
                return await testAction();
            }
            catch (Exception ex)
            {
                TestContext.WriteLine($"Test failed with exception: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Asserts that an async method throws a specific exception
        /// </summary>
        protected async Task AssertThrowsAsync<TException>(Func<Task> action, string expectedMessage = null)
            where TException : Exception
        {
            try
            {
                await action();
                Assert.Fail($"Expected {typeof(TException).Name} to be thrown");
            }
            catch (TException ex)
            {
                if (!string.IsNullOrEmpty(expectedMessage))
                {
                    ex.Message.Should().Contain(expectedMessage);
                }
            }
            catch (Exception ex)
            {
                Assert.Fail($"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Asserts that an async method completes within the specified timeout
        /// </summary>
        protected async Task AssertCompletesWithinTimeout(Func<Task> action, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                Assert.Fail($"Operation did not complete within {timeout.TotalMilliseconds}ms");
            }
        }

        /// <summary>
        /// Verifies that a mock method was called the expected number of times
        /// </summary>
        protected void VerifyMethodCalled<T>(Mock<T> mock, System.Linq.Expressions.Expression<Action<T>> expression, Times times)
            where T : class
        {
            mock.Verify(expression, times);
        }

        /// <summary>
        /// Verifies that an async mock method was called the expected number of times
        /// </summary>
        protected void VerifyAsyncMethodCalled<T>(Mock<T> mock, System.Linq.Expressions.Expression<Func<T, Task>> expression, Times times)
            where T : class
        {
            mock.Verify(expression, times);
        }

        /// <summary>
        /// Sets up a mock to track method calls
        /// </summary>
        protected void SetupMethodCallTracking<T, TResult>(Mock<T> mock, 
            System.Linq.Expressions.Expression<Func<T, TResult>> expression, 
            TResult returnValue)
            where T : class
        {
            mock.Setup(expression).Returns(returnValue);
        }

        /// <summary>
        /// Creates a test context with the specified properties
        /// </summary>
        protected void SetTestContext(string key, object value)
        {
            TestContext.Properties[key] = value;
        }

        /// <summary>
        /// Gets a value from the test context
        /// </summary>
        protected T GetTestContext<T>(string key)
        {
            return (T)TestContext.Properties[key];
        }

        /// <summary>
        /// Writes a message to the test output
        /// </summary>
        protected void WriteTestOutput(string message)
        {
            TestContext.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }

        /// <summary>
        /// Measures the execution time of an operation
        /// </summary>
        protected async Task<TimeSpan> MeasureExecutionTime(Func<Task> operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            return stopwatch.Elapsed;
        }

        /// <summary>
        /// Measures the execution time of an operation with return value
        /// </summary>
        protected async Task<(T Result, TimeSpan Duration)> MeasureExecutionTime<T>(Func<Task<T>> operation)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await operation();
            stopwatch.Stop();
            return (result, stopwatch.Elapsed);
        }

        /// <summary>
        /// Waits for a condition to be true with timeout
        /// </summary>
        protected async Task WaitForCondition(Func<bool> condition, TimeSpan timeout, TimeSpan pollInterval = default)
        {
            if (pollInterval == default)
                pollInterval = TimeSpan.FromMilliseconds(100);

            var endTime = DateTime.UtcNow.Add(timeout);
            
            while (DateTime.UtcNow < endTime)
            {
                if (condition())
                    return;
                
                await Task.Delay(pollInterval);
            }
            
            Assert.Fail($"Condition was not met within {timeout.TotalMilliseconds}ms");
        }

        /// <summary>
        /// Creates a controlled delay for testing timing-sensitive scenarios
        /// </summary>
        protected async Task DelayAsync(int milliseconds)
        {
            await Task.Delay(milliseconds, CancellationTokenSource.Token);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the test context
        /// </summary>
        public TestContext TestContext { get; set; }

        /// <summary>
        /// Gets the cancellation token for the current test
        /// </summary>
        protected CancellationToken CancellationToken => CancellationTokenSource.Token;

        #endregion
    }

    /// <summary>
    /// Attribute to mark tests that require integration with external services
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class IntegrationTestAttribute : Attribute
    {
        public string RequiredService { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Attribute to mark tests that test performance characteristics
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class PerformanceTestAttribute : Attribute
    {
        public int MaxExecutionTimeMs { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Test categories for organizing tests
    /// </summary>
    public static class TestCategories
    {
        public const string Unit = "Unit";
        public const string Integration = "Integration";
        public const string Performance = "Performance";
        public const string Service = "Service";
        public const string UI = "UI";
        public const string Network = "Network";
        public const string Settings = "Settings";
        public const string ErrorHandling = "ErrorHandling";
        public const string Threading = "Threading";
    }
}