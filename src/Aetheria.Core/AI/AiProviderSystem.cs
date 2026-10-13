using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Aetheria.Core.Domain;

namespace Aetheria.Core.AI;

public sealed record ProviderConnectionStatus(bool IsConnected, string Message, IReadOnlyList<string> AvailableModels);

public interface IAiProviderAdapter
{
    string ProviderType { get; }
    Task<ProviderConnectionStatus> CheckConnectionAsync(ModelPreset preset, string apiKey, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamCompletionAsync(ModelPreset preset, string apiKey, IReadOnlyList<ChatMessageTurn> messages, CancellationToken ct = default);
}

public sealed class OllamaAdapter : IAiProviderAdapter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    public string ProviderType => "Ollama";

    public async Task<ProviderConnectionStatus> CheckConnectionAsync(ModelPreset preset, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = NormalizeOllamaRoot(preset.EndpointUrl);
        try
        {
            using var resp = await Http.GetAsync($"{baseUrl}/api/tags", ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new ProviderConnectionStatus(false, $"Ollama returned HTTP {(int)resp.StatusCode}.", Array.Empty<string>());
            }
            var body = await resp.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(body);
            var models = new List<string>();
            if (node?["models"] is JsonArray arr)
            {
                foreach (var item in arr)
                {
                    var name = item?["name"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name)) models.Add(name);
                }
            }
            return new ProviderConnectionStatus(true, $"Connected to Ollama ({models.Count} local models found).", models);
        }
        catch (Exception ex)
        {
            return new ProviderConnectionStatus(false, $"Could not reach Ollama at {baseUrl}: {ex.Message}", Array.Empty<string>());
        }
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        ModelPreset preset,
        string apiKey,
        IReadOnlyList<ChatMessageTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var baseUrl = NormalizeOllamaRoot(preset.EndpointUrl);
        var url = $"{baseUrl}/api/chat";

        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(preset.ModelId) ? "llama3.1:8b" : preset.ModelId,
            ["stream"] = preset.StreamResponses,
            ["messages"] = new JsonArray(messages.Select(m => new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray()),
            ["options"] = new JsonObject
            {
                ["temperature"] = preset.Temperature,
                ["top_p"] = preset.TopP,
                ["top_k"] = preset.TopK,
                ["repeat_penalty"] = preset.RepetitionPenalty,
                ["num_predict"] = preset.MaxOutputTokens,
                ["num_ctx"] = preset.ContextLength
            }
        };

        if (preset.StopSequences.Count > 0)
        {
            payload["options"]!["stop"] = new JsonArray(preset.StopSequences.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? resp = null;
        string? initialError = null;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                initialError = $"[Ollama Error HTTP {(int)resp.StatusCode}: {errBody}]";
            }
        }
        catch (Exception ex)
        {
            initialError = $"[Ollama Connection Error: {ex.Message}. Ensure 'ollama serve' or 'ollama run {preset.ModelId}' is running.]";
        }

        if (initialError != null)
        {
            yield return initialError;
            resp?.Dispose();
            yield break;
        }

        using (resp!)
        {
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                string? chunkText = null;
                try
                {
                    var json = JsonNode.Parse(line);
                    chunkText = json?["message"]?["content"]?.GetValue<string>();
                }
                catch
                {
                    // Ignore malformed line
                }
                if (!string.IsNullOrEmpty(chunkText))
                {
                    yield return chunkText;
                }
            }
        }
    }

    private static string NormalizeOllamaRoot(string url)
    {
        var trimmed = (url ?? "http://127.0.0.1:11434").Trim().TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3];
        return trimmed;
    }
}

