// Every number that shapes how Graveway plays lives in this file, ported
// from graveway/src/config.js. Change a value and press Play.
//
// Coordinates are the web version's, number for number: the road runs up
// -z, away from the camera; the survivor starts at z = START_PZ (2) and
// distance is how far up the road (toward -z) they've got. Unity draws the
// same numbers as the mirror image of the web version's street, which
// changes nothing about how it plays; only the joystick and keys account
// for it (StickInput.Move).

using System.Collections.Generic;
using UnityEngine;

namespace Graveway
{
    public static class Hex
    {
        public static Color C(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }

    // ── Weapons ──────────────────────────────────────────────────────────
    // damage per bullet/pellet (grenades: at the centre of the blast);
    // fireRate shots per second; spread aim wobble in radians either side;
    // range how far the auto-aim reaches; pierce extra zombies a bullet
    // passes through; recoil: sustained fire widens spread by `per` each
    // shot up to `max`, recovering at `recover` radians a second.
    public struct Recoil
    {
        public float per, max, recover;
        public Recoil(float per, float max, float recover) { this.per = per; this.max = max; this.recover = recover; }
    }

    public sealed class Blast
    {
        public float radius, impulse;
        public Blast(float radius, float impulse) { this.radius = radius; this.impulse = impulse; }
        public Blast Clone() => new Blast(radius, impulse);
    }

    public sealed class WeaponDef
    {
        public string id, name, model, blurb;
        public bool grenade;                 // lobbed and explodes, instead of bullets
        public int price;
        public float damage, fireRate, spread;
        public int ammoCapacity;
        public float reloadTime, range, minRange;
        public int pellets = 1, pierce;
        public float knockback, shake;
        public Recoil recoil;
        public Blast blast;
        public string[] slots;               // which part slots it takes
    }

    public sealed class Perk
    {
        public string name, text;
        public float reloadMult = 1, startArmor, explosionResist, magnetBonus, coinMult = 1;
    }

    public sealed class Look
    {
        public Color skin, shirt, pants, hatColor;
        public string hat;                   // cap, helmet, goggles, hood
        public float bulk = 1;
    }

    public sealed class CharacterDef
    {
        public string id, name, role, blurb, startingWeapon;
        public int price;                    // coins to unlock them; 0 for the one you start with
        public float baseHealth, moveSpeed;
        public Perk perk;
        public Look look;
    }

    public sealed class ZombieKind
    {
        public string id;
        public float hp, speedMin, speedMax, damage, attackRate, reach, radius, scale, aggro;
        public int coins;
        public Color eyes;
        public bool smashes;                 // beats down what's in its way
    }

    // Gun parts: one per slot, bought per gun. Multipliers unless noted
    // (1.2 = +20%). spread: lower is more accurate. recoil: kick per shot.
    // stability: how quickly aim settles and how far it can wander.
    // pierce: added. pellets: set (slugs fire 1).
    public sealed class PartMods
    {
        public float damage = 1, fireRate = 1, range = 1, ammoCapacity = 1, reloadTime = 1, spread = 1, knockback = 1;
        public float recoil = 1, stability = 1, blast = 1, impulse = 1;
        public int pierce, pellets;
    }

    public sealed class PartFx
    {
        public float fireDps, fireTime;      // sets what it hits alight (dps as a share of the hit)
        public float big = 1, small = 1, props = 1;
        public bool quiet, laser, light, noPierce;
        public int cluster;
    }

    public sealed class PartDef
    {
        public string id, name, blurb;
        public int price;
        public PartMods mods = new PartMods();
        public PartFx fx;
        public string[] fits, not;
    }

    // What a blast does: radius in metres, damage at its centre, and how hard
    // it throws things.
    public sealed class Explosion
    {
        public float radius, damage, impulse;
        public Explosion(float radius, float damage, float impulse) { this.radius = radius; this.damage = damage; this.impulse = impulse; }
    }

    // Something from the Black Market, called in during a run from the
    // backpack. delay: how long you hear it coming before it arrives.
    public sealed class ItemDef
    {
        public string id, name, blurb;
        public int price;
        public float delay;
    }

    public sealed class PowerUpDef
    {
        public string id, name, tag;
        public Color color;
        public float weight, duration;       // duration 0: not timed
        public int grenades;                 // Frag Barrage
        public float range, damage, radius, impulse, fireRateMult = 1, damageMult = 1;
        public Blast blast;
    }

