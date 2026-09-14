using Vintagestory.API.Common;

namespace SmithingPlus.ToolRecovery;

/// <summary>
///     Marks a tool head as repairable. Behaves exactly as
///     <see cref="CollectibleBehaviorRepairableTool" />; it exists as its own type because the two are
///     attached to different collectibles and told apart by type elsewhere.
/// </summary>
public class CollectibleBehaviorRepairableToolHead(CollectibleObject collObj)
    : CollectibleBehaviorRepairableTool(collObj);
