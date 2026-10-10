using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>
    /// Garante que o servidor do Ollama esteja no ar — iniciando "ollama serve"
    /// escondido se preciso — pra engine não depender do app do Ollama estar aberto.
    /// </summary>
    public static class OllamaManager
    {
        private static Process? _serverProcess;
        private static readonly OllamaClient Client = new();

        public static string? FindExe()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ollama", "ollama.exe"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null; // pode estar no PATH
        }

        public enum State { Running, Started, NotInstalled, Failed }

        /// <summary>Checa o servidor e sobe se necessário. Espera até ~10s ele responder.</summary>
        public static async Task<State> EnsureRunningAsync()
        {
            if (await Client.IsRunningAsync()) return State.Running;

            var exe = FindExe();
            // se não achou o .exe, tenta "ollama" do PATH mesmo assim
            var fileName = exe ?? "ollama";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = "serve",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                _serverProcess = Process.Start(psi);
            }
            catch
            {
                return State.NotInstalled;
            }

            // espera o servidor ficar de pé
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (await Client.IsRunningAsync()) return State.Started;
            }
            return State.Failed;
        }

        public static void Stop()
        {
            // só mata o processo se foi a engine que o iniciou
            try { if (_serverProcess is { HasExited: false }) _serverProcess.Kill(entireProcessTree: true); }
            catch { }
        }
    }
}
