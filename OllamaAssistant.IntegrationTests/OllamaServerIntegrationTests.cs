using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OllamaAssistant.IntegrationTests.TestUtilities;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Tests.TestUtilities;

namespace OllamaAssistant.IntegrationTests
{
    [TestClass]
    [TestCategory(TestCategories.Integration)]
    [TestCategory(IntegrationTestCategories.OllamaServer)]
    [TestCategory(IntegrationTestCategories.Network)]
    public class OllamaServerIntegrationTests : BaseIntegrationTest
    {
        private OllamaHttpClient _ollamaClient;

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();
            _ollamaClient = CreateTestOllamaClient();
        }

        [TestCleanup]
        public override void TestCleanup()
        {
            _ollamaClient?.Dispose();
            base.TestCleanup();
        }

        #region Health Check Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama", Description = "Tests connection to Ollama server")]
        public async Task HealthCheck_WithRunningServer_ShouldReturnHealthy()
        {
            // Arrange
            await RequireOllamaServerAsync();

            // Act
            var healthStatus = await _ollamaClient.CheckHealthAsync(CancellationToken);

            // Assert
            healthStatus.Should().NotBeNull();
            healthStatus.IsAvailable.Should().BeTrue();
            healthStatus.ResponseTimeMs.Should().BeGreaterThan(0);
            healthStatus.Error.Should().BeNullOrEmpty();
            
            WriteTestOutput($"Health check completed in {healthStatus.ResponseTimeMs}ms");
        }

        [TestMethod]
        public async Task HealthCheck_WithUnavailableServer_ShouldReturnUnhealthy()
        {
            // Arrange
            using var client = CreateTestOllamaClient("http://localhost:99999"); // Invalid port

            // Act
            var healthStatus = await client.CheckHealthAsync(CancellationToken);

            // Assert
            healthStatus.Should().NotBeNull();
            healthStatus.IsAvailable.Should().BeFalse();
            healthStatus.Error.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task IsAvailable_WithRunningServer_ShouldReturnTrue()
        {
            // Arrange
            await RequireOllamaServerAsync();

            // Act
            var isAvailable = await _ollamaClient.IsAvailableAsync(CancellationToken);

            // Assert
            isAvailable.Should().BeTrue();
        }

        #endregion

        #region Model Management Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task GetModels_WithRunningServer_ShouldReturnModelList()
        {
            // Arrange
            await RequireOllamaServerAsync();

            // Act
            var modelsResponse = await _ollamaClient.GetModelsAsync(CancellationToken);

            // Assert
            modelsResponse.Should().NotBeNull();
            modelsResponse.Models.Should().NotBeNull();
            
            if (modelsResponse.Models.Any())
            {
                WriteTestOutput($"Found {modelsResponse.Models.Length} models");
                foreach (var model in modelsResponse.Models.Take(3))
                {
                    WriteTestOutput($"Model: {model.Name}, Size: {model.Size} bytes");
                }
                
                // Validate model structure
                var firstModel = modelsResponse.Models.First();
                firstModel.Name.Should().NotBeNullOrEmpty();
                firstModel.Size.Should().BeGreaterThan(0);
            }
            else
            {
                WriteTestOutput("No models found on server");
            }
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task GetVersion_WithRunningServer_ShouldReturnVersionInfo()
        {
            // Arrange
            await RequireOllamaServerAsync();

            // Act
            var versionResponse = await _ollamaClient.GetVersionAsync(CancellationToken);

            // Assert
            versionResponse.Should().NotBeNull();
            versionResponse.Version.Should().NotBeNullOrEmpty();
            
            WriteTestOutput($"Ollama version: {versionResponse.Version}");
        }

        #endregion

        #region Completion Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        [TestCategory(IntegrationTestCategories.Slow)] // AI calls are typically slow
        public async Task SendCompletion_WithValidRequest_ShouldReturnResponse()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var testModel = models.Models.First().Name;
            var request = new OllamaRequest
            {
                Model = testModel,
                Prompt = "Complete this C# method: public void Test() {",
                Options = new OllamaOptions
                {
                    Temperature = 0.1,
                    NumPredict = 50
                },
                Stream = false
            };

            // Act
            var (response, duration) = await MeasureExecutionTime(async () =>
                await _ollamaClient.SendCompletionAsync(request, CancellationToken));

            // Assert
            response.Should().NotBeNull();
            response.Response.Should().NotBeNullOrEmpty();
            response.Done.Should().BeTrue();
            
            WriteTestOutput($"Completion response: {response.Response}");
            WriteTestOutput($"Response time: {duration.TotalMilliseconds}ms");
            WriteTestOutput($"Model: {response.Model}");
            
            if (response.EvalCount.HasValue)
            {
                WriteTestOutput($"Tokens generated: {response.EvalCount}");
            }
            
            AssertValidCodeResponse(response.Response, "csharp");
            AssertAcceptableResponseTime(duration, TimeSpan.FromSeconds(30));
        }

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task SendCompletion_WithInvalidModel_ShouldReturnError()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var request = new OllamaRequest
            {
                Model = "non-existent-model",
                Prompt = "Test prompt",
                Stream = false
            };

            // Act & Assert
            await AssertThrowsAsync<OllamaConnectionException>(async () =>
                await _ollamaClient.SendCompletionAsync(request, CancellationToken));
        }

