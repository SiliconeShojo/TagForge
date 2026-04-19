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
                "LM Studio" => new LMStudioProvider(),
                "Ollama" => new OllamaProvider(),
                "Hugging Face" => new HuggingFaceProvider(),
                _ => null
            };
        }
    }
}
