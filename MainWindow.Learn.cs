using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace EasyEngine
{
    /// <summary>What the AI tab does with a message: Build edits the scene; Brainstorm and Learn only chat.</summary>
    public enum AiMode { Build, Brainstorm, Learn }

    // Learn mode ("teach me"): the user asks how to use something and the AI answers from the engine's REAL behavior
    // catalog + the how-to guides (BehaviorGuides.cs). It never changes the project.
    public partial class MainWindow
    {
        private readonly List<(string q, string a)> _learnHistory = new(); // the last few Q/A pairs, for follow-up questions

        private const string EditorBasics =
            "- Start: the launcher opens first — \"＋ New Project\", \"📂 Open Project…\" or a recent project.\n" +
            "- Top bar: New / Open / Save, Variables (the game's variables — a Number, a Yes/No, a Text, a Colour or a List: score, coins, hasKey, playerName, teamColor, quests…), the Scene dropdown with Add Scene / Rename / Delete / Set as Start, Export (makes a game .exe you can share) and ▶ Test (plays the game).\n" +
            "- SCENE LAYERS panel: Background (drawn behind), Base (the level), UI (fixed on the screen: score, health bars, minimap) and Sky (behind everything, created by 🌗 Day / Night cycle). Click a layer to make it active — new objects go there; ＋ adds a layer.\n" +
            "- OBJECTS panel: every object in the scene; click one to select it. You can also click/drag objects on the scene.\n" +
            "- PREFABS panel: click a prefab to add it to the active layer; \"＋ Empty Object\"; \"Save selection as prefab\" turns a finished object into a reusable prefab.\n" +
            "- Inspector tab (right side), for the selected object: Name, Group, Duplicate / Delete / Save as Prefab; TRANSFORM (X, Y, Width, Height, Rotation); APPEARANCE (Color, \"🎞 Edit Animations…\", Static image, Opacity…; a Tilemap object shows only Opacity — each tile's colour, picture or animation is set in its card); PHYSICS (Mode None / Static / Dynamic, Gravity ×, Bounciness, the \"Hitbox: fit to the drawing\" checkbox — collide with the picture's visible pixels instead of the whole box, shown as a dashed outline in the scene — and \"✂ Trim box to drawing\", which shrinks the object's box to its drawing once); BEHAVIORS: one card per behavior with fields you pick from lists, and the \"＋ Add behavior…\" dropdown below them. Each card has \"?\" (explains it here) and Remove.\n" +
            "- Click an empty spot of the scene to edit the scene itself: Width, Height, Background colour, Ambient light.\n" +
            "- ✨ Custom (AI script) behavior: \"🧩 Build with blocks (easy)\" opens a Scratch-like block editor (When… blocks + what to do).\n" +
            "- While playing (▶ Test): F1 opens the debug console (what happened, errors, variables).\n" +
            "- AI tab modes: 🔨 Build = the AI edits the scene for you; 💡 Brainstorm = chat about ideas; 🎓 Learn = explanations like this one (nothing is changed).";

        // Words a beginner might use (Portuguese and English, without accents) → the behaviors they mean.
        private static readonly (string[] words, BehaviorKind[] kinds)[] LearnSynonyms =
        {
            (new[] { "paralaxe", "parallax", "fundo que mexe", "fundo que se move", "background scroll", "scrolling background", "moving background" }, new[] { BehaviorKind.Parallax }),
            (new[] { "chefe", "chefao", "boss" }, new[] { BehaviorKind.BossAI }),
            (new[] { "cinematica", "cutscene", "cena", "cinema" }, new[] { BehaviorKind.Cutscene }),
            (new[] { "terreno", "terrain", "mundo infinito", "infinite world", "endless world", "caverna", "cave" }, new[] { BehaviorKind.Terrain }),
            (new[] { "minimapa", "minimap", "mapa" }, new[] { BehaviorKind.Minimap }),
            (new[] { "dia e noite", "noite", "dia", "day and night", "night", "day/night" }, new[] { BehaviorKind.DayNight }),
            (new[] { "machado", "picareta", "espada", "ferramenta", "item", "itens", "axe", "pickaxe", "sword", "tool", "inventario", "inventory" }, new[] { BehaviorKind.ToolUse, BehaviorKind.Hotbar }),
            (new[] { "arvore", "pedra", "minerio", "minerar", "tree", "rock", "stone", "mine" }, new[] { BehaviorKind.Harvestable }),
            (new[] { "loja", "comprar", "vender", "bancada", "shop", "buy", "craft", "crafting" }, new[] { BehaviorKind.Shop }),
            (new[] { "porta trancada", "chave", "locked door", "key" }, new[] { BehaviorKind.Gate, BehaviorKind.AbilityPickup }),
            (new[] { "plataforma", "pular", "pulo", "jump", "platform" }, new[] { BehaviorKind.Platformer }),
            (new[] { "inimigo que persegue", "perseguir", "persegue", "chase", "enemy that follows" }, new[] { BehaviorKind.Chase, BehaviorKind.PathChase }),
            (new[] { "patrulha", "patrulhar", "patrol", "caminho", "path" }, new[] { BehaviorKind.AdvancedPatrol }),
            (new[] { "luz", "tocha", "lampada", "light", "torch" }, new[] { BehaviorKind.LightSource }),
            (new[] { "particula", "explosao", "fumaca", "particle" }, new[] { BehaviorKind.Particles }),
            (new[] { "texto", "placar", "pontuacao", "pontos", "score", "text", "hud" }, new[] { BehaviorKind.Label }),
            (new[] { "vida", "barra de vida", "health", "life" }, new[] { BehaviorKind.Health, BehaviorKind.Bar }),
            (new[] { "dano", "espinho", "damage", "spike" }, new[] { BehaviorKind.Damager, BehaviorKind.Health }),
            (new[] { "camera" }, new[] { BehaviorKind.FollowCamera }),
            (new[] { "tile", "tiles", "ladrilho", "tilemap" }, new[] { BehaviorKind.Tilemap }),
            (new[] { "gerador", "procedural", "aleatorio", "random" }, new[] { BehaviorKind.Generator }),
            (new[] { "alavanca", "interruptor", "lever", "switch" }, new[] { BehaviorKind.Switch }),
            (new[] { "npc", "dialogo", "conversar", "falar", "placa", "bau", "sign", "chest" }, new[] { BehaviorKind.Interact }),
            (new[] { "zona", "gatilho", "zone", "trigger" }, new[] { BehaviorKind.Area }),
            (new[] { "bloco", "blocos", "scratch", "script", "codigo", "blocks", "code" }, new[] { BehaviorKind.Custom }),
            (new[] { "visao de cima", "vista de cima", "top down", "top-down" }, new[] { BehaviorKind.TopDown }),
            (new[] { "som", "musica", "sound", "music" }, new[] { BehaviorKind.SoundEmitter }),
            (new[] { "moeda", "coletar", "coin", "collect" }, new[] { BehaviorKind.Collectible }),
            (new[] { "tiro", "atirar", "shoot" }, new[] { BehaviorKind.Shooter }),
        };

        // What a beginner most likely needs when the question matches nothing.
        private static readonly BehaviorKind[] CommonBehaviors =
            { BehaviorKind.Platformer, BehaviorKind.TopDown, BehaviorKind.FollowCamera, BehaviorKind.Health, BehaviorKind.Label };

        private static string Fold(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString();
        }

        // Every phrase that names behaviors — the synonyms, then each behavior's kind and the parts of its name — as a
        // whole-word pattern (plural allowed), built once.
        private static readonly Lazy<List<(Regex phrase, BehaviorKind[] kinds)>> LearnPhrases = new(() =>
            LearnSynonyms.SelectMany(s => s.words.Select(w => (w, s.kinds)))
                .Concat(BehaviorCatalog.All.SelectMany(info => info.Name.Split('(', ')', '/').Select(p => p.Trim()).Where(p => p.Length >= 3)
                    .Prepend(info.Kind.ToString()).Select(p => (p, new[] { info.Kind }))))
                .Select(x => (new Regex($@"\b{Regex.Escape(Fold(x.Item1))}(s|es)?\b"), x.Item2)).ToList());

        /// <summary>The behaviors a question is about: synonyms (PT/EN), behavior names and kinds, and ResolveKind's words.</summary>
        private static List<BehaviorKind> MatchBehaviors(string question)
        {
            var q = Fold(question);
            var found = new List<BehaviorKind>();
            void Add(BehaviorKind k) { if (!found.Contains(k)) found.Add(k); }

            foreach (var (phrase, kinds) in LearnPhrases.Value)
                if (phrase.IsMatch(q)) foreach (var k in kinds) Add(k);
            foreach (var word in Regex.Split(q, "[^a-z]+").Where(w => w.Length >= 3))
                if (ResolveKind(word) is BehaviorKind k) Add(k);
            return found;
        }

        private static string FriendlyLabel(string label) => label.Replace(" (0/1)", "").Replace(" (0-1)", "");

        /// <summary>One behavior in readable form: "Icon Name — Desc", its fields as labelled (with the choices), then the guide.</summary>
        private static string DescribeBehavior(BehaviorInfo info)
        {
            static string Choices(string[]? options) => options == null ? "" : ": " + string.Join(" / ", options.Select(o => ParamSources.Option(o).label));
            var fields = info.Nums.Select(n => FriendlyLabel(n.Label) + (n.Label.EndsWith("(0/1)") ? " [on/off]" : "") + Choices(n.Options))
                .Concat(info.Texts.Where(t => t.Pick != PickKind.Special).Select(t => FriendlyLabel(t.Label) + (t.Pick == PickKind.Choice ? Choices(t.Options) : "")))
                .ToList();
            var sb = new StringBuilder($"{info.Icon} {info.Name} — {info.Desc}\n");
            if (fields.Count > 0) sb.AppendLine("  Fields: " + string.Join("; ", fields));
            if (BehaviorGuides.For(info.Kind) is string guide) sb.AppendLine("  How to use it:\n  " + guide.Replace("\n", "\n  "));
            return sb.ToString();
        }

        private string BuildTeachPrompt(string question, bool compact)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are the friendly teacher inside EasyEngine, a 2D game maker for beginners (no coding needed).");
            sb.AppendLine("Explain how to do what the user asks, using ONLY the features described below.");
            sb.AppendLine();
            sb.AppendLine("THE EDITOR");
            sb.AppendLine(EditorBasics);
            sb.AppendLine();

            var visible = BehaviorCatalog.All.Where(i => !i.Hidden).ToList();
            sb.AppendLine("BEHAVIORS (added with Inspector → ＋ Add behavior…)");
            IEnumerable<BehaviorInfo> detailed = visible;
            if (compact) // small local models: the names of all, details only for what the question is about
            {
                sb.AppendLine("All behaviors: " + string.Join(", ", visible.Select(i => $"{i.Icon} {i.Name}")));
                var matched = MatchBehaviors(question).Select(k => visible.Find(i => i.Kind == k)).OfType<BehaviorInfo>().Take(6).ToList();
                detailed = matched.Count > 0 ? matched : CommonBehaviors.Select(BehaviorCatalog.Info);
                sb.AppendLine();
            }
            foreach (var info in detailed) sb.AppendLine(DescribeBehavior(info));

            sb.AppendLine($"THE USER'S PROJECT: \"{_project.Name}\", scene \"{_scene.Name}\".");
            if (_selected != null)
                sb.AppendLine($"Selected object: \"{_selected.Name}\" with behaviors: " +
                              (_selected.Behaviors.Count > 0 ? string.Join(", ", _selected.Behaviors.Select(b => { var i = BehaviorCatalog.Info(b.Kind); return $"{i.Icon} {i.Name}"; })) : "(none)") + ".");
            sb.AppendLine();
            sb.AppendLine("HOW TO REPLY");
            sb.AppendLine("- Answer in the same language the user writes in.");
            sb.AppendLine("- Short numbered steps a beginner can follow (usually 3-8), no jargon.");
            sb.AppendLine("- Use the exact names shown in the editor, e.g. \"Inspector → ＋ Add behavior → 🏔 Parallax\", and the field names exactly as labelled above.");
            sb.AppendLine("- No JSON and no code. You only explain — nothing is changed. To have it built for them, tell them to switch to 🔨 Build and what to ask.");
            sb.AppendLine("- Don't invent buttons, behaviors or fields. If something is impossible, say so and suggest the closest way.");
            if (!L.IsEnglish) sb.AppendLine(L.AiReplyRule);
            return sb.ToString();
        }

        /// <summary>Learn mode: answers a question from the catalog + guides. 'generate' replaces the AI (tests only).</summary>
        private async Task<string> TeachAsync(string question, Func<string, string, Task<string>>? generate = null)
        {
            static string Short(string s) => s.Length > 600 ? s[..600] + "…" : s;
            var sys = BuildTeachPrompt(question, compact: !_useCloud);
            var prompt = _learnHistory.Count == 0 ? question
                : "Earlier in this conversation:\n" + string.Join("\n", _learnHistory.Select(h => $"Q: {h.q}\nA: {h.a}")) + "\n\nNew question: " + question;
            var ans = (generate != null ? await generate(prompt, sys) : await AiGenerateAsync(prompt, sys, json: false, temperature: 0.3)).Trim();
            _learnHistory.Add((Short(question), Short(ans)));
            if (_learnHistory.Count > 3) _learnHistory.RemoveAt(0);
            return ans;
        }

        // the translated name, with the English one the AI knows the behavior by
        private static string BuildHelpQuestion(BehaviorInfo info) => L.F("How do I use {0} {1}? Explain step by step for a beginner.", info.Icon,
            L.IsEnglish || L.T(info.Name) == info.Name ? info.Name : $"{L.T(info.Name)} ({info.Name})");

        // the "?" on a behavior card: open the AI tab in Learn mode and ask about it
        private void AskHowTo(BehaviorInfo info, bool send = true)
        {
            SetTab(false);
            SetAiMode(AiMode.Learn);
            AiInput.Text = BuildHelpQuestion(info);
            if (send) AiSend_Click(AiSend, new RoutedEventArgs());
        }

        // ---- test hooks ----
        internal string TestTeachPrompt(string question, bool compact) => BuildTeachPrompt(question, compact);
        internal static List<BehaviorKind> TestMatchBehaviors(string question) => MatchBehaviors(question);
        internal void TestSetAiMode(AiMode mode) => SetAiMode(mode);
        internal AiMode TestAiMode => _aiMode;
        /// <summary>Clicks a behavior's "?" without sending: returns the question it put in the AI box.</summary>
        internal string TestAskHowTo(BehaviorKind kind) { AskHowTo(BehaviorCatalog.Info(kind), send: false); return AiInput.Text; }
        /// <summary>Runs Learn with a fake AI that replies 'reply'; returns the answer and the prompt the AI got.</summary>
        internal (string answer, string prompt) TestTeach(string question, string reply)
        {
            string seen = "";
            var ans = TeachAsync(question, (p, s) => { seen = p; return Task.FromResult(reply); }).GetAwaiter().GetResult();
            return (ans, seen);
        }
    }
}
