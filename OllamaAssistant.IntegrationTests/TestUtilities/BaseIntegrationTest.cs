using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.IntegrationTests.TestUtilities
{
    /// <summary>
    /// Base class for integration tests providing common infrastructure
    /// </summary>
    [TestClass]
    public abstract class BaseIntegrationTest : BaseTest
    {
        protected static readonly string TestDataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
        protected OllamaTestServer TestServer { get; private set; }
        protected HttpClient HttpClient { get; private set; }

        [ClassInitialize]
        public static void ClassInitialize(TestContext context)
        {
            // Ensure test data directory exists
            if (!Directory.Exists(TestDataDirectory))
            {
                Directory.CreateDirectory(TestDataDirectory);
            }

            // Create sample test files
            CreateTestFiles();
        }

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            
            // Initialize test server and HTTP client
            InitializeTestInfrastructure();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            TestServer?.Dispose();
            HttpClient?.Dispose();
            base.TestCleanup();
        }

        #region Test Infrastructure

        private void InitializeTestInfrastructure()
        {
            // Setup test HTTP server for mocking Ollama
            TestServer = new OllamaTestServer();
            HttpClient = new HttpClient();
        }

        /// <summary>
        /// Checks if a real Ollama server is available for integration testing
        /// </summary>
        protected async Task<bool> IsOllamaServerAvailableAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                
                var response = await client.GetAsync("http://localhost:11434/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Skips test if Ollama server is not available
        /// </summary>
        protected async Task RequireOllamaServerAsync()
        {
            var isAvailable = await IsOllamaServerAvailableAsync();
            if (!isAvailable)
            {
                Assert.Inconclusive("Ollama server is not available at localhost:11434. Integration test skipped.");
            }
        }

        /// <summary>
        /// Creates a test OllamaHttpClient configured for integration testing
        /// </summary>
        protected OllamaHttpClient CreateTestOllamaClient(string baseUrl = "http://localhost:11434")
        {
            var config = new OllamaHttpClientConfig
            {
                TimeoutMs = 30000,
                HealthCheckTimeoutMs = 5000,
                MaxConcurrentRequests = 2,
                MaxConnectionsPerServer = 5,
                MaxRetryAttempts = 1, // Reduced for faster tests
                BaseRetryDelayMs = 500,
                MaxRetryDelayMs = 2000,
                RetryBackoffMultiplier = 1.5
            };

            return new OllamaHttpClient(baseUrl, config);
        }

        /// <summary>
        /// Waits for a condition to be true or times out
        /// </summary>
        protected async Task<bool> WaitForConditionAsync(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan pollInterval = default)
        {
            if (pollInterval == default)
                pollInterval = TimeSpan.FromMilliseconds(500);

            var endTime = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < endTime)
            {
                if (await condition())
                    return true;

                await Task.Delay(pollInterval, CancellationToken);
            }

            return false;
        }

        /// <summary>
        /// Asserts that an async operation completes successfully within timeout
        /// </summary>
        protected async Task AssertCompletesSuccessfully<T>(Func<Task<T>> operation, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            
            try
            {
                var result = await operation();
                result.Should().NotBeNull();
            }
            catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
            {
                Assert.Fail($"Operation did not complete within {timeout.TotalSeconds} seconds");
            }
        }

        #endregion

        #region Test Data Management

        private static void CreateTestFiles()
        {
            // Create C# test file
            var csharpContent = @"using System;
using System.Collections.Generic;

namespace TestNamespace
{
    public class TestClass
    {
        private readonly IList<string> _items;

        public TestClass()
        {
            _items = new List<string>();
        }

        public void AddItem(string item)
        {
            if (string.IsNullOrEmpty(item))
                throw new ArgumentException(""Item cannot be null or empty"");
            
            _items.Add(item);
        }

        public IEnumerable<string> GetItems()
        {
            return _items;
        }
    }
}";
            File.WriteAllText(Path.Combine(TestDataDirectory, "TestClass.cs"), csharpContent);

            // Create JavaScript test file
            var jsContent = @"class TestClass {
    constructor() {
        this.items = [];
    }

    addItem(item) {
        if (!item) {
            throw new Error('Item cannot be null or empty');
        }
        this.items.push(item);
    }

    getItems() {
        return [...this.items];
    }
}

module.exports = TestClass;";
            File.WriteAllText(Path.Combine(TestDataDirectory, "TestClass.js"), jsContent);

            // Create Python test file
            var pythonContent = @"class TestClass:
    def __init__(self):
        self.items = []
    
    def add_item(self, item):
        if not item:
            raise ValueError('Item cannot be None or empty')
        self.items.append(item)
    
    def get_items(self):
        return list(self.items)";
            File.WriteAllText(Path.Combine(TestDataDirectory, "test_class.py"), pythonContent);
        }

        /// <summary>
        /// Gets the content of a test file
        /// </summary>
        protected string GetTestFileContent(string fileName)
        {
            var filePath = Path.Combine(TestDataDirectory, fileName);
            return File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;
        }

        /// <summary>
        /// Gets all lines from a test file
        /// </summary>
        protected string[] GetTestFileLines(string fileName)
        {
            var content = GetTestFileContent(fileName);
            return content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Creates a temporary test file with specified content
        /// </summary>
        protected string CreateTempTestFile(string content, string extension = ".cs")
        {
            var fileName = $"temp_{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(TestDataDirectory, fileName);
            File.WriteAllText(filePath, content);
            return filePath;
        }

        /// <summary>
        /// Cleans up temporary test files
        /// </summary>
        protected void CleanupTempFiles(params string[] filePaths)
        {
            foreach (var filePath in filePaths)
            {
                try
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        #endregion

        #region Assertions

        /// <summary>
        /// Asserts that a response contains valid code
        /// </summary>
        protected void AssertValidCodeResponse(string response, string language)
        {
            response.Should().NotBeNullOrWhiteSpace();

            switch (language.ToLowerInvariant())
            {
                case "csharp":
                    AssertValidCSharpCode(response);
                    break;
                case "javascript":
                    AssertValidJavaScriptCode(response);
                    break;
                case "python":
                    AssertValidPythonCode(response);
                    break;
                default:
                    // Generic code validation
                    response.Should().NotContain("Error:");
                    response.Should().NotContain("Exception:");
                    break;
            }
        }

        private void AssertValidCSharpCode(string code)
        {
            // Basic C# syntax validation
            if (code.Contains("class ") || code.Contains("interface ") || code.Contains("struct "))
            {
                code.Should().Contain("{");
                code.Should().Contain("}");
            }
            
            if (code.Contains("(") && code.Contains(")"))
            {
                // Method call or declaration - should have balanced parentheses
                var openParens = code.Count(c => c == '(');
                var closeParens = code.Count(c => c == ')');
                openParens.Should().Be(closeParens);
            }

            // Should not contain common error patterns
            code.Should().NotContain("Compilation error");
            code.Should().NotContain("Syntax error");
        }

        private void AssertValidJavaScriptCode(string code)
        {
            // Basic JavaScript syntax validation
            if (code.Contains("function ") || code.Contains("class "))
            {
                code.Should().Contain("{");
                code.Should().Contain("}");
            }

            // Should not contain common error patterns
            code.Should().NotContain("SyntaxError");
            code.Should().NotContain("ReferenceError");
        }

        private void AssertValidPythonCode(string code)
        {
            // Basic Python syntax validation
            if (code.Contains("def ") || code.Contains("class "))
            {
                code.Should().Contain(":");
            }

            // Should not contain common error patterns
            code.Should().NotContain("SyntaxError");
            code.Should().NotContain("IndentationError");
        }

        /// <summary>
        /// Asserts that a response time is within acceptable limits
        /// </summary>
        protected void AssertAcceptableResponseTime(TimeSpan responseTime, TimeSpan maxExpected)
        {
            responseTime.Should().BeLessThan(maxExpected, 
                $"Response time {responseTime.TotalMilliseconds}ms exceeded maximum expected {maxExpected.TotalMilliseconds}ms");
        }

        /// <summary>
        /// Asserts that a confidence score is within valid range
        /// </summary>
        protected void AssertValidConfidenceScore(double confidence)
        {
            confidence.Should().BeInRange(0.0, 1.0, "Confidence score should be between 0 and 1");
        }

        #endregion
    }

    /// <summary>
    /// Mock Ollama server for testing
    /// </summary>
    public class OllamaTestServer : IDisposable
    {
        private bool _disposed;

        public string BaseUrl { get; } = "http://localhost:11434";

        public void ConfigureMockResponse(string endpoint, object response)
        {
            // In a real implementation, this would configure mock HTTP responses
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Cleanup test server resources
        }
    }

    /// <summary>
    /// Categories for integration tests
    /// </summary>
    public static class IntegrationTestCategories
    {
        public const string OllamaServer = "OllamaServer";
        public const string VisualStudio = "VisualStudio";
        public const string FileSystem = "FileSystem";
        public const string Network = "Network";
        public const string EndToEnd = "EndToEnd";
        public const string Slow = "Slow";
    }
}