#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SmithingPlus.HammerTweaks;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(BlockEntityAnvil))]
[HarmonyPatchCategory(Core.HammerTweaksCategory)]
public static class BlockEntityAnvilPatch
{
    /// <summary>Width and depth of an anvil's voxel grid, as BlockEntityAnvil sizes it.</summary>
    private const int VoxelWidth = 16;

    /// <summary>Height of an anvil's voxel grid.</summary>
    private const int VoxelHeight = 6;

    [HarmonyPrefix]
    [HarmonyPatch("OnPlayerInteract")]
    public static bool Prefix_OnPlayerInteract(
        BlockEntityAnvil __instance,
        ref ItemStack ___workItemStack,
        ref bool __result,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (Core.Config.RotationRequiresTongs && !byPlayer.HasHeatResistantHandGear())
        {
            __result = false;
            return false;
        }

        var activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var itemStack = activeSlot.Itemstack;
        if (itemStack?.Collectible is not ItemHammer itemHammer)
            return true;
        var flipItemToolMode = itemStack.TempAttributes.GetInt(ModTempAttributes.FlipItemToolMode);
        if (itemHammer.GetToolMode(activeSlot, byPlayer, blockSel) != flipItemToolMode)
            return true;
        if (byPlayer.Entity.Controls.ShiftKey)
        {
            __instance.RotateWorkItem(byPlayer.Entity.Controls.CtrlKey);
            __result = true;
            return false;
        }

        __instance.FlipWorkItem(___workItemStack, GetFacingHorizontalAxis(byPlayer));
        __result = true;
        return false;
    }

    private static EnumAxis GetFacingHorizontalAxis(IPlayer byPlayer)
    {
        var yaw = GameMath.Mod(byPlayer.Entity.Pos.Yaw, 2 * Math.PI);
        var facing = BlockFacing.EAST.FaceWhenRotatedBy(0.0f, (float)(yaw - Math.PI), 0.0f);
        var rotation = facing.Index switch
        {
            0 => EnumAxis.X, // North
            1 => EnumAxis.Z, // East
            2 => EnumAxis.X, // South
            3 => EnumAxis.Z, // West
            _ => EnumAxis.X // Default to X to avoid crashes
        };
        return rotation;
    }

    [HarmonyReversePatch]
    [HarmonyPatch("RegenMeshAndSelectionBoxes")]
    public static void RegenMeshAndSelectionBoxes(BlockEntityAnvil __instance)
    {
        throw new NotImplementedException("Reverse patch stub");
    }

