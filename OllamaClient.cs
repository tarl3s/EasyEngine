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
                Options = new GenerateOptions { Temperature = temperature }
            };
            var content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await Http.PostAsync($"{BaseUrl}/api/generate", content, ct);
            }
            catch (HttpRequestException)
            {
                throw new OllamaException("Não consegui falar com o Ollama. Ele está instalado e rodando? (servidor em localhost:11434)");
            }
            catch (TaskCanceledException)
            {
                throw new OllamaException("A geração demorou demais e foi cancelada.");
            }

            var responseText = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Ollama devolve 404 com msg quando o modelo não foi baixado.
                if (responseText.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    throw new OllamaException($"O modelo \"{model}\" não foi baixado. Rode no terminal:\n    ollama pull {model}");
                throw new OllamaException($"Ollama respondeu com erro ({(int)resp.StatusCode}): {responseText}");
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

        private class GenerateOptions
        {
            [JsonPropertyName("temperature")] public double Temperature { get; set; }
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

    public class OllamaException : Exception
    {
        public OllamaException(string message) : base(message) { }
    }
}