    public sealed class PlayerUpgradeDef
    {
        public string id, label, unit;
        public int max, baseCost;
        public float step;
    }

    // A survivor's numbers for a run, with their perk and Armory upgrades.
    public sealed class PlayerStats
    {
        public float maxHp, speed, magnet, radius, startArmor, explosionResist, coinMult;
        public Perk perk;
    }

    // Props you can wreck (see PROPS in config.js). hp 0 here means
    // indestructible (the web's null); mass 0 means bolted down.
    public sealed class PropDef
    {
        public float hp, mass, smokeAt, fireAt, crush, bulletMult = 1, blastMult = 1;
        public string material;
        public bool blocksBullets = true, burns, rubble, topples, collapse;
        public Explosion explodes;
    }

    public static class Tunables
    {
        public const string GameName = "Graveway";

        // ── Weapons ──────────────────────────────────────────────────────
        static readonly string[] AllSlots = { "barrel", "muzzle", "mag", "sight", "stock", "trigger", "grip", "rail", "ammo" };
        static string[] Without(params string[] drop) => System.Array.FindAll(AllSlots, s => System.Array.IndexOf(drop, s) < 0);

        public static readonly WeaponDef[] Weapons =
        {
            new WeaponDef { id = "pistol", name = "Pistol", model = "pistol", blurb = "Steady and forgiving. Never empty for long.",
                price = 0, damage = 22, fireRate = 3.2f, spread = 0.03f, ammoCapacity = 12, reloadTime = 1.0f,
                range = 17, knockback = 1.2f, shake = 0.04f, recoil = new Recoil(0.008f, 0.03f, 0.4f), slots = Without("stock") },
            new WeaponDef { id = "smg", name = "SMG", model = "smg", blurb = "Sprays lead. Melts walkers, struggles with tanks.",
                price = 350, damage = 13, fireRate = 11, spread = 0.08f, ammoCapacity = 32, reloadTime = 1.5f,
                range = 15, knockback = 0.6f, shake = 0.03f, recoil = new Recoil(0.006f, 0.06f, 0.3f), slots = AllSlots },
            new WeaponDef { id = "shotgun", name = "Shotgun", model = "shotgun", blurb = "Seven pellets and a big shove. Short range.",
                price = 750, damage = 16, fireRate = 1.3f, spread = 0.30f, ammoCapacity = 6, reloadTime = 2.0f,
                range = 11, pellets = 7, knockback = 3.0f, shake = 0.16f, recoil = new Recoil(0.03f, 0.05f, 0.5f), slots = AllSlots },
            new WeaponDef { id = "rifle", name = "Assault Rifle", model = "rifle",
                blurb = "Fast and far-reaching, and it punches through a second zombie. Kicks the longer you hold it.",
                price = 1500, damage = 24, fireRate = 9, spread = 0.02f, ammoCapacity = 30, reloadTime = 1.7f,
                range = 21, pierce = 1, knockback = 1.2f, shake = 0.06f, recoil = new Recoil(0.014f, 0.16f, 0.35f), slots = AllSlots },
            new WeaponDef { id = "launcher", name = "Grenade Launcher", model = "launcher", grenade = true,
                blurb = "Lobs grenades that blow apart crowds, barrels, barricades and walls.",
                price = 2200, damage = 120, fireRate = 1.0f, spread = 0.02f, ammoCapacity = 4, reloadTime = 2.4f,
                range = 19, minRange = 4, shake = 0.14f, blast = new Blast(3.4f, 10), recoil = new Recoil(0.01f, 0.02f, 0.3f),
                slots = Without("muzzle") },
            new WeaponDef { id = "minigun", name = "Minigun", model = "minigun", blurb = "A wall of bullets. Long, loud reloads.",
                price = 3200, damage = 17, fireRate = 20, spread = 0.10f, ammoCapacity = 160, reloadTime = 3.2f,
                range = 18, knockback = 0.8f, shake = 0.03f, recoil = new Recoil(0.002f, 0.05f, 0.2f), slots = Without("muzzle", "stock") },
        };

        // How many guns you take into a run, counting your survivor's own.
        public const int LoadoutSlots = 4;

