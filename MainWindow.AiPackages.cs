using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using BE = BuilderEngine.Script; // its Project/Scene clash with ours

namespace EasyEngine
{
    // Behavior packages and the AI (BehaviorPackages.cs): the 🔨 Build prompt lists every package by name with the built-in
    // behaviors (and the details of the ones a request is about: their description, settings, AI keywords and guide),
    // 🎓 Learn explains them, and a reply can add one like a built-in — {"kind":"Wobble","nums":{…}} or
    // {"kind":"Package","texts":{"package":"Wobble"}} — cleaned up by the same AiSpecFix as a built-in (its settings as a
    // catalog entry: BehaviorPackages.InfoFor). A built-in with the same name wins.
    public partial class MainWindow
    {
        /// <summary>The package a reply's behavior names, or null (a built-in, or nothing installed by that name).</summary>
        private BE.BehaviorPackage? PackageOfSpec(JsonElement kindEl, JsonElement raw)
        {
            string kind = kindEl.ValueKind == JsonValueKind.String ? (kindEl.GetString() ?? "").Trim() : "";
            if (kind.Equals("Package", StringComparison.OrdinalIgnoreCase) || kind.Equals("BehaviorPackage", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var bag in new[] { raw.TryGetProperty("texts", out var t) ? t : default, raw })
                    if (bag.ValueKind == JsonValueKind.Object && bag.TryGetProperty(BehaviorPackages.NameKey, out var n) && n.ValueKind == JsonValueKind.String)
                        return BehaviorPackages.For(_project).Find(n.GetString());
                return null;
            }
            return PackageNamed(kind);
        }

        /// <summary>An installed package by its exact name ("Wobble", "wobble"), unless a built-in behavior has that name.</summary>
        private BE.BehaviorPackage? PackageNamed(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || Enum.TryParse<BehaviorKind>(name.Trim(), true, out _)) return null;
            return BehaviorPackages.For(_project).Find(name) is { } p && !BehaviorPackages.ReplacesBuiltIn(p) ? p : null; // the shipped TriggerArea is the built-in Area here
        }

