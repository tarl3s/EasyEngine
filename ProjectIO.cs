using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EasyEngine
{
    /// <summary>Salvar/abrir projetos, lista de recentes e importação de assets.</summary>
    public static class ProjectIO
    {
        public const string Extension = ".eeproj";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        private static string AppDataDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyEngine");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string RecentFile => Path.Combine(AppDataDir, "recent.json");

        // ---------------- salvar / abrir ----------------
        public static void Save(Project p)
        {
            if (string.IsNullOrEmpty(p.FilePath))
                throw new InvalidOperationException("Project has no file path.");
            var json = JsonSerializer.Serialize(p, JsonOpts);
            File.WriteAllText(p.FilePath, json);
            AddRecent(p);
        }

        public static Project Load(string path)
        {
            var json = File.ReadAllText(path);
            var p = JsonSerializer.Deserialize<Project>(json) ?? throw new InvalidDataException("Invalid project file.");
            p.FilePath = path;
            p.FolderPath = Path.GetDirectoryName(path);

            // migration / safety: ensure at least one scene
            if (p.Scenes == null || p.Scenes.Count == 0)
            {
                var scene = BuildStarterScene();
                p.Scenes = new List<Scene> { scene };
                p.StartScene = scene.Name;
            }
            if (string.IsNullOrEmpty(p.StartScene)) p.StartScene = p.Scenes[0].Name;
            if (p.Prefabs == null || p.Prefabs.Count == 0) p.Prefabs = PrefabFactory.Defaults();
            MigrateLegacy(p);

            AddRecent(p);
            return p;
        }

        /// <summary>
        /// Upgrades behaviors that no longer exist. Melee (press J to swing a hitbox) became Tool Use (click to use the
        /// item in hand): damage/range/knockback/cooldown carry over; with no Hotbar the item in hand is a sword.
        /// </summary>
        public static int MigrateLegacy(Project p)
        {
            int n = 0;
            var objects = p.Scenes.SelectMany(s => s.Layers).SelectMany(l => l.Objects)
                          .Concat(p.Prefabs.Where(pf => pf.Template != null).Select(pf => pf.Template!));
            foreach (var o in objects)
            {
                for (int i = o.Behaviors.Count - 1; i >= 0; i--)
                {
                    var b = o.Behaviors[i];
                    if (b.Kind != BehaviorKind.Melee) continue;
                    o.Behaviors.RemoveAt(i); n++;
                    if (o.Behaviors.Any(x => x.Kind == BehaviorKind.ToolUse)) continue; // already has the new one
                    var tu = BehaviorCatalog.Info(BehaviorKind.ToolUse).CreateData();
                    tu.Nums["damage"] = b.Num("damage", 1);
                    tu.Nums["reach"] = Math.Max(40, b.Num("range", 40) + 20);
                    tu.Nums["knockback"] = b.Num("knockback", 260);
                    tu.Nums["cooldown"] = b.Num("cooldown", 0.35);
                    tu.Texts["itemDamage"] = "";
                    o.Behaviors.Insert(i, tu);
                }
            }
            return n;
        }

        // ---------------- novo projeto ----------------
        public static Project CreateNew(string name, string parentFolder)
        {
            var folder = Path.Combine(parentFolder, Sanitize(name));
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "Assets", "sprites"));
            Directory.CreateDirectory(Path.Combine(folder, "Assets", "sounds"));

            var p = new Project { Name = name };
            p.FolderPath = folder;
            p.FilePath = Path.Combine(folder, Sanitize(name) + Extension);
            p.Prefabs = PrefabFactory.Defaults();
            var scene = BuildStarterScene();
            p.Scenes.Add(scene);
            p.StartScene = scene.Name;

            Save(p);
            return p;
        }

        // Starter scene, already playable: default layers + ground + platformer player.
        private static Scene BuildStarterScene()
        {
            var scene = new Scene();

            var background = new Layer { Name = "Background", Kind = LayerKind.Background };
            var baseLayer = new Layer { Name = "Base", Kind = LayerKind.Base };
            var ui = new Layer { Name = "UI", Kind = LayerKind.UI, ScreenSpace = true };

            // ground
            baseLayer.Objects.Add(new GameObjectData
            {
                Id = 1, Name = "Ground", Type = "Object",
                X = 0, Y = 460, Width = 960, Height = 80, Color = "#3A3A46",
                Physics = new PhysicsData { Mode = PhysicsMode.Static },
                Behaviors = { new BehaviorData { Kind = BehaviorKind.Solid } }
            });

            // player
            baseLayer.Objects.Add(new GameObjectData
            {
                Id = 2, Name = "Player", Type = "Player",
                X = 120, Y = 360, Width = 48, Height = 64, Color = "#3BC97A",
                Physics = new PhysicsData { Mode = PhysicsMode.Dynamic, GravityScale = 1 },
                Behaviors =
                {
                    new BehaviorData { Kind = BehaviorKind.Platformer,
                        Nums = { ["speed"] = 260, ["jump"] = 620 } },
                    new BehaviorData { Kind = BehaviorKind.FollowCamera }
                }
            });

            scene.Layers.Add(background);
            scene.Layers.Add(baseLayer);
            scene.Layers.Add(ui);
            return scene;
        }

        // ---------------- assets ----------------
        /// <summary>Copia um arquivo pra dentro do projeto e devolve o caminho RELATIVO.</summary>
        public static string ImportAsset(Project p, string sourceFile, string kind /* "sprites" | "sounds" */)
        {
            if (string.IsNullOrEmpty(p.FolderPath))
                throw new InvalidOperationException("Project has no folder.");
            var destDir = Path.Combine(p.FolderPath, "Assets", kind);
            Directory.CreateDirectory(destDir);
            var fileName = Path.GetFileName(sourceFile);
            var dest = Path.Combine(destDir, fileName);

            // evita sobrescrever: acrescenta sufixo se já existe com conteúdo diferente
            if (File.Exists(dest) && !FilesEqual(sourceFile, dest))
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                int i = 1;
                do { dest = Path.Combine(destDir, $"{stem}_{i++}{ext}"); } while (File.Exists(dest));
            }

            if (!File.Exists(dest)) File.Copy(sourceFile, dest, overwrite: false);
            return Path.Combine("Assets", kind, Path.GetFileName(dest));
        }

        /// <summary>Saves a drawn bitmap as a PNG in Assets/sprites and returns its project-relative path.</summary>
        public static string SaveSprite(Project p, System.Windows.Media.Imaging.BitmapSource bmp, string baseName)
        {
            if (string.IsNullOrEmpty(p.FolderPath))
                throw new InvalidOperationException("Project has no folder.");
            var destDir = Path.Combine(p.FolderPath, "Assets", "sprites");
            Directory.CreateDirectory(destDir);
            var stem = Sanitize(string.IsNullOrWhiteSpace(baseName) ? "sprite" : baseName);
            var dest = Path.Combine(destDir, stem + ".png");
            int i = 1;
            while (File.Exists(dest)) dest = Path.Combine(destDir, $"{stem}_{i++}.png");
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = File.Create(dest)) enc.Save(fs);
            return Path.Combine("Assets", "sprites", Path.GetFileName(dest));
        }

        /// <summary>Saves WAV bytes in Assets/sounds (never overwriting) and returns the project-relative path.</summary>
        public static string SaveSound(Project p, byte[] wav, string baseName)
        {
            if (string.IsNullOrEmpty(p.FolderPath))
                throw new InvalidOperationException("Project has no folder.");
            var destDir = Path.Combine(p.FolderPath, "Assets", "sounds");
            Directory.CreateDirectory(destDir);
            var stem = Sanitize(string.IsNullOrWhiteSpace(baseName) ? "sound" : baseName);
            var dest = Path.Combine(destDir, stem + ".wav");
            int i = 1;
            while (File.Exists(dest)) dest = Path.Combine(destDir, $"{stem}_{i++}.wav");
            File.WriteAllBytes(dest, wav);
            return Path.Combine("Assets", "sounds", Path.GetFileName(dest));
        }

        public static string? AbsoluteAsset(Project p, string? relative)
        {
            if (string.IsNullOrEmpty(relative) || string.IsNullOrEmpty(p.FolderPath)) return null;
            var abs = Path.Combine(p.FolderPath, relative);
            return File.Exists(abs) ? abs : null;
        }

        /// <summary>Absolute path of the image shown for this object (default animation's first frame, else static sprite).</summary>
        public static string? DisplayImageAbs(Project p, GameObjectData o)
        {
            var anim = o.ResolveDefaultAnimation();
            if (anim != null && anim.Frames.Count > 0)
                return AbsoluteAsset(p, anim.Frames[0]);
            return AbsoluteAsset(p, o.SpritePath);
        }

        // ---------------- delete ----------------
        /// <summary>Removes a project from the recent list (does NOT touch files).</summary>
        public static void RemoveRecent(string filePath)
        {
            var list = GetRecent();
            list.RemoveAll(r => string.Equals(r.Path, filePath, StringComparison.OrdinalIgnoreCase));
            try { File.WriteAllText(RecentFile, JsonSerializer.Serialize(list, JsonOpts)); } catch { }
        }

        /// <summary>Deletes the entire project folder from disk and removes it from recents.</summary>
        public static void DeleteProjectFiles(string filePath)
        {
            RemoveRecent(filePath);
            var folder = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }

        private static bool FilesEqual(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            return fa.Length == fb.Length;
        }

        // ---------------- recentes ----------------
        public static List<RecentProject> GetRecent()
        {
            try
            {
                if (!File.Exists(RecentFile)) return new();
                var list = JsonSerializer.Deserialize<List<RecentProject>>(File.ReadAllText(RecentFile)) ?? new();
                return list.Where(r => File.Exists(r.Path)).ToList();
            }
            catch { return new(); }
        }

        // Test runs create many throwaway projects; with this on they never touch the user's Recent list.
        public static bool SuppressRecent { get; set; }

        public static void AddRecent(Project p)
        {
            if (SuppressRecent || string.IsNullOrEmpty(p.FilePath)) return;
            var list = GetRecent();
            list.RemoveAll(r => string.Equals(r.Path, p.FilePath, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, new RecentProject
            {
                Name = p.Name,
                Path = p.FilePath,
                LastOpened = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            });
            if (list.Count > 12) list = list.Take(12).ToList();
            try { File.WriteAllText(RecentFile, JsonSerializer.Serialize(list, JsonOpts)); } catch { }
        }

        private static string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Projeto" : name.Trim();
        }
    }
}
