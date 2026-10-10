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
    /// <summary>
    /// Cliente pra IA na nuvem via Groq (API gratuita, sem cartão, muito rápida).
    /// Usa o endpoint compatível com OpenAI, então é só HttpClient + JSON.
    /// Mesma assinatura do OllamaClient.GenerateAsync pra ser intercambiável.
    /// </summary>
    public class CloudClient
    {
        private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        public async Task<string> GenerateAsync(string model, string prompt, string system, string apiKey, bool json = false, double temperature = 0.2, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new CloudException("Falta a chave de API do Groq. Cole sua chave gratuita no painel de IA (pegue em console.groq.com/keys).");

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
                // Groq exige a palavra "json" em alguma mensagem quando usa json_object — o nosso system prompt já pede JSON.
                ResponseFormat = json ? new ResponseFormat { Type = "json_object" } : null
            };

            var payload = JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req, ct);
            }
            catch (HttpRequestException)
            {
                throw new CloudException("Não consegui falar com o Groq. Verifique sua conexão com a internet.");
            }
            catch (TaskCanceledException)
            {
                throw new CloudException("A geração na nuvem demorou demais e foi cancelada.");
            }

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = TryGetErrorMessage(text);
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                    throw new CloudException("Chave de API do Groq inválida. Confira a chave no painel (console.groq.com/keys).");
                if (resp.StatusCode == (HttpStatusCode)429)
                    throw new CloudException("Limite gratuito do Groq atingido por agora. Espere um pouco e tente de novo" + (detail != null ? $" ({detail})." : "."));
                if (resp.StatusCode == HttpStatusCode.NotFound || (detail?.Contains("model", StringComparison.OrdinalIgnoreCase) ?? false))
                    throw new CloudException($"O modelo \"{model}\" não está disponível no Groq. Escolha outro no painel de IA." + (detail != null ? $"\n({detail})" : ""));
                throw new CloudException($"Groq respondeu com erro ({(int)resp.StatusCode}): {detail ?? text}");
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponse>(text);
                return parsed?.Choices is { Count: > 0 } ? (parsed.Choices[0].Message?.Content?.Trim() ?? "") : "";
            }
            catch
            {
                return "";
            }
        }

        private static string? TryGetErrorMessage(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                    return msg.GetString();
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

        private class ResponseFormat
        {
            [JsonPropertyName("type")] public string Type { get; set; } = "";
        }

        private class ChatResponse
        {
            [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        }

        private class Choice
        {
            [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
        }
    }

    public class CloudException : AiException
    {
        public CloudException(string message) : base(message) { }
    }

    /// <summary>Guarda configurações do app (ex: chave de API) em %APPDATA%\EasyEngine\settings.json.</summary>
    public static class AiSettings
    {
        private static string Dir
        {
            get
            {
                var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyEngine");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static string File => Path.Combine(Dir, "settings.json");

        public static string GroqApiKey { get; set; } = "";
        public static Dictionary<string, string> DockLayout { get; set; } = new();

        public static void Load()
        {
            try
            {
                if (!System.IO.File.Exists(File)) return;
                var data = JsonSerializer.Deserialize<Data>(System.IO.File.ReadAllText(File));
                if (data != null)
                {
                    GroqApiKey = data.GroqApiKey ?? "";
                    DockLayout = data.DockLayout ?? new();
                }
            }
            catch { /* settings corrompido -> ignora */ }
        }

        public static void Save()
        {
            try { System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new Data { GroqApiKey = GroqApiKey, DockLayout = DockLayout })); }
            catch { }
        }

        private class Data
        {
            [JsonPropertyName("groqApiKey")] public string? GroqApiKey { get; set; }
            [JsonPropertyName("dockLayout")] public Dictionary<string, string>? DockLayout { get; set; }
        }
    }
}
