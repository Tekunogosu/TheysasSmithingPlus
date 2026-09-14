#nullable enable
using System;
using System.Collections.Generic;
using SmithingPlus.Common.Metal;
using SmithingPlus.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SmithingPlus.Common;

public abstract class CollectibleBehaviorAnvilWorkable(CollectibleObject collObj) :
    CollectibleBehavior(collObj), IAnvilWorkable
{
    private ICoreAPI? _api;
    private MetalMaterial? _metalMaterial;
    private bool _metalMaterialResolved;

    /// <summary>
    ///     The API, read once from the collectible's private field and kept.
    ///     <para>
    ///         Each read is an uncached reflection lookup, and this is on the path the anvil's interaction
    ///         help walks for every workable item in the game. The field is set when the collectible loads
    ///         and does not change afterwards, so reading it repeatedly buys nothing.
    ///     </para>
    /// </summary>
    protected ICoreAPI? Api => _api ??= collObj.GetLoadedApi();

    protected abstract byte[,,] Voxels { get; }

    /// <summary>
    ///     The metal this collectible is made of, resolved once. Null is a real answer here and is kept as
    ///     one, so a collectible with no metal does not repeat the smelted-variant search on every access.
    ///     Resolution is deferred rather than done at load, because materials are only resolvable once
    ///     <see cref="MetalMaterialLoader" /> has finalised.
    /// </summary>
    protected MetalMaterial? MetalMaterial
    {
        get
        {
            if (_metalMaterialResolved) return _metalMaterial;
            var api = Api;
            if (api == null) return null;
            _metalMaterial = MetalMaterialLoader.GetMaterial(api, collObj.GetMetalVariant())
                             ?? collObj.GetMetalMaterialSmelted(api);
            _metalMaterialResolved = true;
            return _metalMaterial;
        }
    }

    protected virtual AnvilPlacementMode PlacementMode { get; set; } = AnvilPlacementMode.Normal;

    public virtual ItemStack? TryPlaceOn(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        if (Api == null || !CanWork(stack) || beAnvil is { WorkItemStack: not null, CanWorkCurrent: false })
            return null;
        var workItemStack = MetalMaterial?.WorkItemStack;
        if (workItemStack == null)
            return null;
        var sourceTemp = stack.GetTemperature(Api.World);
        workItemStack.SetTemperature(Api.World, sourceTemp);

        if (beAnvil.WorkItemStack == null && PlacementMode.AllowsEmpty())
        {
            TryAddVoxelsFromWorkable(Api, ref beAnvil.Voxels);
            return workItemStack;
        }

        if (!PlacementMode.AllowsPresent())
            return null;

        var workItemMaterial = beAnvil.WorkItemStack?.GetMetalMaterialProcessed(Api);
        Core.Logger.VerboseDebug(
            $"[{nameof(CollectibleBehaviorAnvilWorkable)}#{nameof(TryPlaceOn)}] base material: {MetalMaterial?.IngotCode}, " +
            $"workItem base material: {workItemMaterial?.IngotCode}");

        if (workItemMaterial == null || !workItemMaterial.Equals(MetalMaterial))
        {
            (Api as ICoreClientAPI)?.TriggerIngameError(this, "notequal",
                Lang.Get("Must be the same metal to add voxels"));
            return null;
        }

        var didSucceed = TryAddVoxelsFromWorkable(Api, ref beAnvil.Voxels);
        if (didSucceed) return workItemStack;

        (Api as ICoreClientAPI)?.TriggerIngameError(this, "requireshammering",
            Lang.Get("Try hammering down before adding additional voxels"));
        return null;
    }

    /// <summary>
    ///     Whether the stack is hot enough to work: at or above its stated <c>workableTemperature</c>, or
    ///     half its melting point when it states none.
    /// </summary>
    public virtual bool CanWork(ItemStack stack)
    {
        var world = Api?.World;
        var temperature = stack.Collectible.GetTemperature(world, stack);

        // The stated value first, so the melting point is only derived when it is the answer. Deriving it
        // allocates a slot to query through, and this runs on every placement attempt.
        var stated = stack.ItemAttributes?["workableTemperature"];
        if (stated?.Exists == true)
        {
            var value = stated.AsFloat(float.NaN);
            if (!float.IsNaN(value)) return temperature >= value;
        }

        return temperature >= stack.Collectible.GetMeltingPoint(world, null, new DummySlot(stack)) / 2;
    }

    public virtual int GetRequiredAnvilTier(ItemStack stack)
    {
        var material = MetalMaterial;
        var tier = material != null ? material.Tier - 1 : 0;
        var stated = stack.Collectible?.Attributes?["requiresAnvilTier"];
        return stated?.Exists == true ? stated.AsInt(tier) : tier;
    }

    /// <summary>
    ///     The smithing recipes this stack can be worked into, ordered by output code then output stack
    ///     size, with recipes sharing one output stack instance collapsed to the first of them.
    /// </summary>
    public virtual List<SmithingRecipe> GetMatchingRecipes(ItemStack stack)
    {
        var all = Api?.GetSmithingRecipes();
        if (all == null) return [];

        // Built once rather than per recipe: IngotStack allocates a new stack on every read, and the
        // match below is tested once per smithing recipe in the game.
        var baseMetal = MetalMaterial?.IngotStack;

        // The resolved output is carried with each match: it is reached through four nullable links, and
        // the ordering and the dedupe below would otherwise each walk them again.
        var matching = new List<(SmithingRecipe Recipe, ItemStack Output)>();
        for (var i = 0; i < all.Count; i++)
        {
            var recipe = all[i];
            var output = recipe?.Output?.ResolvedItemstack;
            if (output?.Collectible?.Code == null || output.Collectible.Code.Equals(collObj.Code)) continue;
            var ingredient = recipe!.Ingredient;
            if (ingredient == null) continue;
            if (!(baseMetal != null && ingredient.SatisfiesAsIngredient(baseMetal))
                && !ingredient.SatisfiesAsIngredient(stack)) continue;
            matching.Add((recipe, output));
        }

        // Sorted on an index array so the comparison can fall back to the original position. List.Sort is
        // unstable where OrderBy/ThenBy were stable, and equal-ranking recipes changing places between
        // calls would move what a stored recipe selection index points at.
        var order = new int[matching.Count];
        for (var i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(order, (x, y) =>
        {
            var byOutput = CompareByOutput(matching[x].Output, matching[y].Output);
            return byOutput != 0 ? byOutput : x.CompareTo(y);
        });

        // One recipe per distinct output stack instance, keeping the first in sorted order. This reproduces
        // DistinctBy on the resolved stack: ItemStack overrides GetHashCode but not Equals(object), so the
        // default comparer it uses comes down to reference identity. Equal-looking stacks that are separate
        // instances are therefore kept apart, and only recipes genuinely sharing one stack collapse -- which
        // is why this needs a set over the whole sequence rather than a comparison with the previous item.
        var seen = new HashSet<ItemStack>(OutputStackIdentity.Instance);
        var sorted = new List<SmithingRecipe>(matching.Count);
        for (var i = 0; i < order.Length; i++)
        {
            var (recipe, output) = matching[order[i]];
            if (seen.Add(output)) sorted.Add(recipe);
        }

        return sorted;
    }

    /// <summary>
    ///     Compares resolved output stacks the way the default comparer does: by identity. ItemStack
    ///     overrides GetHashCode but not Equals(object), so two equal-looking stacks are distinct unless
    ///     they are the same instance. Stated explicitly rather than relying on that asymmetry holding.
    /// </summary>
    private sealed class OutputStackIdentity : IEqualityComparer<ItemStack>
    {
        public static readonly OutputStackIdentity Instance = new();

        public bool Equals(ItemStack? x, ItemStack? y) => ReferenceEquals(x, y);

        public int GetHashCode(ItemStack obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>Orders by output collectible code, then by output stack size.</summary>
    private static int CompareByOutput(ItemStack a, ItemStack b)
    {
        var byCode = a.Collectible.Code.CompareTo(b.Collectible.Code);
        return byCode != 0 ? byCode : a.StackSize.CompareTo(b.StackSize);
    }

    public virtual ItemStack? GetBaseMaterial(ItemStack stack)
    {
        return MetalMaterial?.IngotStack;
    }

    public virtual EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        return EnumHelveWorkableMode.NotWorkable;
    }

    public virtual int VoxelCountForHandbook(ItemStack stack)
    {
        return Voxels.MaterialCount();
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (Api == null)
        {
            Core.Logger?.Error(
                "[CollectibleBehaviorAnvilWorkable] Reflection failed: field 'api' in collectible class is null.");
            return;
        }

        if (Voxels.MaterialCount() == 0)
            Api?.Logger.Error("CollectibleBehaviorAnvilWorkable for {0} has no voxels defined. " +
                              "Please check the 'voxels' attribute in the item JSON.", collObj.Code);
    }

    /// <summary>
    ///     Drops this workable's voxels onto the anvil, each column resting on what is already there.
    ///     Succeeds only if every column fits; on failure the anvil is left untouched.
    /// </summary>
    protected virtual bool TryAddVoxelsFromWorkable(ICoreAPI api, ref byte[,,] beAnvilVoxels)
    {
        // Read once. Voxels is regenerated per access for a pattern with random voxels, so measuring one
        // array and then placing from another would fit a shape that is not the one being added.
        var source = Voxels;
        if (source.MaterialCount() == 0) return false;

        var targetHeight = beAnvilVoxels.GetLength(1);
        var width = Math.Min(beAnvilVoxels.GetLength(0), source.GetLength(0));
        var depth = Math.Min(beAnvilVoxels.GetLength(2), source.GetLength(2));
        var sourceHeight = source.GetLength(1);

        // Built into a copy so a column that does not fit leaves the anvil as it was.
        var placed = (byte[,,])beAnvilVoxels.Clone();

        for (var x = 0; x < width; x++)
        for (var z = 0; z < depth; z++)
        {
            // Rest this column on the first empty voxel above what is already stacked there.
            var floor = 0;
            while (floor < targetHeight && placed[x, floor, z] != 0) floor++;
            if (floor >= targetHeight) return false;

            for (var y = 0; y < sourceHeight; y++)
            {
                var material = source[x, y, z];
                if (material == 0) continue;
                if (floor + y >= targetHeight) return false;
                placed[x, floor + y, z] = material;
            }
        }

        beAnvilVoxels = placed;
        return true;
    }
}

