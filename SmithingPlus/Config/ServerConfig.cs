using System.Linq;
using Newtonsoft.Json;
using SmithingPlus.Util;

namespace SmithingPlus.Config;

public class ServerConfig
{
    /// <summary>
    ///     Schema version this build writes. A file whose <see cref="ConfigVersion" /> is lower, or absent, is
    ///     migrated by <see cref="ConfigLoader" />. Raise it together with a new migration step there.
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>
    ///     Schema version of the file this was read from. Files written before versioning existed have no such
    ///     key and count as version 1; the loader reads that from the raw JSON, because the property default
    ///     would otherwise report every old file as current.
    /// </summary>
    public int ConfigVersion { get; set; } = CurrentVersion;

    public bool RecoverBitsOnSplit { get; set; } = true;
    public bool HelveHammerBitsRecovery { get; set; } = true;
    public float VoxelsPerBit { get; set; } = 2.1f;
    public bool SmithWithBits { get; set; } = true;
    public bool BitsTopUp { get; set; } = true;
    public bool EnableToolRecovery { get; set; } = true;
    public float DurabilityPenaltyPerRepair { get; set; } = 0.05f;
    public string ToolRepairForgettableAttributes { get; set; } = "quality,maxRepair,buffs";
    public float RepairableToolDurabilityMultiplier { get; set; } = 1.0f;
    public float BrokenToolVoxelPercent { get; set; } = 0.8f;

    public string RepairableToolSelector { get; set; } =
        "@.*(pickaxe|shovel|saw|axe|hoe|knife|hammer|chisel|shears|sword|spear|bow|shield|sickle|scythe|tongs|wrench|solderingiron|cleaver|prospectingpick|crossbow|pistol|rifle|shotgun|blade|halberd|poleaxe|quarterstaff|pike).*";

    public string ToolHeadSelector { get; set; } = "@(.*)(head|blade|boss|barrel|stirrup|part)(.*)";
    public string IngotSelector { get; set; } = "@(.*):ingot-(.*)";
    public string WorkItemSelector { get; set; } = "@(.*):workitem-(.*)";
    public bool DontRepairBrokenToolHeads { get; set; } = false;
    public bool CanRepairForlornHopeEstoc { get; set; } = true;
    public bool ShowRepairedCount { get; set; } = true;
    public bool ShowBrokenCount { get; set; } = true;
    public bool ShowRepairSmithName { get; set; } = false;
    public float HelveHammerSmithingQualityModifier { get; set; } = 1;
    public bool ArrowsDropBits { get; set; } = true;
    public string ArrowSelector { get; set; } = "@(.*):arrow-(.*)";
    public bool MetalCastingTweaks { get; set; } = true;
    public float CastToolDurabilityPenalty { get; set; } = 0.1f;
    public bool DynamicMoldUnits { get; set; } = false;
    public bool HammerTweaks { get; set; } = true;
    public bool RotationRequiresTongs { get; set; } = false;
    [ClientSetting]
    public bool AnvilShowRecipeVoxels { get; set; } = true;
    [ClientSetting]
    public bool RememberHammerToolMode { get; set; } = true;
    [ClientSetting]
    public bool ShowWorkableTemperature { get; set; } = true;
    [ClientSetting]
    public bool HandbookExtraInfo { get; set; } = true;
    [ClientSetting]
    public int AnvilRecipeSelectionColumns { get; set; } = 8;

    /// <summary>
    ///     Makes the server's values win for the <see cref="ClientSettingAttribute" /> settings too, so a server
    ///     can fix how its players see and use the anvil. Off, each player keeps their own values for those.
    /// </summary>
    public bool ServerOverridesClientSettings { get; set; } = false;

    /// <summary>
    ///     Logs every anvil action, on both sides, for tracking down actions that appear to happen twice.
    ///     Noisy by design; leave it off unless chasing that.
    /// </summary>
    public bool TraceAnvilActions { get; set; } = false;

    /// <summary>
    ///     Answers a mod asking whether "smithingplus" is enabled with yes, since this fork is SmithingPlus
    ///     published under a different modid. Turning it off makes mods that integrate with SmithingPlus --
    ///     Toolsmith among them -- stop doing so. See <see cref="Common.LegacyModIdPatch" />.
    /// </summary>
    public bool AnswerToLegacyModId { get; set; } = true;

    // public bool StoneSmithing { get; set; } = false;
    [JsonIgnore]
    public string[] GetToolRepairForgettableAttributes =>
        ToolRepairForgettableAttributes.Split(",")
            .Append("durability")
            .Append(ModStackAttributes.RepairedToolStack)
            .Append(ModStackAttributes.CastTool)
            .ToArray();
}