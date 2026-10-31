using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EasyEngine
{
    /// <summary>
    /// A request written in another language is first turned into English by the same AI, in one short call: the prompt's
    /// hints and examples are picked by its words, the reply fix-ups read it, and small local models follow an English
    /// request far better (measured: Portuguese 77–86% against English 96–98%). Names, numbers and quoted text stay as
    /// written; a translation that loses any of them is not used. The call asks for JSON so every AI path can make it
    /// (the block editor's generator only answers JSON).
    /// </summary>
    public static class AiTranslate
    {
        internal const string System =
            "Translate the user's request for a 2D game editor into plain English, as a command (\"make…\", \"create…\", \"when…\"). " +
            "Keep every name that starts with a capital letter (objects, scenes, variables, keys), every number and every text inside " +
            "quotes exactly as written. Translate the meaning, not word by word, with game words: inimigo/enemigo = enemy, chefe/jefe = boss, espinho/pinchos = " +
            "spike, escada = stairs, vai e volta/va y viene = patrols back and forth, persegue/sigue = chases, foge = flees, vida = health, " +
            "moeda/moneda = coin, tela/pantalla = screen, \"sobe e desce\" = \"moves up and down\", \"tira a vida do jogador\" = " +
            "\"remove the health from the player\". " +
            "Answer ONLY with JSON, e.g. for \"faz a Box pular\": {\"english\":\"make the Box jump\"}";


        private static readonly Regex WordRx = new(@"\p{L}+'?\p{L}*", RegexOptions.Compiled);
        private static readonly Regex QuotedRx = new("[\"“‘«']([^\"”’»']{2,})[\"”’»']", RegexOptions.Compiled);
        private static readonly System.Collections.Generic.HashSet<string> English = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "an", "and", "or", "to", "of", "when", "make", "it", "is", "with", "every", "that", "on", "in", "at", "from",
            "after", "i", "me", "my", "create", "add", "give", "put", "let", "its", "into", "be", "while", "only", "if", "then", "each",
            "player", "enemy", "touches", "press", "show", "goes", "go", "seconds", "second", "dies", "him", "his", "her", "them", "up",
            "down", "can", "has", "have", "this", "for", "until", "but", "not", "don't", "build", "turn", "set", "remove", "delete", "also",
        };
        private static readonly System.Collections.Generic.HashSet<string> Foreign = new(StringComparer.OrdinalIgnoreCase)
        {
            // Portuguese
            "o", "os", "as", "um", "uma", "que", "de", "do", "da", "dos", "das", "para", "pra", "pro", "com", "quando", "faz", "faça", "cria",
            "crie", "coloca", "coloque", "deixa", "deixe", "ele", "ela", "no", "na", "em", "e", "é", "ao", "à", "se", "não", "mais", "jogador",
            "inimigo", "depois", "cada", "segundos", "tela", "vai", "fica", "eu", "aperto", "apertar", "encostar", "encosta", "pega", "tira",
            // Spanish
            "el", "la", "los", "las", "y", "en", "con", "cuando", "haz", "pon", "jugador", "enemigo", "después", "pantalla", "del", "al",
            "lo", "su", "muy", "hacia", "presione", "toque", "crea",
            // French
            "le", "les", "et", "une", "des", "quand", "fais", "joueur", "ennemi", "après", "chaque", "écran", "avec", "il", "elle", "du",
            // German
            "der", "die", "das", "und", "mit", "wenn", "ein", "eine", "spieler", "gegner", "nach", "jede", "jeden", "sekunden", "mach",
            // Italian
            "gli", "giocatore", "nemico", "dopo", "ogni", "fai", "schermo",
        };

        /// <summary>True when the request is (mostly) English — then no translation is made. Text in quotes and names don't
        /// count; a request in a non-Latin script (Japanese, Chinese, Russian…) is never English.</summary>
        public static bool LooksEnglish(string request)
        {
            var text = QuotedRx.Replace(request ?? "", " ");
            if (text.Any(c => c > 0x24F && char.IsLetter(c))) return false;
            int en = 0, other = 0;
            foreach (Match m in WordRx.Matches(text))
            {
                if (char.IsUpper(m.Value[0]) && m.Index > 0) continue; // a name ("Box", "Level2")
                if (English.Contains(m.Value)) en++;
                if (Foreign.Contains(m.Value)) other++;
            }
            return other == 0 || en > other;
        }

        /// <summary>The model's translation if it can be trusted, else null: plain text, not absurdly long or short, keeping
        /// every number, quoted text and name (a capitalised word inside a sentence) of the original.</summary>
        public static string? Clean(string original, string reply)
        {
            var s = (reply ?? "").Trim();
            if (LooseJson.Extract(s) is string json)
                try
                {
                    using var doc = JsonDocument.Parse(json, LooseJson.Options);
                    var root = doc.RootElement;
                    s = root.ValueKind != JsonValueKind.Object ? ""
                        : (root.TryGetProperty("english", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString()
                           : root.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString()).FirstOrDefault()) ?? "";
                }
                catch (JsonException) { return null; }
            if (s.Contains("request in English", StringComparison.OrdinalIgnoreCase) || s == "make the Box jump") return null; // the prompt copied back
            s = Regex.Replace(s, @"^(english|translation)\s*:\s*|^(a|the) request (is )?(to )?", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*\n\s*", " ").Trim();
            if (s.Length < 3 || s.Length > original.Length * 3 + 40) return null;
            foreach (Match m in Regex.Matches(original, @"\d+(?:[.,]\d+)?"))
                if (!s.Contains(m.Value) && !s.Contains(m.Value.Replace(',', '.'))) return null;
            foreach (Match m in QuotedRx.Matches(original)) if (!s.Contains(m.Groups[1].Value)) return null;
            foreach (Match m in Regex.Matches(original, @"(?<![.!?:;]\s*|^\s*)(?<=\s)\p{Lu}[\p{L}\d_]*")) // Box, Level2, GameOver, E
                if (!Regex.IsMatch(s, @"(?<![\p{L}\d])" + Regex.Escape(m.Value) + @"(?![\p{L}\d])")) return null;
            return s;
        }

        /// <summary>The request in English (unchanged when it already is, or when the translation can't be trusted).</summary>
        public static async Task<string> ToEnglishAsync(string request, Func<string, string, Task<string>> generate)
        {
            if (string.IsNullOrWhiteSpace(request) || LooksEnglish(request)) return request;
            try { return Clean(request, await generate(request, System)) ?? request; }
            catch (Exception ex) when (ex is not OutOfMemoryException and not AiException) { return request; }
        }
    }
}