        #endregion

        #region Streaming Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        [TestCategory(IntegrationTestCategories.Slow)]
        public async Task SendStreamingCompletion_WithValidRequest_ShouldStreamResponse()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var testModel = models.Models.First().Name;
            var request = new OllamaRequest
            {
                Model = testModel,
                Prompt = "Write a simple hello world function in C#",
                Options = new OllamaOptions
                {
                    Temperature = 0.1,
                    NumPredict = 100
                },
                Stream = true
            };

            // Act
            var responses = new System.Collections.Generic.List<OllamaStreamResponse>();
            var streamStartTime = DateTime.UtcNow;
            
            await foreach (var streamResponse in _ollamaClient.SendStreamingCompletionAsync(request, CancellationToken))
            {
                responses.Add(streamResponse);
                
                if (!string.IsNullOrEmpty(streamResponse.Response))
                {
                    WriteTestOutput($"Stream chunk: {streamResponse.Response}");
                }
                
                if (streamResponse.Done)
                {
                    WriteTestOutput("Stream completed");
                    break;
                }
                
                // Safety check to prevent infinite loops
                if (responses.Count > 100)
                {
                    WriteTestOutput("Safety break: Too many stream responses");
                    break;
                }
            }

            var totalDuration = DateTime.UtcNow - streamStartTime;

            // Assert
            responses.Should().NotBeEmpty();
            responses.Should().Contain(r => r.Done);
            
            var fullResponse = string.Join("", responses.Select(r => r.Response));
            fullResponse.Should().NotBeNullOrEmpty();
            
            WriteTestOutput($"Full streaming response: {fullResponse}");
            WriteTestOutput($"Total streaming time: {totalDuration.TotalMilliseconds}ms");
            WriteTestOutput($"Number of chunks: {responses.Count}");
            