        /// <summary>Adds (or retunes) a package behavior from the reply, its settings cleaned up like a built-in's.</summary>
        private bool ApplyPackageSpec(List<BehaviorData> result, BE.BehaviorPackage pkg, JsonElement raw, List<string> impliedRules,
                                      HashSet<BehaviorData> usedByReply, HashSet<string> sameTwice)
        {
            if (LooseJson.Node(raw) is not System.Text.Json.Nodes.JsonObject node) return false;
            var info = BehaviorPackages.InfoFor(pkg);
            AiSpecFix.Behavior(info, node, impliedRules); // params on the behavior, keys spelled as the package spells them, numbers in texts…
            if (node.ContainsKey("drop")) return false;
            using var doc = JsonDocument.Parse(node.ToJsonString());
            var item = doc.RootElement;
            if (pkg.Repeatable && !sameTwice.Add(pkg.Name + item.GetRawText())) return false;
            bool Mine(BehaviorData b) => string.Equals(BehaviorPackages.NameOf(b), pkg.Name, StringComparison.OrdinalIgnoreCase);
            var existing = pkg.Repeatable ? result.FirstOrDefault(b => Mine(b) && !usedByReply.Contains(b)) : result.FirstOrDefault(Mine);
            var data = existing ?? BehaviorPackages.CreateData(pkg);
            usedByReply.Add(data);
            var settings = pkg.AllParams();
            if (item.TryGetProperty("nums", out var nums) && nums.ValueKind == JsonValueKind.Object)
                foreach (var p in nums.EnumerateObject())
                    if (settings.FirstOrDefault(s => s.Name == p.Name) is { } sp && BehaviorPackages.InNums(sp.Kind) && LooseJson.TryNumber(p.Value, out var v))
                        data.Nums[p.Name] = sp.Kind == BE.ParamKind.Bool ? (v >= 0.5 ? 1 : 0) : Clamp(sp, v);
            if (item.TryGetProperty("texts", out var texts) && texts.ValueKind == JsonValueKind.Object)
                foreach (var p in texts.EnumerateObject())
                {
                    if (p.Name == BehaviorPackages.NameKey || settings.FirstOrDefault(s => s.Name == p.Name) is not { } sp || BehaviorPackages.InNums(sp.Kind)) continue;
                    string value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                    if (sp.Kind == BE.ParamKind.Choice && !sp.Options.Contains(value))
                        value = sp.Options.FirstOrDefault(o => o.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? BehaviorPackages.Text(data, sp); // not one of its options: kept as it was
                    if (sp.Kind == BE.ParamKind.Key) value = BehaviorPackages.EeKey(value);
                    if (sp.Kind == BE.ParamKind.Sound && value.Length > 0 && !value.Replace('\\', '/').Contains("Assets/")) value = ResolveSound(value) ?? value;
                    data.Texts[p.Name] = value;
                }
            data.Texts[BehaviorPackages.NameKey] = pkg.Name;
            if (existing == null) result.Add(data);
            try { BehaviorPackages.EnsureInProject(_project, pkg); } catch (Exception) { } // the copy is a convenience: the behavior is added anyway
            return true;
        }

        private static double Clamp(BE.BehaviorPackage.PackageParam p, double v)
        {
            if (p.Min is double min) v = Math.Max(min, v);
            if (p.Max is double max) v = Math.Min(max, v);
            return v;
        }

        // ---------------------------------------------------------------- the Build prompt
        /// <summary>The prompt's package lines: every package's name (with the built-ins' list) and the details of the ones the
        /// request is about (its name or keywords). Empty when there are none.</summary>
        private (string names, List<string> details) PackagesForPrompt(string request)
        {
            var set = BehaviorPackages.For(_project);
            var usable = set.All.Where(p => !BehaviorPackages.ReplacesBuiltIn(p)).ToList(); // EasyEngine keeps its built-in Spin, Spawner…
            if (usable.Count == 0) return ("", new List<string>());
            var details = BehaviorPackages.Matching(usable, request).Select(BehaviorPackages.AiLine).ToList();
            return (string.Join(", ", usable.Select(p => p.Name)), details);
        }

        // ---------------------------------------------------------------- Learn
        /// <summary>One package for 🎓 Learn: what it does, its settings as labelled (with the choices), and its guide.</summary>
        private static string DescribePackage(BE.BehaviorPackage pkg)
        {
            var fields = pkg.AllParams().Select(p => (p.Label.Length > 0 ? p.Label : p.Name)
                + (p.Kind == BE.ParamKind.Bool ? " [on/off]" : "") + (p.Kind == BE.ParamKind.Choice ? ": " + string.Join(" / ", p.Options) : "")).ToList();
            var sb = new StringBuilder($"{pkg.Icon} {pkg.Name} (📦 behavior package, {pkg.Category}) — {pkg.Description}\n");
            if (fields.Count > 0) sb.AppendLine("  Fields: " + string.Join("; ", fields));
            if (pkg.Repeatable) sb.AppendLine("  It can be added more than once to the same object (each one has its own settings).");
            if (pkg.AiGuide.Length > 0) sb.AppendLine("  How to use it:\n  " + pkg.AiGuide.Replace("\n", "\n  "));
            return sb.ToString();
        }

        /// <summary>Learn's package section: the names of all (marked 📦 in ＋ Add behavior…), details of the ones asked about
        /// (all of them when there are few and the prompt isn't compact).</summary>
        private string PackagesForTeach(string question, bool compact)
        {
            var set = BehaviorPackages.For(_project);
            var all = set.All.Where(p => !BehaviorPackages.ReplacesBuiltIn(p)).ToList(); // the shipped Spin, Spawner… are EasyEngine's built-ins here
            if (all.Count == 0) return "";
            var sb = new StringBuilder();
            sb.AppendLine("BEHAVIOR PACKAGES (behaviors made in BuilderEngine or shared by others; in ＋ Add behavior… marked 📦): "
                          + string.Join(", ", all.Select(p => $"{p.Icon} {p.Name}")));
            var matched = BehaviorPackages.Matching(all, question, 6);
            IEnumerable<BE.BehaviorPackage> detailed = matched.Count > 0 ? matched : compact ? Enumerable.Empty<BE.BehaviorPackage>() : all.Take(8);
            foreach (var p in detailed) sb.AppendLine(DescribePackage(p));
            if (matched.Count > 0) sb.AppendLine("This question is about: " + string.Join(", ", matched.Select(p => $"{p.Icon} {p.Name}")) + " (described above) — build the answer on them.");
            return sb.ToString();
        }
    }
}
