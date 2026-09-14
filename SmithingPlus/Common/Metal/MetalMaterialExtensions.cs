using System.Collections.Generic;
using SmithingPlus.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SmithingPlus.Common.Metal;

#nullable enable
public static class MetalMaterialExtensions
{
    #region CollectibleObject

    /// <summary>
    ///     The metal material of <paramref name="collObj" />, resolved once and remembered for the rest of
    ///     the session, including when the answer is that it has none. Caching the "none" answer is what
    ///     keeps a collectible that resolves no metal from repeating the recipe search behind
    ///     <see cref="GetMetalMaterial" /> on every call.
    /// </summary>
    public static MetalMaterial? GetOrCacheMetalMaterial(this CollectibleObject? collObj, ICoreAPI api)
    {
        if (collObj?.Code == null) return null;
        return CacheHelper.GetOrAddNullable(Core.MetalMaterialCache, collObj.Code.ToString(),
            () => collObj.GetMetalMaterial(api));
    }

    /// <summary>
    ///     Works out what metal a collectible is made of, from its own declaration first and from what the
    ///     recipes say about it only as a fallback. Callers should go through
    ///     <see cref="GetOrCacheMetalMaterial(CollectibleObject,ICoreAPI)" />: the last step here searches
    ///     the recipes this collectible is an ingredient of, which is not something to repeat per call.
    /// </summary>
    private static MetalMaterial? GetMetalMaterial(this CollectibleObject collObj, ICoreAPI api)
    {
        // What the collectible says about itself.
        var metalMaterial = GetMetalMaterialDirect(collObj, api);
        if (metalMaterial != null) return metalMaterial;

        // What it is smithed from (a coke oven door names no metal, its smithing recipe does).
        var smithingRecipe = collObj.GetSmithingRecipe(api);
        if (smithingRecipe is { Ingredient.ResolvedItemStack.Collectible: { } ingredient })
        {
            metalMaterial = MetalMaterialLoader.GetMaterial(api, ingredient.GetMetalVariant());
            if (metalMaterial != null) return metalMaterial;
        }

        // What it is crafted alongside: the metal of the other materials in recipes using it.
        return MetalMaterialFromIngredients(api, collObj.GetGridRecipesAsIngredient(api));
    }

    /// <summary>
    ///     The metal material named by the collectible itself, through a <c>metalMaterial</c> attribute or
    ///     its own metal variant. Reads nothing outside the collectible, so it is cheap enough to call for
    ///     each ingredient of each recipe while searching.
    /// </summary>
    private static MetalMaterial? GetMetalMaterialDirect(this CollectibleObject collObj, ICoreAPI api)
    {
        var stated = collObj.Attributes?["metalMaterial"];
        if (stated?.Exists == true)
        {
            var material = MetalMaterialLoader.GetMaterial(api, stated.AsString());
            if (material != null) return material;
            Core.Logger?.VerboseDebug(
                "[MetalMaterial] {0} names metalMaterial '{1}', which matches no loaded material.",
                collObj.Code, stated.AsString());
        }

        return MetalMaterialLoader.GetMaterial(api, collObj.GetMetalVariant());
    }

    /// <summary>
    ///     The first metal named by a material ingredient of the given recipes.
    ///     <para>
    ///         Only ingredients the recipe does not use up are considered: an ingredient that is consumed,
    ///         or that is spent as durability, is a tool the recipe is applied with rather than the stuff
    ///         the output is made of, and its metal says nothing about the output's.
    ///     </para>
    ///     <para>
    ///         Each ingredient is asked only what it declares about itself. Asking the full question would
    ///         recurse back into the recipe search through every ingredient of every recipe.
    ///     </para>
    /// </summary>
    private static MetalMaterial? MetalMaterialFromIngredients(ICoreAPI api, IReadOnlyList<GridRecipe> gridRecipes)
    {
        // Indexed over the outer list rather than enumerated: this is the innermost loop of the fallback,
        // reached once per collectible whose metal is not known from anything cheaper.
        for (var i = 0; i < gridRecipes.Count; i++)
        {
            // ResolvedIngredients is the backing array. The RecipeIngredients property wraps it in an
            // OfType projection, allocating an enumerator per recipe on a path that visits every recipe.
            var ingredients = gridRecipes[i]?.ResolvedIngredients;
            if (ingredients == null) continue;
            for (var j = 0; j < ingredients.Length; j++)
            {
                var ingredient = ingredients[j];
                if (ingredient == null || IsToolIngredient(ingredient)) continue;
                var collectible = ingredient.ResolvedItemStack?.Collectible;
                if (collectible == null) continue;
                var metalMaterial = collectible.GetMetalMaterialDirect(api);
                if (metalMaterial != null) return metalMaterial;
            }
        }

        return null;
    }

