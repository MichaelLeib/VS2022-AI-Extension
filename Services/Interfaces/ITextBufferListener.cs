using Microsoft.VisualStudio.Text;

namespace OllamaAssistant.Services.Interfaces
{
    /// <summary>
    /// Interface for text buffer listeners (not part of standard VS SDK)
    /// </summary>
    public interface ITextBufferListener
    {
        void TextBufferCreated(ITextBuffer textBuffer);
        void TextBufferDisposed(ITextBuffer textBuffer);
    }
}