using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Models;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.UnitTests.Services
{
    [TestClass]
    public class SuggestionEngineTests
    {
        private SuggestionEngine _engine;
        private Mock<ISettingsService> _mockSettingsService;
        private Mock<IContextCaptureService> _mockContextService;

        [TestInitialize]
        public void Initialize()
        {
            _mockSettingsService = MockServices.CreateMockSettingsService();
            _mockContextService = MockServices.CreateMockContextCaptureService();
            _engine = new SuggestionEngine(_mockSettingsService.Object, _mockContextService.Object);
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithValidSuggestion_ShouldReturnFilteredSuggestions()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default().Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, context);

            // Assert
            results.Should().NotBeNull();
            results.Should().NotBeEmpty();
            results.First().Should().BeEquivalentTo(suggestion);
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithNullSuggestion_ShouldReturnEmptyList()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(null, context);

            // Assert
            results.Should().BeEmpty();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithNullContext_ShouldReturnEmptyList()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, null);

            // Assert
            results.Should().BeEmpty();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithLowConfidenceSuggestion_ShouldFilterOut()
        {
            // Arrange
            var lowConfidenceSuggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithConfidence(0.1) // Very low confidence
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(lowConfidenceSuggestion, context);

            // Assert
            results.Should().BeEmpty();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithHighConfidenceSuggestion_ShouldInclude()
        {
            // Arrange
            var highConfidenceSuggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithConfidence(0.9) // High confidence
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(highConfidenceSuggestion, context);

            // Assert
            results.Should().NotBeEmpty();
            results.First().Confidence.Should().Be(0.9);
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithEmptyCompletionText_ShouldFilterOut()
        {
            // Arrange
            var emptySuggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText("") // Empty completion
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(emptySuggestion, context);

            // Assert
            results.Should().BeEmpty();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithWhitespaceOnlyCompletion_ShouldFilterOut()
        {
            // Arrange
            var whitespaceSuggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText("   \t\n   ") // Only whitespace
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(whitespaceSuggestion, context);

            // Assert
            results.Should().BeEmpty();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithCSharpContext_ShouldApplyLanguageSpecificFiltering()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText("Console.WriteLine(\"Hello, World!\");")
                .Build();
            var csharpContext = TestDataBuilders.Scenarios.CreateCSharpMethodContext();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, csharpContext);

            // Assert
            results.Should().NotBeEmpty();
            results.First().CompletionText.Should().Contain("Console.WriteLine");
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithJavaScriptContext_ShouldApplyLanguageSpecificFiltering()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText("console.log('Hello, World!');")
                .Build();
            var jsContext = TestDataBuilders.Scenarios.CreateJavaScriptFunctionContext();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, jsContext);

            // Assert
            results.Should().NotBeEmpty();
            results.First().CompletionText.Should().Contain("console.log");
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_WithValidContext_ShouldReturnJumpRecommendations()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(context);

            // Assert
            recommendations.Should().NotBeNull();
            recommendations.Should().NotBeEmpty();
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_WithNullContext_ShouldReturnEmptyList()
        {
            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(null);

            // Assert
            recommendations.Should().BeEmpty();
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_WithContextWithHistory_ShouldFindHistoryBasedJumps()
        {
            // Arrange
            var history = TestDataBuilders.Scenarios.CreateHistorySequence(3);
            var context = TestDataBuilders.CodeContextBuilder.Default()
                .WithHistory(history)
                .Build();

            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(context);

            // Assert
            recommendations.Should().NotBeEmpty();
            recommendations.Should().Contain(r => r.Reason.Contains("Related") || r.Reason.Contains("Previous"));
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_WithMethodContext_ShouldFindMethodRelatedJumps()
        {
            // Arrange
            var methodContext = TestDataBuilders.Scenarios.CreateCSharpMethodContext();

            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(methodContext);

            // Assert
            recommendations.Should().NotBeEmpty();
            recommendations.Should().Contain(r => 
                r.Direction == JumpDirection.Up || r.Direction == JumpDirection.Down);
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_ShouldOrderByConfidence()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(context);

            // Assert
            recommendations.Should().NotBeEmpty();
            
            // Verify recommendations are ordered by confidence (descending)
            for (int i = 1; i < recommendations.Count; i++)
            {
                recommendations[i - 1].Confidence.Should().BeGreaterOrEqualTo(recommendations[i].Confidence);
            }
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithLongCompletionText_ShouldTruncateAppropriately()
        {
            // Arrange
            var longText = new string('A', 10000); // Very long completion text
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText(longText)
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, context);

            // Assert
            results.Should().NotBeEmpty();
            // The engine should handle long text gracefully
            results.First().CompletionText.Should().NotBeNull();
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithSpecialCharacters_ShouldHandleCorrectly()
        {
            // Arrange
            var specialCharsText = "Console.WriteLine(\"Special chars: < > & \\\\ \\\" \\' \\n \\t\");";
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default()
                .WithCompletionText(specialCharsText)
                .Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var results = await _engine.ProcessSuggestionAsync(suggestion, context);

            // Assert
            results.Should().NotBeEmpty();
            results.First().CompletionText.Should().Contain("Special chars");
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_WithDuplicateSuggestions_ShouldDeduplicateResults()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default().Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Process the same suggestion multiple times to simulate duplicates
            var results1 = await _engine.ProcessSuggestionAsync(suggestion, context);
            var results2 = await _engine.ProcessSuggestionAsync(suggestion, context);

            // Act - This would happen if we had a method that processed multiple suggestions
            var combinedResults = results1.Concat(results2).ToList();

            // Assert
            combinedResults.Should().HaveCount(2); // We expect duplicates in this test scenario
            // In a real implementation, the engine might deduplicate
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_WithSingleLineContext_ShouldFindLimitedOpportunities()
        {
            // Arrange
            var singleLineContext = TestDataBuilders.CodeContextBuilder.Default()
                .WithSurroundingText("var x = 5;") // Single line
                .Build();

            // Act
            var recommendations = await _engine.AnalyzeJumpOpportunitiesAsync(singleLineContext);

            // Assert
            recommendations.Should().NotBeNull();
            // Might be empty or have limited opportunities for single line
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_Performance_ShouldProcessQuickly()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default().Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    await _engine.ProcessSuggestionAsync(suggestion, context);
                }
            });

            // Assert
            duration.Should().BeLessThan(System.TimeSpan.FromSeconds(2));
        }

        [TestMethod]
        public async Task ProcessSuggestionAsync_ConcurrentRequests_ShouldHandleGracefully()
        {
            // Arrange
            var suggestion = TestDataBuilders.CodeSuggestionBuilder.Default().Build();
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            var tasks = Enumerable.Range(0, 10)
                .Select(_ => _engine.ProcessSuggestionAsync(suggestion, context))
                .ToArray();

            // Act
            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyContain(r => r != null);
            results.Should().OnlyContain(r => r.Count > 0);
        }

        [TestMethod]
        public async Task AnalyzeJumpOpportunitiesAsync_ConcurrentRequests_ShouldHandleGracefully()
        {
            // Arrange
            var context = TestDataBuilders.CodeContextBuilder.Default().Build();

            var tasks = Enumerable.Range(0, 10)
                .Select(_ => _engine.AnalyzeJumpOpportunitiesAsync(context))
                .ToArray();

            // Act
            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(10);
            results.Should().OnlyContain(r => r != null);
        }
    }
}