using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.Common.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SmithingPlus.ToolRecovery;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(CollectibleObject))]
[HarmonyPatchCategory(Core.ToolRecoveryCategory)]
public class ItemDamagedPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CollectibleObject.OnCreatedByCrafting))]
    [HarmonyPriority(-int.MaxValue)]
    public static void Postfix_OnCreatedByCrafting(
        ItemSlot[] allInputSlots,
        ItemSlot outputSlot,
        IRecipeBase byRecipe)
    {
        if (outputSlot.Itemstack == null) return;
        var brokenStack = allInputSlots.FirstOrDefault(slot =>
            slot.Itemstack?.GetBrokenCount() > 0 &&
            slot.Itemstack?.Collectible.HasBehavior<CollectibleBehaviorRepairableToolHead>() == true
        )?.Itemstack;
        if (brokenStack == null) return;
        var brokenCount = brokenStack.GetBrokenCount();
        if (brokenCount <= 0) return;
        if (brokenStack.Item?.IsRepairableTool() is not true) return;
        var repairedStack = brokenStack.GetRepairedToolStack();
        if (repairedStack == null) return;
        repairedStack.ResolveBlockOrItem((allInputSlots.FirstOrDefault()?.Inventory?.Api ?? Core.Api)
            .World);
        if (repairedStack.Collectible.Code != byRecipe.RecipeOutput.ResolvedItemStack?.Collectible.Code) return;
        foreach (var attributeKey in Core.Config.GetToolRepairForgettableAttributes)
            repairedStack.Attributes?.RemoveAttribute(attributeKey);
        var repairSmith = brokenStack.GetRepairSmith();
        if (repairSmith != null) repairedStack.SetRepairSmith(repairSmith);
        var smithingQuality = brokenStack.GetSmithingQuality();
        if (smithingQuality != 0) repairedStack.SetSmithingQuality(smithingQuality);
        var toolRepairPenaltyModifier = brokenStack.GetToolRepairPenaltyModifier();
        if (toolRepairPenaltyModifier != 0)
            repairedStack.SetToolRepairPenaltyModifier(toolRepairPenaltyModifier);
        var repairedAttributes = repairedStack.Attributes ?? new TreeAttribute();
        var outputAttributes = outputSlot.Itemstack.Attributes;
        foreach (var attribute in repairedAttributes)
            outputAttributes[attribute.Key] = attribute.Value;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CollectibleObject.DamageItem))]
    private static void Prefix_DamageItem(
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemSlot,
        int amount = 1,
        bool destroyOnZeroDurability = true)
    {
        if (world.Api.Side.IsClient())
            return;
        if (!destroyOnZeroDurability)
            return;
        var durability = itemSlot?.Itemstack?.GetRemainingDurability();
        if (!durability.HasValue || durability > amount) return;
        if (itemSlot.Itemstack?.Collectible.HasBehavior<CollectibleBehaviorRepairableTool>() != true) return;
        Core.Logger.VerboseDebug("Broken tool in InventoryID: {0}, Entity: {1}", itemSlot.Inventory?.InventoryID,
            byEntity.GetName());
        var entityPlayer = byEntity as EntityPlayer;
        var itemStack = itemSlot.Itemstack;
        var toolCode = itemStack?.Collectible.Code.ToString();
        var smithingRecipe = CacheHelper.GetOrAdd(Core.ToolToRecipeCache, toolCode,
            () => GetHeadSmithingRecipe(world.Api, itemStack));
        if (smithingRecipe == null)
        {
            Core.Logger.VerboseDebug("Head or tool smithing recipe not found for: {0}", toolCode);
            return;
        }

        var metalMaterial = itemStack?.GetOrCacheMetalMaterial(byEntity.Api);
        var workItem = metalMaterial?.WorkItem;
        if (workItem is null)
        {
            Core.Logger.VerboseDebug(
                $"Work item not found. Metal material: {metalMaterial?.IngotCode}, " +
                $"collectible: {itemStack?.Collectible.Code}");
            return;
        }

        Core.Logger.VerboseDebug("Found work item: {0}", workItem.Code);
        var wItemStack = new ItemStack(workItem);
        Core.Logger.VerboseDebug("Found smithing recipe: {0}",
            smithingRecipe.Output.ResolvedItemstack.Collectible.Code);
        var byteVoxels = ByteVoxelsFromRecipe(smithingRecipe, smithingRecipe.Output.ResolvedItemstack.StackSize);
        wItemStack.Attributes.SetBytes("voxels", BlockEntityAnvil.serializeVoxels(byteVoxels));
        wItemStack.Attributes.SetInt("selectedRecipeId", smithingRecipe.RecipeId);
        var cloneStack = itemStack?.Clone();
        cloneStack.CloneBrokenCount(itemStack, 1);
        wItemStack.SetRepairedToolStack(cloneStack);

        var gaveStack = false;
        if (entityPlayer != null) gaveStack = entityPlayer.TryGiveItemStack(wItemStack);
        if (!gaveStack) world.SpawnItemEntity(wItemStack, byEntity.Pos.XYZ);
        Core.Logger.VerboseDebug(gaveStack ? "Gave work item {0} to player {1}" : "Dropped work item {0} to player {1}",
            wItemStack.Collectible.Code, entityPlayer?.Player.PlayerName);
        itemSlot.MarkDirty();
    }

    private static SmithingRecipe GetHeadSmithingRecipe(ICoreAPI api, ItemStack itemStack)
    {
        var toolHead = GetToolHead(api, itemStack);
        var smithingRecipe = toolHead.GetSmithingRecipe(api);
        return smithingRecipe;
    }

    /// <summary>
    ///     The tool head this item is crafted from: the repairable-tool-head ingredient of the first grid
    ///     recipe producing a single one of it. Falls back to the item itself when there is no such recipe.
    /// </summary>
    private static ItemStack GetToolHead(ICoreAPI api, ItemStack itemStack)
    {
        var toolHead = FindToolHead(api, itemStack);
        if (toolHead == null)
        {
            toolHead = itemStack;
            Core.Logger.VerboseDebug("Tool head not found for: {0}", itemStack);
        }

        Core.Logger.VerboseDebug("Tool head: {0}", toolHead);
        return toolHead;
    }

    private static ItemStack? FindToolHead(ICoreAPI api, ItemStack itemStack)
    {
        var recipes = api.World.GridRecipes;
        for (var i = 0; i < recipes.Count; i++)
        {
            var recipe = recipes[i];
            if (recipe?.Output?.ResolvedItemStack?.StackSize != 1) continue;
            if (recipe.Output.ResolvedItemStack.Satisfies(itemStack) != true) continue;

            var ingredients = recipe.ResolvedIngredients;
            if (ingredients == null) continue;
            for (var j = 0; j < ingredients.Length; j++)
            {
                var stack = ingredients[j]?.ResolvedItemStack;
                if (stack?.Collectible?.HasBehavior<CollectibleBehaviorRepairableToolHead>() == true) return stack;
            }

            // The original took the first single-output recipe and then looked inside only that one.
            return null;
        }

        return null;
    }

    private static byte[,,] ByteVoxelsFromRecipe(SmithingRecipe recipe, int stackSize = 1)
    {
        var recipeVoxels = recipe.Voxels;
        if (Core.Config.BrokenToolVoxelPercent < 0.2)
            Core.Logger.Warning(
                $"[ItemDamagedPatches#ByteVoxelsFromRecipe] Config setting {nameof(Core.Config.BrokenToolVoxelPercent)}" +
                $"has a very low value, your broken tools well be almost or fully empty.");
        ;
        var byteVoxels = recipeVoxels.ErodeToPercentage(Core.Config.BrokenToolVoxelPercent);
        return byteVoxels;
    }
}