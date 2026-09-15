#nullable enable
using System;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SmithingPlus.Util;

/// <summary>
///     Finds the smithing recipe that produces a given stack.
///     <para>
///         Each lookup walks the recipe registry directly rather than through a filtering enumerator: these
///         run per held item and per crafting input, where an enumerator and its closure are allocated for
///         a search that usually ends on the first match.
///     </para>
/// </summary>
public static class SmithingRecipeLookups
{
    /// <summary>The first recipe whose output satisfies the stack, or null.</summary>
    public static SmithingRecipe? GetSmithingRecipe(this ItemStack toolHead, ICoreAPI api)
    {
        var recipes = api.GetSmithingRecipes();
        if (recipes == null) return null;
        for (var i = 0; i < recipes.Count; i++)
            if (SatisfyingOutput(recipes[i], toolHead) != null)
                return recipes[i];
        return null;
    }

    /// <summary>The first recipe whose output satisfies the stack and has the given output size, or null.</summary>
    public static SmithingRecipe? GetSmithingRecipe(this ItemStack toolHead, ICoreAPI api, int withOutputStackSize)
    {
        var recipes = api.GetSmithingRecipes();
        if (recipes == null) return null;
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (SatisfyingOutput(recipe, toolHead)?.StackSize == withOutputStackSize) return recipe;
        }

        return null;
    }

    /// <summary>A recipe only if it produces exactly one of the stack.</summary>
    public static SmithingRecipe? GetSingleSmithingRecipe(this ItemStack toolHead, ICoreAPI api)
    {
        return toolHead.GetSmithingRecipe(api, 1);
    }

    /// <summary>The matching recipe with the largest output stack.</summary>
    public static SmithingRecipe? GetLargestSmithingRecipe(this ItemStack toolHead, ICoreAPI api)
    {
        return toolHead.HighestScoring(api, static (_, output) => output.StackSize);
    }

    /// <summary>
    ///     The matching recipe costing the most material per item produced, which is the one a player gains
    ///     least by going through -- picked so the mechanic cannot be used to turn an expensive item into a
    ///     cheap recipe's worth of material.
    /// </summary>
    public static SmithingRecipe? GetCheapestSmithingRecipe(this ItemStack toolHead, ICoreAPI api)
    {
        return toolHead.HighestScoring(api,
            static (r, output) => r.Voxels.VoxelCount() / Math.Max(output.StackSize, 1));
    }

    /// <summary>
    ///     The material voxels one of <paramref name="toolHead" />'s items is worth, or null when no smithing
    ///     recipe prices it.
    ///     <para>
    ///         Priced through the cheapest recipe, so the answer is the one a player gains least by going
    ///         through; see <see cref="GetCheapestSmithingRecipe" />. Callers wanting the whole stack multiply
    ///         by the stack size they are actually consuming, which is not always
    ///         <see cref="ItemStack.StackSize" /> -- a crafting ingredient consumes only what the recipe asks
    ///         for.
    ///     </para>
    /// </summary>
    public static int? VoxelCostPerItem(this ItemStack toolHead, ICoreAPI api)
    {
        var cheapestRecipe = toolHead.GetCheapestSmithingRecipe(api);
        var output = cheapestRecipe?.Output?.ResolvedItemstack;
        if (cheapestRecipe == null || output == null) return null;
        // A recipe declaring a zero-sized output would divide by zero; it is a malformed recipe rather than
        // a free one, so it prices as a single item.
        var outputStackSize = Math.Max(output.StackSize, 1);
        return Math.Max(cheapestRecipe.Voxels.VoxelCount() / outputStackSize, 0);
    }

    /// <summary>
    ///     The matching recipe scoring highest under <paramref name="score" />, or null when none match.
    ///     Ties go to the earliest in registry order, as the ordering these replaced did.
    /// </summary>
    private static SmithingRecipe? HighestScoring(this ItemStack toolHead, ICoreAPI api,
        // Qualified: Vintagestory.API.Common declares its own Func, so the unqualified name is ambiguous.
        System.Func<SmithingRecipe, ItemStack, int> score)
    {
        var recipes = api.GetSmithingRecipes();
        if (recipes == null) return null;

        SmithingRecipe? best = null;
        var bestScore = int.MinValue;
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            var output = SatisfyingOutput(recipe, toolHead);
            if (output == null) continue;
            var value = score(recipe, output);
            // Strictly greater, so the first of several equal-scoring recipes wins.
            if (value <= bestScore) continue;
            bestScore = value;
            best = recipe;
        }

        return best;
    }

    /// <summary>
    ///     The recipe's resolved output when it satisfies <paramref name="toolHead" />, otherwise null.
    ///     Handing the stack back rather than a bool means callers read it from here instead of walking
    ///     <c>recipe.Output.ResolvedItemstack</c> again through nullable links they have already checked.
    /// </summary>
    private static ItemStack? SatisfyingOutput(SmithingRecipe? recipe, ItemStack toolHead)
    {
        var output = recipe?.Output?.ResolvedItemstack;
        return output?.Satisfies(toolHead) == true ? output : null;
    }
}
