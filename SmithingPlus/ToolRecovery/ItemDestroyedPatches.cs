#nullable enable
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.Common.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SmithingPlus.ToolRecovery;

/// <summary>
///     Drops a broken tool head when a repairable tool is destroyed.
///     <para>
///         This reacts to the tool actually being destroyed rather than predicting destruction from the
///         damage about to be applied. Predicting it in <see cref="CollectibleObject.DamageItem" /> is
///         wrong whenever a behavior sets <see cref="EnumHandling.PreventDefault" /> there and routes the
///         damage elsewhere: the tool then survives, or breaks for a reason of the behavior's own, while
///         the head has already dropped. Toolsmith's tinkered tools do exactly this, damaging head, handle
///         and binding separately and only calling <see cref="CollectibleObject.DestroyItem" /> when the
///         head itself breaks.
///     </para>
///     <para>
///         The work is split across a prefix and a postfix because <c>DestroyItem</c> walks the
///         collectible's behaviors and any one of them can cancel the destruction, while each behavior is
///         handed a fresh <see cref="EnumHandling" /> and so cannot observe that cancellation. Only after
///         the call has returned is it known whether the tool was really destroyed, which is why an
///         <c>OnDestroyItem</c> override cannot do this correctly.
///     </para>
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(CollectibleObject))]
[HarmonyPatchCategory(Core.ToolRecoveryCategory)]
public class ItemDestroyedPatches
{
    /// <summary>What the prefix hands the postfix: null when this destruction is not ours to act on.</summary>
    public class DestroyState
    {
        public ItemStack Stack = null!;
        public string? InventoryId;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CollectibleObject.DestroyItem))]
    public static void Prefix_DestroyItem(
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemSlot,
        out DestroyState? __state)
    {
        __state = null;
        if (world.Api.Side.IsClient()) return;
        var itemStack = itemSlot?.Itemstack;
        if (itemStack == null) return;
        // Tool heads are themselves repairable, and a broken head is not a tool that yields another head.
        if (!itemStack.Collectible.HasBehavior<CollectibleBehaviorRepairableTool>()) return;
        if (itemStack.Collectible.HasBehavior<CollectibleBehaviorRepairableToolHead>()) return;
        if (itemStack.Collectible.HasBehavior<CollectibleBehaviorBrokenToolHead>()) return;
        // Destroyed with durability to spare means something other than wear destroyed it.
        if (itemStack.GetRemainingDurability() is not <= 0) return;
        __state = new DestroyState
        {
            Stack = itemStack,
            InventoryId = itemSlot?.Inventory?.InventoryID
        };
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CollectibleObject.DestroyItem))]
    public static void Postfix_DestroyItem(
        IWorldAccessor world,
        Entity byEntity,
        ItemSlot itemSlot,
        DestroyState? __state)
    {
        if (__state == null) return;
        // The slot still holding the very stack the prefix saw means a behavior cancelled the destruction.
        // Comparing by reference rather than for an empty slot keeps this correct when the game or another
        // mod refills the slot with a replacement tool.
        if (ReferenceEquals(itemSlot?.Itemstack, __state.Stack)) return;
        DropBrokenToolHead(world, byEntity, itemSlot, __state);
    }

    private static void DropBrokenToolHead(IWorldAccessor world, Entity byEntity, ItemSlot? itemSlot,
        DestroyState state)
    {
        var itemStack = state.Stack;
        Core.Logger.VerboseDebug("Broken tool in InventoryID: {0}, Entity: {1}", state.InventoryId,
            byEntity.GetName());
        var entityPlayer = byEntity as EntityPlayer;
        var toolCode = itemStack.Collectible.Code.ToString();
        var smithingRecipe = CacheHelper.GetOrAddNullable(Core.ToolToRecipeCache, toolCode,
            () => ItemDamagedPatches.GetHeadSmithingRecipe(world.Api, itemStack));
        if (smithingRecipe == null)
        {
            Core.Logger.VerboseDebug("Head or tool smithing recipe not found for: {0}", toolCode);
            return;
        }

        var metalMaterial = itemStack.GetOrCacheMetalMaterial(byEntity.Api);
        var workItem = metalMaterial?.WorkItem;
        if (workItem is null)
        {
            Core.Logger.VerboseDebug(
                $"Work item not found. Metal material: {metalMaterial?.IngotCode}, " +
                $"collectible: {itemStack.Collectible.Code}");
            return;
        }

        var recipeOutput = smithingRecipe.Output?.ResolvedItemstack;
        if (recipeOutput == null)
        {
            Core.Logger.VerboseDebug("Smithing recipe {0} has no resolved output", smithingRecipe.RecipeId);
            return;
        }

        Core.Logger.VerboseDebug("Found work item: {0}", workItem.Code);
        var wItemStack = new ItemStack(workItem);
        Core.Logger.VerboseDebug("Found smithing recipe: {0}", recipeOutput.Collectible.Code);
        var byteVoxels = ItemDamagedPatches.ByteVoxelsFromRecipe(smithingRecipe, recipeOutput.StackSize);
        wItemStack.Attributes.SetBytes("voxels", BlockEntityAnvil.serializeVoxels(byteVoxels));
        wItemStack.Attributes.SetInt("selectedRecipeId", smithingRecipe.RecipeId);
        var cloneStack = itemStack.Clone();
        cloneStack.CloneBrokenCount(itemStack, 1);
        wItemStack.SetRepairedToolStack(cloneStack);

        var gaveStack = false;
        if (entityPlayer != null) gaveStack = entityPlayer.TryGiveItemStack(wItemStack);
        if (!gaveStack) world.SpawnItemEntity(wItemStack, byEntity.Pos.XYZ);
        Core.Logger.VerboseDebug(gaveStack ? "Gave work item {0} to player {1}" : "Dropped work item {0} to player {1}",
            wItemStack.Collectible.Code, entityPlayer?.Player.PlayerName);
        itemSlot?.MarkDirty();
    }
}