        // ── The Black Market (playtest note 4) ───────────────────────────
        // A shop of its own, apart from the Armory: things to call in during
        // a run, bought with coins and carried in a backpack. None arrive at
        // once: you hear the jet or the helicopter coming first.
        public const int BackpackSize = 6;
        public static readonly ItemDef[] Items =
        {
            new ItemDef { id = "strafe", name = "Strafing run", price = 120, delay = 3,
                blurb = "A jet rakes the road ahead with cannon fire, 25 m of it." },
            new ItemDef { id = "cluster", name = "Cluster strike", price = 150, delay = 3,
                blurb = "A dozen bomblets scattered across the road ahead." },
            new ItemDef { id = "napalm", name = "Incendiary strike", price = 180, delay = 3,
                blurb = "Sets a strip of road ahead burning for 8 seconds." },
            new ItemDef { id = "backup", name = "Backup survivor", price = 200, delay = 4,
                blurb = "Another survivor joins you with a random gun, until they fall." },
            new ItemDef { id = "heli", name = "Helicopter", price = 250, delay = 4,
                blurb = "Hovers over you and guns down zombies for 30 seconds." },
            new ItemDef { id = "skipboss", name = "Skip the boss", price = 300, delay = 0,
                blurb = "The next boss never turns up." },
        };

        // What the items do when they get there.
        public static class Support
        {
            public const int StrafeHits = 20;                  // cannon hits in a line up the road
            public const float StrafeAhead = 6, StrafeLength = 25, StrafeRadius = 1.3f, StrafeDamage = 70, StrafePropDamage = 40;
            public const int ClusterBombs = 12;                // scattered round a point ahead
            public const float ClusterAhead = 16, ClusterWidth = 5.5f, ClusterDepth = 7;
            public static readonly Explosion ClusterBlast = new Explosion(2.6f, 140, 9);
            public const float NapalmAhead = 14, NapalmDepth = 6, NapalmTime = 8, NapalmDps = 30, NapalmBurn = 3;
            public const float HeliTime = 30, HeliRange = 22, HeliDamage = 22, HeliRate = 9;   // seconds, metres, per shot, shots a second
            public const int MaxBackup = 3;
            public const float BackupHp = 100;
        }

        // ── Gun parts ────────────────────────────────────────────────────
        public static readonly Dictionary<string, string> PartSlots = new Dictionary<string, string>
        {
            { "barrel", "Barrel" }, { "muzzle", "Muzzle" }, { "mag", "Magazine" }, { "sight", "Sight" }, { "stock", "Stock" },
            { "trigger", "Trigger" }, { "grip", "Grip" }, { "rail", "Handguard" }, { "ammo", "Ammo" },
        };
        static readonly string[] Bullets = { "pistol", "smg", "rifle", "minigun" };

