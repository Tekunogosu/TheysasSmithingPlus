using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.CastingTweaks;
using SmithingPlus.SmithWithBits;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SmithingPlus.ClientTweaks;
#nullable enable

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public partial class HandbookInfoPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CollectibleBehaviorHandbookTextAndExtraInfo), "addCreatedByInfo")]
    public static void PatchBitsInfo(
        CollectibleBehaviorHandbookTextAndExtraInfo __instance,
        ICoreClientAPI capi,
        ItemStack[] allStacks,
        ActionConsumable<string> openDetailPageFor,
        ItemStack stack,
        List<RichTextComponentBase> components)
    {
        if (stack.Collectible is not ItemNugget ||
            !stack.Collectible.HasBehavior<CollectibleBehaviorWorkableNugget>()) return;
        if (stack.Collectible.CombustibleProps?.MeltingPoint > MaxFuelBurnTemp(capi, allStacks)) return;
        var moldStacks = allStacks.Where(s =>
                s.Collectible is BlockToolMold &&
                GetStackForVariant(capi, s, stack.Collectible.LastCodePart()) != null)
            .OrderBy(s => s.Collectible.Code.Domain == "game" ? -100 : 0)
            .ThenBy(s => s.ItemAttributes["requiredUnits"].AsInt(100))
            .ToArray();
        var haveText = components.Count > 0;
        if (moldStacks.Length <= 0) return;
        AddHeading(components, capi, "Can be cast in", ref haveText);
        
        Array.ForEach(moldStacks, s =>
            s.StackSize = ToolMoldUnitsPatch.GetPatchedRequiredUnits(capi, s.Block, stack));
        AddAlignedSlideshows(capi, openDetailPageFor, components, moldStacks.ToList());
    }
}