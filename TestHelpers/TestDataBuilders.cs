using System;
using System.Collections.Generic;
using OllamaAssistant.Models;

namespace OllamaAssistant.Tests.TestHelpers
{
    /// <summary>
    /// Builder classes for creating test data objects
    /// </summary>
    public static class TestDataBuilders
    {
        /// <summary>
        /// Builder for CursorHistoryEntry objects
        /// </summary>
        public class CursorHistoryEntryBuilder
        {
            private CursorHistoryEntry _entry = new CursorHistoryEntry();

            public CursorHistoryEntryBuilder WithFilePath(string filePath)
            {
                _entry.FilePath = filePath;
                return this;
            }

            public CursorHistoryEntryBuilder WithPosition(int line, int column)
            {
                _entry.Line = line;
                _entry.Column = column;
                return this;
            }

            public CursorHistoryEntryBuilder WithTimestamp(DateTime timestamp)
            {
                _entry.Timestamp = timestamp;
                return this;
            }

            public CursorHistoryEntryBuilder WithContext(string context)
            {
                _entry.Context = context;
                return this;
            }

            public CursorHistoryEntryBuilder WithJumpReason(string reason)
            {
                _entry.JumpReason = reason;
                return this;
            }

            public CursorHistoryEntry Build() => _entry;

            public static CursorHistoryEntryBuilder Default() => new CursorHistoryEntryBuilder()
                .WithFilePath("C:\\TestFile.cs")
                .WithPosition(10, 5)
                .WithTimestamp(DateTime.Now)
                .WithContext("// Test context");
        }

        /// <summary>
        /// Builder for CodeContext objects
        /// </summary>
        public class CodeContextBuilder
        {
            private CodeContext _context = new CodeContext();

            public CodeContextBuilder WithFilePath(string filePath)
            {
                _context.FilePath = filePath;
                return this;
            }

            public CodeContextBuilder WithCaretPosition(int line, int column)
            {
                _context.CaretLine = line;
                _context.CaretColumn = column;
                return this;
            }

            public CodeContextBuilder WithLanguage(string languageId)
            {
                _context.LanguageId = languageId;
                return this;
            }

            public CodeContextBuilder WithSurroundingText(string text)
            {
                _context.SurroundingText = text;
                return this;
            }

            public CodeContextBuilder WithProjectContext(string projectContext)
            {
                _context.ProjectContext = projectContext;
                return this;
            }

            public CodeContextBuilder WithHistory(List<CursorHistoryEntry> history)
            {
                _context.CursorHistory = history;
                return this;
            }

            public CodeContext Build() => _context;

            public static CodeContextBuilder Default() => new CodeContextBuilder()
                .WithFilePath("C:\\TestFile.cs")
                .WithCaretPosition(10, 5)
                .WithLanguage("csharp")
                .WithSurroundingText("public class TestClass\n{\n    public void TestMethod()\n    {\n        // cursor here\n    }\n}")
                .WithProjectContext("TestProject.csproj")
                .WithHistory(new List<CursorHistoryEntry>());
        }

        /// <summary>
        /// Builder for CodeSuggestion objects
        /// </summary>
        public class CodeSuggestionBuilder
        {
            private CodeSuggestion _suggestion = new CodeSuggestion();

            public CodeSuggestionBuilder WithCompletionText(string text)
            {
                _suggestion.CompletionText = text;
                return this;
            }

            public CodeSuggestionBuilder WithDisplayText(string text)
            {
                _suggestion.DisplayText = text;
                return this;
            }

            public CodeSuggestionBuilder WithDescription(string description)
            {
                _suggestion.Description = description;
                return this;
            }

            public CodeSuggestionBuilder WithConfidence(double confidence)
            {
                _suggestion.Confidence = confidence;
                return this;
            }

            public CodeSuggestionBuilder WithPosition(int start, int end)
            {
                _suggestion.StartPosition = start;
                _suggestion.EndPosition = end;
                return this;
            }

            public CodeSuggestionBuilder WithProcessingTime(int milliseconds)
            {
                _suggestion.ProcessingTime = milliseconds;
                return this;
            }

            public CodeSuggestion Build() => _suggestion;

