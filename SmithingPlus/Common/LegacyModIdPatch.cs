#nullable enable
using System;
using HarmonyLib;
using JetBrains.Annotations;
using Vintagestory.Common;

namespace SmithingPlus.Common;

/// <summary>
///     Answers <c>IsModEnabled("smithingplus")</c> with true, so mods that integrate with SmithingPlus still
///     find it after this fork was published under a different modid.
///     <para>
///         This fork is SmithingPlus; it carries the old asset domain, config file and stack attributes, and
///         is published as <see cref="Core.PublishedModId" /> only because ModDB will not host two mods under
///         one id. Integrating mods ask the loader for the old id -- Toolsmith gates its whole SmithingPlus
///         compatibility on it, including clearing this mod's own attributes off a tool head and standing
///         down its bits-smithing in favour of this one -- and without this they silently get false and stop
///         doing any of it.
///     </para>
///     <para>
///         The question those callers are asking is "is SmithingPlus's functionality present", and the
///         truthful answer is yes. Only that question is answered: <c>GetMod</c> still returns nothing for
///         the old id, so nothing can mistake this for the upstream mod actually being installed. Turn it off
///         with <see cref="Config.ServerConfig.AnswerToLegacyModId" /> to see what an integrating mod does
///         without it.
///     </para>
/// </summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(ModLoader))]
[HarmonyPatchCategory(Core.LegacyModIdCategory)]
public static class LegacyModIdPatch
{
    /// <summary>
    ///     Logged on the first answer only. <c>IsModEnabled</c> is called freely, including per crafted item
    ///     by some callers, so logging every call would bury the log.
    /// </summary>
    private static bool _announced;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ModLoader.IsModEnabled))]
    public static void Postfix_IsModEnabled(string modID, ref bool __result)
    {
        if (__result) return;
        if (!string.Equals(modID, Core.ModId, StringComparison.OrdinalIgnoreCase)) return;

        __result = true;
        if (_announced) return;
        _announced = true;
        Core.Logger.Notification(
            "Answering IsModEnabled(\"{0}\") with true: this mod is SmithingPlus, published as \"{1}\". " +
            "Mods integrating with SmithingPlus ask for the old id. Set {2} to false in the config to " +
            "stop answering.",
            Core.ModId, Core.PublishedModId, nameof(Config.ServerConfig.AnswerToLegacyModId));
    }
}