    /// <summary>Whether the recipe spends this ingredient rather than building the output out of it.</summary>
    private static bool IsToolIngredient(IRecipeIngredient ingredient)
    {
        var consume = ingredient.ConsumeProperties;
        return consume.Consume || consume.DurabilityCost != 0;
    }

    public static MetalMaterial? GetMetalMaterialSmelted(this CollectibleObject? collectibleObject, ICoreAPI api)
    {
        var variantCode = collectibleObject?.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible
            .GetMetalVariant();
        return variantCode == null ? null : MetalMaterialLoader.GetMaterial(api, variantCode);
    }

    // Use when what matters is the processed result (e.g., iron bloom > iron, blister steel > steel)
    private static MetalMaterial? GetMetalMaterialProcessed(this CollectibleObject? collectibleObject, ICoreAPI api)
    {
        if (collectibleObject?.Code == null) return null;
        // Cached separately from the unprocessed material: the same collectible has a different answer here
        // (a bloom's processed metal is iron, its own is none), so the two must not share a cache entry.
        return CacheHelper.GetOrAddNullable(Core.MetalMaterialProcessedCache, collectibleObject.Code.ToString(),
            () => collectibleObject.ResolveMetalMaterialProcessed(api));
    }

    private static MetalMaterial? ResolveMetalMaterialProcessed(this CollectibleObject collectibleObject, ICoreAPI api)
    {
        // This instead gets the metal material of the items created by smithing this item
        var recipes = collectibleObject.GetSmithingRecipesAsIngredient(api);
        for (var i = 0; i < recipes.Count; i++)
        {
            var output = recipes[i].Output?.ResolvedItemstack?.Collectible;
            if (output == null) continue;
            var metalMaterial = MetalMaterialLoader.GetMaterial(api, output.GetMetalVariant());
            if (metalMaterial != null) return metalMaterial;
        }

        return null;
    }

    public static string GetMetalVariant(this CollectibleObject collObj)
    {
        return collObj.Variant["metal"] ?? collObj.Variant["material"] ?? collObj.LastCodePart();
    }

    // Simplified check using the basic vanilla convention that uses 'metal' and 'material' variants
    public static bool HasMetalMaterialSimple(this CollectibleObject collObj)
    {
        return (collObj.Variant["metal"] ?? collObj.Variant["material"]) != null;
    }

    #endregion

    #region ItemStack

    /// <summary>
    ///     The metal material of the stack's collectible.
    ///     <para>
    ///         An anvil-workable collectible answers from the base material of this particular stack, which
    ///         is a property of the stack rather than of the collectible, so it is resolved per call and
    ///         only the ingot's own lookup is cached. Anything else depends on the collectible alone and is
    ///         cached against it.
    ///     </para>
    /// </summary>
    public static MetalMaterial? GetOrCacheMetalMaterial(this ItemStack? itemStack, ICoreAPI api)
    {
        var collObj = itemStack?.Collectible;
        if (collObj is not IAnvilWorkable anvilWorkable) return collObj.GetOrCacheMetalMaterial(api);
        var ingotStack = anvilWorkable.GetBaseMaterial(itemStack);
        return ingotStack?.Collectible.GetOrCacheMetalMaterial(api) ?? collObj.GetOrCacheMetalMaterial(api);
    }

    // Use when what matters is the processed result (e.g., iron bloom > iron, blister steel > steel)
    public static MetalMaterial? GetMetalMaterialProcessed(this ItemStack? itemStack, ICoreAPI api)
    {
        var collObj = itemStack?.Collectible;
        // Resort to the CollectibleObject method for items that are not anvil workable
        if (collObj is not IAnvilWorkable anvilWorkable) return collObj.GetOrCacheMetalMaterial(api);
        // Grab from IAnvilWorkable, then the processed material from the ingot stack
        var ingotStack = anvilWorkable.GetBaseMaterial(itemStack);
        return ingotStack?.Collectible.GetMetalMaterialProcessed(api);
    }

    #endregion
}