        public static readonly Dictionary<string, PartDef[]> Parts = new Dictionary<string, PartDef[]>
        {
            { "barrel", new[] {
                new PartDef { id = "factory", name = "Factory barrel" },
                new PartDef { id = "long", name = "Long barrel", price = 250, blurb = "Reaches further and groups tighter. A little slower.",
                    mods = new PartMods { range = 1.2f, spread = 0.85f, fireRate = 0.95f } },
                new PartDef { id = "short", name = "Short barrel", price = 200, blurb = "Quick and handy, but it sprays.",
                    mods = new PartMods { fireRate = 1.1f, reloadTime = 0.9f, range = 0.85f, spread = 1.15f } },
                new PartDef { id = "heavy", name = "Heavy barrel", price = 400, blurb = "Hits harder and holds steady. Slower to fire and reload.",
                    mods = new PartMods { damage = 1.2f, stability = 1.3f, fireRate = 0.9f, reloadTime = 1.1f } } } },
            { "muzzle", new[] {
                new PartDef { id = "none", name = "Bare muzzle" },
                new PartDef { id = "comp", name = "Compensator", price = 200, blurb = "Vents gas upward to fight the kick.",
                    mods = new PartMods { recoil = 0.7f }, not = new[] { "shotgun" } },
                new PartDef { id = "brake", name = "Muzzle brake", price = 300, blurb = "Kills most of the kick, at some cost to accuracy.",
                    mods = new PartMods { recoil = 0.55f, spread = 1.1f } },
                new PartDef { id = "suppressor", name = "Suppressor", price = 350, blurb = "Gunfire no longer brings zombies running. Slightly less punch.",
                    mods = new PartMods { recoil = 0.85f, damage = 0.92f }, fx = new PartFx { quiet = true } },
                new PartDef { id = "choke", name = "Full choke", price = 250, blurb = "Keeps the shot together for longer.",
                    mods = new PartMods { spread = 0.65f, range = 1.15f }, fits = new[] { "shotgun" } } } },
            { "mag", new[] {
                new PartDef { id = "factory", name = "Standard magazine" },
                new PartDef { id = "extended", name = "Extended magazine", price = 250, blurb = "Half as many rounds again. Slower to change.",
                    mods = new PartMods { ammoCapacity = 1.5f, reloadTime = 1.15f } },
                new PartDef { id = "drum", name = "Drum magazine", price = 450, blurb = "More than twice the rounds. Heavy and slow to change.",
                    mods = new PartMods { ammoCapacity = 2.2f, reloadTime = 1.4f, stability = 0.9f }, not = new[] { "launcher" } },
                new PartDef { id = "quick", name = "Speed magazine", price = 300, blurb = "Pull tab and flared well: very quick reloads, a few rounds fewer.",
                    mods = new PartMods { reloadTime = 0.6f, ammoCapacity = 0.85f } } } },
            { "sight", new[] {
                new PartDef { id = "iron", name = "Iron sights" },
                new PartDef { id = "reddot", name = "Red dot", price = 200, blurb = "Quicker, tighter aim.", mods = new PartMods { spread = 0.85f } },
                new PartDef { id = "holo", name = "Holographic", price = 300, blurb = "Tighter aim that holds under fire.",
                    mods = new PartMods { spread = 0.8f, stability = 1.1f } },
                new PartDef { id = "scope", name = "Scope", price = 450, blurb = "Reaches much further and very accurate, but slower between shots.",
                    mods = new PartMods { range = 1.35f, spread = 0.7f, fireRate = 0.9f } } } },
            { "stock", new[] {
                new PartDef { id = "factory", name = "Standard stock" },
                new PartDef { id = "light", name = "Skeleton stock", price = 200, blurb = "Light: quicker reloads, a bit less steady.",
                    mods = new PartMods { reloadTime = 0.85f, stability = 0.9f } },
                new PartDef { id = "heavy", name = "Padded stock", price = 300, blurb = "Soaks up the kick. Slower to reload.",
                    mods = new PartMods { recoil = 0.75f, stability = 1.25f, reloadTime = 1.1f } },
                new PartDef { id = "precision", name = "Precision stock", price = 350, blurb = "Locks the gun in for accurate, steady fire.",
                    mods = new PartMods { spread = 0.85f, stability = 1.15f, fireRate = 0.95f } } } },
            { "trigger", new[] {
                new PartDef { id = "factory", name = "Factory trigger" },
                new PartDef { id = "hair", name = "Hair trigger", price = 300, blurb = "Fires faster. A little wilder.",
                    mods = new PartMods { fireRate = 1.15f, spread = 1.08f } },
                new PartDef { id = "match", name = "Match trigger", price = 300, blurb = "Clean break: more accurate, slightly harder hits.",
                    mods = new PartMods { spread = 0.85f, damage = 1.05f } } } },
            { "grip", new[] {
                new PartDef { id = "factory", name = "Factory grip" },
                new PartDef { id = "rubber", name = "Rubber grip", price = 150, blurb = "Takes the sting out of each shot.", mods = new PartMods { recoil = 0.85f } },
                new PartDef { id = "stippled", name = "Stippled grip", price = 250, blurb = "Never slips: steadier, and quicker to reload.",
                    mods = new PartMods { stability = 1.2f, reloadTime = 0.95f } },
                new PartDef { id = "ergo", name = "Ergonomic grip", price = 250, blurb = "Comfortable in a long fight: a little faster, a little less kick.",
                    mods = new PartMods { fireRate = 1.05f, recoil = 0.92f } } } },
            { "rail", new[] {
                new PartDef { id = "none", name = "Nothing fitted" },
                new PartDef { id = "foregrip", name = "Vertical foregrip", price = 250, blurb = "A second hand holds it down.",
                    mods = new PartMods { recoil = 0.75f, stability = 1.15f }, not = new[] { "pistol" } },
                new PartDef { id = "angled", name = "Angled foregrip", price = 300, blurb = "Very steady, and quicker to bring back on target.",
                    mods = new PartMods { stability = 1.35f, reloadTime = 0.95f }, not = new[] { "pistol" } },
                new PartDef { id = "laser", name = "Laser sight", price = 300, blurb = "Tighter aim, and you can see what it's on.",
                    mods = new PartMods { spread = 0.8f }, fx = new PartFx { laser = true } },
                new PartDef { id = "light", name = "Weapon light", price = 200, blurb = "A brighter, wider, longer flashlight.",
                    mods = new PartMods { range = 1.1f }, fx = new PartFx { light = true } } } },
            { "ammo", new[] {
                new PartDef { id = "fmj", name = "Full metal jacket", fits = Bullets },
                new PartDef { id = "buck", name = "Buckshot", fits = new[] { "shotgun" } },
                new PartDef { id = "he", name = "HE grenades", fits = new[] { "launcher" } },
                new PartDef { id = "hollow", name = "Hollow points", price = 300, fits = Bullets,
                    blurb = "Tear through walkers and runners. Weaker on tanks, never go through.",
                    mods = new PartMods { knockback = 1.2f }, fx = new PartFx { small = 1.3f, big = 0.8f, noPierce = true } },
                new PartDef { id = "ap", name = "Armor-piercing", price = 400, fits = Bullets,
                    blurb = "Go through one more zombie, hit tanks and bosses harder, and chew through cover.",
                    mods = new PartMods { pierce = 1 }, fx = new PartFx { big = 1.5f, props = 2 } },
                new PartDef { id = "incendiary", name = "Incendiary", price = 450, fits = Bullets, blurb = "Set zombies alight. Slightly less punch.",
                    mods = new PartMods { damage = 0.9f }, fx = new PartFx { fireDps = 0.35f, fireTime = 3 } },
                new PartDef { id = "slug", name = "Slugs", price = 300, fits = new[] { "shotgun" },
                    blurb = "One heavy, accurate ball that goes through a zombie and reaches further.",
                    mods = new PartMods { pellets = 1, damage = 5.5f, spread = 0.25f, pierce = 1, range = 1.3f, knockback = 1.3f }, fx = new PartFx { props = 2 } },
                new PartDef { id = "dragon", name = "Dragon's breath", price = 450, fits = new[] { "shotgun" }, blurb = "A cone of fire. Shorter reach.",
                    mods = new PartMods { damage = 0.85f, range = 0.85f }, fx = new PartFx { fireDps = 0.5f, fireTime = 3 } },
                new PartDef { id = "flechette", name = "Flechettes", price = 350, fits = new[] { "shotgun" }, blurb = "Darts that pass through the first zombie.",
                    mods = new PartMods { damage = 0.85f, spread = 0.9f, pierce = 1 } },
                new PartDef { id = "incendiary-g", name = "Incendiary grenades", price = 450, fits = new[] { "launcher" }, blurb = "Everything caught in the blast burns.",
                    mods = new PartMods { damage = 0.8f }, fx = new PartFx { fireDps = 0.12f, fireTime = 4 } },
                new PartDef { id = "cluster", name = "Cluster grenades", price = 550, fits = new[] { "launcher" }, blurb = "Each one throws out three more small blasts.",
                    mods = new PartMods { damage = 0.75f }, fx = new PartFx { cluster = 3 } },
                new PartDef { id = "concussion", name = "Concussion grenades", price = 300, fits = new[] { "launcher" },
                    blurb = "A much wider blast that throws zombies about, for less damage.",
                    mods = new PartMods { damage = 0.6f, blast = 1.35f, impulse = 2 } } } },
        };
        public const double PartTierCost = 0.35;     // each later gun's parts cost 35% more
        public const double UpgradeCostGrowth = 1.65;