public sealed class OpenAiCompatibleAdapter : IAiProviderAdapter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    public string ProviderType { get; }

    public OpenAiCompatibleAdapter(string providerType = "OpenAI")
    {
        ProviderType = providerType;
    }

    public async Task<ProviderConnectionStatus> CheckConnectionAsync(ModelPreset preset, string apiKey, CancellationToken ct = default)
    {
        var baseUrl = (preset.EndpointUrl ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
        try
        {
            // Special check for KoboldCpp native status endpoint first
            if (string.Equals(preset.ProviderType, "KoboldCpp", StringComparison.OrdinalIgnoreCase))
            {
                var rootUrl = baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? baseUrl[..^3] : baseUrl;
                using var kResp = await Http.GetAsync($"{rootUrl}/api/v1/model", ct);
                if (kResp.IsSuccessStatusCode)
                {
                    var kBody = await kResp.Content.ReadAsStringAsync(ct);
                    var kNode = JsonNode.Parse(kBody);
                    var modelName = kNode?["result"]?.GetValue<string>() ?? "koboldcpp-local";
                    return new ProviderConnectionStatus(true, $"Connected to KoboldCpp ({modelName}).", new[] { modelName });
                }
            }

            var modelsUrl = baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/models";

            using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new ProviderConnectionStatus(false, "Authentication failed (HTTP 401): Check your API key.", Array.Empty<string>());
            }
            if (!resp.IsSuccessStatusCode)
            {
                return new ProviderConnectionStatus(false, $"Server responded with HTTP {(int)resp.StatusCode}.", Array.Empty<string>());
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(body);
            var models = new List<string>();
            if (node?["data"] is JsonArray dataArr)
            {
                foreach (var item in dataArr)
                {
                    var id = item?["id"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(id)) models.Add(id);
                }
            }
            return new ProviderConnectionStatus(true, $"Connected successfully ({models.Count} models available).", models);
        }
        catch (Exception ex)
        {
            return new ProviderConnectionStatus(false, $"Connection failed: {ex.Message}", Array.Empty<string>());
        }
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        ModelPreset preset,
        string apiKey,
        IReadOnlyList<ChatMessageTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var baseUrl = (preset.EndpointUrl ?? "https://api.openai.com/v1").Trim().TrimEnd('/');
        if (string.Equals(preset.ProviderType, "KoboldCpp", StringComparison.OrdinalIgnoreCase) ||
            baseUrl.Contains(":5001", StringComparison.OrdinalIgnoreCase))
        {
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                !baseUrl.Contains("/v1/", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = $"{baseUrl}/v1";
            }
        }
        var url = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";

        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(preset.ModelId) ? "gpt-4o-mini" : preset.ModelId,
            ["stream"] = preset.StreamResponses,
            ["temperature"] = preset.Temperature,
            ["top_p"] = preset.TopP,
            ["max_tokens"] = preset.MaxOutputTokens,
            ["messages"] = new JsonArray(messages.Select(m => new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray())
        };

        // Include frequency & presence penalties for providers that support them
        if (!string.Equals(preset.ProviderType, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            payload["frequency_penalty"] = preset.FrequencyPenalty;
            payload["presence_penalty"] = preset.PresencePenalty;
        }

        if (preset.StopSequences.Count > 0)
        {
            payload["stop"] = new JsonArray(preset.StopSequences.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        }

        // Local providers / OpenRouter advanced sampling parameters
        if (preset.ProviderType is "KoboldCpp" or "LMStudio" or "OpenRouter" or "Custom")
        {
            if (preset.RepetitionPenalty > 1.0)
            {
                payload["repetition_penalty"] = preset.RepetitionPenalty;
                if (string.Equals(preset.ProviderType, "KoboldCpp", StringComparison.OrdinalIgnoreCase))
                {
                    payload["rep_pen"] = preset.RepetitionPenalty;
                    payload["rep_pen_range"] = 1024;
                }
            }
            if (preset.MinP > 0.0)
                payload["min_p"] = preset.MinP;
            if (preset.TopK > 0)
                payload["top_k"] = preset.TopK;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (baseUrl.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://aetheria-roleplay.app");
            req.Headers.TryAddWithoutValidation("X-Title", "Aetheria Windows 11 Roleplay Platform");
        }

        HttpResponseMessage? resp = null;
        string? errText = null;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                errText = $"[{preset.ProviderType} HTTP {(int)resp.StatusCode}: {errBody}]";
            }
        }
        catch (Exception ex)
        {
            errText = $"[{preset.ProviderType} Connection Failed: {ex.Message}]";
        }

        if (errText != null)
        {
            yield return errText;
            resp?.Dispose();
            yield break;
        }

        using (resp!)
        {
            if (!preset.StreamResponses)
            {
                var fullBody = await resp.Content.ReadAsStringAsync(ct);
                var json = JsonNode.Parse(fullBody);
                var text = json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(text)) yield return text;
                yield break;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;

                var dataStr = trimmed[5..].Trim();
                if (dataStr == "[DONE]") yield break;

                string? deltaText = null;
                try
                {
                    var node = JsonNode.Parse(dataStr);
                    deltaText = node?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>()
                             ?? node?["choices"]?[0]?["text"]?.GetValue<string>()
                             ?? node?["token"]?.GetValue<string>()
                             ?? node?["delta"]?.GetValue<string>();
                }
                catch
                {
                    // Ignore partial JSON frame
                }

                if (!string.IsNullOrEmpty(deltaText))
                {
                    yield return deltaText;
                }
            }
        }
    }
}

