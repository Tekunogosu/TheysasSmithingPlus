namespace SmithingPlus;

using PatchCategory = string;

public partial class Core
{
    // This is getting a little large, might want to refactor to a system with patch dependencies later
    internal const PatchCategory AlwaysPatchCategory = "always";
    internal const PatchCategory NeverPatchCategory = "never";
    internal const PatchCategory ToolRecoveryCategory = "toolRecovery";
    internal const PatchCategory BitSmithingCategory = "smithingBits";
    internal const PatchCategory StoneSmithingCategory = "stoneSmithing";
    internal const PatchCategory BitsRecoveryCategory = "bitsRecovery";
    internal const PatchCategory HelveHammerBitsRecoveryCategory = $"{BitsRecoveryCategory}.helveHammer";
    internal const PatchCategory CastingTweaksCategory = "castingTweaks";
    internal const PatchCategory DynamicMoldsCategory = "moldTweaks";
    internal const PatchCategory HammerTweaksCategory = "hammerTweaks";
    internal const PatchCategory SkillfulSmithingCategory = "skillfulSmithing";
    internal const PatchCategory AnvilTraceCategory = "anvilTrace";

    internal struct ClientTweaksCategories
    {
        public const PatchCategory AnvilShowRecipeVoxels = "anvilShowRecipeVoxels";
        public const PatchCategory RememberHammerToolMode = "rememberHammerToolMode";
        public const PatchCategory ShowWorkablePatches = "showWorkablePatches";
        public const PatchCategory HandbookExtraInfo = "handbookExtraInfo";
    }
}

public static class PatchExtensions
{
    /// <summary>
    ///     Patches the category if the boolean flag is enabled.
    /// </summary>
    /// <param name="patchCategory">String HarmonyPatchCategory to patch.</param>
    /// <param name="configFlag">Boolean flag to determine if the patch should be applied.</param>
    /// <param name="withDebugLogs">Flag to determine if debug logs should be printed.</param>
    public static void PatchIfEnabled(this PatchCategory patchCategory, bool configFlag, bool withDebugLogs = true)
    {
        if (!configFlag) return;
        Core.HarmonyInstance.PatchCategory(patchCategory);
        if (withDebugLogs) Core.Logger.VerboseDebug("Patched {0}...", patchCategory);
    }
}