        // ── Survivors ────────────────────────────────────────────────────
        // Rook to start with; the others are bought on survivor select, each
        // for more than the gun they bring.
        public static readonly CharacterDef[] Characters =
        {
            new CharacterDef { id = "rook", name = "Rook", role = "Ex-cop", blurb = "Keeps his head when the street fills up.",
                baseHealth = 100, moveSpeed = 6.2f, startingWeapon = "pistol",
                perk = new Perk { name = "Quick Hands", text = "Reloads 40% faster", reloadMult = 0.6f },
                look = new Look { skin = Hex.C(0xc58c64), shirt = Hex.C(0x2f4a78), pants = Hex.C(0x23262e), hat = "cap", hatColor = Hex.C(0x1d2a44) } },
            new CharacterDef { id = "bram", name = "Bram", role = "Riot guard", blurb = "Slow, huge and very hard to put down.",
                baseHealth = 140, moveSpeed = 5.4f, startingWeapon = "shotgun", price = 1000,
                perk = new Perk { name = "Riot Gear", text = "Starts every run with 75 armor", startArmor = 75 },
                look = new Look { skin = Hex.C(0x8d5a3c), shirt = Hex.C(0x3a3f46), pants = Hex.C(0x1f2226), hat = "helmet", hatColor = Hex.C(0x2a2e33), bulk = 1.12f } },
            new CharacterDef { id = "juno", name = "Juno", role = "Demolitions", blurb = "Brought her own launcher. Mostly unharmed by it.",
                baseHealth = 90, moveSpeed = 6.0f, startingWeapon = "launcher", price = 2500,
                perk = new Perk { name = "Blast Proof", text = "Takes 80% less explosion damage", explosionResist = 0.8f },
                look = new Look { skin = Hex.C(0xe2b48e), shirt = Hex.C(0xb8382e), pants = Hex.C(0x3b3328), hat = "goggles", hatColor = Hex.C(0xffb02e) } },
            new CharacterDef { id = "wren", name = "Wren", role = "Courier", blurb = "Fastest legs in the city, and a nose for loot.",
                baseHealth = 80, moveSpeed = 7.2f, startingWeapon = "smg", price = 600,
                perk = new Perk { name = "Scavenger", text = "Coin magnet +2 m, 25% more coins", magnetBonus = 2, coinMult = 1.25f },
                look = new Look { skin = Hex.C(0xf0c9a4), shirt = Hex.C(0xe0b020), pants = Hex.C(0x2a3a52), hat = "hood", hatColor = Hex.C(0xe0b020) } },
        };

