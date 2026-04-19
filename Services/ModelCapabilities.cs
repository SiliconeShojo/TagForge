using System;

namespace TagForge.Services
{
    public static class ModelCapabilities
    {
        public static bool SupportsVision(string modelName, string provider = "")
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            var name = modelName.ToLowerInvariant();

            // 1. Google Gemini (Almost all 1.5+ models support it)
            if (name.Contains("gemini") && (name.Contains("1.5") || name.Contains("vision") || name.Contains("pro"))) return true;

            // 2. Groq (Llama 3.2 Vision - 11b/90b)
            // Note: 1b and 3b are text-only.
            if (provider == "Groq" && (name.Contains("vision") || (name.Contains("llama-3.2") && (name.Contains("11b") || name.Contains("90b"))))) return true;

            // 3. OpenRouter / General Keywords
            if (name.Contains("vision") || 
                name.Contains("gpt-4o") || 
                name.Contains("claude-3") || 
                name.Contains("llava") || 
                name.Contains("bakllava") || 
                name.Contains("yi-vl") ||
                name.Contains("vl") || // Generic "Visual Language" check
                name.Contains("qwen-vl")) 
            {
                return true;
            }

            return false;
        }
    }
}
