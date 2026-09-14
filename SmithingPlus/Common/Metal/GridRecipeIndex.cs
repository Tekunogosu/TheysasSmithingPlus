#nullable enable
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SmithingPlus.Common.Metal;

/// <summary>
///     Maps each resolved ingredient code to the grid recipes that use it.
///     <para>
///         Resolving the metal of a collectible that declares none falls back to asking which recipes
///         consume it. Answering that by scanning <see cref="IWorldAccessor.GridRecipes" /> costs one pass
///         over every recipe per question. The anvil's placed-block interaction help asks it once per
///         anvil-workable item, so the two multiply: in a large modpack that is thousands of items against
///         thousands of recipes, and the scan dominates the first look at an anvil.
///     </para>
///     <para>
///         One pass builds the reverse mapping for every ingredient at once, after which each question is a
///         dictionary lookup. The index is held in the world's object cache, so it is discarded with the
///         world and is never shared between a client and a server running in the same process.
///     </para>
/// </summary>
public static class GridRecipeIndex
{
    private const string CacheKey = $"{Core.ModId}:gridRecipesByIngredient";

    private static readonly GridRecipe[] NoRecipes = [];

    /// <summary>
    ///     The recipes using <paramref name="collObj" /> as an ingredient, in registry order.
    ///     A recipe listing the same ingredient in several slots appears once per slot, matching what a
    ///     direct scan over every recipe's ingredients would yield.
    /// </summary>
    public static GridRecipe[] RecipesAsIngredient(ICoreAPI api, CollectibleObject? collObj)
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

    private static Dictionary<string, GridRecipe[]> Index(ICoreAPI api)
    {
        return ObjectCacheUtil.GetOrCreate(api, CacheKey, () => Build(api));
    }

    private static Dictionary<string, GridRecipe[]> Build(ICoreAPI api)
    {
        var recipes = api.World.GridRecipes;
        var building = new Dictionary<string, List<GridRecipe>>(recipes.Count);
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            // The backing array, not the RecipeIngredients property, which wraps it in an OfType
            // projection and allocates an enumerator per recipe.
            var ingredients = recipe?.ResolvedIngredients;
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

        var index = new Dictionary<string, GridRecipe[]>(building.Count);
        foreach (var (code, usedBy) in building) index[code] = usedBy.ToArray();
        Core.Logger?.Notification(
            "[GridRecipeIndex] Indexed {0} grid recipes by {1} ingredient codes on side {2}.",
            recipes.Count, index.Count, api.Side);
        return index;
    }
}
