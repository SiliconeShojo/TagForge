using System;
using TagForge.Services.Providers;

namespace TagForge.Services
{
    public class ProviderFactory
    {
        public IAIProvider CreateProvider(string name)
        {
            return name switch
            {
                "Google Gemini" => new GeminiProvider(),
                "Groq" => new GroqProvider(),
                "OpenRouter" => new OpenRouterProvider(),
                "Ollama" => new OllamaProvider(),
                "Cerebras" => new CerebrasProvider(),
                "Hugging Face" => new HuggingFaceProvider(),
                _ => null
            };
        }
    }
}
