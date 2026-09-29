using System;
using System.Collections.Generic;
using SmithingPlus.Metal;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus;
#nullable enable

/// <summary>
///     Caches, each held in the object cache of the API it is asked for.
///     <para>
///         Every cache here is per side. In singleplayer the client and the server run in one process, and a
///         cache shared between them hands one side the other's collectibles: a server-side work item that is
///         really the client's instance has baked textures, and the anvil's slag particles then dereference a
///         null client API on the server. Callers therefore pass the API of the side they are running on.
///     </para>
/// </summary>
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

    public static Dictionary<int, string> RecipeOutputNameCache(ICoreAPI api) =>
        CacheFor<Dictionary<int, string>>(api, RecipeOutputNameCacheKey);

    public static Dictionary<int, int> RecipeVoxelCountCache(ICoreAPI api) =>
        CacheFor<Dictionary<int, int>>(api, RecipeVoxelCountCacheKey);

    /// <summary>
    ///     Head smithing recipe by tool code. Stores nulls, so a tool with no such recipe is searched for
    ///     once rather than on every break; see <see cref="Util.CacheHelper.GetOrAddNullable{TKey,TValue}" />.
    /// </summary>
    public static Dictionary<string, SmithingRecipe?> ToolToRecipeCache(ICoreAPI api) =>
        CacheFor<Dictionary<string, SmithingRecipe?>>(api, ToolToRecipeCacheKey);

    /// <summary>
    ///     Metal material by collectible code. Stores nulls, so a collectible with no metal is searched for
    ///     once rather than on every lookup; see <see cref="Util.CacheHelper.GetOrAddNullable{TKey,TValue}" />.
    /// </summary>
    public static Dictionary<string, MetalMaterial?> MetalMaterialCache(ICoreAPI api) =>
        CacheFor<Dictionary<string, MetalMaterial?>>(api, MetalMaterialCacheKey);

    /// <summary>
    ///     Metal material of what a collectible smiths into, by collectible code. Separate from
    ///     <see cref="MetalMaterialCache" />: the same collectible has a different answer in each.
    /// </summary>
    public static Dictionary<string, MetalMaterial?> MetalMaterialProcessedCache(ICoreAPI api) =>
        CacheFor<Dictionary<string, MetalMaterial?>>(api, MetalMaterialProcessedCacheKey);

    public static Dictionary<string, ItemStack[]> MoldStacksCache(ICoreAPI api) =>
        CacheFor<Dictionary<string, ItemStack[]>>(api, MoldStacksCacheKey);

    public static float GetOrCreateMaxFuelBurnTemp(ICoreAPI api, Func<float> create) =>
        ObjectCacheUtil.GetOrCreate(api, MaxFuelBurnTempCacheKey, () => create());

    public static ItemStack[] GetOrCreateMetalBitStacks(ICoreAPI api, Func<ItemStack[]> create) =>
        ObjectCacheUtil.GetOrCreate(api, MetalBitStacksCacheKey, () => create());

    public static string[] GetOrCreateCastableMetalVariants(ICoreAPI api, Func<string[]> create) =>
        ObjectCacheUtil.GetOrCreate(api, CastableMetalVariantsCacheKey, () => create());

    /// <summary>The empty-on-first-use cache stored under <paramref name="key" /> for this API's side.</summary>
    private static TCache CacheFor<TCache>(ICoreAPI api, string key) where TCache : new() =>
        ObjectCacheUtil.GetOrCreate(api, key, () => new TCache());
}
