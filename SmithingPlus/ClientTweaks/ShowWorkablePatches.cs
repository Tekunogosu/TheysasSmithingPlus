using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SmithingPlus.ClientTweaks;

[HarmonyPatchCategory(Core.ClientTweaksCategories.ShowWorkablePatches)]
public partial class ShowWorkablePatches
{
    private const string TemperatureRegexPattern = @"(\d+(.*)°C)";
    private const string TemperatureRangeRegexPattern = @"(\s*\(\d+°C\s*-\s*\d+°C\))";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BlockEntityAnvil), nameof(BlockEntityAnvil.GetBlockInfo))]
    [HarmonyPriority(Priority.VeryLow)]
    public static void Postfix_BlockEntityAnvil_GetBlockInfo(BlockEntityAnvil __instance, IPlayer forPlayer,
        StringBuilder dsc)
    {
        if (__instance.WorkItemStack == null || __instance.SelectedRecipe == null) return;
        if (!__instance.CanWorkCurrent) return;
        var temperature =
            (int)__instance.WorkItemStack.Collectible.GetTemperature(__instance.Api.World, __instance.WorkItemStack);
        var localizedString = Lang.Get("Temperature: {0}°C", temperature);
        const string pattern = @"(\d+°C)";
        var replacement = Regex.Replace(localizedString, pattern,
            $"<font color=\"{Constants.AnvilWorkableColor}\">$1</font>");
        dsc.Replace(localizedString, replacement);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BlockEntityForge), nameof(BlockEntityForge.GetBlockInfo))]
    [HarmonyPriority(Priority.VeryLow)]
    public static void Postfix_BlockEntityForge_GetBlockInfo(BlockEntityForge __instance, IPlayer forPlayer,
        StringBuilder dsc)
    {
        if (__instance.WorkItemStack is not { } workItemStack) return;
        var temperature =
            (int)workItemStack.Collectible.GetTemperature(__instance.Api.World, workItemStack);
        var workableTemp = workItemStack.GetWorkableTemperature(__instance.Api);

        var metalProps = workItemStack.Collectible
            .GetBehavior<CollectibleBehaviorQuenchable>()
            ?.GetMetalProps();
        if (metalProps != null)
        {
            var quenchIteration = workItemStack.Attributes.GetInt("quenchIteration");
            var temperIteration = workItemStack.Attributes.GetInt("temperIteration");

            var localizedStringQ = Lang.Get("itemstack-quenchable",
                metalProps.quenchMinTemp,
                metalProps.quenchMaxTemp);

            dsc.AppendLine(localizedStringQ);

            if (temperature > metalProps.quenchMinTemp && temperature < metalProps.quenchMaxTemp)
            {
                var replacementQ = TemperatureRangeRegex().Replace(localizedStringQ,
                    SetColor("$1", Constants.QuenchableColor));

                dsc.Replace(localizedStringQ, replacementQ);
            }

            if (quenchIteration > temperIteration)
            {
                var localizedStringT = Lang.Get("itemstack-temperable",
                    metalProps.temperMinTemp,
                    metalProps.temperMaxTemp);

                dsc.AppendLine(localizedStringT);

                if (temperature > metalProps.temperMinTemp && temperature < metalProps.temperMaxTemp)
                {
                    var replacementT = TemperatureRangeRegex().Replace(localizedStringT,
                        SetColor("$1", Constants.QuenchableColor));

                    dsc.Replace(localizedStringT, replacementT);
                }
            }
        }

        if (temperature < workableTemp) return;
        var localizedString = Lang.Get("forge-contentsandtemp", __instance.WorkItemStack.StackSize,
            __instance.WorkItemStack.GetName(), temperature);
        var replacement = TemperatureValueRegex().Replace(localizedString,
            SetColor("$1", Constants.AnvilWorkableColor));
        dsc.Replace(localizedString, replacement);
    }

    [GeneratedRegex(TemperatureRegexPattern)]
    public static partial Regex TemperatureValueRegex();

    [GeneratedRegex(TemperatureRangeRegexPattern)]
    public static partial Regex TemperatureRangeRegex();


    public static string SetColor<T>(T value, string color)
    {
        return $"<font color=\"{color}\">{value}</font>";
    }
}