public sealed class AnthropicAdapter : IAiProviderAdapter
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    public string ProviderType => "Anthropic";

    public async Task<ProviderConnectionStatus> CheckConnectionAsync(ModelPreset preset, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ProviderConnectionStatus(false, "Anthropic API key is required.", Array.Empty<string>());
        }

        var baseUrl = (preset.EndpointUrl ?? "https://api.anthropic.com/v1").Trim().TrimEnd('/');
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new ProviderConnectionStatus(false, "Invalid Anthropic API key (HTTP 401).", Array.Empty<string>());
            }

            var defaultModels = new[]
            {
                "claude-3-7-sonnet-latest",
                "claude-3-5-sonnet-latest",
                "claude-3-5-haiku-latest",
                "claude-3-opus-latest"
            };
            return new ProviderConnectionStatus(true, "Anthropic API key verified.", defaultModels);
        }
        catch (Exception ex)
        {
            return new ProviderConnectionStatus(false, $"Anthropic connection error: {ex.Message}", Array.Empty<string>());
        }
    }

    public async IAsyncEnumerable<string> StreamCompletionAsync(
        ModelPreset preset,
        string apiKey,
        IReadOnlyList<ChatMessageTurn> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var baseUrl = (preset.EndpointUrl ?? "https://api.anthropic.com/v1").Trim().TrimEnd('/');
        var url = baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/messages";

        var systemBuilder = new StringBuilder();
        var chatTurns = new JsonArray();

        foreach (var m in messages)
        {
            if (m.Role == "system")
            {
                systemBuilder.AppendLine(m.Content);
                systemBuilder.AppendLine();
            }
            else
            {
                chatTurns.Add(new JsonObject
                {
                    ["role"] = m.Role == "assistant" ? "assistant" : "user",
                    ["content"] = m.Content
                });
            }
        }

        if (chatTurns.Count == 0)
        {
            chatTurns.Add(new JsonObject { ["role"] = "user", ["content"] = "Begin the roleplay." });
        }

        var payload = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(preset.ModelId) ? "claude-3-5-sonnet-latest" : preset.ModelId,
            ["max_tokens"] = Math.Max(64, preset.MaxOutputTokens),
            ["temperature"] = preset.Temperature,
            ["stream"] = true,
            ["messages"] = chatTurns
        };

        var sysText = systemBuilder.ToString().Trim();
        if (!string.IsNullOrEmpty(sysText))
        {
            payload["system"] = sysText;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        HttpResponseMessage? resp = null;
        string? errText = null;
        try
        {
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                errText = $"[Anthropic HTTP {(int)resp.StatusCode}: {body}]";
            }
        }
        catch (Exception ex)
        {
            errText = $"[Anthropic Connection Error: {ex.Message}]";
        }

        if (errText != null)
        {
            yield return errText;
            resp?.Dispose();
            yield break;
        }

        using (resp!)
        {
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (string.IsNullOrWhiteSpace(line)) continue;
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("data:", StringComparison.Ordinal)) continue;

                var dataStr = trimmed[5..].Trim();
                string? chunk = null;
                try
                {
                    var node = JsonNode.Parse(dataStr);
                    if (node?["type"]?.GetValue<string>() == "content_block_delta")
                    {
                        chunk = node?["delta"]?["text"]?.GetValue<string>();
                    }
                }
                catch
                {
                }
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }
}

public sealed class AiOrchestrator
{
    private readonly SecretVault _secretVault;
    private readonly Dictionary<string, IAiProviderAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);

    public AiOrchestrator(SecretVault secretVault)
    {
        _secretVault = secretVault;
        RegisterAdapter(new OllamaAdapter());
        RegisterAdapter(new AnthropicAdapter());
        RegisterAdapter(new OpenAiCompatibleAdapter("OpenAI"));
        RegisterAdapter(new OpenAiCompatibleAdapter("OpenRouter"));
        RegisterAdapter(new OpenAiCompatibleAdapter("Gemini"));
        RegisterAdapter(new OpenAiCompatibleAdapter("DeepSeek"));
        RegisterAdapter(new OpenAiCompatibleAdapter("Groq"));
        RegisterAdapter(new OpenAiCompatibleAdapter("KoboldCpp"));
        RegisterAdapter(new OpenAiCompatibleAdapter("LMStudio"));
        RegisterAdapter(new OpenAiCompatibleAdapter("Custom"));
    }

    public void RegisterAdapter(IAiProviderAdapter adapter)
    {
        _adapters[adapter.ProviderType] = adapter;
    }

    public IAiProviderAdapter GetAdapter(string providerType)
    {
        if (_adapters.TryGetValue(providerType ?? "OpenAI", out var adapter))
            return adapter;
        return _adapters["OpenAI"];
    }

    public string ResolveApiKey(ModelPreset preset)
    {
        var byPreset = _secretVault.GetApiKey($"preset:{preset.Id}");
        if (!string.IsNullOrWhiteSpace(byPreset)) return byPreset;
        return _secretVault.GetApiKey(preset.ProviderType);
    }

    public Task<ProviderConnectionStatus> TestPresetConnectionAsync(ModelPreset preset, CancellationToken ct = default)
    {
        var adapter = GetAdapter(preset.ProviderType);
        var apiKey = ResolveApiKey(preset);
        return adapter.CheckConnectionAsync(preset, apiKey, ct);
    }

    public IAsyncEnumerable<string> StreamSessionResponseAsync(
        RoleplaySession session,
        Character? activeCharacter,
        Persona activePersona,
        World? world,
        ModelPreset preset,
        bool isNarrator = false,
        bool isRoundResolution = false,
        CancellationToken ct = default)
    {
        var adapter = GetAdapter(preset.ProviderType);
        var apiKey = ResolveApiKey(preset);
        var promptMessages = PromptComposer.BuildPrompt(session, activeCharacter, activePersona, world, preset, isNarrator, isRoundResolution);
        return adapter.StreamCompletionAsync(preset, apiKey, promptMessages, ct);
    }
}
