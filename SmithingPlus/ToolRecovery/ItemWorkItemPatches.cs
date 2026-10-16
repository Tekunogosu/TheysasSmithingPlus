#nullable enable
using HarmonyLib;
using JetBrains.Annotations;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SmithingPlus.ToolRecovery;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof (ItemWorkItem))]
[HarmonyPatchCategory(Core.ToolRecoveryCategory)]
public class ItemWorkItemPatches
{
    /// <summary>
    ///     Refuses a broken tool head on the anvil when <see cref="SmithingPlus.Config.ServerConfig.DontRepairBrokenToolHeads" /> is set.
    ///     <para>
    ///         This is a prefix because the vanilla method writes the work item's voxels and recipe onto the anvil
    ///         before returning. Cancelling afterwards would leave those voxels on an empty anvil for the next
    ///         placed workable to stack onto, while the player keeps the broken head.
    ///     </para>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(ItemWorkItem.TryPlaceOn))]
    public static bool Prefix_TryPlaceOn(ItemStack stack, BlockEntityAnvil beAnvil, ref ItemStack? __result)
    {
        if (!Core.Config.DontRepairBrokenToolHeads) return true;
        if (stack?.Collectible?.HasBehavior<CollectibleBehaviorBrokenToolHead>() != true) return true;
        if (!CollectibleBehaviorBrokenToolHead.IsBrokenToolHead(stack)) return true;
        (beAnvil.Api as ICoreClientAPI)?.TriggerIngameError(beAnvil, "needschiseling",
            Lang.Get($"{Core.ModId}:itemdesc-needschiseling"));
        __result = null;
        return false;
    }
}