    [HarmonyReversePatch]
    [HarmonyPatch("HasAnyMetalVoxel")]
    public static bool HasAnyMetalVoxel(BlockEntityAnvil __instance)
    {
        throw new NotImplementedException("Reverse patch stub");
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockEntityAnvil.recipeVoxels), MethodType.Getter)]
    public static bool BlockEntityAnvil_recipeVoxels_Patch(BlockEntityAnvil __instance, ref bool[,,] __result)
    {
        if (__instance.WorkItemStack == null) return true;
        if (__instance.SelectedRecipe == null)
            return true;
        __result = __instance.SelectedRecipe.Voxels;
        var rotationAxis = __instance.WorkItemStack.GetHorizontalRotationAxis();

        if (rotationAxis != null)
        {
            //var rotationValue = __instance.WorkItemStack.GetHorizontalRotation(rotationAxis.Value);
            int? minY = __instance.WorkItemStack.Attributes.GetInt(ModStackAttributes.MinY);
            __result = __result.ToByteArray()
                .RotateAroundAxis(rotationAxis.Value, ref minY).ToBoolArray();
        }

        var rotation = __instance.rotation;
        for (var i = 0; i < rotation / 90; i++)
            __result = __result.ToByteArray().RotateAroundAxis(EnumAxis.Y).ToBoolArray();
        return false;
    }

    private static bool RotateWorkItem(this BlockEntityAnvil beAnvil, bool ccw)
    {
        var rotatedVoxels = RotateAroundAxis(beAnvil.Voxels, EnumAxis.Y);
        if (ccw) rotatedVoxels = rotatedVoxels.RotateAroundAxis(EnumAxis.Y);
        beAnvil.rotation = (beAnvil.rotation + (ccw ? 180 : 90)) % 360;
        beAnvil.Voxels = rotatedVoxels;
        RegenMeshAndSelectionBoxes(beAnvil);
        beAnvil.MarkDirty();
        return true;
    }

    private static byte[,,] RotateAroundAxis(this byte[,,] beAnvilVoxels, EnumAxis axis)
    {
        int? minY = null;
        return beAnvilVoxels.RotateAroundAxis(axis, ref minY);
    }

    private static void FlipWorkItem(this BlockEntityAnvil beAnvil, ItemStack workItemStack, EnumAxis axis)
    {
        if (axis == EnumAxis.Y) throw new ArgumentException("Axis Y is not supported for flipping.");
        if (!HasAnyMetalVoxel(beAnvil)) return;

        // Rotated for the offset alone, which comes back through minY; the rotated grid itself is not
        // wanted. Measuring against the recipe outline combined with the work item keeps the piece and its
        // outline settling by the same amount, so the two stay aligned after the flip.
        int? minY = null;
        _ = beAnvil.recipeVoxels.ToByteArray().Union(beAnvil.Voxels).RotateAroundAxis(axis, ref minY);

        var rotatedVoxels = beAnvil.Voxels.RotateAroundAxis(axis, ref minY);
        if (minY.HasValue) beAnvil.WorkItemStack.Attributes.SetInt(ModStackAttributes.MinY, minY.Value);
        beAnvil.Voxels = rotatedVoxels;
        workItemStack.FlipHorizontalRotationAttribute(axis);
        RegenMeshAndSelectionBoxes(beAnvil);
        beAnvil.MarkDirty();
    }

    private static void FlipHorizontalRotationAttribute(this ItemStack workItemStack, EnumAxis axis)
    {
        var rotationAttribute = axis.HorizontalRotationAttribute();
        var rotation = workItemStack.Attributes.GetInt(rotationAttribute);
        rotation = (rotation + 180) % 360;
        workItemStack.Attributes.SetInt(rotationAttribute, rotation);
    }

    private static void ResolveRotations(this BlockEntityAnvil beAnvil)
    {
        if (beAnvil.Api.World.Side != EnumAppSide.Server) return;
        var workItemStack = beAnvil.WorkItemStack;
        var rotationX = workItemStack.Attributes.GetInt(ModStackAttributes.RotationX);
        var rotationZ = workItemStack.Attributes.GetInt(ModStackAttributes.RotationZ);
        if (rotationX % 360 != 180 || rotationZ % 360 != 180) return;
        var rotationY = beAnvil.rotation;
        rotationY = (rotationY + 180) % 360;
        workItemStack.Attributes.SetInt(ModStackAttributes.RotationX, 0);
        workItemStack.Attributes.SetInt(ModStackAttributes.RotationZ, 0);
        beAnvil.rotation = rotationY;
    }

    private static EnumAxis? GetHorizontalRotationAxis(this ItemStack workItemStack)
    {
        var rotationX = workItemStack.Attributes.GetInt(ModStackAttributes.RotationX);
        var rotationZ = workItemStack.Attributes.GetInt(ModStackAttributes.RotationZ);
        if ((rotationX != 0 && rotationZ != 0) ||
            (rotationX % 360 == 0 && rotationZ % 360 == 0))
            return null;
        return rotationX % 360 == 0 ? EnumAxis.Z : EnumAxis.X;
    }

    private static string HorizontalRotationAttribute(this EnumAxis axis)
    {
        return axis switch
        {
            EnumAxis.X => ModStackAttributes.RotationX,
            EnumAxis.Z => ModStackAttributes.RotationZ,
            EnumAxis.Y => throw new ArgumentException("Axis Y rotations are not horizontal."),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, null)
        };
    }

    private static int GetHorizontalRotation(this ItemStack workItemStack, EnumAxis axis)
    {
        var rotationAttribute = axis.HorizontalRotationAttribute();
        return workItemStack.Attributes.GetInt(rotationAttribute);
    }

    // Allow to override and store the minY of rotation to reuse when rotating the mesh
    private static byte[,,] RotateAroundAxis(this byte[,,] voxels, EnumAxis axis, ref int? minY)
    {
        if ((int)axis > 2)
            throw new ArgumentOutOfRangeException(nameof(axis), axis, "Axis must be X, Y, or Z.");
        var rotatedVoxels = new byte[16, 6, 16];
        // Perform normal rotation around the Y axis. (From BlockEntityAnvil.RotateWorkItem)
        if (axis == EnumAxis.Y)
        {
            for (var index1 = 0; index1 < 16; ++index1)
            for (var index2 = 0; index2 < 6; ++index2)
            for (var index3 = 0; index3 < 16; ++index3)
                rotatedVoxels[index3, index2, index1] = voxels[16 - index1 - 1, index2, index3];

            return rotatedVoxels;
        }

        // Flipping about the grid's centre: doubling the centre and subtracting the coordinate mirrors it,
        // which for a half-integer centre lands exactly on another cell, so no rounding is involved.
        // X rotation mirrors Y and Z; Z rotation mirrors X and Y.
        const int mirrorY = VoxelHeight - 1;
        const int mirrorXz = VoxelWidth - 1;
        var aroundX = axis == EnumAxis.X;

        // Written into a scratch grid in one pass rather than collected as points and replayed. The offset
        // that settles the result back onto the anvil needs the lowest occupied row, which is tracked while
        // writing instead of scanned for afterwards.
        var mirrored = new byte[VoxelWidth, VoxelHeight, VoxelWidth];
        var lowestY = int.MaxValue;
        for (var x = 0; x < VoxelWidth; x++)
        for (var y = 0; y < VoxelHeight; y++)
        for (var z = 0; z < VoxelWidth; z++)
        {
            var value = voxels[x, y, z];
            if (value == 0) continue;

            var newX = aroundX ? x : mirrorXz - x;
            var newY = mirrorY - y;
            var newZ = aroundX ? mirrorXz - z : z;

            mirrored[newX, newY, newZ] = value;
            if (newY < lowestY) lowestY = newY;
        }

        // Settle the shape back down so its lowest voxel rests at y = 0. The caller keeps this offset so a
        // later mesh rotation of the same work item shifts by the same amount.
        //
        // An empty grid still records the offset, as the scan this replaced did, so a caller that pins the
        // value sees the same thing either way. Nothing is then written, since there is nothing to write.
        var drop = minY ??= lowestY;
        if (lowestY == int.MaxValue) return rotatedVoxels;
        for (var x = 0; x < VoxelWidth; x++)
        for (var y = 0; y < VoxelHeight; y++)
        for (var z = 0; z < VoxelWidth; z++)
        {
            var value = mirrored[x, y, z];
            if (value == 0) continue;
            var finalY = y - drop;
            if (finalY is >= 0 and < VoxelHeight) rotatedVoxels[x, finalY, z] = value;
        }

        return rotatedVoxels;
    }
}