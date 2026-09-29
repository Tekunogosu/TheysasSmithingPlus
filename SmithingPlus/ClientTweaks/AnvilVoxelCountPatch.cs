using System.Text;
using HarmonyLib;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SmithingPlus.ClientTweaks;

[HarmonyPatch(typeof(BlockEntityAnvil))]
[HarmonyPatchCategory(Core.ClientTweaksCategories.AnvilShowRecipeVoxels)]
public class AnvilVoxelCountPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockEntityAnvil.GetBlockInfo))]
    [HarmonyPriority(Priority.VeryLow)]
    public static void Postfix_GetBlockInfo(BlockEntityAnvil __instance, IPlayer forPlayer, StringBuilder dsc)
    {
        var activeStack = forPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (activeStack == null)
            return;
        if (activeStack.Collectible is not ItemHammer &&
            activeStack.Collectible.GetCollectibleInterface<IAnvilWorkable>()?.CanWork(activeStack) != true)
            return;
        if (__instance.recipeVoxels == null) return;
        if (!__instance.CanWorkCurrent) return;
        var voxelCount = CacheHelper.GetOrAdd(Core.RecipeVoxelCountCache(__instance.Api), __instance.SelectedRecipeId, () =>
        {
            Core.Logger.VerboseDebug("Calculating voxel count for: {0}", __instance.SelectedRecipeId);
            return __instance.recipeVoxels.VoxelCount();
        });
        var currentVoxelCount = __instance.Voxels?.MaterialCount();
        var currentSlagCount = __instance.Voxels?.SlagCount();
        dsc.AppendLine(Lang.Get($"{Core.ModId}:blockdesc-voxelcount", currentVoxelCount, voxelCount));
        if (currentSlagCount > 0) dsc.AppendLine(Lang.Get($"{Core.ModId}:blockdesc-slagcount", currentSlagCount));
    }
}