        public const float PlayerMagnet = 3, PlayerRadius = 0.45f;
        // Armory upgrades for whoever you pick. step per level; speed is a share.
        public static readonly PlayerUpgradeDef[] PlayerUpgrades =
        {
            new PlayerUpgradeDef { id = "maxHp", label = "Max health", max = 5, step = 20, baseCost = 100, unit = " HP" },
            new PlayerUpgradeDef { id = "speed", label = "Move speed", max = 5, step = 0.06f, baseCost = 120, unit = "%" },
            new PlayerUpgradeDef { id = "magnet", label = "Coin magnet", max = 5, step = 1, baseCost = 70, unit = " m" },
        };
        // Armor soaks up this share of every hit until it runs out.
        public const float ArmorAbsorb = 1, MaxArmor = 100, PickupHealth = 35, PickupArmor = 50;

        // ── Power-ups ────────────────────────────────────────────────────
        // Glowing badges on the road, and sometimes left by tanks (bosses
        // always leave one). Walk over one and it goes off at once. Timed ones
        // stack up to twice their duration.
        public static readonly PowerUpDef[] PowerUps =
        {
            new PowerUpDef { id = "frag", name = "Frag Barrage", tag = "Grenades out", color = Hex.C(0x9be15d), weight = 3,
                grenades = 4, range = 18, damage = 150, blast = new Blast(3.6f, 12) },
            new PowerUpDef { id = "rapid", name = "Rapid Fire", tag = "No reloads", color = Hex.C(0xffb02e), weight = 2, duration = 10, fireRateMult = 2 },
            new PowerUpDef { id = "double", name = "Double Damage", tag = "2× damage", color = Hex.C(0xff5a4a), weight = 2, duration = 10, damageMult = 2 },
            new PowerUpDef { id = "shock", name = "Shockwave", tag = "Get back!", color = Hex.C(0x7ac8ff), weight = 2, radius = 7, damage = 180, impulse = 24 },
            new PowerUpDef { id = "vacuum", name = "Coin Vacuum", tag = "Every coin, to you", color = Hex.C(0xffc928), weight = 1.5f, duration = 8, radius = 32 },
        };
        public const float PowerUpPerChunk = 0.5f;    // chance of one on each 32 m of road
        public const float PowerUpFrom = 60;          // metres up the road before the first
        public const float PowerUpFromTank = 0.25f;   // chance a tank leaves one

