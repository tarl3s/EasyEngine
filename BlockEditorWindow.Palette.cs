using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    // The block palette: every WHEN event and DO action the block editor offers, with its fields. It is the one list the
    // blocks, their ✨ AI prompt and RuleDocs() (the rule language for the Build prompt) are written from; FullTest.Parity
    // checks it against what the game really reads.
    public partial class BlockEditorWindow
    {
        // field types: num (a number, or a formula saved as a string), text, choice, obj (self, an object or a prefab), aim
        // (optional: an object or prefab, or none), scene, key, sound, behkind (one of the behaviors that can rest, offered from
        // the object's own when it is known; Options = all of them), val (number OR expression, an expression saved as
        // "expr"), textvar / flagvar / colorvar / listvar (a text / Yes-No / colour / list variable, picked), numvar / localvar
        // (a number variable / this object's own one, picked), item (picked), color (a colour picker, or a colour variable as
        // "{name}"), bool (a checkbox, saved as true or left out). Every picked kind is saved as plain text, like a typed one.
        // Absent: what the game uses when a rule leaves the field out, when that isn't the default a NEW block gets — a rule
        // opened without it shows (and saves) that, so opening and saving never changes what it does.
        internal record Field(string Key, string Label, string Type, string Default = "", string[]? Options = null) { public string? Absent { get; init; } }
        // Needs: the behaviors that make this block useful (the object must have one of them for the block to be offered; an
        // existing rule always keeps its block). Null = every object.
        internal record BlockDef(string Id, string Label, Field[] Fields, string Hint = "") { public BehaviorKind[]? Needs { get; init; } }

        private static readonly string[] Ops = { ">=", ">", "<=", "<", "==", "!=" };
        private static readonly string[] TextOps = { "is", "isnot", "contains", "empty", "notempty" };
        private static readonly string[] ListOps = { "contains", "notcontains", "empty", "notempty", "countis", "countatleast" };
        private static readonly string[] Props = { "x", "y", "vx", "vy", "rot", "facing", "health", "w", "h", "speed", "opacity", "scale" };

        // the other keys the game reads for a field (a rule written by hand, by an older version or by the AI): a rule opened
        // in the blocks shows them in that field and saves them under its key — in the game's order of preference
        internal static readonly Dictionary<string, (string alias, string key)[]> KeyAliases = MakeAliases();

        private static Dictionary<string, (string, string)[]> MakeAliases()
        {
            var value = new[] { ("value", "to") };
            var amount = new[] { ("value", "amount"), ("by", "amount") };
            var text = new[] { ("text", "to"), ("value", "to") };
            var item = new[] { ("item", "value"), ("text", "value"), ("to", "value") };
            return new(StringComparer.OrdinalIgnoreCase)
            {
                ["after"] = new[] { ("every", "seconds") },
                ["shoot"] = new[] { ("aim", "at"), ("target", "at") },
                ["spawn"] = new[] { ("name", "object") },
                ["setvar"] = value, ["setlocal"] = value, ["set"] = value, ["tween"] = value,
                ["addvar"] = amount, ["addlocal"] = amount,
                ["settext"] = text, ["addtext"] = text, ["setcolor"] = text,
                ["tint"] = new[] { ("to", "color"), ("value", "color") },
                ["listadd"] = item, ["listremove"] = item,
                ["log"] = new[] { ("message", "text") },
            };
        }

        // the blocks, with their labels in the editor's language (ids, keys, types and values stay English: they are saved)
        private readonly BlockDef[] Events = MakeEvents(), Actions = MakeActions();

        internal static BlockDef[] MakeEvents() => new BlockDef[]
        {
            new("start",      L.T("When it spawns"), Array.Empty<Field>()),
            new("always",     L.T("Every frame (always)"), Array.Empty<Field>()),
            new("key",        L.T("While a key is held"), new[] { new Field("key", L.T("Key"), "key", "Space") }),
            new("keypress",   L.T("When a key is pressed"), new[] { new Field("key", L.T("Key"), "key", "Space"), new Field("cooldown", L.T("Cooldown (s)"), "num", "0.3") }, L.T("Fires once per press. Cooldown stops it repeating too fast while held.")),
            new("timer",      L.T("Every few seconds"), new[] { new Field("every", L.T("Seconds"), "num", "1") }, L.T("Repeats forever on this interval.")),
            new("after",      L.T("After some seconds"), new[] { new Field("seconds", L.T("Seconds"), "num", "5") { Absent = "1" } }, L.T("Fires once, that many seconds after it appears.")),
            new("touch",      L.T("When it touches an object"), new[] { new Field("name", L.T("Object"), "obj") }, L.T("Fires the moment it starts touching that object (once per touch).")),
            new("death",      L.T("When it dies"), Array.Empty<Field>()) { Needs = new[] { BehaviorKind.Health, BehaviorKind.Harvestable, BehaviorKind.BossAI } },
            new("hurt",       L.T("When it gets hurt"), Array.Empty<Field>(), L.T("Fires the moment its health goes down.")) { Needs = new[] { BehaviorKind.Health } },
            new("landed",     L.T("When it lands"), Array.Empty<Field>(), L.T("Fires once when it touches the ground after being in the air.")) { Needs = new[] { BehaviorKind.Platformer } },
            new("interact",   L.T("When someone interacts with it"), Array.Empty<Field>(), L.T("Fires when its Interact behavior is used (the key pressed near it).")) { Needs = new[] { BehaviorKind.Interact } },
            new("ifvar",      L.T("While a variable is…"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("op", L.T("is"), "choice", ">=", Ops), new Field("value", L.T("Value"), "num", "1") { Absent = "0" } }),
            new("iflocal",    L.T("While its own variable is…"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("op", L.T("is"), "choice", ">=", Ops), new Field("value", L.T("Value"), "num", "1") { Absent = "0" } }),
            new("hasitem",    L.T("While it has an item"), new[] { new Field("item", L.T("Item"), "item") }),
            new("iftext",     L.T("While a text variable…"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("op", L.T("is"), "choice", "is", TextOps), new Field("value", L.T("Text"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Compares words (capital letters don't matter). \"Its own\" reads this object's own text variable.")),
            new("iflist",     L.T("While a list…"), new[] { new Field("var", L.T("List"), "listvar"), new Field("op", L.T("is"), "choice", "contains", ListOps), new Field("value", L.T("Item or number"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Checks a list: has an item (capital letters don't matter), is empty, or has a number of items.")),
            new("isnight",    L.T("While it's night"), Array.Empty<Field>(), L.T("Needs a Day/Night cycle object in the scene. Great for enemies that only come out at night.")),
            new("isday",      L.T("While it's day"), Array.Empty<Field>(), L.T("Needs a Day/Night cycle object in the scene.")),
            new("wheelup",    L.T("When the mouse wheel scrolls up"), Array.Empty<Field>()),
            new("wheeldown",  L.T("When the mouse wheel scrolls down"), Array.Empty<Field>()),
            new("expr",       L.T("While a condition is true"), new[] { new Field("expr", L.T("Condition"), "text") }),
            new("click",      L.T("When clicked"), Array.Empty<Field>()),
            new("rightclick", L.T("When right-clicked"), Array.Empty<Field>()),
            new("mouseover",  L.T("While the mouse is over it"), Array.Empty<Field>()),
            new("mousedown",  L.T("While the mouse is pressed on it"), Array.Empty<Field>()),
            new("mousepress", L.T("When the mouse is clicked (anywhere)"), Array.Empty<Field>()),
        };

        private static readonly string[] TweenProps = { "x", "y", "opacity", "rot", "w", "h", "scale" };
        private static readonly string[] Eases = { "linear", "easein", "easeout", "easeinout" };
        internal static BlockDef[] MakeActions() => new BlockDef[]
        {
            new("velocity",  L.T("Move at a speed"), new[] { new Field("vx", L.T("→ sideways"), "num", "0"), new Field("vy", L.T("↓ up / down"), "num", "0") },
                L.T("Speed in each direction.  sideways: + = right, − = left.   up/down: + = down, − = up.   e.g. −250 sideways = move left.")),
            new("move",      L.T("Nudge (shift position)"), new[] { new Field("dx", L.T("→ sideways / s"), "num", "0"), new Field("dy", L.T("↓ up-down / s"), "num", "0") },
                L.T("Shifts its position a little each second, ignoring physics.")),
            new("accelerate",L.T("Speed up over time"), new[] { new Field("ax", L.T("→ sideways"), "num", "0"), new Field("ay", L.T("↓ up-down"), "num", "0") },
                L.T("Adds to its speed gradually (not instant). + up/down makes it fall faster.")),
            new("jump",      L.T("Jump"), new[] { new Field("force", L.T("Strength"), "num", "600") }, L.T("Only works while standing on the ground. Bigger = higher.")),
            new("rotate",    L.T("Spin"), new[] { new Field("speed", L.T("Degrees / s"), "num", "90") }, L.T("Rotates continuously. Negative spins the other way.")),
            new("shoot",     L.T("Shoot"), new[] { new Field("directions", L.T("Directions"), "num", "1") { Absent = "8" }, new Field("speed", L.T("Speed"), "num", "420"), new Field("life", L.T("Life (s)"), "num", "2"), new Field("bullet", L.T("Bullet"), "obj", "Bullet"), new Field("at", L.T("Aim at (optional)"), "aim") },
                L.T("Fires projectiles. 1 direction = straight ahead, or straight at the object it aims at; more = spread all around.")),
            new("spawn",     L.T("Spawn an object"), new[] { new Field("object", L.T("Object"), "obj") { Absent = "Enemy" }, new Field("at", L.T("From"), "choice", "", new[] { "", "top", "near" }) },
                L.T("From (empty) = at this object · top = falling from the top of the screen · near = on the ground, a little to the left or right.")),
            new("destroy",   L.T("Destroy itself"), Array.Empty<Field>()),
            new("hide",      L.T("Hide"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self") }, L.T("Makes it invisible — it is still there and still touches things. \"Show\" brings it back.")),
            new("show",      L.T("Show"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self") }, L.T("Makes a hidden object visible again.")),
            new("goscene",   L.T("Go to another scene"), new[] { new Field("scene", L.T("Scene"), "scene") }),
            new("setvar",    L.T("Set a variable"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("to", L.T("To"), "val", "0") }),
            new("addvar",    L.T("Add to a variable"), new[] { new Field("var", L.T("Variable"), "numvar"), new Field("amount", L.T("Amount"), "val", "1") }),
            new("setlocal",  L.T("Set its own variable"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("to", L.T("To"), "val", "0") }),
            new("addlocal",  L.T("Add to its own variable"), new[] { new Field("var", L.T("Variable"), "localvar"), new Field("amount", L.T("Amount"), "val", "1") }),
            new("settext",   L.T("Set a text"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("to", L.T("To"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Puts words in a text variable. {name} inside is replaced by that variable's value, e.g. Hello {playerName}!")),
            new("addtext",   L.T("Add to a text"), new[] { new Field("var", L.T("Text variable"), "textvar"), new Field("to", L.T("Words to add"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Adds words to the end of a text variable.")),
            new("toggle",    L.T("Switch a Yes/No"), new[] { new Field("var", L.T("Yes/No variable"), "flagvar"), new Field("local", L.T("Its own"), "bool") },
                L.T("Yes becomes No and No becomes Yes.")),
            new("setcolor",  L.T("Set a colour"), new[] { new Field("var", L.T("Colour variable"), "colorvar"), new Field("to", L.T("Colour"), "color", "#FFFFFF") { Absent = "" }, new Field("local", L.T("Its own"), "bool") },
                L.T("Puts a colour in a colour variable. Use it in \"Change the colour\" or in particles.")),
            new("tint",      L.T("Change the colour"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self"), new Field("color", L.T("Colour"), "color", "#FFFFFF") { Absent = "" } },
                L.T("Paints this object (or another one) with a colour or a colour variable. An object drawn with a picture keeps its picture.")),
            new("listadd",   L.T("Add to a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("value", L.T("Item"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Adds an item to the end of a list. {name} inside is replaced by that variable's value.")),
            new("listremove",L.T("Remove from a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("value", L.T("Item"), "text"), new Field("local", L.T("Its own"), "bool") },
                L.T("Removes the first item equal to it (capital letters don't matter).")),
            new("listclear", L.T("Empty a list"), new[] { new Field("var", L.T("List"), "listvar"), new Field("local", L.T("Its own"), "bool") }),
            new("set",      L.T("Set a property"), new[] { new Field("target", L.T("Object (or self)"), "obj", "self"), new Field("prop", L.T("Property"), "choice", "x", Props) { Absent = "" }, new Field("to", L.T("To"), "val", "0") }),
            new("additem",   L.T("Give an item"), new[] { new Field("item", L.T("Item"), "item"), new Field("amount", L.T("Amount"), "num", "1") }),
            new("removeitem",L.T("Remove an item"), new[] { new Field("item", L.T("Item"), "item"), new Field("amount", L.T("Amount"), "num", "1") }),
            new("boost",     L.T("Multiply its speed"), new[] { new Field("factor", L.T("Times"), "num", "2") }, L.T("2 = twice as fast, 0.5 = half. Affects Platformer/Top-down speed.")),
            new("setspeed",  L.T("Set its speed level"), new[] { new Field("factor", L.T("Level"), "num", "1") }, L.T("1 = normal, 0.5 = half, 2 = double. Resets the boost.")),
            new("say",       L.T("Show a message"), new[] { new Field("text", L.T("Text"), "text"), new Field("speaker", L.T("Speaker (optional)"), "text") }),
            new("emit",      L.T("Burst of particles"), new[] { new Field("count", L.T("How many"), "num", "16"), new Field("speed", L.T("Speed"), "num", "180"), new Field("spread", L.T("Spread (°)"), "num", "360"), new Field("angle", L.T("Angle (°)"), "num", "0"), new Field("life", L.T("Life (s)"), "num", "0.8"), new Field("size", L.T("Size"), "num", "8"), new Field("gravity", L.T("Gravity"), "num", "400"), new Field("color", L.T("Color"), "color", "#FFD24D") },
                L.T("A one-off puff of particles (sparks, dust, explosions).")),
            new("tween",     L.T("Glide a property"), new[] { new Field("prop", L.T("Property"), "choice", "x", TweenProps), new Field("to", L.T("To"), "val", "0"), new Field("duration", L.T("Over (s)"), "num", "0.5"), new Field("ease", L.T("Ease"), "choice", "linear", Eases), new Field("loop", L.T("Loop"), "bool"), new Field("yoyo", L.T("Back and forth (yoyo)"), "bool") },
                L.T("Smoothly animates a property to a value. Loop repeats; yoyo bounces back and forth.")),
            new("setvolume", L.T("Set volume"), new[] { new Field("channel", L.T("Channel"), "choice", "master", new[] { "master", "music", "sfx" }), new Field("to", L.T("Level (0-1)"), "num", "1") }),
            new("breaktile", L.T("Break a tile"), new[] { new Field("where", L.T("Which"), "choice", "touching", new[] { "touching", "below", "above", "left", "right", "front", "atmouse" }) },
                L.T("Breaks a tile of a breakable tilemap: any it overlaps (touching), the one below/above/beside/in front of it, or the one at the mouse.")),
            new("settime",   L.T("Set time of day"), new[] { new Field("time", L.T("Time"), "choice", "night", new[] { "day", "noon", "dusk", "night", "midnight" }) },
                L.T("Jumps the Day/Night cycle to that moment (needs a Day/Night cycle object in the scene).")),
            new("playsound", L.T("Play a sound"), new[] { new Field("sound", L.T("Sound"), "sound"), new Field("volume", L.T("Volume (0-1)"), "num", "0.8"), new Field("channel", L.T("Channel"), "choice", "sfx", new[] { "sfx", "music" }) },
                L.T("Plays a sound file from the project's Assets/sounds folder.")),
            new("log",       L.T("Write to the console"), new[] { new Field("text", L.T("Message"), "text"), new Field("level", L.T("Level"), "choice", "info", new[] { "info", "warn", "error" }) },
                L.T("Shows a line in the debug console (F1 while playing). Use {variable} to print a value, e.g. Coins: {coins}.")),
            new("save",      L.T("Save the game"), Array.Empty<Field>()),
            new("load",      L.T("Load the game"), Array.Empty<Field>()),
            // blocks for a behavior the object has (offered only when it has one of them)
            new("controls",  L.T("Turn its controls on or off"), new[] { new Field("on", L.T("Controls"), "choice", "off", OnOff) },
                L.T("Off: the arrow keys / WASD no longer move it (e.g. during a cutscene). On brings them back.")) { Needs = new[] { BehaviorKind.Platformer, BehaviorKind.TopDown } },
            new("ability",   L.T("Turn an ability on or off"), new[] { new Field("ability", L.T("Ability"), "choice", "doubleJump", new[] { "doubleJump", "wallJump", "dash" }), new Field("on", L.T("Turn"), "choice", "on", OnOff) },
                L.T("Gives or takes away double jump, wall jump or dash.")) { Needs = new[] { BehaviorKind.Platformer } },
            new("setjump",   L.T("Change its jump height"), new[] { new Field("jump", L.T("Jump strength"), "num", "800") },
                L.T("Bigger = higher jumps. The Platformer's usual strength is 640.")) { Needs = new[] { BehaviorKind.Platformer } },
            new("heal",      L.T("Heal"), new[] { new Field("amount", L.T("Amount"), "num", "1") },
                L.T("Gives back health, never above its maximum.")) { Needs = new[] { BehaviorKind.Health } },
            new("behavior",  L.T("Turn one of its behaviors on or off"), new[] { new Field("kind", L.T("Behavior"), "behkind", "Patrol", SwitchableKinds) { Absent = "Patrol" }, new Field("on", L.T("Turn"), "choice", "off", OnOff) },
                L.T("Off: that behavior rests (a mover stands still) until it is turned on again.")) { Needs = PlayWindow.OnlyWhileKinds.ToArray() },
        };

        private static readonly string[] OnOff = { "on", "off" };
        // the behaviors the "behavior" block can switch (the ones that can rest: PlayWindow.OnlyWhileKinds)
        internal static readonly string[] SwitchableKinds = PlayWindow.OnlyWhileKinds.Select(k => k.ToString()).ToArray();

        // a behavior kind as the inspector names it ("🏃 Platformer Character"); the saved value is the kind's name
        private static string KindLabel(string kind)
            => Enum.TryParse<BehaviorKind>(kind, out var k) && BehaviorCatalog.All.Find(b => b.Kind == k) is { } info ? info.Icon + "  " + L.T(info.Name) : kind;

        // what a choice option shows (the value is what's saved; symbols and property names show as they are)
        private static string OptionLabel(string o) => o switch
        {
            "" => L.T("(none)"),
            "top" => L.T("top"), "near" => L.T("near"),
            "touching" => L.T("touching"), "below" => L.T("below"), "above" => L.T("above"), "left" => L.T("left"), "right" => L.T("right"),
            "front" => L.T("front"), "atmouse" => L.T("at the mouse"),
            "day" => L.T("day"), "noon" => L.T("noon"), "dusk" => L.T("dusk"), "night" => L.T("night"), "midnight" => L.T("midnight"),
            "info" => L.T("info"), "warn" => L.T("warn"), "error" => L.T("error"),
            "is" => L.T("is"), "isnot" => L.T("is not"), "contains" => L.T("contains"), "empty" => L.T("is empty"), "notempty" => L.T("is not empty"),
            "notcontains" => L.T("doesn't contain"), "countis" => L.T("has exactly (items)"), "countatleast" => L.T("has at least (items)"),
            "master" => L.T("All sound"), "music" => L.T("Music"), "sfx" => L.T("Sound effects"),
            "linear" => L.T("linear"), "easein" => L.T("ease in"), "easeout" => L.T("ease out"), "easeinout" => L.T("ease in and out"),
            "on" => L.T("on"), "off" => L.T("off"), "doubleJump" => L.T("double jump"), "wallJump" => L.T("wall jump"), "dash" => L.T("dash"),
            _ => o,
        };

        /// <summary>The rule language in English, one short line per block, written from the palette (for the AI prompts):
        /// e.g. <c>tween(prop=x|y|opacity|rot|w|h|scale, to, duration=0.5, ease=linear|easein|easeout|easeinout, loop, yoyo) — glide a property</c>.</summary>
        internal static IReadOnlyList<(string id, bool isEvent, string doc)> RuleDocs() => _ruleDocs ??= L.InEnglish(() =>
            MakeEvents().Select(d => (d.Id, true, RuleDoc(d))).Concat(MakeActions().Select(d => (d.Id, false, RuleDoc(d)))).ToList());
        private static List<(string id, bool isEvent, string doc)>? _ruleDocs; // the same in every language: made once

        private static string RuleDoc(BlockDef d)
        {
            var fields = d.Fields.Select(f => f.Key + (f.Type switch
            {
                "choice" => "=" + string.Join("|", (f.Options ?? Array.Empty<string>()).Where(o => o.Length > 0)),
                "num" or "obj" when (f.Absent ?? f.Default).Length > 0 => "=" + (f.Absent ?? f.Default), // what leaving it out means
                "behkind" => "=<one of its behaviors: Patrol, Chase, Shooter…>",
                _ => "",
            }));
            var meaning = d.Label.TrimEnd('…');
            return d.Id + (d.Fields.Length > 0 ? "(" + string.Join(", ", fields) + ")" : "") + " — " + char.ToLowerInvariant(meaning[0]) + meaning[1..];
        }

        /// <summary>Text written in a number field that reads as a formula (a number, an operator, a function, "self.x"…),
        /// not as a stray word ("high") — an AI's word there is left out, so the field keeps its default.</summary>
        internal static bool LooksLikeFormula(string s) => s.IndexOfAny(FormulaChars) >= 0;
        private static readonly char[] FormulaChars = "0123456789()+-*/%<>.".ToCharArray();
    }
}