            public static CodeSuggestionBuilder Default() => new CodeSuggestionBuilder()
                .WithCompletionText("Console.WriteLine(\"Hello, World!\");")
                .WithDisplayText("Console.WriteLine")
                .WithDescription("Write Hello World to console")
                .WithConfidence(0.85)
                .WithPosition(5, 5)
                .WithProcessingTime(500);
        }

        /// <summary>
        /// Builder for JumpRecommendation objects
        /// </summary>
        public class JumpRecommendationBuilder
        {
            private JumpRecommendation _recommendation = new JumpRecommendation();

            public JumpRecommendationBuilder WithDirection(JumpDirection direction)
            {
                _recommendation.Direction = direction;
                return this;
            }

            public JumpRecommendationBuilder WithTargetPosition(int line, int column)
            {
                _recommendation.TargetLine = line;
                _recommendation.TargetColumn = column;
                return this;
            }

            public JumpRecommendationBuilder WithTargetFile(string filePath)
            {
                _recommendation.TargetFilePath = filePath;
                _recommendation.IsCrossFile = !string.IsNullOrEmpty(filePath);
                return this;
            }

            public JumpRecommendationBuilder WithConfidence(double confidence)
            {
                _recommendation.Confidence = confidence;
                return this;
            }

            public JumpRecommendationBuilder WithReason(string reason)
            {
                _recommendation.Reason = reason;
                return this;
            }

            public JumpRecommendationBuilder WithPreview(string preview)
            {
                _recommendation.TargetPreview = preview;
                return this;
            }

            public JumpRecommendation Build() => _recommendation;

            public static JumpRecommendationBuilder Default() => new JumpRecommendationBuilder()
                .WithDirection(JumpDirection.Down)
                .WithTargetPosition(15, 8)
                .WithConfidence(0.75)
                .WithReason("Related method implementation")
                .WithPreview("public void RelatedMethod()");
        }

        /// <summary>
        /// Factory methods for common test scenarios
        /// </summary>
        public static class Scenarios
        {
            public static List<CursorHistoryEntry> CreateHistorySequence(int count)
            {
                var history = new List<CursorHistoryEntry>();
                var baseTime = DateTime.Now.AddMinutes(-count);

                for (int i = 0; i < count; i++)
                {
                    history.Add(CursorHistoryEntryBuilder.Default()
                        .WithPosition(10 + i, 5)
                        .WithTimestamp(baseTime.AddMinutes(i))
                        .WithContext($"// Context {i}")
                        .Build());
                }

                return history;
            }

            public static CodeContext CreateCSharpMethodContext()
            {
                return CodeContextBuilder.Default()
                    .WithLanguage("csharp")
                    .WithSurroundingText(@"
public class Calculator
{
    public int Add(int a, int b)
    {
        // cursor position
        return a + b;
    }
}")
                    .Build();
            }

            public static CodeContext CreateJavaScriptFunctionContext()
            {
                return CodeContextBuilder.Default()
                    .WithFilePath("C:\\TestFile.js")
                    .WithLanguage("javascript")
                    .WithSurroundingText(@"
function calculateSum(a, b) {
    // cursor position
    return a + b;
}")
                    .Build();
            }

            public static List<CodeSuggestion> CreateMultipleSuggestions()
            {
                return new List<CodeSuggestion>
                {
                    CodeSuggestionBuilder.Default()
                        .WithCompletionText("Console.WriteLine(\"Hello, World!\");")
                        .WithConfidence(0.85)
                        .Build(),
                    CodeSuggestionBuilder.Default()
                        .WithCompletionText("System.Console.WriteLine(\"Hello, World!\");")
                        .WithConfidence(0.75)
                        .Build(),
                    CodeSuggestionBuilder.Default()
                        .WithCompletionText("Debug.WriteLine(\"Hello, World!\");")
                        .WithConfidence(0.65)
                        .Build()
                };
            }

            public static List<JumpRecommendation> CreateJumpRecommendations()
            {
                return new List<JumpRecommendation>
                {
                    JumpRecommendationBuilder.Default()
                        .WithDirection(JumpDirection.Down)
                        .WithTargetPosition(20, 4)
                        .WithReason("Method implementation")
                        .WithConfidence(0.85)
                        .Build(),
                    JumpRecommendationBuilder.Default()
                        .WithDirection(JumpDirection.Up)
                        .WithTargetPosition(5, 0)
                        .WithReason("Class declaration")
                        .WithConfidence(0.75)
                        .Build()
                };
            }
        }
    }
}