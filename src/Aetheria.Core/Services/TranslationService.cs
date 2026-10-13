using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aetheria.Core.Services;

public sealed class TranslationService
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TranslationService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Translates text into the target language using free Google GTX endpoint with MyMemory fallback.
    /// Completely free, zero API key required, fast execution.
    /// </summary>
    public async Task<string> TranslateAsync(
        string text,
        string targetLanguage = "tr",
        string sourceLanguage = "auto",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var cleanText = text.Trim();
        var cacheKey = $"{sourceLanguage}:{targetLanguage}:{cleanText}";
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // Primary: Google Translate GTX endpoint (Instant, high accuracy, free)
        try
        {
            var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLanguage}&tl={targetLanguage}&dt=t&q={Uri.EscapeDataString(cleanText)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Aetheria/1.0");

            using var resp = await _httpClient.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var chunks = root[0];
                    if (chunks.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new StringBuilder();
                        foreach (var chunk in chunks.EnumerateArray())
                        {
                            if (chunk.ValueKind == JsonValueKind.Array && chunk.GetArrayLength() > 0)
                            {
                                sb.Append(chunk[0].GetString());
                            }
                        }
                        var translated = sb.ToString();
                        if (!string.IsNullOrWhiteSpace(translated))
                        {
                            _cache[cacheKey] = translated;
                            return translated;
                        }
                    }
                }
            }
        }
        catch
        {
            // Primary failed, continue to fallback
        }

        // Fallback: MyMemory Free Translation API
        try
        {
            var pair = sourceLanguage == "auto" ? $"en|{targetLanguage}" : $"{sourceLanguage}|{targetLanguage}";
            var mmUrl = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(cleanText)}&langpair={pair}";
            using var mmReq = new HttpRequestMessage(HttpMethod.Get, mmUrl);
            mmReq.Headers.Add("User-Agent", "Mozilla/5.0 Aetheria/1.0");

            using var mmResp = await _httpClient.SendAsync(mmReq, ct);
            if (mmResp.IsSuccessStatusCode)
            {
                var mmJson = await mmResp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(mmJson);
                if (doc.RootElement.TryGetProperty("responseData", out var respData) &&
                    respData.TryGetProperty("translatedText", out var ttProp))
                {
                    var mmText = ttProp.GetString();
                    if (!string.IsNullOrWhiteSpace(mmText))
                    {
                        _cache[cacheKey] = mmText;
                        return mmText;
                    }
                }
            }
        }
        catch
        {
            // Fallback failed
        }

        return text;
    }
}
