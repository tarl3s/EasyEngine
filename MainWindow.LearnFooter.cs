using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyEngine
{
    // Under every Learn answer, from the engine itself (not the model): the behaviors the question is about and, for
    // something done with blocks, the closest 📚 Recipe and the blocks it is made of — so even a small model's muddled
    // answer points at the right place.
    public partial class MainWindow
    {
        internal static string LearnFooter(string question)
        {
            var lines = new List<string>();
            var kinds = MatchBehaviors(question).Where(k => !BehaviorCatalog.Info(k).Hidden).ToList();
            var named = kinds.Where(k => k != BehaviorKind.Custom).Take(3).Select(k => { var i = BehaviorCatalog.Info(k); return $"{i.Icon} {L.T(i.Name)}"; }).ToList();
            if (named.Count > 0) lines.Add("👉 " + L.T("Behaviors for this (Inspector → ＋ Add behavior):") + " " + string.Join(", ", named));
            if (kinds.Contains(BehaviorKind.Custom))
            {
                lines.Add("🧩 " + L.T("Block editor (✨ Custom → Build with blocks): each block is one rule — WHEN something happens → DO something."));
                var words = Regex.Split(Fold(question), "[^a-z]+").Where(w => w.Length >= 4).ToHashSet();
                var best = BlockEditorWindow.Recipes()
                    .Select(r => (r.label, r.json, score: Regex.Split(Fold(r.label + " " + r.json), "[^a-z]+").Distinct().Count(words.Contains)))
                    .OrderByDescending(x => x.score).First();
                if (best.score >= 2 && RecipeBlocks(best.json) is { Length: > 0 } blocks) lines.Add("📚 " + L.T("Recipe:") + " " + best.label + " — " + blocks);
            }
            return lines.Count == 0 ? "" : "\n\n" + string.Join("\n", lines);
        }

        // "After some seconds → Glide a property": the blocks a recipe is made of, as the block editor names them
        private static string RecipeBlocks(string json)
        {
            try
            {
                using var d = JsonDocument.Parse(json);
                var ev = BlockEditorWindow.MakeEvents(); var ac = BlockEditorWindow.MakeActions();
                string Label(BlockEditorWindow.BlockDef[] defs, JsonElement r, string key) =>
                    r.TryGetProperty(key, out var v) && Array.Find(defs, x => x.Id == v.GetString()) is { } def ? def.Label : "";
                return string.Join(" · ", d.RootElement.EnumerateArray().Select(r => Label(ev, r, "when") + " → " + Label(ac, r, "do")).Distinct());
            }
            catch (Exception) { return ""; }
        }
    }
}