        // ── Zombies ──────────────────────────────────────────────────────
        public static readonly Dictionary<string, ZombieKind> Zombies = new Dictionary<string, ZombieKind>
        {
            { "walker", new ZombieKind { id = "walker", hp = 45, speedMin = 1.4f, speedMax = 2.2f, damage = 10, attackRate = 1.0f, reach = 1.1f,
                radius = 0.42f, scale = 1.0f, coins = 3, aggro = 15, eyes = Hex.C(0xd8ff6a) } },
            { "runner", new ZombieKind { id = "runner", hp = 28, speedMin = 4.0f, speedMax = 4.8f, damage = 7, attackRate = 1.4f, reach = 1.0f,
                radius = 0.38f, scale = 0.92f, coins = 3, aggro = 24, eyes = Hex.C(0xff3a2a) } },
            { "tank", new ZombieKind { id = "tank", hp = 260, speedMin = 1.1f, speedMax = 1.4f, damage = 24, attackRate = 0.7f, reach = 1.5f,
                radius = 0.7f, scale = 1.5f, coins = 12, aggro = 16, eyes = Hex.C(0xffa020), smashes = true } },
            { "boss", new ZombieKind { id = "boss", hp = 1400, speedMin = 1.6f, speedMax = 1.6f, damage = 35, attackRate = 0.6f, reach = 2.2f,
                radius = 1.15f, scale = 2.4f, coins = 100, aggro = 30, eyes = Hex.C(0xff2020), smashes = true } },
        };

        // Gunfire wakes idle zombies this many metres away (a suppressor stops it).
        public const float GunfireNoise = 22;

        // ── The Game Director ────────────────────────────────────────────
        // A run is one endless road. Every StageLength metres is a new stage,
        // and zombies scale with it:
        //   health = base x (1 + HpPerStage x stage), damage likewise, speed
        //   capped at MaxSpeedMult. Hordes wait every HordeEvery metres,
        //   stragglers arrive every TrickleEvery seconds, and no more than
        //   MaxActive are up at once.
        public static class Director
        {
            public const float StageLength = 100, HpPerStage = 0.15f, DamagePerStage = 0.05f, SpeedPerStage = 0.025f, MaxSpeedMult = 1.45f;
            public const int UnlockRunner = 1, UnlockTank = 3, UnlockBoss = 5, BossEvery = 5;
            public static int MaxActive(int stage) => Mathf.Min(20 + stage * 5, 64);
            public static float HordeEvery(int stage) => Mathf.Max(24 - stage * 1.2f, 12);
            public static int HordeMin(int stage) => 2 + Mathf.FloorToInt(stage * 0.6f);
            public static int HordeMax(int stage) => 4 + Mathf.FloorToInt(stage * 0.9f);
            public static float TrickleEvery(int stage) => Mathf.Max(3.5f - stage * 0.25f, 0.8f);
            public static float SideAmbush(int stage) => stage < 1 ? 0 : 0.25f;
            public static float RunnerShare(int stage) => Mathf.Min(0.08f + stage * 0.04f, 0.35f);
            public static float TankShare(int stage) => Mathf.Min(0.03f + stage * 0.015f, 0.16f);
            public static int StageBonus(int stage) => 10 + stage * 5;
        }

        // ── Props you can wreck ──────────────────────────────────────────
        // Everything on the street stops bullets and can be broken.
        //   hp          damage to destroy it
        //   material    what bullets hit: metal sparks, concrete/brick/wood dust
        //   mass        how far blasts shove it (bigger = less); 0 = bolted down
        //   explodes    blows up when destroyed
        //   burns       smokes below smokeAt of its hp, catches fire below
        //               fireAt, and leaves a burning wreck when it blows
        //   rubble      leaves a rough pile that slows everyone crossing it
        //   topples     falls over when destroyed, crushing what it lands on
        //               for `crush` damage
        //   bulletMult / blastMult   scale incoming damage
        //   collapse    the big set piece: creaks, then comes down across the
        //               road as a rubble field with slabs in it
        public static readonly Dictionary<string, PropDef> Props = new Dictionary<string, PropDef>
        {
            { "car", new PropDef { hp = 420, material = "metal", mass = 8, burns = true, smokeAt = 0.6f, fireAt = 0.3f,
                explodes = new Explosion(5, 170, 18) } },
            { "tanker", new PropDef { hp = 520, material = "metal", mass = 24, burns = true, smokeAt = 0.6f, fireAt = 0.35f,
                explodes = new Explosion(9, 320, 30) } },
            { "barricade", new PropDef { hp = 260, material = "concrete", mass = 3, rubble = true } },
            { "barrel", new PropDef { hp = 30, material = "metal", mass = 1, explodes = new Explosion(3.8f, 150, 12) } },
            { "wall", new PropDef { hp = 480, material = "brick", rubble = true, topples = true, crush = 220, blastMult = 1.5f } },
            { "lamp", new PropDef { hp = 70, material = "metal", topples = true, crush = 90 } },
            { "fire", new PropDef { hp = 60, material = "metal", mass = 1 } },
            { "slab", new PropDef { hp = 320, material = "concrete", rubble = true } },
            { "condemned", new PropDef { hp = 900, material = "concrete", topples = true, collapse = true, crush = 2000,
                bulletMult = 0.15f, blastMult = 1.6f } },
        };
        // How often the road offers the big set pieces.
        public const int CondemnedEvery = 7;          // one condemned building every 7 chunks (~224 m)
        public const float TankerChance = 0.06f;      // per row of props, once past TankerFrom metres
        public const float TankerFrom = 120;
        public const float RubbleSlow = 0.55f;        // speed multiplier on rubble for zombies
        public const float RubbleSlowPlayer = 0.75f;  // and for you

