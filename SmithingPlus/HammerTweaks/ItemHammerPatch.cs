using System;
using System.Linq;
using Cairo;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus.HammerTweaks;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
[HarmonyPatch(typeof(ItemHammer))]
[HarmonyPatchCategory(Core.HammerTweaksCategory)]
public static class ItemHammerPatch
{
    private const string ToolModeCacheKey = $"{Core.ModId}:extraHammerToolModes";

    /// <summary>Code of the tool mode this mod appends, used to find it again by identity.</summary>
    private const string FlipToolModeCode = "flip";

    [HarmonyPostfix]
    [HarmonyPatch(nameof(ItemHammer.GetToolModes))]
    [HarmonyPriority(Priority.Last)]
    public static void Postfix_GetToolModes(ItemHammer __instance, ItemSlot slot,
        IClientPlayer forPlayer, BlockSelection blockSel, ref SkillItem[] __result, ref SkillItem[] ___toolModes)
    {
        try
        {
            if (!Core.Config.HammerTweaks) return;
            if (forPlayer?.Entity?.Api is not ICoreClientAPI capi) return;
            if (__result is null || slot.Itemstack is null) return;
            if (___toolModes is not null)
            {
                // Where "flip" sits is the number of modes that existed before this mod appended it. Counted
                // from the array each time rather than latched into a static: the static kept the first
                // count it ever saw for the life of the process, so a hammer whose mode list differed -- or
                // a second world joined in the same session -- was measured against a stale number.
                var originalToolModesCount = OriginalToolModeCount(___toolModes);

                // The index the server must agree on, recorded on the stack itself so it travels with the
                // hammer, and sent so the server has it before the first strike. TempAttributes were used
                // for this and are neither saved nor synchronized, so the server could be reading a value
                // that had silently reverted to 0 -- a valid mode index -- and treat an ordinary hit as a
                // flip, or the reverse.
                slot.Itemstack.Attributes.SetInt(ModStackAttributes.FlipToolModeIndex, originalToolModesCount);
                HammerTweaksNetwork.SendFlipToolMode(capi, originalToolModesCount);
                if (Core.Config.RotationRequiresTongs && !forPlayer.HasHeatResistantHandGear())
                {
                    __result = ___toolModes =
                        ClearExtraToolModes(__instance, slot, forPlayer, blockSel, ___toolModes,
                            originalToolModesCount);
                    return;
                }

                // Only add new toolmode if it hasn’t been added yet.
                if (___toolModes.Length > originalToolModesCount) return;
            }

            var newModes = GetOrCreateFlipToolMode(capi);
            __result = ___toolModes = ___toolModes?.Concat(newModes).ToArray() ?? newModes;
        }
        catch (ArgumentNullException ex)
        {
            Core.Logger.Error(ex);
        }
    }

    /// <summary>
    ///     How many modes the hammer had before this mod appended its own, which is the index "flip" lands
    ///     at. Found by looking for a mode this mod added rather than by remembering a count, so calling it
    ///     again on an already-extended list returns the same answer instead of growing.
    /// </summary>
    private static int OriginalToolModeCount(SkillItem[] toolModes)
    {
        for (var i = 0; i < toolModes.Length; i++)
            if (toolModes[i]?.Code?.Path == FlipToolModeCode)
                return i;
        return toolModes.Length;
    }

    private static SkillItem[] ClearExtraToolModes(ItemHammer itemHammer, ItemSlot slot, IPlayer forPlayer,
        BlockSelection blockSel, SkillItem[] toolModes, int originalToolModesCount)
    {
        toolModes = toolModes.Take(originalToolModesCount).ToArray();
        if (itemHammer.GetToolMode(slot, forPlayer, blockSel) < originalToolModesCount) return toolModes;
        slot.Itemstack.Attributes.SetInt("toolMode", 0); // Reset to first tool mode if the current one is out of range
        return toolModes;
    }

    public static bool HasHeatResistantHandGear(this IPlayer player)
    {
        if (player == null)
            return false;
        var leftHandItemSlot = player.Entity.LeftHandItemSlot;
        return leftHandItemSlot != null &&
               (leftHandItemSlot.Itemstack?.Collectible.Attributes?.IsTrue("heatResistant") ?? false);
    }

    private static SkillItem[] GetOrCreateFlipToolMode(ICoreClientAPI capi)
    {
        return ObjectCacheUtil.GetOrCreate(capi, ToolModeCacheKey, () => new[]
        {
            new SkillItem
            {
                Code = new AssetLocation(FlipToolModeCode),
                Name = Lang.Get("Flip")
            }.WithIcon(capi, DrawFlipSvg)
        });
    }

    private static void DrawFlipSvg(
        Context cr,
        int x,
        int y,
        float canvasWidth,
        float canvasHeight,
        double[] rgba)
    {
        var matrix1 = cr.Matrix;
        cr.Save();
        var num1 = 119f;
        var num2 = 115f;
        var num3 = Math.Min(canvasWidth / num1, canvasHeight / num2);
        matrix1.Translate(x + (double)Math.Max(0.0f, (float)((canvasWidth - num1 * (double)num3) / 2.0)),
            y + (double)Math.Max(0.0f, (float)((canvasHeight - num2 * (double)num3) / 2.0)));
        matrix1.Scale(num3, num3);
        cr.Matrix = matrix1;
        cr.Operator = Operator.Over;
        cr.LineWidth = 15.0;
        cr.MiterLimit = 10.0;
        cr.LineCap = LineCap.Butt;
        cr.LineJoin = LineJoin.Miter;
        var source1 = (Pattern)new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
        cr.SetSource(source1);
        cr.NewPath();
        cr.MoveTo(100.761719, 29.972656);
        cr.CurveTo(7429.0 / 64.0, 46.824219, 111.929688, 74.050781, 3137.0 / 32.0, 89.949219);
        cr.CurveTo(78.730469, 112.148438, 45.628906, 113.027344, 23.527344, 93.726563);
        cr.CurveTo(-13.023438, 56.238281, 17.898438, 7.355469, 61.082031, 7.5);
        cr.Tolerance = 0.1;
        cr.Antialias = Antialias.Default;
        var matrix2 = new Matrix(1.0, 0.0, 0.0, 1.0, 219.348174, -337.87843);
        source1.Matrix = matrix2;
        cr.StrokePreserve();
        source1?.Dispose();
        cr.Operator = Operator.Over;
        var source2 = (Pattern)new SolidPattern(rgba[0], rgba[1], rgba[2], rgba[3]);
        cr.SetSource(source2);
        cr.NewPath();
        cr.MoveTo(5241.0 / 64.0, 177.0 / 16.0);
        cr.CurveTo(86.824219, 21.769531, 91.550781, 36.472656, 92.332031, 47.808594);
        cr.LineTo(100.761719, 29.972656);
        cr.LineTo(118.585938, 21.652344);
        cr.CurveTo(107.269531, 20.804688, 5927.0 / 64.0, 15.976563, 5241.0 / 64.0, 177.0 / 16.0);
        cr.ClosePath();
        cr.MoveTo(5241.0 / 64.0, 177.0 / 16.0);
        cr.Tolerance = 0.1;
        cr.Antialias = Antialias.Default;
        cr.FillRule = FillRule.Winding;
        cr.FillPreserve();
        source2?.Dispose();
        cr.Restore();
    }
}