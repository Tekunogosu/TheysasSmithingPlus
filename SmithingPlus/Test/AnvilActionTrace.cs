#nullable enable
using System.Threading;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SmithingPlus.Test;

/// <summary>
///     Traces every anvil action to the log, to find actions that appear to happen twice for one click.
///     <para>
///         Enabled by the <c>TraceAnvilActions</c> config flag; off by default and inert when off. It only
///         reads and logs, so it cannot change what the anvil does.
///     </para>
///     <para>
///         Each line carries the side, a per-side counter, the voxel aimed at, the tool mode, and the voxel
///         count before and after. The counts are what distinguish the two explanations: one entry whose
///         count drops by two means a single action did double work, whereas two entries for one click mean
///         the action ran twice. The side tells apart a client/server disagreement from a genuine duplicate,
///         since vanilla runs this path on both.
///     </para>
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(BlockEntityAnvil))]
[HarmonyPatchCategory(Core.AnvilTraceCategory)]
public static class AnvilActionTrace
{
    private static int clientCalls;
    private static int serverCalls;
    private static int announced;

    /// <summary>Says once per side that the trace is on, so a log carrying these lines is never a mystery.</summary>
    private static void AnnounceOnce(EnumAppSide side)
    {
        if (Interlocked.Exchange(ref announced, 1) == 1) return;
        Core.Logger?.Notification(
            "[AnvilTrace] Anvil action tracing is ON ({0}). This is a debug build; set TraceAnvilActions " +
            "to false in ModConfig/SmithingPlus.json to silence it.", side);
    }

    /// <summary>The grid and tool mode captured before the action, compared against after.</summary>
    public struct Before
    {
        public int Call;
        public byte[,,]? Voxels;
        public int VoxelCount;
        public int ToolMode;
        public byte VoxelMaterial;
        public bool Traced;
    }

    /// <summary>
    ///     How many cells one action of this tool mode is expected to change.
    ///     <para>
    ///         An upset moves a voxel: one cell cleared, one set, so two changes. A split removes one. A
    ///         heavy hit flattens one. Anything above these means the action ran more than once, which a
    ///         voxel count alone cannot show -- a move leaves the count identical.
    ///     </para>
    /// </summary>
    private static int ExpectedChanges(int toolMode)
    {
        return toolMode switch
        {
            0 => 1, // heavy hit
            1 or 2 or 3 or 4 => 2, // upset: cleared + set
            5 => 1, // split
            _ => -1 // unknown, do not judge
        };
    }