        public struct Scaling { public float hp, damage, speed; }

        public static Scaling StageScaling(int stage) => new Scaling
        {
            hp = 1 + Director.HpPerStage * stage,
            damage = 1 + Director.DamagePerStage * stage,
            speed = Mathf.Min(1 + Director.SpeedPerStage * stage, Director.MaxSpeedMult),
        };

        // ── Lookups and derived numbers (no tuning below) ────────────────
        public static WeaponDef Weapon(string id) => System.Array.Find(Weapons, w => w.id == id) ?? Weapons[0];
        public static CharacterDef Character(string id) => System.Array.Find(Characters, c => c.id == id) ?? Characters[0];
        public static PowerUpDef PowerUp(string id) => System.Array.Find(PowerUps, p => p.id == id) ?? PowerUps[0];
        public static ItemDef Item(string id) => System.Array.Find(Items, i => i.id == id);

        // A power-up id, picked by weight; r is a random number in [0, 1).
        public static string PickPowerup(float r)
        {
            float total = 0;
            foreach (var p in PowerUps) total += p.weight;
            float w = r * total;
            foreach (var p in PowerUps) { w -= p.weight; if (w < 0) return p.id; }
            return PowerUps[0].id;
        }

        // A survivor's numbers for a run: their own, their perk, and the
        // Armory upgrades bought (levels by upgrade id).
        public static PlayerStats StatsFor(CharacterDef c, IDictionary<string, int> ups)
        {
            int Lv(string k) => ups != null && ups.TryGetValue(k, out var v) ? v : 0;
            var perk = c.perk ?? new Perk();
            return new PlayerStats
            {
                maxHp = c.baseHealth + PlayerUpgrades[0].step * Lv("maxHp"),
                speed = c.moveSpeed * (1 + PlayerUpgrades[1].step * Lv("speed")),
                magnet = PlayerMagnet + PlayerUpgrades[2].step * Lv("magnet") + perk.magnetBonus,
                radius = PlayerRadius,
                startArmor = perk.startArmor,
                explosionResist = perk.explosionResist,
                coinMult = perk.coinMult,
                perk = perk,
            };
        }

        // Rounded like the web version's Math.round (halves up), in doubles.
        public static int PlayerUpgradeCost(string id, int level)
        {
            var u = System.Array.Find(PlayerUpgrades, x => x.id == id);
            return (int)System.Math.Floor(u.baseCost * System.Math.Pow(UpgradeCostGrowth, level) / 5 + 0.5) * 5;
        }

        // The parts that go in one slot of one gun; the first is what it comes with.
        public static List<PartDef> PartsFor(WeaponDef def, string slot)
        {
            var list = new List<PartDef>();
            if (System.Array.IndexOf(def.slots, slot) < 0) return list;
            foreach (var p in Parts[slot])
            {
                if (p.fits != null && System.Array.IndexOf(p.fits, def.id) < 0) continue;
                if (p.not != null && System.Array.IndexOf(p.not, def.id) >= 0) continue;
                list.Add(p);
            }
            return list;
        }

        public static PartDef FittedPart(WeaponDef def, string slot, IDictionary<string, string> fitted)
        {
            var list = PartsFor(def, slot);
            if (list.Count == 0) return null;
            if (fitted != null && fitted.TryGetValue(slot, out var want))
            {
                var found = list.Find(p => p.id == want);
                if (found != null) return found;
            }
            return list[0];
        }

        // In doubles and rounding halves up, exactly like the web version's
        // Math.round: Mathf.RoundToInt rounds halves to even, which made some
        // prices 5 coins off.
        public static int PartCost(WeaponDef def, PartDef part)
        {
            int tier = Mathf.Max(0, System.Array.IndexOf(Weapons, def));
            return (int)System.Math.Floor(part.price * (1 + tier * PartTierCost) / 5 + 0.5) * 5;
        }
    }
}
