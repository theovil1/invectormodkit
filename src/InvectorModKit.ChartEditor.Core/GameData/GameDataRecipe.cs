using System.Collections.Generic;
using System.Linq;

namespace InvectorModKit.ChartEditor
{
    public enum RecipeKind { Prefab, Clip, Controller, Settings, Level }

    /// <summary>One thing to read from a game bundle: a prefab, clip, controller or level by name, or a settings class.</summary>
    public sealed class RecipeItem
    {
        public string Bundle;
        public RecipeKind Kind;
        public string Name;   // Prefab, Clip, Controller, Level (the world)
        public string Class;  // Settings
        public string Key => Kind + ":" + (Name ?? Class);
    }

    /// <summary>What the 3D preview needs from the installed game. Change Version when the cache format or this list changes.</summary>
    public static class GameDataRecipe
    {
        public const int Version = 1;

        const string Shared = "basegame_assets_shared.bundle";
        const string Ship = "basegame_assets_ship_beginner.bundle";
        const string LevelModule = "basegame_assets_levelmodule.bundle";

        public static readonly IReadOnlyList<string> Worlds = new[]
        {
            "city", "darkvalley", "desert", "ice", "inferno", "oblivion", "ruins", "space", "valley", "void",
        };

        public static readonly IReadOnlyList<string> LevelClasses = new[]
        {
            "Spline", "SplineEditor", "SplineMesh", "Toblerone", "TobleroneWalls",
        };

        public static readonly IReadOnlyList<string> WalkerClasses = new[]
        {
            "Walker", "CameraController", "Steering", "PlacePickupsOnPath", "PickupEffectTrigger", "GameplayEffectsTrigger",
        };

        public static readonly IReadOnlyList<RecipeItem> Items = Build();

        static IReadOnlyList<RecipeItem> Build()
        {
            var items = new List<RecipeItem>();
            void Add(string bundle, RecipeKind kind, params string[] names)
            {
                foreach (var n in names)
                    items.Add(kind == RecipeKind.Settings
                        ? new RecipeItem { Bundle = bundle, Kind = kind, Class = n }
                        : new RecipeItem { Bundle = bundle, Kind = kind, Name = n });
            }

            Add(Shared, RecipeKind.Prefab,
                "XBOX Pickup Left X OPTIMIZED", "XBOX Pickup Right B OPTIMIZED", "XBOX Pickup Down A OPTIMIZED", "XBOX Pickup Up Y OPTIMIZED",
                "Keyboard PS4 Pickup Left OPTIMIZED", "Keyboard PS4 Pickup Right OPTIMIZED", "Keyboard PS4 Pickup Up OPTIMIZED", "Keyboard PS4 Pickup Down OPTIMIZED",
                "Pickup 4 Left OPTIMIZED", "Pickup 5 Right OPTIMIZED",
                "Pickup Strum, List/Bounce OPTIMIZED", "Pickup StrumHold Start, List/Bounce OPTIMIZED", "PickupStrumHoldEnd, List/Bounce, All Platforms",
                "BeatLine, List", "BeatLine, Bounce", "PickupAir, All Platforms OPTIMIZED");
            Add(Shared, RecipeKind.Clip,
                "RollRightLong", "coaster@RollRightLong2Turns", "coaster@RollRightLong3Turns", "Airpickup Spawn Legacy", "Airpickup ActivateEffect Legacy");
            Add(Shared, RecipeKind.Controller, "Coaster Animator");
            Add(Ship, RecipeKind.Prefab, "Ship Beginner");
            Add(LevelModule, RecipeKind.Prefab, "PlayerWalkerGlobal");
            Add(LevelModule, RecipeKind.Settings, "MaterialsDatabase", "ShipColorScheme", "GeneratorFreefly", "GeneratorSpawnSettings");
            foreach (var w in Worlds)
                Add("basegame_scenes_level" + w + ".bundle", RecipeKind.Level, w);
            return items.ToList();
        }
    }
}
