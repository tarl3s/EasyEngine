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
    /// <summary>A cloud AI provider. Free=true are free-tier (used by Auto). Anthropic=true uses the native Claude API; the rest are OpenAI-compatible.</summary>
    public record CloudProvider(string Id, string Name, string BaseUrl, string Model, bool JsonMode, string KeyUrl, bool Free = true, bool Anthropic = false);

    /// <summary>
    /// Talks to cloud AIs: OpenAI-compatible endpoints (Groq, Gemini, OpenRouter, OpenAI, DeepSeek, Mistral, custom)
    /// and the native Anthropic/Claude API. Same shape as OllamaClient so the panel can swap and fall back.
    /// </summary>
    public class CloudClient
    {
        public static readonly CloudProvider[] Providers =
        {
            // free tiers (Auto rotates these)
            new("groq",       "Groq",       "https://api.groq.com/openai/v1",                         "openai/gpt-oss-120b",                    true,  "console.groq.com/keys"),
            new("gemini",     "Gemini",     "https://generativelanguage.googleapis.com/v1beta/openai","gemini-2.5-flash",                       false, "aistudio.google.com/apikey"),
            new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1",                           "meta-llama/llama-3.3-70b-instruct:free", true,  "openrouter.ai/keys"),
            new("mistral",    "Mistral",    "https://api.mistral.ai/v1",                              "mistral-small-latest",                   true,  "console.mistral.ai/api-keys"),
            // paid — your own account, billed per use (never used by Auto)
            new("openai",     "OpenAI (ChatGPT)",   "https://api.openai.com/v1", "gpt-4o-mini",      true,  "platform.openai.com/api-keys",            Free: false),
            new("anthropic",  "Claude (Anthropic)", "https://api.anthropic.com/v1", "claude-haiku-5-5", false, "console.anthropic.com/settings/keys",  Free: false, Anthropic: true),
            new("deepseek",   "DeepSeek",           "https://api.deepseek.com",  "deepseek-chat",     true,  "platform.deepseek.com/api_keys",          Free: false),
            new("custom",     "Custom (OpenAI-compatible)", "", "",              true,  "any OpenAI-compatible URL",               Free: false),
        };

        public static CloudProvider? Provider(string id) => Array.Find(Providers, p => p.Id == id);

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

        public async Task<string> GenerateAsync(CloudProvider p, string prompt, string system, string apiKey, bool json, double temperature, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new CloudException($"No API key for {p.Name}. Paste your key in the AI settings (get one at {p.KeyUrl}).");

            // the custom provider takes its endpoint + model from settings
            string baseUrl = p.BaseUrl, model = p.Model;
            if (p.Id == "custom")
            {
                baseUrl = AiSettings.CustomUrl; model = AiSettings.CustomModel;
                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                    throw new CloudException("Set the Custom endpoint URL and model name in the AI settings.");
            }

            if (p.Anthropic) return await AnthropicGenerate(baseUrl, model, prompt, system, apiKey, p, ct);

            var body = new ChatRequest
            {
                Model = model,
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
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions")
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
                    throw new CloudException($"{p.Name} limit reached for now." + (detail != null ? $" ({detail})" : "")) { RateLimited = true };
                if (resp.StatusCode == HttpStatusCode.NotFound || (detail?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false))
                    throw new CloudException($"Model \"{model}\" isn't available on {p.Name}." + (detail != null ? $"\n({detail})" : ""));
                throw new CloudException($"{p.Name} error ({(int)resp.StatusCode}): {detail ?? text}");
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponse>(text);
                return parsed?.Choices is { Count: > 0 } ? (parsed.Choices[0].Message?.Content?.Trim() ?? "") : "";
            }
            catch { return ""; }
        }

        // Native Anthropic Messages API (Claude): different endpoint, headers and body than the OpenAI shape.
        private async Task<string> AnthropicGenerate(string baseUrl, string model, string prompt, string system, string apiKey, CloudProvider p, CancellationToken ct)
        {
            var body = new
            {
                model,
                max_tokens = 4096,
                system,
                messages = new[] { new { role = "user", content = prompt } }
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/messages")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");

            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req, ct); }
            catch (HttpRequestException) { throw new CloudException($"Couldn't reach {p.Name}. Check your internet connection."); }
            catch (TaskCanceledException) { throw new CloudException($"{p.Name} took too long and was cancelled."); }

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = TryGetErrorMessage(text);
                if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new CloudException($"{p.Name} API key is invalid ({p.KeyUrl}).");
                if (resp.StatusCode == (HttpStatusCode)429) throw new CloudException($"{p.Name} rate limit reached." + (detail != null ? $" ({detail})" : "")) { RateLimited = true };
                if (resp.StatusCode == HttpStatusCode.NotFound || (detail?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false))
                    throw new CloudException($"Model \"{model}\" isn't available on {p.Name}." + (detail != null ? $"\n({detail})" : ""));
                throw new CloudException($"{p.Name} error ({(int)resp.StatusCode}): {detail ?? text}");
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    foreach (var block in content.EnumerateArray())
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var tx))
                            return (tx.GetString() ?? "").Trim();
            }
            catch { }
            return "";
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
        public static string CustomUrl { get; set; } = "";   // Custom provider: OpenAI-compatible base URL
        public static string CustomModel { get; set; } = ""; // Custom provider: model name
        public static string? AiChoice { get; set; }         // last "Which AI to use" pick (the combo item's Tag); null = never chosen
        public static string LocalModel { get; set; } = "";  // name typed for "Custom local model…"
        public static List<string> SamplesInstalled { get; set; } = new(); // sample projects already put on this computer (SampleProjects)
        public static string SamplesVersion { get; set; } = "";             // the engine version they were built with

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
                    CustomUrl = data.CustomUrl ?? "";
                    CustomModel = data.CustomModel ?? "";
                    AiChoice = data.AiChoice;
                    LocalModel = data.LocalModel ?? "";
                    SamplesInstalled = data.SamplesInstalled ?? new();
                    SamplesVersion = data.SamplesVersion ?? "";
                }
            }
            catch { }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new Data { CloudKeys = CloudKeys, DockLayout = DockLayout, CustomUrl = CustomUrl, CustomModel = CustomModel, AiChoice = AiChoice, LocalModel = LocalModel, SamplesInstalled = SamplesInstalled, SamplesVersion = SamplesVersion })); }
            catch { }
        }

        private class Data
        {
            [JsonPropertyName("cloudKeys")]  public Dictionary<string, string>? CloudKeys { get; set; }
            [JsonPropertyName("groqApiKey")] public string? GroqApiKey { get; set; } // legacy
            [JsonPropertyName("dockLayout")] public Dictionary<string, string>? DockLayout { get; set; }
            [JsonPropertyName("customUrl")]  public string? CustomUrl { get; set; }
            [JsonPropertyName("customModel")] public string? CustomModel { get; set; }
            [JsonPropertyName("aiChoice")]   public string? AiChoice { get; set; }
            [JsonPropertyName("localModel")] public string? LocalModel { get; set; }
            [JsonPropertyName("samplesInstalled")] public List<string>? SamplesInstalled { get; set; }
            [JsonPropertyName("samplesVersion")] public string? SamplesVersion { get; set; }
        }
    }
}
