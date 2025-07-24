using System;
using System.Collections.Generic;

namespace OllamaAssistant.Models
{
    /// <summary>
    /// Request object for Ollama API
    /// </summary>
    public class OllamaRequest
    {
        /// <summary>
        /// The model to use for the request
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// The prompt to send to the model
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// System message to provide context
        /// </summary>
        public string System { get; set; }

        /// <summary>
        /// Options for controlling the generation
        /// </summary>
        public OllamaOptions Options { get; set; }

        /// <summary>
        /// Whether to stream the response
        /// </summary>
        public bool Stream { get; set; } = false;

        /// <summary>
        /// Context from previous requests for continuity
        /// </summary>
        public string Context { get; set; }

        /// <summary>
        /// Template to use for formatting the prompt
        /// </summary>
        public string Template { get; set; }

        /// <summary>
        /// Whether to keep the model loaded in memory
        /// </summary>
        public bool KeepAlive { get; set; } = true;

        /// <summary>
        /// Images to include in the request (for multimodal models)
        /// </summary>
        public string[] Images { get; set; }

        /// <summary>
        /// Format for the response
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Whether to include raw response data
        /// </summary>
        public bool Raw { get; set; } = false;
    }

    /// <summary>
    /// Options for controlling Ollama model generation
    /// </summary>
    public class OllamaOptions
    {
        /// <summary>
        /// Controls randomness in generation (0.0 to 2.0)
        /// </summary>
        public double Temperature { get; set; } = 0.1;

        /// <summary>
        /// Limits the number of highest probability tokens to consider
        /// </summary>
        public int TopK { get; set; } = 40;

        /// <summary>
        /// Controls diversity via nucleus sampling (0.0 to 1.0)
        /// </summary>
        public double TopP { get; set; } = 0.9;

        /// <summary>
        /// Maximum number of tokens to predict
        /// </summary>
        public int NumPredict { get; set; } = 128;

        /// <summary>
        /// Stop sequences to end generation
        /// </summary>
        public string[] Stop { get; set; }

        /// <summary>
        /// Repeat penalty (1.0 = no penalty)
        /// </summary>
        public double RepeatPenalty { get; set; } = 1.1;

        /// <summary>
        /// Number of previous tokens to consider for repeat penalty
        /// </summary>
        public int RepeatLastN { get; set; } = 64;

        /// <summary>
        /// Penalize newline tokens
        /// </summary>
        public bool PenalizeNewline { get; set; } = true;

        /// <summary>
        /// Typical P sampling parameter
        /// </summary>
        public double TypicalP { get; set; } = 1.0;

        /// <summary>
        /// Tail free sampling parameter
        /// </summary>
        public double TfsZ { get; set; } = 1.0;

        /// <summary>
        /// Random seed for generation (-1 for random)
        /// </summary>
        public int Seed { get; set; } = -1;

        /// <summary>
        /// Mirostat sampling mode (0=disabled, 1=Mirostat, 2=Mirostat 2.0)
        /// </summary>
        public int Mirostat { get; set; } = 0;

        /// <summary>
        /// Mirostat target entropy
        /// </summary>
        public double MirostatTau { get; set; } = 5.0;

        /// <summary>
        /// Mirostat learning rate
        /// </summary>
        public double MirostatEta { get; set; } = 0.1;

        /// <summary>
        /// Number of threads to use for generation
        /// </summary>
        public int NumThread { get; set; } = -1;

        /// <summary>
        /// Number of layers to offload to GPU
        /// </summary>
        public int NumGpu { get; set; } = -1;

        /// <summary>
        /// Main GPU to use
        /// </summary>
        public int MainGpu { get; set; } = 0;

        /// <summary>
        /// Enable low VRAM mode
        /// </summary>
        public bool LowVram { get; set; } = false;

        /// <summary>
        /// Use memory mapping
        /// </summary>
        public bool UseMLock { get; set; } = false;

