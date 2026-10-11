using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>A free cloud AI provider (OpenAI-compatible chat endpoint).</summary>
    public record CloudProvider(string Id, string Name, string BaseUrl, string Model, bool JsonMode, string KeyUrl);

    /// <summary>
    /// Talks to any OpenAI-compatible cloud AI (Groq, Gemini, OpenRouter...). Same shape as OllamaClient
    /// so the panel can swap between them and fall back to another provider when one hits its daily limit.
    /// </summary>
    public class CloudClient
    {
        // Free providers, tried in this order when "auto" fallback is used.
        public static readonly CloudProvider[] Providers =
        {
            new("groq",       "Groq",       "https://api.groq.com/openai/v1",                       "openai/gpt-oss-120b",                  true,  "console.groq.com/keys"),
            new("gemini",     "Gemini",     "https://generativelanguage.googleapis.com/v1beta/openai","gemini-2.5-flash",                    false, "aistudio.google.com/apikey"),
            new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1",                          "meta-llama/llama-3.3-70b-instruct:free", true,  "openrouter.ai/keys"),
        };

        public static CloudProvider? Provider(string id) => Array.Find(Providers, p => p.Id == id);

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

        public async Task<string> GenerateAsync(CloudProvider p, string prompt, string system, string apiKey, bool json, double temperature, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new CloudException($"No API key for {p.Name}. Paste your free key in the AI settings (get one at {p.KeyUrl}).");

            var body = new ChatRequest
            {
                Model = p.Model,
                Temperature = temperature,
                Stream = false,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = system },
                    new() { Role = "user",   Content = prompt }
                },
                ResponseFormat = (json && p.JsonMode) ? new ResponseFormat { Type = "json_object" } : null
            };

            var payload = JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            using var req = new HttpRequestMessage(HttpMethod.Post, p.BaseUrl.TrimEnd('/') + "/chat/completions")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req, ct); }
            catch (HttpRequestException) { throw new CloudException($"Couldn't reach {p.Name}. Check your internet connection."); }
            catch (TaskCanceledException) { throw new CloudException($"{p.Name} took too long and was cancelled."); }

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = TryGetErrorMessage(text);
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new CloudException($"{p.Name} API key is invalid. Check it in the AI settings ({p.KeyUrl}).");
                if (resp.StatusCode == (HttpStatusCode)429)
                    throw new CloudException($"{p.Name} free limit reached for now." + (detail != null ? $" ({detail})" : "")) { RateLimited = true };
                if (resp.StatusCode == HttpStatusCode.NotFound || (detail?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false))
                    throw new CloudException($"Model \"{p.Model}\" isn't available on {p.Name}." + (detail != null ? $"\n({detail})" : ""));
                throw new CloudException($"{p.Name} error ({(int)resp.StatusCode}): {detail ?? text}");
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponse>(text);
                return parsed?.Choices is { Count: > 0 } ? (parsed.Choices[0].Message?.Content?.Trim() ?? "") : "";
            }
            catch { return ""; }
        }

        private static string? TryGetErrorMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    if (err.ValueKind == JsonValueKind.String) return err.GetString();
                    if (err.TryGetProperty("message", out var msg)) return msg.GetString();
                }
            }
            catch { }
            return null;
        }

        // ---- DTOs ----
        private class ChatRequest
        {
            [JsonPropertyName("model")]           public string Model { get; set; } = "";
            [JsonPropertyName("messages")]        public List<ChatMessage> Messages { get; set; } = new();
            [JsonPropertyName("temperature")]     public double Temperature { get; set; }
            [JsonPropertyName("stream")]          public bool Stream { get; set; }
            [JsonPropertyName("response_format")] public ResponseFormat? ResponseFormat { get; set; }
        }
        private class ChatMessage
        {
            [JsonPropertyName("role")]    public string Role { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
        }
        private class ResponseFormat { [JsonPropertyName("type")] public string Type { get; set; } = ""; }
        private class ChatResponse { [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; } }
        private class Choice { [JsonPropertyName("message")] public ChatMessage? Message { get; set; } }
    }

    public class CloudException : AiException
    {
        public bool RateLimited { get; init; }
        public CloudException(string message) : base(message) { }
    }

    /// <summary>App settings (API keys, panel layout) in %APPDATA%\EasyEngine\settings.json.</summary>
    public static class AiSettings
    {
        private static string Dir
        {
            get { var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyEngine"); Directory.CreateDirectory(d); return d; }
        }
        private static string File => Path.Combine(Dir, "settings.json");

        public static Dictionary<string, string> CloudKeys { get; set; } = new();
        public static Dictionary<string, string> DockLayout { get; set; } = new();

        public static string Key(string providerId) => CloudKeys.TryGetValue(providerId, out var v) ? v : "";
        public static void SetKey(string providerId, string value) { CloudKeys[providerId] = value?.Trim() ?? ""; }

        // back-compat alias
        public static string GroqApiKey { get => Key("groq"); set => SetKey("groq", value); }

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;
                var data = JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText(File));
                if (data != null)
                {
                    CloudKeys = data.CloudKeys ?? new();
                    if (!string.IsNullOrWhiteSpace(data.GroqApiKey) && !CloudKeys.ContainsKey("groq")) CloudKeys["groq"] = data.GroqApiKey; // migrate old field
                    DockLayout = data.DockLayout ?? new();
                }
            }
            catch { }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new Data { CloudKeys = CloudKeys, DockLayout = DockLayout })); }
            catch { }
        }

        private class Data
        {
            [JsonPropertyName("cloudKeys")]  public Dictionary<string, string>? CloudKeys { get; set; }
            [JsonPropertyName("groqApiKey")] public string? GroqApiKey { get; set; } // legacy
            [JsonPropertyName("dockLayout")] public Dictionary<string, string>? DockLayout { get; set; }
        }
    }
}
