#nullable enable
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus.Common.Metal;

/// <summary>
///     Maps each resolved ingredient code to the smithing recipes that use it, the smithing-recipe
///     counterpart to <see cref="GridRecipeIndex" />.
///     <para>
///         Resolving what a collectible smiths into asks which smithing recipes consume it. Answering that
///         by scanning the recipe list costs one pass per question, and the anvil path asks it per
///         collectible, so the two multiply the same way the grid recipe scan did.
///     </para>
///     <para>
///         The index is held in the world's object cache, so it is discarded with the world and is never
///         shared between a client and a server running in the same process.
///     </para>
/// </summary>
public static class SmithingRecipeIndex
{
    private const string CacheKey = $"{Core.ModId}:smithingRecipesByIngredient";

    private static readonly SmithingRecipe[] NoRecipes = [];

    /// <summary>
    ///     The smithing recipes using <paramref name="collObj" /> as an ingredient, in registry order.
    ///     A recipe listing the same ingredient in several slots appears once per slot, matching what a
    ///     direct scan over every recipe's ingredients would yield.
    /// </summary>
    public static SmithingRecipe[] RecipesAsIngredient(ICoreAPI api, CollectibleObject? collObj)
    {
        var code = collObj?.Code;
        if (code == null) return NoRecipes;
        return Index(api).TryGetValue(code.ToString(), out var recipes) ? recipes : NoRecipes;
    }

    /// <summary>
    ///     Discards the index so the next lookup rebuilds it. For the reset command; recipes do not change
    ///     during normal play.
    /// </summary>
    public static void Invalidate(ICoreAPI api)
    {
        ObjectCacheUtil.Delete(api, CacheKey);
    }

    private static Dictionary<string, SmithingRecipe[]> Index(ICoreAPI api)
    {
        return ObjectCacheUtil.GetOrCreate(api, CacheKey, () => Build(api));
    }

    private static Dictionary<string, SmithingRecipe[]> Build(ICoreAPI api)
    {
        var recipes = api.GetSmithingRecipes();
        var building = new Dictionary<string, List<SmithingRecipe>>(recipes?.Count ?? 0);
        for (var i = 0; i < (recipes?.Count ?? 0); i++)
        {
            var recipe = recipes![i];
            var ingredients = recipe?.Ingredients;
            if (ingredients == null) continue;
            for (var j = 0; j < ingredients.Length; j++)
            {
                var code = ingredients[j]?.ResolvedItemStack?.Collectible?.Code;
                if (code == null) continue;
                var key = code.ToString();
                if (!building.TryGetValue(key, out var usedBy)) building[key] = usedBy = [];
                usedBy.Add(recipe!);
            }
        }

        var index = new Dictionary<string, SmithingRecipe[]>(building.Count);
        foreach (var (code, usedBy) in building) index[code] = usedBy.ToArray();
        Core.Logger?.Notification(
            "[SmithingRecipeIndex] Indexed {0} smithing recipes by {1} ingredient codes on side {2}.",
            recipes?.Count ?? 0, index.Count, api.Side);
        return index;
    }
}
