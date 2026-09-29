using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SmithingPlus.CastingTweaks;
using SmithingPlus.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus.ClientTweaks;
#nullable enable

public partial class HandbookInfoPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
    public static void PatchFromMoldInfo(
        CollectibleBehaviorHandbookTextAndExtraInfo __instance,
        ICoreClientAPI capi,
        ItemStack[] allStacks,
        ActionConsumable<string> openDetailPageFor,
        ItemStack stack,
        List<RichTextComponentBase> components)
    {
        var moldStacks = CacheHelper.GetOrAdd(
            Core.MoldStacksCache(capi),
            stack.Collectible.Code.ToString(),
            () => allStacks.Where(s =>
                    s.Collectible is BlockToolMold &&
                    stack.Collectible.FirstCodePart().Equals(ToolMoldType(s.Collectible)))
                .OrderBy(s => s.Collectible.Code.Domain == "game" ? -100 : 0)
                .ThenBy(s => s.ItemAttributes["requiredUnits"].AsInt())
                .ToArray()
        );
        if (moldStacks.Length <= 0) return;
        AddSubHeading(components, capi, openDetailPageFor,
            $"{Lang.Get("Metal molding")} {Lang.Get("requires")}",
            "craftinginfo-smelting");
        AddAlignedSlideshows(capi, openDetailPageFor, components, moldStacks.ToList());
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
    public static void PatchMoldInfo(
        CollectibleBehaviorHandbookTextAndExtraInfo __instance,
        ICoreClientAPI capi,
        ItemStack[] allStacks,
        ActionConsumable<string> openDetailPageFor,
        ItemStack stack,
        List<RichTextComponentBase> components)
    {
        if (stack.Collectible is not BlockToolMold) return;
        var maxFuelBurnTemp = MaxFuelBurnTemp(capi, allStacks);

        // This is a hack, should refactor this to use per-metal required units in the future
        var copperIngot = capi.World.GetItem(new AssetLocation("game:ingot-copper"));
        if (copperIngot == null)
            return;
        var copperStack = new ItemStack(copperIngot);

        var mold = stack.Collectible;
        int requiredUnits;
        if (mold is Block moldBlock && Core.Config.DynamicMoldUnits)
            requiredUnits = ToolMoldUnitsPatch.GetPatchedRequiredUnits(capi, moldBlock, copperStack);
        else requiredUnits = mold.Attributes["requiredUnits"].AsInt(100);

        var castStacks = StacksFromCode(capi, stack, out var existingMetalVariants);
        // Use linq search to find all metal bit stacks
        var metalBitStacks = Core.GetOrCreateMetalBitStacks(capi, () =>
            allStacks.Where(s =>
                s.Collectible.Code.Path.Contains("metalbit") &&
                Enumerable.Contains(existingMetalVariants, s.Collectible.LastCodePart()) &&
                s.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack != null &&
                s.Collectible.CombustibleProps.SmeltingType == EnumSmeltType.Smelt &&
                s.Collectible.CombustibleProps.MeltingPoint <= maxFuelBurnTemp
            ).ToArray());
        var castableMetalVariants = Core.GetOrCreateCastableMetalVariants(capi, () =>
            metalBitStacks.Select(s => s.Collectible.Variant["metal"]).ToArray());

        var castStackList = castStacks.Where(s => castableMetalVariants.Contains(s.Collectible.LastCodePart()))
            .ToList();

        var haveText = components.Count > 0;
        if (castStackList.Count > 0)
        {
            AddHeading(components, capi, "Mold for", ref haveText);
            AddAlignedSlideshows(capi, openDetailPageFor, components, castStackList);
        }

        if (metalBitStacks.Length <= 0) return;
        {
            AddHeading(components, capi, "Requires for casting", ref haveText);
            Array.ForEach(metalBitStacks, s =>
                s.StackSize =
                    (int)Math.Ceiling(requiredUnits /
                                      (100f / (s.Collectible.CombustibleProps?.SmeltedRatio ?? 5))));
            AddAlignedSlideshows(capi, openDetailPageFor, components, metalBitStacks.ToList());
        }
    }

    private static void AddAlignedSlideshows(
        ICoreClientAPI capi,
        ActionConsumable<string> openDetailPageFor,
        List<RichTextComponentBase> components,
        List<ItemStack> stacks)
    {
        var firstPadding = 10;
        while (stacks.Count > 0)
        {
            var dstack = stacks[0];
            stacks.RemoveAt(0);

            var slideshowStack = new SlideshowItemstackTextComponent(
                capi, dstack, stacks, 40, EnumFloat.Inline,
                cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
            {
                ShowStackSize = true,
                PaddingLeft = firstPadding
            };
            firstPadding = 0;
            components.Add(slideshowStack);
        }
    }
}