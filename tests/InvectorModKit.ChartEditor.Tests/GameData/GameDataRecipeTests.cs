namespace InvectorModKit.ChartEditor.Tests;

public class GameDataRecipeTests
{
    [Fact]
    public void EveryItemHasABundleAndANameOrClass()
    {
        foreach (var i in GameDataRecipe.Items)
        {
            Assert.EndsWith(".bundle", i.Bundle);
            Assert.False(string.IsNullOrEmpty(i.Kind == RecipeKind.Settings ? i.Class : i.Name), i.Key);
        }
        Assert.Equal(GameDataRecipe.Items.Count, GameDataRecipe.Items.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void TenWorldsEachWithTheirSceneBundle()
    {
        Assert.Equal(new[] { "city", "darkvalley", "desert", "ice", "inferno", "oblivion", "ruins", "space", "valley", "void" }, GameDataRecipe.Worlds);
        foreach (var w in GameDataRecipe.Worlds)
            Assert.Contains(GameDataRecipe.Items, i => i.Kind == RecipeKind.Level && i.Name == w && i.Bundle == $"basegame_scenes_level{w}.bundle");
    }

    [Theory]
    [InlineData(RecipeKind.Prefab, "Ship Beginner")]
    [InlineData(RecipeKind.Prefab, "PickupAir, All Platforms OPTIMIZED")]
    [InlineData(RecipeKind.Clip, "coaster@RollRightLong3Turns")]
    [InlineData(RecipeKind.Controller, "Coaster Animator")]
    [InlineData(RecipeKind.Settings, "MaterialsDatabase")]
    // The spec's "List/Bounce" names two prefabs each, one per lane mode (as the game names them).
    [InlineData(RecipeKind.Prefab, "Pickup Strum, List OPTIMIZED")]
    [InlineData(RecipeKind.Prefab, "Pickup Strum, Bounce OPTIMIZED")]
    [InlineData(RecipeKind.Prefab, "Pickup StrumHold Start, List OPTIMIZED")]
    [InlineData(RecipeKind.Prefab, "Pickup StrumHold Start, Bounce OPTIMIZED")]
    [InlineData(RecipeKind.Prefab, "PickupStrumHoldEnd, List, All Platforms")]
    [InlineData(RecipeKind.Prefab, "PickupStrumHoldEnd, Bounce, All Platforms")]
    public void HoldsTheSpecItems(RecipeKind kind, string nameOrClass) =>
        Assert.Contains(GameDataRecipe.Items, i => i.Kind == kind && (i.Name ?? i.Class) == nameOrClass);

    [Fact]
    public void NoNameIsAShorthandForTwoVariants() =>
        Assert.DoesNotContain(GameDataRecipe.Items, i => (i.Name ?? i.Class).Contains("List/Bounce"));

    [Fact]
    public void KeyIsKindAndNameOrClass()
    {
        Assert.Contains(GameDataRecipe.Items, i => i.Key == "Prefab:Ship Beginner");
        Assert.Contains(GameDataRecipe.Items, i => i.Key == "Settings:ShipColorScheme");
    }

    [Fact]
    public void ClassListsMatchTheSpec()
    {
        Assert.Equal(new[] { "Spline", "SplineEditor", "SplineMesh", "Toblerone", "TobleroneWalls" }, GameDataRecipe.LevelClasses);
        Assert.Equal(new[] { "Walker", "CameraController", "Steering", "PlacePickupsOnPath", "PickupEffectTrigger", "GameplayEffectsTrigger" }, GameDataRecipe.WalkerClasses);
    }
}