    [HarmonyPrefix]
    [HarmonyPatch("OnUseOver", typeof(IPlayer), typeof(Vec3i), typeof(BlockSelection))]
    [HarmonyPriority(Priority.First)]
    public static void Prefix_OnUseOver(BlockEntityAnvil __instance, out Before __state, IPlayer byPlayer,
        Vec3i voxelPos, BlockSelection blockSel)
    {
        __state = default;
        if (__instance.Api == null || voxelPos == null) return;
        AnnounceOnce(__instance.Api.Side);

        var isClient = __instance.Api.Side == EnumAppSide.Client;
        __state.Call = isClient
            ? Interlocked.Increment(ref clientCalls)
            : Interlocked.Increment(ref serverCalls);
        // A copy, not a reference: the action mutates this same array in place, so holding the reference
        // would compare the grid against itself and show nothing ever changed.
        __state.Voxels = (byte[,,])__instance.Voxels.Clone();
        __state.VoxelCount = __instance.Voxels.MaterialCount();
        __state.VoxelMaterial = InBounds(voxelPos) ? __instance.Voxels[voxelPos.X, voxelPos.Y, voxelPos.Z] : (byte)255;

        var slot = byPlayer?.InventoryManager?.ActiveHotbarSlot;
        __state.ToolMode = slot?.Itemstack?.Collectible?.GetToolMode(slot, byPlayer, blockSel) ?? -1;
        __state.Traced = true;

        Core.Logger?.Notification(
            "[AnvilTrace] {0} #{1} ENTER voxel=({2},{3},{4}) mat={5} toolMode={6} voxels={7} recipe={8} player={9}",
            __instance.Api.Side, __state.Call, voxelPos.X, voxelPos.Y, voxelPos.Z, __state.VoxelMaterial,
            __state.ToolMode, __state.VoxelCount, __instance.SelectedRecipeId, byPlayer?.PlayerName ?? "?");
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnUseOver", typeof(IPlayer), typeof(Vec3i), typeof(BlockSelection))]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix_OnUseOver(BlockEntityAnvil __instance, Before __state, IPlayer byPlayer,
        Vec3i voxelPos, BlockSelection blockSel)
    {
        if (!__state.Traced || __state.Voxels == null || __instance.Api == null) return;

        var before = __state.Voxels;
        var now = __instance.Voxels;
        var after = now.MaterialCount();

        // Which cells actually differ. This is what a voxel count cannot show: an upset moves a voxel, so
        // the count is unchanged whether the move happened once or twice, but the number of changed cells
        // doubles.
        var changes = new System.Text.StringBuilder();
        var changed = 0;
        for (var x = 0; x < 16; x++)
        for (var y = 0; y < 6; y++)
        for (var z = 0; z < 16; z++)
        {
            if (before[x, y, z] == now[x, y, z]) continue;
            changed++;
            if (changed <= 12)
                changes.Append($" ({x},{y},{z}):{before[x, y, z]}->{now[x, y, z]}");
        }

        // The anvil clears itself when the item is finished or has no metal left, which rewrites the whole
        // grid. That is not a doubled action, so it is named rather than flagged.
        var expected = ExpectedChanges(__state.ToolMode);
        var cleared = after == 0 && __state.VoxelCount > 0;
        var note = cleared
            ? "  (work item left the anvil)"
            : expected >= 0 && changed > expected
                ? $"  <== DOUBLED? expected {expected} changed cells, saw {changed}"
                : "";

        Core.Logger?.Notification(
            "[AnvilTrace] {0} #{1} EXIT  voxels={2} -> {3}  changedCells={4}{5}{6}",
            __instance.Api.Side, __state.Call, __state.VoxelCount, after, changed,
            cleared ? "" : changes.ToString(), note);
    }

    /// <summary>Logs the packet the client sends so a doubled packet is visible as two lines.</summary>
    [HarmonyPrefix]
    [HarmonyPatch("SendUseOverPacket")]
    public static void Prefix_SendUseOverPacket(BlockEntityAnvil __instance, IPlayer byPlayer, Vec3i voxelPos)
    {
        Core.Logger?.Notification("[AnvilTrace] Client PACKET voxel=({0},{1},{2})",
            voxelPos?.X ?? -1, voxelPos?.Y ?? -1, voxelPos?.Z ?? -1);
    }

    /// <summary>Logs the anvil's own interact entry, above OnUseOver, to show how often a click arrives.</summary>
    [HarmonyPrefix]
    [HarmonyPatch("OnPlayerInteract")]
    [HarmonyPriority(Priority.First)]
    public static void Prefix_OnPlayerInteract(BlockEntityAnvil __instance, IWorldAccessor world, IPlayer byPlayer,
        BlockSelection blockSel)
    {
        Core.Logger?.Notification("[AnvilTrace] {0} INTERACT selectionBox={1} shift={2} ctrl={3}",
            world?.Side, blockSel?.SelectionBoxIndex ?? -1,
            byPlayer?.Entity?.Controls?.ShiftKey, byPlayer?.Entity?.Controls?.CtrlKey);
    }

    private static bool InBounds(Vec3i pos)
    {
        return pos.X is >= 0 and < 16 && pos.Y is >= 0 and < 6 && pos.Z is >= 0 and < 16;
    }
}
