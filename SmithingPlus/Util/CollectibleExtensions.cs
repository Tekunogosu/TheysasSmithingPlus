using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SmithingPlus.Common.Metal;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus.Util;

#nullable enable
public static class CollectibleExtensions
{
    private static readonly JToken ForgeTransformToken = JToken.FromObject(
        new ModelTransform
        {
            Translation = new Vec3f(0, -0.1f, 0.35f),
            Rotation = new Vec3f(0, 90f, 0),
            Scale = 0.7f
        }
    );

    private static void EnsureAttributesNotNull(this CollectibleObject obj)
    {
        obj.Attributes ??= new JsonObject(new JObject());
    }

    public static void MakeForgeable(this CollectibleObject collObj)
    {
        collObj.EnsureAttributesNotNull();
        var token = collObj.Attributes.Token;
        token["forgable"] = true;
        token["inForgeTransform"] = ForgeTransformToken;
        collObj.Attributes.Token = token;
    }

    /// <summary>Adds the behavior, replacing one of the same type if the collectible already carries it.</summary>
    public static void AddBehavior<T>(this CollectibleObject collectible) where T : CollectibleBehavior
    {
        // A plain scan of a short array: this runs for every collectible in the game during asset finalize,
        // where the closure a predicate needs is allocated once per call for no benefit.
        //
        // Remove() returns a new array rather than mutating in place, so its result has to be assigned
        // back. Discarding it leaves the old behavior in place and the new one appended beside it.
        var behaviors = collectible.CollectibleBehaviors;
        for (var i = 0; i < behaviors.Length; i++)
            if (behaviors[i].GetType() == typeof(T))
            {
                collectible.CollectibleBehaviors = behaviors.Remove(behaviors[i]);
                break;
            }

        if (Activator.CreateInstance(typeof(T), collectible) is not T behavior)
        {
            Core.Logger.Error("[CollectibleExtensions] Failed to create behavior {0} for {1}", typeof(T).Name,
                collectible.Code);
            return;
        }

        collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(behavior);
    }

    public static void AddBehaviorIf<T>(this CollectibleObject collectible, bool condition)
        where T : CollectibleBehavior
    {
        if (!condition) return;
        collectible.AddBehavior<T>();
    }

    public static bool IsRepairableTool(this CollectibleObject collObj, bool verbose = false)
    {
        var repairable = WildcardUtil.Match(Core.Config.RepairableToolSelector, collObj.Code.ToString());
        if (verbose && !repairable) Core.Logger.VerboseDebug("Not a repairable tool: {0}", collObj.Code);
        return repairable;
    }

    public static bool MatchesToolHeadSelector(this CollectibleObject collObj, bool verbose = false)
    {
        var repairable = WildcardUtil.Match(Core.Config.ToolHeadSelector, collObj.Code.ToString());
        if (verbose && !repairable) Core.Logger.VerboseDebug("Not a tool head: {0}", collObj.Code);
        return repairable;
    }

    /// <summary>The first smithing recipe whose output is <paramref name="collObj" />, or null.</summary>
    public static SmithingRecipe? GetSmithingRecipe(this CollectibleObject collObj, ICoreAPI api)
    {
        var recipes = api.GetSmithingRecipes();
        if (recipes == null) return null;
        // A plain loop over the backing list: this runs once per collectible whose metal is still unknown
        // while the anvil's interaction help is built, and it exits at the first match.
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (recipe?.Output?.ResolvedItemstack?.Collectible?.Code?.Equals(collObj.Code) is true) return recipe;
        }

        return null;
    }

    /// <summary>
    ///     The smithing recipes using <paramref name="collObj" /> as an ingredient. Served from
    ///     <see cref="SmithingRecipeIndex" />, which answers from one pass over the recipe list rather than
    ///     one pass per caller. The result is already materialised, so enumerating it twice costs nothing.
    /// </summary>
    public static IReadOnlyList<SmithingRecipe> GetSmithingRecipesAsIngredient(this CollectibleObject collObj,
        ICoreAPI api)
    {
        return SmithingRecipeIndex.RecipesAsIngredient(api, collObj);
    }

    /// <summary>
    ///     The grid recipes using <paramref name="collObj" /> as an ingredient. Served from
    ///     <see cref="GridRecipeIndex" />, which answers from one pass over the recipe list rather than one
    ///     pass per caller. The result is already materialised, so enumerating it twice costs nothing.
    /// </summary>
    public static IReadOnlyList<GridRecipe> GetGridRecipesAsIngredient(this CollectibleObject collObj, ICoreAPI api)
    {
        return GridRecipeIndex.RecipesAsIngredient(api, collObj);
    }

    /// <summary>
    ///     The API a collectible was loaded with, read from the private field the game sets on it.
    ///     <para>
    ///         CollectibleObject exposes no accessor for it, so reflection is the only route. The field name
    ///         lives here alone rather than at each call site, so a rename in the game breaks one place.
    ///         Callers reading this per frame or per item should hold the result; the lookup itself is
    ///         cached by <see cref="ReflectionExtensions" />, but the call is not free.
    ///     </para>
    /// </summary>
    public static ICoreAPI? GetLoadedApi(this CollectibleObject collObj)
    {
        return collObj.GetField<ICoreAPI>("api");
    }

    /// <summary>
    ///     The collectible of the same code with one variant part replaced, resolved against the item class
    ///     it belongs to, or null if there is no such collectible.
    /// </summary>
    public static CollectibleObject? CollectibleWithVariant(this CollectibleObject collObj, string type, string value)
    {
        var api = collObj.GetLoadedApi();
        if (api == null)
        {
            Core.Logger?.Error("[CollectibleWithVariant] Reflection failed to get collectible object api field");
            return null;
        }

        var codeWithVariant = collObj.CodeWithVariant(type, value);
        return collObj.ItemClass switch
        {
            EnumItemClass.Block => api.World.GetBlock(codeWithVariant),
            EnumItemClass.Item => api.World.GetItem(codeWithVariant),
            _ => null
        };
    }

    public static T GetBehavior<T>(this CollectibleObject collObj, bool withInheritance) where T : CollectibleBehavior
    {
        return (T)collObj.GetCollectibleBehavior(typeof(T), withInheritance);
    }

    /// <summary>
    ///     Gets the metal properties variant from a CollectibleBehaviorQuenchable behavior.
    /// </summary>
    public static CollectibleBehaviorQuenchable.MetalPropertyVariant? GetMetalProps(
        this CollectibleBehaviorQuenchable behavior)
    {
        return behavior?.GetField<CollectibleBehaviorQuenchable.MetalPropertyVariant>("metalProps");
    }
}