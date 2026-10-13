using System.Collections.Generic;
using System.Linq;

namespace EasyEngine
{
    // Whole answers to common requests. The Build prompt shows the two closest to what was asked (by shared word stems,
    // like the hints): small models copy what they see, so what they see should already be nearly right; big ones just get
    // the exact shape. Names here (Box, Enemy, Level2…) are placeholders the model replaces with the scene's own.
    // After " | " come more words that find the example (not shown): "make the Box red | color blue…" also answers "make it blue".
    public partial class MainWindow
    {
        private static readonly (string ask, string json)[] Examples =
        {
            ("make the player run faster", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"speed\":400}}]}]}"),
            ("make the player jump higher", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"jump\":800}}]}]}"),
            ("give the player 5 hearts", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":5,\"respawn\":1}}]}]}"),
            ("let the player double jump and dash", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Platformer\",\"nums\":{\"doubleJump\":1,\"dash\":1}}]}]}"),
            ("give the player a sword attack", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"ToolUse\",\"texts\":{\"tool\":\"sword\"}}]}]}"),
            ("make the Box red | color colour blue green yellow purple black white orange pink paint", "{\"edit\":[{\"name\":\"Box\",\"color\":\"#E5484B\"}]}"),
            ("make the Box twice as big | size bigger smaller larger half", "{\"edit\":[{\"name\":\"Box\",\"width\":\"self.w * 2\",\"height\":\"self.h * 2\"}]}"),
            ("make the Box invisible | hide hidden transparent", "{\"edit\":[{\"name\":\"Box\",\"opacity\":0}]}"),
            ("remove the health from the Box", "{\"edit\":[{\"name\":\"Box\",\"remove\":[\"Health\"]}]}"),
            ("don't let the player leave the screen | walk out edge inside stay", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"StayInside\",\"texts\":{\"area\":\"screen\"}}]}]}"),
            ("when I press T the Box teleports to x 300", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"keypress\",\"key\":\"T\",\"do\":\"set\",\"prop\":\"x\",\"to\":300}]}]}"),
            ("create a ball that keeps moving left | right moves always direction", "{\"create\":[{\"name\":\"Ball\",\"x\":700,\"y\":300,\"width\":32,\"height\":32,\"color\":\"#4DA3FF\",\"custom\":[{\"when\":\"always\",\"do\":\"velocity\",\"vx\":-150,\"vy\":0}]}]}"),
            ("make the Box follow the mouse", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"x\",\"to\":\"mousex - self.w/2\"},{\"when\":\"always\",\"do\":\"set\",\"prop\":\"y\",\"to\":\"mousey - self.h/2\"}]}]}"),
            ("make the Box spin while I hold Q | rotate turn", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"key\",\"key\":\"Q\",\"do\":\"rotate\",\"speed\":180}]}]}"),
            ("make the Box move in a circle", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"-sin(time*2) * 200\"},{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vy\",\"to\":\"cos(time*2) * 200\"}]}]}"),
            ("make a ball that bounces | bouncy bounce", "{\"create\":[{\"name\":\"Ball\",\"x\":400,\"y\":100,\"width\":32,\"height\":32,\"color\":\"#FF8A3D\",\"physics\":{\"mode\":\"Dynamic\",\"bounciness\":0.8}}]}"),
            ("put a wall on the left side of the screen | right side solid", "{\"create\":[{\"name\":\"Wall\",\"x\":0,\"y\":100,\"width\":32,\"height\":360,\"color\":\"#4A4A58\",\"physics\":\"Static\",\"behaviors\":[{\"kind\":\"Solid\"}]}]}"),
            ("after 3 seconds the Box slowly fades away", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"after\",\"seconds\":3,\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":2}]}]}"),
            ("make the Box blink", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"opacity\",\"to\":0,\"duration\":0.5,\"loop\":1,\"yoyo\":1}]}]}"),
            ("when the game starts the Box grows to 3 times its size in 1 second", "{\"edit\":[{\"name\":\"Box\",\"custom\":[{\"when\":\"start\",\"do\":\"tween\",\"prop\":\"scale\",\"to\":3,\"duration\":1}]}]}"),
            ("every 3 seconds the Box jumps", "{\"edit\":[{\"name\":\"Box\",\"physics\":\"Dynamic\",\"custom\":[{\"when\":\"timer\",\"every\":3,\"do\":\"jump\",\"force\":600}]}]}"),
            ("a 30 second countdown shown on screen | timer clock time left", "{\"variables\":{\"scene\":{\"timer\":30}},\"create\":[{\"name\":\"TimerText\",\"layer\":\"UI\",\"x\":20,\"y\":16,\"width\":220,\"height\":32,\"behaviors\":[{\"kind\":\"Label\",\"texts\":{\"text\":\"Time: {timer}\"}}],\"custom\":[{\"when\":\"timer\",\"every\":1,\"cond\":\"timer > 0\",\"do\":\"addvar\",\"var\":\"timer\",\"amount\":-1}]}]}"),
            ("after 5 seconds go to Level3", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"after\",\"seconds\":5,\"do\":\"goscene\",\"scene\":\"Level3\"}]}]}"),
            ("make rocks fall from the sky every 2 seconds | rain drop falling", "{\"create\":[{\"name\":\"Rock\",\"width\":32,\"height\":32,\"color\":\"#8A7A6A\",\"physics\":\"Dynamic\",\"asPrefab\":true},{\"name\":\"RockSpawner\",\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"behaviors\":[{\"kind\":\"Spawner\",\"texts\":{\"target\":\"Rock\",\"at\":\"top\",\"key\":\"\"},\"nums\":{\"interval\":2,\"max\":0}}]}]}"),
            ("spawn a slime every 4 seconds | enemies enemy appear wave", "{\"create\":[{\"name\":\"Slime\",\"width\":40,\"height\":32,\"color\":\"#5BD16B\",\"physics\":\"Dynamic\",\"asPrefab\":true,\"behaviors\":[{\"kind\":\"Patrol\"},{\"kind\":\"Health\",\"nums\":{\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]},{\"name\":\"SlimeSpawner\",\"x\":800,\"y\":380,\"width\":10,\"height\":10,\"behaviors\":[{\"kind\":\"Spawner\",\"texts\":{\"target\":\"Slime\",\"key\":\"\"},\"nums\":{\"interval\":4,\"max\":0}}]}]}"),
            ("after 8 seconds a Dragon appears", "{\"create\":[{\"name\":\"Dragon\",\"width\":96,\"height\":96,\"color\":\"#B03A2E\",\"physics\":\"Dynamic\",\"asPrefab\":true,\"behaviors\":[{\"kind\":\"Health\",\"nums\":{\"max\":10,\"respawn\":0}}]}],\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"after\",\"seconds\":8,\"do\":\"spawn\",\"object\":\"Dragon\",\"at\":\"near\"}]}]}"),
            ("give the player a flashlight | lanterna beam cone headlight spotlight", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"LightSource\",\"texts\":{\"shape\":\"beam\",\"direction\":\"facing\",\"color\":\"#FFF2C0\"},\"nums\":{\"radius\":320,\"spread\":40}}]}]}"),
            ("add lava that hurts the player | spike spikes trap hazard damage", "{\"create\":[{\"name\":\"Lava\",\"x\":500,\"y\":440,\"width\":120,\"height\":20,\"color\":\"#FF5500\",\"behaviors\":[{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]}]}"),
            ("create an enemy that walks back and forth", "{\"create\":[{\"name\":\"Enemy\",\"x\":600,\"y\":380,\"width\":40,\"height\":48,\"color\":\"#E5484B\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Patrol\",\"nums\":{\"distance\":200}},{\"kind\":\"Health\",\"nums\":{\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]}]}"),
            ("create an enemy that chases the player | follows hunts monster", "{\"create\":[{\"name\":\"Enemy\",\"x\":700,\"y\":380,\"width\":40,\"height\":48,\"color\":\"#E5484B\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}},{\"kind\":\"Health\",\"nums\":{\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]}]}"),
            ("make the Ghost run away from the player | flee escape", "{\"edit\":[{\"name\":\"Ghost\",\"custom\":[{\"when\":\"always\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"sign(self.cx - Player.cx) * 160\"}]}]}"),
            ("make a turret that shoots at the player every 2 seconds | gun cannon fire bullets", "{\"create\":[{\"name\":\"Turret\",\"x\":750,\"y\":380,\"width\":40,\"height\":40,\"color\":\"#777788\",\"behaviors\":[{\"kind\":\"Shooter\",\"texts\":{\"key\":\"\",\"aim\":\"Player\"},\"nums\":{\"directions\":1,\"cooldown\":2,\"speed\":300}}]}]}"),
            ("when the Enemy dies it explodes | particles explosion", "{\"edit\":[{\"name\":\"Enemy\",\"custom\":[{\"when\":\"death\",\"do\":\"emit\",\"count\":30,\"color\":\"#FF8A3D\"}]}]}"),
            ("when the player touches the Slime, the Slime turns green", "{\"edit\":[{\"name\":\"Slime\",\"custom\":[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"tint\",\"color\":\"#3BC97A\"}]}]}"),
            ("delete the Rock", "{\"delete\":[\"Rock\"]}"),
            ("create a boss with 15 health that chases the player", "{\"create\":[{\"name\":\"Boss\",\"x\":700,\"y\":330,\"width\":96,\"height\":96,\"color\":\"#8E44AD\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Chase\",\"texts\":{\"target\":\"Player\"}},{\"kind\":\"Health\",\"nums\":{\"max\":15,\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]}]}"),
            ("make a flying boss that shoots", "{\"create\":[{\"name\":\"Boss\",\"x\":700,\"y\":120,\"width\":96,\"height\":96,\"color\":\"#8E44AD\",\"behaviors\":[{\"kind\":\"BossAI\",\"texts\":{\"moves\":\"fly\",\"starts\":\"start\"}},{\"kind\":\"Health\",\"nums\":{\"max\":20,\"respawn\":0}},{\"kind\":\"Damager\",\"texts\":{\"target\":\"Player\"}}]}]}"),
            ("when the Boss dies, go to Level3", "{\"edit\":[{\"name\":\"Boss\",\"custom\":[{\"when\":\"death\",\"do\":\"goscene\",\"scene\":\"Level3\"}]}]}"),
            ("when the player dies, go to the Lose scene", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"death\",\"do\":\"goscene\",\"scene\":\"Lose\"}]}]}"),
            ("create a global variable gold at 0 and lives at 3", "{\"variables\":{\"global\":{\"gold\":0,\"lives\":3}}}"),
            ("show the gold at the top of the screen | score points hud display label text", "{\"create\":[{\"name\":\"GoldText\",\"layer\":\"UI\",\"x\":20,\"y\":16,\"width\":220,\"height\":32,\"behaviors\":[{\"kind\":\"Label\",\"texts\":{\"text\":\"Gold: {gold}\",\"align\":\"left\"}}]}]}"),
            ("when gold reaches 20 go to Level3", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"ifvar\",\"var\":\"gold\",\"op\":\">=\",\"value\":20,\"do\":\"goscene\",\"scene\":\"Level3\"}]}]}"),
            ("make a gem that gives 5 points | coin collect pickup score", "{\"create\":[{\"name\":\"Gem\",\"x\":500,\"y\":400,\"width\":24,\"height\":24,\"color\":\"#4DE1FF\",\"behaviors\":[{\"kind\":\"Collectible\",\"texts\":{\"var\":\"score\"},\"nums\":{\"points\":5}}]}]}"),
            ("when I click the Button, add 1 to clicks", "{\"edit\":[{\"name\":\"Button\",\"custom\":[{\"when\":\"click\",\"do\":\"addvar\",\"var\":\"clicks\",\"amount\":1}]}]}"),
            ("add a health bar | life bar", "{\"create\":[{\"name\":\"HealthBar\",\"layer\":\"UI\",\"x\":20,\"y\":20,\"width\":200,\"height\":20,\"behaviors\":[{\"kind\":\"Bar\",\"texts\":{\"var\":\"health\"}}]}]}"),
            ("make a Play button that goes to Level3", "{\"create\":[{\"name\":\"PlayButton\",\"layer\":\"UI\",\"x\":380,\"y\":240,\"width\":200,\"height\":60,\"behaviors\":[{\"kind\":\"UIButton\",\"texts\":{\"action\":\"goscene\",\"scene\":\"Level3\"}},{\"kind\":\"Label\",\"texts\":{\"text\":\"Play\"}}]}]}"),
            ("when the level starts the player says 'Let's go!'", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"start\",\"do\":\"say\",\"text\":\"Let's go!\"}]}]}"),
            ("when the player touches the Chest, show 'Locked'", "{\"edit\":[{\"name\":\"Chest\",\"custom\":[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"say\",\"text\":\"Locked\"}]}]}"),
            ("create an NPC that talks when I press E | sign read dialog", "{\"create\":[{\"name\":\"NPC\",\"x\":300,\"y\":396,\"width\":40,\"height\":64,\"color\":\"#C08457\",\"behaviors\":[{\"kind\":\"Interact\",\"texts\":{\"text\":\"Hello, traveler!\"}}]}]}"),
            ("create a door that goes to Level3 | exit next level portal", "{\"create\":[{\"name\":\"Door\",\"x\":850,\"y\":364,\"width\":56,\"height\":96,\"color\":\"#8B5A2B\",\"behaviors\":[{\"kind\":\"GoToScene\",\"texts\":{\"scene\":\"Level3\"}}]}]}"),
            ("make a platform that moves left and right | moving elevator lift", "{\"create\":[{\"name\":\"MovingPlatform\",\"x\":300,\"y\":340,\"width\":140,\"height\":20,\"color\":\"#6B5B45\",\"physics\":\"Static\",\"behaviors\":[{\"kind\":\"Solid\"},{\"kind\":\"Patrol\",\"nums\":{\"axis\":0,\"distance\":200}}]}]}"),
            ("add a trampoline", "{\"create\":[{\"name\":\"Trampoline\",\"x\":400,\"y\":440,\"width\":64,\"height\":20,\"color\":\"#3BC97A\",\"behaviors\":[{\"kind\":\"Spring\"}]}]}"),
            ("add a crate I can push | block pushable", "{\"create\":[{\"name\":\"Crate\",\"x\":400,\"y\":412,\"width\":48,\"height\":48,\"color\":\"#A0763C\",\"physics\":\"Dynamic\",\"behaviors\":[{\"kind\":\"Pushable\"}]}]}"),
            ("add a checkpoint", "{\"create\":[{\"name\":\"Checkpoint\",\"x\":480,\"y\":396,\"width\":24,\"height\":64,\"color\":\"#3BC97A\",\"behaviors\":[{\"kind\":\"Checkpoint\"}]}]}"),
            ("a key that opens the Gate | locked door lock", "{\"create\":[{\"name\":\"Key\",\"x\":300,\"y\":400,\"width\":24,\"height\":24,\"color\":\"#FFD700\",\"behaviors\":[{\"kind\":\"AbilityPickup\",\"texts\":{\"var\":\"hasKey\"}}]},{\"name\":\"Gate\",\"x\":700,\"y\":364,\"width\":32,\"height\":96,\"color\":\"#555566\",\"behaviors\":[{\"kind\":\"Gate\",\"texts\":{\"var\":\"hasKey\"}}]}]}"),
            ("add a day and night cycle | sun moon", "{\"create\":[{\"name\":\"Sky\",\"x\":0,\"y\":0,\"width\":10,\"height\":10,\"behaviors\":[{\"kind\":\"DayNight\"}]}]}"),
            ("a coin that disappears when the player touches it | collect pickup", "{\"create\":[{\"name\":\"Coin\",\"x\":500,\"y\":400,\"width\":24,\"height\":24,\"color\":\"#FFD700\",\"behaviors\":[{\"kind\":\"Collectible\"}]}]}"),
            ("build a level with 2 platforms, 3 coins and an enemy", "{\"level\":{\"platforms\":2,\"coins\":3,\"enemies\":1}}"),
            ("make a top-down room with walls", "{\"level\":{\"style\":\"topdown\"}}"),
            ("make stairs of 4 steps | staircase", "{\"level\":{\"stairs\":4}}"),
            ("put 8 coins in a row", "{\"level\":{\"coins\":8}}"),
            ("when I press E near the Merchant with 5 gold, pay 5 gold and get a Potion | shop buy sell shopkeeper", "{\"edit\":[{\"name\":\"Merchant\",\"custom\":[{\"when\":\"keynear\",\"key\":\"E\",\"name\":\"Player\",\"radius\":100,\"cond\":\"gold >= 5\",\"do\":\"additem\",\"item\":\"Potion\",\"amount\":1},{\"when\":\"keynear\",\"key\":\"E\",\"name\":\"Player\",\"radius\":100,\"cond\":\"gold >= 5\",\"do\":\"addvar\",\"var\":\"gold\",\"amount\":-5}]}]}"),
            ("the player has 3 lives shown on screen; touching the Lava takes one and sends him back to the start; when they run out go to GameOver | lives vidas tries life lose", "{\"edit\":[{\"name\":\"Player\",\"behaviors\":[{\"kind\":\"Lives\",\"nums\":{\"lives\":3},\"texts\":{\"hazard\":\"Lava\",\"back\":\"start\",\"scene\":\"GameOver\"}}]}]}"),
            ("when all the coins are collected go to Level3 | all todas todos collected none left gems stars", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"allgone\",\"name\":\"Coin\",\"do\":\"goscene\",\"scene\":\"Level3\"}]}]}"),
            ("every time I press J add 1 to combo; if J isn't pressed for 1 second combo goes back to 0; at 3 say 'Combo!' and start over | combo idle reset", "{\"edit\":[{\"name\":\"Player\",\"custom\":[{\"when\":\"keypress\",\"key\":\"J\",\"cooldown\":0.1,\"do\":\"addvar\",\"var\":\"combo\",\"amount\":1},{\"when\":\"keyidle\",\"key\":\"J\",\"seconds\":1,\"do\":\"setvar\",\"var\":\"combo\",\"to\":0},{\"when\":\"becomes\",\"var\":\"combo\",\"op\":\">=\",\"value\":3,\"do\":\"say\",\"text\":\"Combo!\"},{\"when\":\"becomes\",\"var\":\"combo\",\"op\":\">=\",\"value\":3,\"do\":\"setvar\",\"var\":\"combo\",\"to\":0}]}]}"),
            ("when the player touches the Heart he gets 1 health back and the Heart disappears | heal potion cura vida", "{\"edit\":[{\"name\":\"Heart\",\"custom\":[{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"heal\",\"target\":\"Player\",\"amount\":1},{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"destroy\"}]}]}"),
            ("the Platform starts moving up and down only after the player steps on it | stands begins moving platform", "{\"edit\":[{\"name\":\"Platform\",\"behaviors\":[{\"kind\":\"Patrol\",\"nums\":{\"axis\":1,\"distance\":120}}],\"custom\":[{\"when\":\"start\",\"do\":\"behavior\",\"kind\":\"Patrol\",\"on\":\"off\"},{\"when\":\"touch\",\"name\":\"Player\",\"do\":\"behavior\",\"kind\":\"Patrol\",\"on\":\"on\"}]}]}"),
            ("the Wolf chases the player only when it is closer than 200 pixels", "{\"edit\":[{\"name\":\"Wolf\",\"custom\":[{\"when\":\"always\",\"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) < 200\",\"do\":\"set\",\"prop\":\"vx\",\"to\":\"sign(Player.cx - self.cx) * 150\"},{\"when\":\"always\",\"cond\":\"dist(self.cx,self.cy,Player.cx,Player.cy) >= 200\",\"do\":\"set\",\"prop\":\"vx\",\"to\":0}]}]}"),
        };

        internal static string ExamplesText => string.Join("\n", Examples.Select(e => e.json)); // (tests: their rules must survive the blocks)

        /// <summary>The two whole examples closest to the request (none when nothing is close: one common word like
        /// "player" isn't enough — a wrong example is worse than none, small models copy it).</summary>
        internal static List<(string ask, string json)> ExamplesFor(string request, int count = 2)
            => Match(_exampleWords ??= Examples.Select(e => Words(e.ask)).ToArray(), request)
               .Where(x => x.score >= 2.5).OrderByDescending(x => x.score).ThenBy(x => x.i).Take(count)
               .Reverse() // the closest LAST, right before the scene and the request: small models copy what they read last
               .Select(x => (Examples[x.i].ask.Split(" | ")[0], Examples[x.i].json)).ToList();
        private static HashSet<string>[]? _exampleWords;
    }
}
