using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>
    /// Cliente simples pro servidor local do Ollama (http://localhost:11434).
    /// Não precisa de nenhum pacote externo — só HttpClient + System.Text.Json.
    /// </summary>
    public class OllamaClient
    {
        private const string BaseUrl = "http://localhost:11434";

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(10) // 1ª resposta pode demorar (carregar modelo na RAM)
        };

        /// <summary>True se o servidor do Ollama estiver de pé.</summary>
        public async Task<bool> IsRunningAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var resp = await Http.GetAsync($"{BaseUrl}/api/tags", cts.Token);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        /// <summary>Lista os modelos já baixados (ex: "qwen2.5-coder:7b").</summary>
        public async Task<List<string>> ListModelsAsync()
        {
            var result = new List<string>();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var json = await Http.GetStringAsync($"{BaseUrl}/api/tags", cts.Token);
                var tags = JsonSerializer.Deserialize<TagsResponse>(json);
                if (tags?.Models != null)
                    foreach (var m in tags.Models)
                        if (!string.IsNullOrEmpty(m.Name)) result.Add(m.Name);
            }
            catch { /* servidor fora do ar -> lista vazia */ }
            return result;
        }

        /// <summary>
        /// Gera uma resposta (sem streaming). Lança OllamaException com mensagem amigável
        /// se o servidor estiver fora ou o modelo não existir.
        /// </summary>
        public async Task<string> GenerateAsync(string model, string prompt, string system, bool json = false, double temperature = 0.2, CancellationToken ct = default)
        {
            var body = new GenerateRequest
            {
                Model = model,
                Prompt = prompt,
                System = system,
                Stream = false,
                Format = json ? "json" : null,   // forces valid JSON output (huge reliability win on small models)
                Options = new GenerateOptions { Temperature = temperature, NumCtx = ContextFor(system.Length + prompt.Length), NumPredict = 3000 }
            };
            var content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.PostAsync($"{BaseUrl}/api/generate", content, ct);
            }
            catch (HttpRequestException)
            {
                throw new OllamaException(L.T("Couldn't talk to Ollama. Is it installed and running? (server at localhost:11434)"));
            }
            catch (TaskCanceledException)
            {
                throw new OllamaException(L.T("The answer took too long and was cancelled."));
            }

            var responseText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Ollama answers 404 with a message when the model isn't downloaded.
                if (responseText.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    throw new OllamaException(L.F("The model \"{0}\" isn't downloaded. Run this in a terminal:", model) + $"\n    ollama pull {model}");
                throw new OllamaException(L.F("Ollama answered with an error ({0}): {1}", (int)resp.StatusCode, responseText));
            }

            var parsed = JsonSerializer.Deserialize<GenerateResponse>(responseText);
            return parsed?.Response?.Trim() ?? "";
        }

        // ---- DTOs ----
        private class GenerateRequest
        {
            [JsonPropertyName("model")]  public string Model  { get; set; } = "";
            [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
            [JsonPropertyName("system")] public string System { get; set; } = "";
            [JsonPropertyName("stream")] public bool   Stream { get; set; }
            [JsonPropertyName("format")] public string? Format { get; set; }
            [JsonPropertyName("options")] public GenerateOptions? Options { get; set; }
        }

        // Ollama runs a model with a small window (often 4096 tokens) unless asked: a longer prompt is silently cut and the
        // model never sees the start of its instructions. Ask for room for the whole prompt (~3 characters a token) plus
        // the answer, in steps of 2048 so the model isn't reloaded for every small change.
        internal static int ContextFor(int promptChars) => Math.Clamp((promptChars / 3 + 3000 + 2047) / 2048 * 2048, 4096, 32768);

        private class GenerateOptions
        {
            [JsonPropertyName("temperature")] public double Temperature { get; set; }
            [JsonPropertyName("num_ctx")] public int NumCtx { get; set; }
            [JsonPropertyName("num_predict")] public int NumPredict { get; set; } // a small model can loop forever: a whole level fits in 3000 tokens
        }

        private class GenerateResponse
        {
            [JsonPropertyName("response")] public string? Response { get; set; }
        }

        private class TagsResponse
        {
            [JsonPropertyName("models")] public List<ModelInfo>? Models { get; set; }
        }

        private class ModelInfo
        {
            [JsonPropertyName("name")] public string? Name { get; set; }
        }
    }

    /// <summary>Base pra erros de IA (local ou nuvem), pra o painel tratar os dois igual.</summary>
    public class AiException : Exception
    {
        public AiException(string message) : base(message) { }
    }

    public class OllamaException : AiException
    {
        public OllamaException(string message) : base(message) { }
    }
}
