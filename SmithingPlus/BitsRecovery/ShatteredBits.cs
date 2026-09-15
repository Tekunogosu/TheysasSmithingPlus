#nullable enable
using System;
using SmithingPlus.Common.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SmithingPlus.BitsRecovery;

/// <summary>
///     What a broken item is worth in metal bits.
///     <para>
///         This lives with the bits-recovery feature rather than among the stack utilities: it is the
///         mod's payout rule, reading <see cref="Core.Config" /> and deciding what a player gets back,
///         not a general fact about an <see cref="ItemStack" />.
///     </para>
/// </summary>
public static class ShatteredBits
{
    /// <summary>
    ///     The metal bits recovered when this broken or shattered stack is destroyed, or null when it is
    ///     worth none. Scaled by the stack's remaining durability, so a nearly spent tool pays out least.
    /// </summary>
    public static ItemStack? GetShatteredBitsStack(this ItemStack brokenStack, ICoreAPI api)
    {
        var metalMaterial = brokenStack.GetOrCacheMetalMaterial(api);
        if (metalMaterial?.MetalBitStack == null)
            return null;

        var voxelsInStack = VoxelsIn(brokenStack, api);
        var durabilityPercentage = brokenStack.GetDurabilityPercentage() ?? 1f;
        var reducedVoxels = voxelsInStack * durabilityPercentage;
        var recoveredBits = (int)MathF.Floor(reducedVoxels / Core.Config.VoxelsPerBit);

        if (recoveredBits <= 0)
            return null;

        var bitsStack = metalMaterial.MetalBitStack.Clone();
        bitsStack.StackSize = recoveredBits;
        return bitsStack;
    }

    /// <summary>
    ///     The material voxels the whole stack holds. A work item carries its own voxels; anything else is
    ///     priced through its cheapest smithing recipe, so the mechanic cannot turn an expensive item into a
    ///     cheap recipe's worth of material.
    /// </summary>
    private static int VoxelsIn(ItemStack brokenStack, ICoreAPI api)
    {
        if (brokenStack.Collectible is ItemWorkItem)
        {
            var bytes = brokenStack.Attributes.GetBytes("voxels");
            return BlockEntityAnvil.deserializeVoxels(bytes).MaterialCount();
        }

        return (brokenStack.VoxelCostPerItem(api) ?? 0) * brokenStack.StackSize;
    }
}
