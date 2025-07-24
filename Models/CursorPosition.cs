using System;

namespace OllamaAssistant.Models
{
    public class CursorPosition
    {
        public int Line { get; set; }  
        public int Column { get; set; }
        public string FilePath { get; set; }
        public DateTime Timestamp { get; set; }
    }
}