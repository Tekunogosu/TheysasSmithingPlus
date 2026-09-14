#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using SmithingPlus.Common.Metal;
using SmithingPlus.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SmithingPlus.CastingTweaks;

public class CollectibleBehaviorCastToolHead(CollectibleObject collObj) : CollectibleBehavior(collObj), IAnvilWorkable
{
    private ICoreAPI? _api;

    /// <summary>
    ///     The API, read once from the collectible's private field and kept; see the same property on
    ///     <see cref="Common.CollectibleBehaviorAnvilWorkable" />. This one is also on the path the anvil's
    ///     interaction help walks, through <see cref="GetRequiredAnvilTier" />.
    /// </summary>
    private ICoreAPI? Api => _api ??= collObj.GetLoadedApi();

    public int GetRequiredAnvilTier(ItemStack stack)
    {
        var api = Api;
        return api == null ? 0 : stack.GetOrCacheMetalMaterial(api)?.Tier ?? 0;
    }

    public List<SmithingRecipe> GetMatchingRecipes(ItemStack stack)
    {
        var api = Api;
        if (api == null) return [];
        var smithingRecipe = stack.GetSmithingRecipe(api);
        return smithingRecipe != null ? [smithingRecipe] : [];
    }

    public bool CanWork(ItemStack stack)
    {
        if (!stack.IsCastTool())
            return false;
        var api = Api;
        if (api == null) return false;
        var temperature = stack.Collectible.GetTemperature(api.World, stack);
        var threshold = GetWorkableTemperature(stack);
        Core.Logger.VerboseDebug(
            $"[CollectibleBehaviorCastToolHead#CanWork] {stack.Collectible.Code} - Temperature: {temperature}, Threshold: {threshold}");
        return temperature >= threshold;
    }

    public ItemStack? TryPlaceOn(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        if (beAnvil.WorkItemStack != null || !CanWork(stack))
            return null;
        var recipe = stack.GetSingleSmithingRecipe(beAnvil.Api);
        var durabilityPercent = stack.GetDurabilityPercentage();
        if (recipe == null || durabilityPercent == null) return null;
        var voxels = recipe.Voxels.ErodeToPercentage(durabilityPercent.Value);
        var world = beAnvil.Api.World;
        var random = world.Rand;
        var slagCount = (int)Math.Ceiling(0.2f * voxels.MaterialCount());
        voxels.AddSlag(slagCount, random);
        var workItemStack = stack.GetOrCacheMetalMaterial(beAnvil.Api)?.WorkItemStack;
        if (workItemStack == null)
            return null;
        beAnvil.Voxels = voxels;
        beAnvil.SelectedRecipeId = recipe.RecipeId;
        var temperature = stack.Collectible.GetTemperature(world, stack);
        workItemStack.Collectible.SetTemperature(world, workItemStack, temperature);
        return workItemStack;
    }

    public ItemStack? GetBaseMaterial(ItemStack stack)
    {
        var api = Api;
        if (api == null) return null;
        var metalMaterial = stack.GetOrCacheMetalMaterial(api);
        Debug.Write(
            $"[CollectibleBehaviorCastToolHead#GetBaseMaterial] {stack.Collectible.Code} -> {metalMaterial?.IngotCode}");
        return metalMaterial?.IngotStack;
    }

    public EnumHelveWorkableMode GetHelveWorkableMode(ItemStack stack, BlockEntityAnvil beAnvil)
    {
        return EnumHelveWorkableMode.TestSufficientVoxelsWorkable;
    }

    public int VoxelCountForHandbook(ItemStack stack)
    {
        var api = Api;
        if (api == null) return 0;
        var voxels = stack.GetSingleSmithingRecipe(api)?.Voxels.ToByteArray();
        return voxels?.MaterialCount() ?? 0;
    }

    public override void GetHeldItemName(StringBuilder dsc, ItemStack itemStack)
    {
        base.GetHeldItemName(dsc, itemStack);
        if (!itemStack.IsCastTool())
            return;
        var toolName = dsc.ToString();
        dsc.Clear();
        dsc.AppendLine(Lang.Get("Cast {0}", toolName.ToLower()));
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (!inSlot.Itemstack.IsCastTool())
            return;
        dsc.AppendLine(Lang.Get($"{Core.ModId}:setting-casttooldurabilitypenalty") +
                       $": {100 * Core.Config.CastToolDurabilityPenalty}%");
        dsc.AppendLine(Lang.Get($"{Core.ModId}:itemdesc-needsrefining"));
        var workableTemp = GetWorkableTemperature(inSlot.Itemstack);
        var temperature = inSlot.Itemstack?.Collectible.GetTemperature(world, inSlot.Itemstack);
        dsc.AppendLine(Lang.Get("Workable Temperature: {0}",
            workableTemp > 0
                ? temperature > workableTemp
                    ? $"<font color=\"{Constants.AnvilWorkableColor}\">{Math.Round(workableTemp)}\u00B0C</font>"
                    : $"{Math.Round(workableTemp)}\u00B0C"
                : Lang.Get($"{Core.ModId}:itemdesc-temp-always")));
    }

    /// <summary>
    ///     The temperature at which the cast head becomes workable: the ingot's stated
    ///     <c>workableTemperature</c> if it has one, otherwise half the melting point.
    ///     <para>
    ///         The stated value is read first so the melting point is only queried when it will be used.
    ///         This runs per frame while the item's tooltip is open.
    ///     </para>
    /// </summary>
    private float GetWorkableTemperature(ItemStack itemStack)
    {
        var api = Api;
        if (api == null) return 0f;

        var metalIngot = itemStack.GetOrCacheMetalMaterial(api)?.IngotItem;
        var workableAttr = metalIngot?.Attributes?["workableTemperature"];
        if (workableAttr?.Exists == true)
        {
            var stated = workableAttr.AsFloat(float.NaN);
            if (!float.IsNaN(stated)) return stated;
        }

        var querySlot = new DummySlot(itemStack);
        var meltingPoint = metalIngot?.GetMeltingPoint(api.World, null, querySlot)
                           ?? itemStack.Collectible.GetMeltingPoint(api.World, null, querySlot);
        return meltingPoint / 2f;
    }
}