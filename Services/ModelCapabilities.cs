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

        public static int GetSuggestedMaxTokens(string provider)
        {
            if (string.IsNullOrEmpty(provider)) return 4096;

            return provider.ToLowerInvariant() switch
            {
                "gemini" => 8192,
                "openrouter" => 4096,
                "huggingface" => 2048,
                "ollama" or "lmstudio" => 4096,
                _ => 4096
            };
        }
    }
}
