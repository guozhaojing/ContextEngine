// =============================================================================
// CodeFix/LlmProvider.cs — OpenAI-compatible LLM calling proxy
// =============================================================================
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Core.Cognition.CodeFix;

public sealed class LlmProvider
{
    private readonly HttpClient _http;
    private readonly LlmConfig _config;

    public LlmProvider(LlmConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs) };
        if (!string.IsNullOrEmpty(config.ApiKey))
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var request = new
        {
            model = _config.Model,
            messages = new[]
            {
                new { role = "system", content = "You are a precise C# code fixer. Respond ONLY with the corrected method body including signature and braces. No explanations." },
                new { role = "user", content = prompt },
            },
            temperature = _config.Temperature,
            max_tokens = _config.MaxTokens,
        };

        var response = await _http.PostAsJsonAsync($"{_config.BaseUrl.TrimEnd('/')}/v1/chat/completions", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
        return result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "";
    }
}

public class LlmConfig
{
    public string BaseUrl { get; init; } = "https://api.openai.com";
    public string Model { get; init; } = "gpt-4o";
    public string ApiKey { get; init; } = "";
    public double Temperature { get; init; } = 0.1;
    public int MaxTokens { get; init; } = 4096;
    public int TimeoutMs { get; init; } = 120_000;

    public static LlmConfig Default => new();
}

public class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoice>? Choices { get; set; }
}

public class ChatChoice
{
    [JsonPropertyName("message")]
    public ChatMessage? Message { get; set; }
}

public class ChatMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
