using System.Collections.Generic;
using SmithingPlus.Metal;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus;
#nullable enable

public partial class Core
{
    private const string RecipeOutputNameCacheKey = $"{ModId}:recipeOutputName";
    private const string ToolToRecipeCacheKey = $"{ModId}:toolToRecipe";
    private const string RecipeVoxelCountCacheKey = $"{ModId}:recipeVoxelCount";
    private const string MaxFuelBurnTempCacheKey = $"{ModId}:maxFuelBurnTemperature";
    private const string MoldStacksCacheKey = $"{ModId}:moldStacks";
    private const string MetalBitStacksCacheKey = $"{ModId}:metalbitStacks";
    private const string CastableMetalVariantsCacheKey = $"{ModId}:castableMetalVariants";
    internal const string MetalMaterialCacheKey = $"{ModId}:metalMaterial";
    internal const string MetalMaterialProcessedCacheKey = $"{ModId}:metalMaterialProcessed";

    public static Dictionary<int, string> RecipeOutputNameCache =>
        ObjectCacheUtil.GetOrCreate(Api, RecipeOutputNameCacheKey, () => new Dictionary<int, string>());

    public static Dictionary<int, int> RecipeVoxelCountCache =>
        ObjectCacheUtil.GetOrCreate(Api, RecipeVoxelCountCacheKey, () => new Dictionary<int, int>());

    /// <summary>
    ///     Head smithing recipe by tool code. Stores nulls, so a tool with no such recipe is searched for
    ///     once rather than on every break; see <see cref="Util.CacheHelper.GetOrAddNullable{TKey,TValue}" />.
    /// </summary>
    public static Dictionary<string, SmithingRecipe?> ToolToRecipeCache =>
        ObjectCacheUtil.GetOrCreate(Api, ToolToRecipeCacheKey, () => new Dictionary<string, SmithingRecipe?>());

    /// <summary>
    ///     Metal material by collectible code. Stores nulls, so a collectible with no metal is searched for
    ///     once rather than on every lookup; see <see cref="Util.CacheHelper.GetOrAddNullable{TKey,TValue}" />.
    /// </summary>
    public static Dictionary<string, MetalMaterial?> MetalMaterialCache =>
        ObjectCacheUtil.GetOrCreate(Api, MetalMaterialCacheKey, () => new Dictionary<string, MetalMaterial?>());

    /// <summary>
    ///     Metal material of what a collectible smiths into, by collectible code. Separate from
    ///     <see cref="MetalMaterialCache" />: the same collectible has a different answer in each.
    /// </summary>
    public static Dictionary<string, MetalMaterial?> MetalMaterialProcessedCache =>
        ObjectCacheUtil.GetOrCreate(Api, MetalMaterialProcessedCacheKey,
            () => new Dictionary<string, MetalMaterial?>());

    public static Dictionary<string, ItemStack[]> MoldStacksCache =>
        ObjectCacheUtil.GetOrCreate(Api, MoldStacksCacheKey, () => new Dictionary<string, ItemStack[]>());

    public static float? MaxFuelBurnTemp
    {
        get => Api.ObjectCache.TryGetValue(MaxFuelBurnTempCacheKey, out var temperature) ? (float?)temperature : null;
        set => Api.ObjectCache[MaxFuelBurnTempCacheKey] = value;
    }

    public static ItemStack[]? MetalBitStacksCache
    {
        get => Api.ObjectCache.TryGetValue(MetalBitStacksCacheKey, out var stacks) ? (ItemStack[])stacks : null;
        set => Api.ObjectCache[MetalBitStacksCacheKey] = value;
    }

    public static string[]? CastableMetalVariantsCache
    {
        get => Api.ObjectCache.TryGetValue(CastableMetalVariantsCacheKey, out var variants) ? (string[])variants : null;
        set => Api.ObjectCache[CastableMetalVariantsCacheKey] = value;
    }
}