        /// <summary>
        /// Number of context tokens to keep
        /// </summary>
        public int NumCtx { get; set; } = 2048;

        /// <summary>
        /// Batch size for prompt processing
        /// </summary>
        public int NumBatch { get; set; } = 512;
    }

    /// <summary>
    /// Response from Ollama API
    /// </summary>
    public class OllamaResponse
    {
        /// <summary>
        /// The model that generated the response
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// The generated response text
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// Whether the response is complete
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Context for maintaining conversation state
        /// </summary>
        public int[] Context { get; set; }

        /// <summary>
        /// Total time for the request in nanoseconds
        /// </summary>
        public long? TotalDuration { get; set; }

        /// <summary>
        /// Time spent loading the model in nanoseconds
        /// </summary>
        public long? LoadDuration { get; set; }

        /// <summary>
        /// Number of tokens in the prompt
        /// </summary>
        public int? PromptEvalCount { get; set; }

        /// <summary>
        /// Time spent evaluating the prompt in nanoseconds
        /// </summary>
        public long? PromptEvalDuration { get; set; }

        /// <summary>
        /// Number of tokens generated
        /// </summary>
        public int? EvalCount { get; set; }

        /// <summary>
        /// Time spent generating the response in nanoseconds
        /// </summary>
        public long? EvalDuration { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Error message if request failed
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Streaming response chunk from Ollama API
    /// </summary>
    public class OllamaStreamResponse
    {
        /// <summary>
        /// The model that generated this chunk
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// The response text chunk
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// Whether this is the final chunk
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Context for maintaining conversation state
        /// </summary>
        public int[] Context { get; set; }

        /// <summary>
        /// Total time for the request in nanoseconds (final chunk only)
        /// </summary>
        public long? TotalDuration { get; set; }

        /// <summary>
        /// Time spent loading the model in nanoseconds (final chunk only)
        /// </summary>
        public long? LoadDuration { get; set; }

        /// <summary>
        /// Number of tokens in the prompt (final chunk only)
        /// </summary>
        public int? PromptEvalCount { get; set; }

        /// <summary>
        /// Time spent evaluating the prompt in nanoseconds (final chunk only)
        /// </summary>
        public long? PromptEvalDuration { get; set; }

        /// <summary>
        /// Number of tokens generated (final chunk only)
        /// </summary>
        public int? EvalCount { get; set; }

        /// <summary>
        /// Time spent generating the response in nanoseconds (final chunk only)
        /// </summary>
        public long? EvalDuration { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Error message if chunk failed
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Response containing available models
    /// </summary>
    public class OllamaModelsResponse
    {
        /// <summary>
        /// List of available models
        /// </summary>
        public OllamaModelInfo[] Models { get; set; }
    }

    /// <summary>
    /// Information about an Ollama model
    /// </summary>
    public class OllamaModelInfo
    {
        /// <summary>
        /// Model name and tag
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// When the model was last modified
        /// </summary>
        public DateTime ModifiedAt { get; set; }

        /// <summary>
        /// Size of the model in bytes
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Model digest/hash
        /// </summary>
        public string Digest { get; set; }

        /// <summary>
        /// Model details
        /// </summary>
        public OllamaModelDetails Details { get; set; }
    }

    /// <summary>
    /// Detailed information about a model
    /// </summary>
    public class OllamaModelDetails
    {
        /// <summary>
        /// Parent model
        /// </summary>
        public string Parent { get; set; }

        /// <summary>
        /// Model format
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Model family
        /// </summary>
        public string Family { get; set; }

        /// <summary>
        /// Model families
        /// </summary>
        public string[] Families { get; set; }

        /// <summary>
        /// Parameter size
        /// </summary>
        public string ParameterSize { get; set; }

        /// <summary>
        /// Quantization level
        /// </summary>
        public string QuantizationLevel { get; set; }
    }

    /// <summary>
    /// Response from Ollama version endpoint
    /// </summary>
    public class OllamaVersionResponse
    {
        /// <summary>
        /// Ollama version string
        /// </summary>
        public string Version { get; set; }
    }

    /// <summary>
    /// Request to create/pull a model
    /// </summary>
    public class OllamaCreateModelRequest
    {
        /// <summary>
        /// Name of the model to create
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Modelfile content or path to existing model
        /// </summary>
        public string Modelfile { get; set; }

        /// <summary>
        /// Whether to stream the creation process
        /// </summary>
        public bool Stream { get; set; } = false;

        /// <summary>
        /// Path to context files
        /// </summary>
        public string Path { get; set; }
    }

    /// <summary>
    /// Response from model creation/pull
    /// </summary>
    public class OllamaCreateModelResponse
    {
        /// <summary>
        /// Status of the operation
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Whether the operation is complete
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Error message if operation failed
        /// </summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Request to delete a model
    /// </summary>
    public class OllamaDeleteModelRequest
    {
        /// <summary>
        /// Name of the model to delete
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// Request for copying a model
    /// </summary>
    public class OllamaCopyModelRequest
    {
        /// <summary>
        /// Source model name
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Destination model name
        /// </summary>
        public string Destination { get; set; }
    }

    /// <summary>
    /// Request for showing model information
    /// </summary>
    public class OllamaShowModelRequest
    {
        /// <summary>
        /// Name of the model to show
        /// </summary>
        public string Name { get; set; }
    }

    /// <summary>
    /// Response with detailed model information
    /// </summary>
    public class OllamaShowModelResponse
    {
        /// <summary>
        /// Modelfile content
        /// </summary>
        public string Modelfile { get; set; }

        /// <summary>
        /// Model parameters
        /// </summary>
        public string Parameters { get; set; }

        /// <summary>
        /// Model template
        /// </summary>
        public string Template { get; set; }

        /// <summary>
        /// Model details
        /// </summary>
        public OllamaModelDetails Details { get; set; }
    }

    /// <summary>
    /// Chat message for chat completions
    /// </summary>
    public class OllamaChatMessage
    {
        /// <summary>
        /// Role of the message sender
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Content of the message
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Images included in the message
        /// </summary>
        public string[] Images { get; set; }
    }

    /// <summary>
    /// Chat completion request
    /// </summary>
    public class OllamaChatRequest
    {
        /// <summary>
        /// Model to use for chat
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// List of messages in the conversation
        /// </summary>
        public OllamaChatMessage[] Messages { get; set; }

        /// <summary>
        /// Whether to stream the response
        /// </summary>
        public bool Stream { get; set; } = false;

        /// <summary>
        /// Options for generation
        /// </summary>
        public OllamaOptions Options { get; set; }

        /// <summary>
        /// Response format
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Whether to keep the model loaded
        /// </summary>
        public bool KeepAlive { get; set; } = true;
    }

    /// <summary>
    /// Chat completion response
    /// </summary>
    public class OllamaChatResponse
    {
        /// <summary>
        /// Model used for the response
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Generated message
        /// </summary>
        public OllamaChatMessage Message { get; set; }

        /// <summary>
        /// Whether the response is complete
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Created timestamp
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Total time for the request in nanoseconds
        /// </summary>
        public long? TotalDuration { get; set; }

        /// <summary>
        /// Time spent loading the model in nanoseconds
        /// </summary>
        public long? LoadDuration { get; set; }

        /// <summary>
        /// Number of tokens in the prompt
        /// </summary>
        public int? PromptEvalCount { get; set; }

        /// <summary>
        /// Time spent evaluating the prompt in nanoseconds
        /// </summary>
        public long? PromptEvalDuration { get; set; }

        /// <summary>
        /// Number of tokens generated
        /// </summary>
        public int? EvalCount { get; set; }

        /// <summary>
        /// Time spent generating the response in nanoseconds
        /// </summary>
        public long? EvalDuration { get; set; }
    }
}