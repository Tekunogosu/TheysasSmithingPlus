using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SmithingPlus.ClientTweaks;

[HarmonyPatch(typeof(ItemHammer))]
[HarmonyPatchCategory(Core.ClientTweaksCategories.RememberHammerToolMode)]
public class RememberHammerToolModePatch
{
    /// <summary>The mode the player last chose, kept on the player so a fresh hammer starts there.</summary>
    private const string RememberedToolModeAttribute = "hammerToolMode";

    /// <summary>Where ItemHammer keeps the mode of one particular hammer.</summary>
    private const string ToolModeAttribute = "toolMode";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ItemHammer.SetToolMode))]
    public static void Postfix_SetToolMode(
        ItemSlot slot,
        IPlayer byPlayer,
        BlockSelection blockSel,
        int toolMode)
    {
        byPlayer.Entity?.Attributes?.SetInt(RememberedToolModeAttribute, toolMode);
    }
    
    /// <summary>
    ///     Applies the remembered mode to a hammer that has none of its own yet.
    ///     <para>
    ///         A hammer that already carries a mode keeps it. Overriding unconditionally pinned every
    ///         hammer to the remembered value and made the mode look unchangeable: GetInt answers 0 for a
    ///         key that was never written, so the override always applied and always won, undoing whatever
    ///         the player had just selected.
    ///     </para>
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ItemHammer.GetToolMode))]
    public static void Postfix_GetToolMode(
        ref int __result,
        ItemSlot slot,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (slot?.Itemstack?.Attributes?.HasAttribute(ToolModeAttribute) == true) return;

        var remembered = byPlayer.Entity?.Attributes;
        if (remembered?.HasAttribute(RememberedToolModeAttribute) != true) return;
        __result = remembered.GetInt(RememberedToolModeAttribute);
    }
}