            AssertValidCodeResponse(fullResponse, "csharp");
        }

        #endregion

        #region Error Handling Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task SendCompletion_WithTimeout_ShouldHandleGracefully()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var shortTimeoutConfig = new OllamaHttpClientConfig
            {
                TimeoutMs = 100, // Very short timeout
                MaxRetryAttempts = 0 // No retries
            };
            
            using var client = new OllamaHttpClient("http://localhost:11434", shortTimeoutConfig);
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var request = new OllamaRequest
            {
                Model = models.Models.First().Name,
                Prompt = "This is a test prompt that should timeout due to very short timeout setting",
                Stream = false
            };

            // Act & Assert
            await AssertThrowsAsync<OllamaRetryableException>(async () =>
                await client.SendCompletionAsync(request, CancellationToken));
        }

        [TestMethod]
        public async Task SendCompletion_WithCancelledToken_ShouldRespectCancellation()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var request = new OllamaRequest
            {
                Model = models.Models.First().Name,
                Prompt = "Test prompt",
                Stream = false
            };

            // Act & Assert
            await AssertThrowsAsync<OperationCanceledException>(async () =>
                await _ollamaClient.SendCompletionAsync(request, cts.Token));
        }

        #endregion

        #region Performance Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        [PerformanceTest(MaxExecutionTimeMs = 60000)] // 60 seconds max
        [TestCategory(IntegrationTestCategories.Slow)]
        public async Task SendCompletion_PerformanceTest_ShouldMeetPerformanceRequirements()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var testModel = models.Models.First().Name;
            var request = new OllamaRequest
            {
                Model = testModel,
                Prompt = "public class Test { // Complete this class",
                Options = new OllamaOptions
                {
                    Temperature = 0.1,
                    NumPredict = 50
                },
                Stream = false
            };

            // Act - Run multiple requests to test performance consistency
            var durations = new System.Collections.Generic.List<TimeSpan>();
            const int numberOfRequests = 3;
            
            for (int i = 0; i < numberOfRequests; i++)
            {
                WriteTestOutput($"Performance test iteration {i + 1}/{numberOfRequests}");
                
                var (response, duration) = await MeasureExecutionTime(async () =>
                    await _ollamaClient.SendCompletionAsync(request, CancellationToken));
                
                durations.Add(duration);
                
                response.Should().NotBeNull();
                response.Response.Should().NotBeNullOrEmpty();
                
                WriteTestOutput($"Iteration {i + 1} completed in {duration.TotalMilliseconds}ms");
            }

            // Assert
            var averageDuration = TimeSpan.FromMilliseconds(durations.Average(d => d.TotalMilliseconds));
            var maxDuration = durations.Max();
            var minDuration = durations.Min();
            
            WriteTestOutput($"Performance results:");
            WriteTestOutput($"Average: {averageDuration.TotalMilliseconds}ms");
            WriteTestOutput($"Min: {minDuration.TotalMilliseconds}ms");
            WriteTestOutput($"Max: {maxDuration.TotalMilliseconds}ms");
            
            // Performance assertions
            averageDuration.Should().BeLessThan(TimeSpan.FromSeconds(30), "Average response time should be under 30 seconds");
            maxDuration.Should().BeLessThan(TimeSpan.FromSeconds(60), "Maximum response time should be under 60 seconds");
        }

        #endregion

        #region Connection Resilience Tests

        [TestMethod]
        [IntegrationTest(RequiredService = "Ollama")]
        public async Task MultipleSimultaneousRequests_ShouldHandleConcurrency()
        {
            // Arrange
            await RequireOllamaServerAsync();
            
            var models = await _ollamaClient.GetModelsAsync(CancellationToken);
            if (!models.Models.Any())
            {
                Assert.Inconclusive("No models available for testing");
            }

            var testModel = models.Models.First().Name;
            const int numberOfConcurrentRequests = 3;
            var tasks = new System.Threading.Tasks.Task<OllamaResponse>[numberOfConcurrentRequests];
            
            // Act
            for (int i = 0; i < numberOfConcurrentRequests; i++)
            {
                var request = new OllamaRequest
                {
                    Model = testModel,
                    Prompt = $"Test prompt {i + 1}: public void Method{i + 1}() {{",
                    Options = new OllamaOptions
                    {
                        Temperature = 0.1,
                        NumPredict = 30
                    },
                    Stream = false
                };
                
                tasks[i] = _ollamaClient.SendCompletionAsync(request, CancellationToken);
            }

            var responses = await Task.WhenAll(tasks);

            // Assert
            responses.Should().HaveCount(numberOfConcurrentRequests);
            responses.Should().OnlyContain(r => r != null);
            responses.Should().OnlyContain(r => !string.IsNullOrEmpty(r.Response));
            responses.Should().OnlyContain(r => r.Done);
            
            WriteTestOutput($"Successfully completed {numberOfConcurrentRequests} concurrent requests");
            for (int i = 0; i < responses.Length; i++)
            {
                WriteTestOutput($"Response {i + 1}: {responses[i].Response}");
            }
        }

        #endregion
    }
}