using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OllamaAssistant.Services.Implementation;
using OllamaAssistant.Services.Interfaces;
using OllamaAssistant.Tests.TestHelpers;

namespace OllamaAssistant.Tests.UnitTests.Services
{
    [TestClass]
    public class ContextCaptureServiceTests
    {
        private ContextCaptureService _service;
        private Mock<ICursorHistoryService> _mockHistoryService;
        private Mock<ITextViewService> _mockTextViewService;

        [TestInitialize]
        public void Initialize()
        {
            _mockHistoryService = MockServices.CreateMockCursorHistoryService();
            _mockTextViewService = MockServices.CreateMockTextViewService();
            _service = new ContextCaptureService(_mockHistoryService.Object, _mockTextViewService.Object);
        }

        [TestMethod]
        public async Task CaptureContextAsync_ShouldReturnValidCodeContext()
        {
            // Arrange
            var filePath = "C:\\TestFile.cs";
            var line = 10;
            var column = 5;

            var historyEntries = TestDataBuilders.Scenarios.CreateHistorySequence(3);
            _mockHistoryService.Setup(x => x.GetRelevantHistoryAsync(filePath, line, column))
                .ReturnsAsync(historyEntries);

            // Act
            var context = await _service.CaptureContextAsync(filePath, line, column);

            // Assert
            context.Should().NotBeNull();
            context.FilePath.Should().Be(filePath);
            context.CaretLine.Should().Be(line);
            context.CaretColumn.Should().Be(column);
            context.CursorHistory.Should().HaveCount(3);
            context.SurroundingText.Should().NotBeNullOrEmpty();
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithCSharpFile_ShouldDetectLanguageCorrectly()
        {
            // Arrange
            var csharpFilePath = "C:\\TestFile.cs";

            // Act
            var context = await _service.CaptureContextAsync(csharpFilePath, 10, 5);

            // Assert
            context.LanguageId.Should().Be("csharp");
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithJavaScriptFile_ShouldDetectLanguageCorrectly()
        {
            // Arrange
            var jsFilePath = "C:\\TestFile.js";

            // Act
            var context = await _service.CaptureContextAsync(jsFilePath, 10, 5);

            // Assert
            context.LanguageId.Should().Be("javascript");
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithPythonFile_ShouldDetectLanguageCorrectly()
        {
            // Arrange
            var pythonFilePath = "C:\\TestFile.py";

            // Act
            var context = await _service.CaptureContextAsync(pythonFilePath, 10, 5);

            // Assert
            context.LanguageId.Should().Be("python");
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithUnknownFile_ShouldDefaultToPlainText()
        {
            // Arrange
            var unknownFilePath = "C:\\TestFile.unknown";

            // Act
            var context = await _service.CaptureContextAsync(unknownFilePath, 10, 5);

            // Assert
            context.LanguageId.Should().Be("plaintext");
        }

        [TestMethod]
        [DataRow("C:\\TestFile.cs", "csharp")]
        [DataRow("C:\\TestFile.js", "javascript")]
        [DataRow("C:\\TestFile.ts", "typescript")]
        [DataRow("C:\\TestFile.py", "python")]
        [DataRow("C:\\TestFile.java", "java")]
        [DataRow("C:\\TestFile.cpp", "cpp")]
        [DataRow("C:\\TestFile.c", "c")]
        [DataRow("C:\\TestFile.h", "c")]
        [DataRow("C:\\TestFile.html", "html")]
        [DataRow("C:\\TestFile.xml", "xml")]
        [DataRow("C:\\TestFile.json", "json")]
        [DataRow("C:\\TestFile.sql", "sql")]
        [DataRow("C:\\TestFile.txt", "plaintext")]
        public async Task CaptureContextAsync_WithVariousFileExtensions_ShouldDetectLanguageCorrectly(string filePath, string expectedLanguage)
        {
            // Act
            var context = await _service.CaptureContextAsync(filePath, 10, 5);

            // Assert
            context.LanguageId.Should().Be(expectedLanguage);
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithNullFilePath_ShouldReturnContextWithDefaults()
        {
            // Act
            var context = await _service.CaptureContextAsync(null, 10, 5);

            // Assert
            context.Should().NotBeNull();
            context.FilePath.Should().BeNullOrEmpty();
            context.LanguageId.Should().Be("plaintext");
            context.CaretLine.Should().Be(10);
            context.CaretColumn.Should().Be(5);
        }

        [TestMethod]
        public async Task CaptureContextAsync_ShouldIncludeProjectContext()
        {
            // Arrange
            var filePath = "C:\\MyProject\\Source\\TestFile.cs";

            // Act
            var context = await _service.CaptureContextAsync(filePath, 10, 5);

            // Assert
            context.ProjectContext.Should().NotBeNullOrEmpty();
            context.ProjectContext.Should().Contain("MyProject");
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_ShouldReturnSurroundingText()
        {
            // Arrange
            var line = 10;
            var column = 5;
            var expectedSnippet = "// Test snippet";

            _mockTextViewService.Setup(x => x.GetSurroundingTextAsync(line, column, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(expectedSnippet);

            // Act
            var snippet = await _service.GetContextSnippetAsync(line, column);

            // Assert
            snippet.Should().Be(expectedSnippet);
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_WithTextViewServiceError_ShouldReturnEmptyString()
        {
            // Arrange
            _mockTextViewService.Setup(x => x.GetSurroundingTextAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new System.Exception("Text view error"));

            // Act
            var snippet = await _service.GetContextSnippetAsync(10, 5);

            // Assert
            snippet.Should().BeEmpty();
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithHistoryServiceError_ShouldReturnContextWithEmptyHistory()
        {
            // Arrange
            _mockHistoryService.Setup(x => x.GetRelevantHistoryAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
                .ThrowsAsync(new System.Exception("History service error"));

            // Act
            var context = await _service.CaptureContextAsync("C:\\TestFile.cs", 10, 5);

            // Assert
            context.Should().NotBeNull();
            context.CursorHistory.Should().BeEmpty();
        }

        [TestMethod]
        public async Task CaptureContextAsync_ShouldCallHistoryServiceWithCorrectParameters()
        {
            // Arrange
            var filePath = "C:\\TestFile.cs";
            var line = 15;
            var column = 8;

            // Act
            await _service.CaptureContextAsync(filePath, line, column);

            // Assert
            _mockHistoryService.Verify(x => x.GetRelevantHistoryAsync(filePath, line, column), Times.Once);
        }

        [TestMethod]
        public async Task CaptureContextAsync_ShouldCallTextViewServiceForSurroundingText()
        {
            // Act
            await _service.CaptureContextAsync("C:\\TestFile.cs", 10, 5);

            // Assert
            _mockTextViewService.Verify(x => x.GetSurroundingTextAsync(10, 5, It.IsAny<int>(), It.IsAny<int>()), Times.Once);
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithLongFilePath_ShouldHandleGracefully()
        {
            // Arrange
            var longFilePath = "C:\\" + new string('A', 1000) + "\\TestFile.cs";

            // Act
            var context = await _service.CaptureContextAsync(longFilePath, 10, 5);

            // Assert
            context.Should().NotBeNull();
            context.FilePath.Should().Be(longFilePath);
        }

        [TestMethod]
        public async Task CaptureContextAsync_WithNegativeLineColumn_ShouldHandleGracefully()
        {
            // Act
            var context = await _service.CaptureContextAsync("C:\\TestFile.cs", -1, -1);

            // Assert
            context.Should().NotBeNull();
            context.CaretLine.Should().Be(-1);
            context.CaretColumn.Should().Be(-1);
        }

        [TestMethod]
        public async Task GetContextSnippetAsync_WithLargeLineNumbers_ShouldHandleGracefully()
        {
            // Arrange
            _mockTextViewService.Setup(x => x.GetSurroundingTextAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync("Large line context");

            // Act
            var snippet = await _service.GetContextSnippetAsync(10000, 5000);

            // Assert
            snippet.Should().Be("Large line context");
        }

        [TestMethod]
        public async Task CaptureContextAsync_MultipleCallsConcurrently_ShouldHandleGracefully()
        {
            // Arrange
            var tasks = Enumerable.Range(0, 10)
                .Select(i => _service.CaptureContextAsync($"C:\\TestFile{i}.cs", i, i))
                .ToArray();

            // Act
            var contexts = await Task.WhenAll(tasks);

            // Assert
            contexts.Should().HaveCount(10);
            contexts.Should().OnlyContain(c => c != null);
            
            for (int i = 0; i < 10; i++)
            {
                contexts[i].FilePath.Should().Be($"C:\\TestFile{i}.cs");
                contexts[i].CaretLine.Should().Be(i);
                contexts[i].CaretColumn.Should().Be(i);
            }
        }

        [TestMethod]
        public async Task CaptureContextAsync_Performance_ShouldBeReasonablyFast()
        {
            // Act
            var duration = await TestUtilities.MeasureExecutionTimeAsync(async () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    await _service.CaptureContextAsync("C:\\TestFile.cs", i, i);
                }
            });

            // Assert
            duration.Should().BeLessThan(System.TimeSpan.FromSeconds(2));
        }
    }
}