using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            new("gemini",     "Gemini",     "https://generativelanguage.googleapis.com/v1beta/openai","gemini-3.8-flash",                       true,  "aistudio.google.com/apikey"),
            new("openrouter", "OpenRouter", "https://openrouter.ai/api/v1",                           "nvidia/nemotron-3-super-120b-a12b:free", true,  "openrouter.ai/keys"),
            new("mistral",    "Mistral",    "https://api.mistral.ai/v1",                              "mistral-small-latest",                   true,  "console.mistral.ai/api-keys"),
            // paid — your own account, billed per use (never used by Auto)
            new("openai",     "OpenAI (ChatGPT)",   "https://api.openai.com/v1", "gpt-4o-mini",      true,  "platform.openai.com/api-keys",            Free: false),
            new("anthropic",  "Claude (Anthropic)", "https://api.anthropic.com/v1", "claude-haiku-5-5", false, "console.anthropic.com/settings/keys",  Free: false, Anthropic: true),
            new("deepseek",   "DeepSeek",           "https://api.deepseek.com",  "deepseek-chat",     true,  "platform.deepseek.com/api_keys",          Free: false),
            new("custom",     "Custom (OpenAI-compatible)", "", "",              true,  "any OpenAI-compatible URL",               Free: false),
        };

        public static CloudProvider? Provider(string id) => Array.Find(Providers, p => p.Id == id);

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

        // Free model ids get retired (a 404 "no longer available"). For a built-in provider on its default model, the
        // client then asks the provider which models it has, picks the best fit (PickModel) and remembers it.
        private static readonly Dictionary<string, string> Discovered = new();

        // Free tiers count their daily quota PER MODEL (Groq: tokens a day; Gemini: requests a day). When a built-in provider's
        // model is used up for the day, the next model of the same provider answers instead, and the tired one rests.
        private static readonly Dictionary<string, string[]> DailyFallbacks = new()
        {
            ["groq"] = new[] { "qwen/qwen3.8-27b", "openai/gpt-oss-20b" },
            ["gemini"] = new[] { "gemini-3.7-flash", "gemini-3.5-flash", "gemini-3.1-flash-lite" },
        };
        private static readonly Dictionary<string, DateTime> Resting = new(); // "provider/model" → usable again after
        private static bool IsDaily(CloudException ex) => !System.Text.RegularExpressions.Regex.IsMatch(ex.Message, @"per minute|PerMinute|\bTPM\b|\bRPM\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(ex.Message,
            @"per day|per-day|\bTPD\b|\bRPD\b|daily|free_tier_requests|free-models-per-day|exceeded your current quota", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public async Task<string> GenerateAsync(CloudProvider p, string prompt, string system, string apiKey, bool json, double temperature, CancellationToken ct = default)
        {
            bool builtIn = Provider(p.Id) is { } def && def.Model == p.Model && p.Id != "custom";
            if (!builtIn) return await GenerateWith(p, prompt, system, apiKey, json, temperature, ct);

            var first = Discovered.TryGetValue(p.Id, out var known) ? known : p.Model;
            var chain = new[] { first }.Concat(DailyFallbacks.TryGetValue(p.Id, out var more) ? more : Array.Empty<string>()).Distinct().ToList();
            var awake = chain.Where(m => !(Resting.TryGetValue(p.Id + "/" + m, out var until) && until > DateTime.UtcNow)).ToList();
            if (awake.Count == 0) awake = chain; // all resting: try anyway (a day may have passed for some)
            CloudException? last = null;
            foreach (var model in awake)
            {
                try { return await GenerateWith(p with { Model = model }, prompt, system, apiKey, json, temperature, ct); }
                catch (CloudException ex) when (ex.ModelMissing && model == first && !Discovered.ContainsKey(p.Id))
                {
                    var alt = await DiscoverModel(p, apiKey, ct);
                    if (alt == null || alt == model) { last = ex; continue; }
                    Discovered[p.Id] = alt;
                    try { return await GenerateWith(p with { Model = alt }, prompt, system, apiKey, json, temperature, ct); }
                    catch (CloudException ex2) when (ex2.RateLimited && IsDaily(ex2)) { Resting[p.Id + "/" + alt] = DateTime.UtcNow.AddSeconds(Math.Max(ex2.RetryAfter, 3600)); last = ex2; }
                }
                catch (CloudException ex) when (ex.RateLimited && IsDaily(ex)) { Resting[p.Id + "/" + model] = DateTime.UtcNow.AddSeconds(Math.Max(ex.RetryAfter, 3600)); last = ex; }
                catch (CloudException ex) when (ex.ModelMissing) { last = ex; if (model == first) Discovered.Remove(p.Id); } // gone (a fallback, or a model found earlier): the next one
            }
            throw last ?? new CloudException(L.F("{0} limit reached for now.", p.Name)) { RateLimited = true };
        }

        private async Task<string?> DiscoverModel(CloudProvider p, string apiKey, CancellationToken ct)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, p.BaseUrl.TrimEnd('/') + "/models");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var resp = await Http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var ids = new List<string>();
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var m in data.EnumerateArray())
                        if (m.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } s) ids.Add(s.StartsWith("models/") ? s[7..] : s);
                return PickModel(p.Id, ids);
            }
            catch { return null; }
        }

        /// <summary>The best chat model for the engine among a provider's current ids (null = none fits).</summary>
        public static string? PickModel(string providerId, IReadOnlyList<string> ids)
        {
            static bool Chat(string id) => !Regex.IsMatch(id, "tts|whisper|guard|safety|image|embed|audio|transcribe|orpheus|robotics|computer-use|lyria|omni|vision", RegexOptions.IgnoreCase);
            var chat = ids.Where(Chat).ToList();
            if (providerId == "gemini")
            {
                // the newest numbered "gemini-N-flash", else the moving alias
                var best = chat.Select(id => (id, m: Regex.Match(id, @"^gemini-(\d+(?:\.\d+)?)-flash$")))
                               .Where(x => x.m.Success).OrderByDescending(x => double.Parse(x.m.Groups[1].Value, CultureInfo.InvariantCulture)).Select(x => x.id).FirstOrDefault();
                return best ?? chat.FirstOrDefault(id => id == "gemini-flash-latest");
            }
            string[] prefer = providerId switch
            {
                "openrouter" => new[] { "nemotron-3-super", "gpt-oss-120b", "llama-3.3-70b", "gemma-4-31b", "qwen", "deepseek", "gemma" },
                "groq" => new[] { "gpt-oss-120b", "llama-3.3-70b", "qwen", "gpt-oss-20b", "llama" },
                "mistral" => new[] { "mistral-small-latest", "mistral-medium-latest", "mistral-large-latest" },
                _ => Array.Empty<string>()
            };
            if (providerId == "openrouter") chat = chat.Where(id => id.EndsWith(":free")).ToList(); // Auto never spends money
            foreach (var want in prefer) { var hit = chat.FirstOrDefault(id => id.Contains(want, StringComparison.OrdinalIgnoreCase)); if (hit != null) return hit; }
            return prefer.Length > 0 ? chat.FirstOrDefault() : null;
        }

        private async Task<string> GenerateWith(CloudProvider p, string prompt, string system, string apiKey, bool json, double temperature, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new CloudException(L.F("No API key for {0}. Paste your key in the AI settings (get one at {1}).", p.Name, p.KeyUrl));

            // the custom provider takes its endpoint + model from settings
            string baseUrl = p.BaseUrl, model = p.Model;
            if (p.Id == "custom")
            {
                baseUrl = AiSettings.CustomUrl; model = AiSettings.CustomModel;
                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(model))
                    throw new CloudException(L.T("Set the Custom endpoint URL and model name in the AI settings."));
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
            catch (HttpRequestException) { throw new CloudException(L.F("Couldn't reach {0}. Check your internet connection.", p.Name)); }
            catch (TaskCanceledException) { throw new CloudException(L.F("{0} took too long and was cancelled.", p.Name)); }

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = TryGetErrorMessage(text);
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new CloudException(L.F("{0} API key is invalid. Check it in the AI settings ({1}).", p.Name, p.KeyUrl));
                if (resp.StatusCode is (HttpStatusCode)429 or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway) // busy / out of quota for now
                    throw new CloudException(L.F("{0} limit reached for now.", p.Name) + (detail != null ? $" ({detail})" : "")) { RateLimited = true, RetryAfter = RetryAfterSeconds(resp, detail) };
                if (resp.StatusCode == HttpStatusCode.NotFound || ModelGone(detail))
                    throw new CloudException(L.F("Model \"{0}\" isn't available on {1}.", model, p.Name) + (detail != null ? $"\n({detail})" : "")) { ModelMissing = true };
                throw new CloudException(L.F("{0} error ({1}): {2}", p.Name, (int)resp.StatusCode, detail ?? text));
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
            catch (HttpRequestException) { throw new CloudException(L.F("Couldn't reach {0}. Check your internet connection.", p.Name)); }
            catch (TaskCanceledException) { throw new CloudException(L.F("{0} took too long and was cancelled.", p.Name)); }

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = TryGetErrorMessage(text);
                if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new CloudException(L.F("{0} API key is invalid ({1}).", p.Name, p.KeyUrl));
                if (resp.StatusCode is (HttpStatusCode)429 or HttpStatusCode.ServiceUnavailable or (HttpStatusCode)529) throw new CloudException(L.F("{0} rate limit reached.", p.Name) + (detail != null ? $" ({detail})" : "")) { RateLimited = true, RetryAfter = RetryAfterSeconds(resp, detail) };
                if (resp.StatusCode == HttpStatusCode.NotFound || ModelGone(detail))
                    throw new CloudException(L.F("Model \"{0}\" isn't available on {1}.", model, p.Name) + (detail != null ? $"\n({detail})" : "")) { ModelMissing = true };
                throw new CloudException(L.F("{0} error ({1}): {2}", p.Name, (int)resp.StatusCode, detail ?? text));
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

        // how long a busy provider asks to wait: the Retry-After header, or "try again in 7.1s" / "in 1m3s" / "in 250ms" in its message
        private static double RetryAfterSeconds(HttpResponseMessage resp, string? detail)
            => resp.Headers.RetryAfter?.Delta is TimeSpan d ? d.TotalSeconds : RetryAfterSeconds(detail);

        internal static double RetryAfterSeconds(string? detail)
        {
            var m = Regex.Match(detail ?? "", @"(?:try again|retry) in (?:(\d+)m)?(\d+(?:\.\d+)?)(ms|s)", RegexOptions.IgnoreCase);
            if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
            return (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) * 60 : 0) + (m.Groups[3].Value.Equals("ms", StringComparison.OrdinalIgnoreCase) ? v / 1000 : v);
        }

        // the model id itself is the problem (retired, renamed, unknown) — not just any message that says "model"
        // ("Request too large for model …" is a size/quota limit)
        private static bool ModelGone(string? detail) => detail != null && Regex.IsMatch(detail,
            @"not found|does not exist|no longer available|decommissioned|deprecated|not supported|invalid model|unknown model|model_not_found|isn't available|is not available",
            RegexOptions.IgnoreCase) && !Regex.IsMatch(detail, @"too large|rate limit|quota|tokens per", RegexOptions.IgnoreCase);

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
        public bool RateLimited { get; init; }   // busy or out of quota for now: another provider may answer
        public double RetryAfter { get; init; }   // seconds the provider asked to wait (0 = unknown)
        public bool ModelMissing { get; init; }   // the model id is gone (retired / renamed)
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
        public static string Language { get; set; } = "en";                // editor language (L.All codes)
        public static string ThemeName { get; set; } = "dark";             // "dark" / "light"
        public static double UiScale { get; set; } = 1.0;                   // interface size (1 = 100%)
        public static bool CheckUpdates { get; set; } = true;               // the launcher looks for a new version on start
        public static bool HideTestBar { get; set; }                         // the bar on top of a test run (Stop, Console…) starts hidden (F2)

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
                    Language = data.Language ?? "en";
                    ThemeName = data.Theme ?? "dark";
                    UiScale = data.UiScale is double s && s >= 0.8 && s <= 1.5 ? s : 1.0;
                    CheckUpdates = data.CheckUpdates ?? true;
                    HideTestBar = data.HideTestBar ?? false;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new Data { CloudKeys = CloudKeys, DockLayout = DockLayout, CustomUrl = CustomUrl, CustomModel = CustomModel, AiChoice = AiChoice, LocalModel = LocalModel, SamplesInstalled = SamplesInstalled, SamplesVersion = SamplesVersion, Language = Language, Theme = ThemeName, UiScale = UiScale, CheckUpdates = CheckUpdates, HideTestBar = HideTestBar })); }
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
            [JsonPropertyName("language")] public string? Language { get; set; }
            [JsonPropertyName("theme")] public string? Theme { get; set; }
            [JsonPropertyName("uiScale")] public double? UiScale { get; set; }
            [JsonPropertyName("checkUpdates")] public bool? CheckUpdates { get; set; }
            [JsonPropertyName("hideTestBar")] public bool? HideTestBar { get; set; }
        }
    }
}
