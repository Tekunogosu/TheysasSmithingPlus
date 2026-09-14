namespace SmithingPlus.Common;

/// <summary>When a workable may be placed on an anvil, relative to what is already on it.</summary>
public enum AnvilPlacementMode
{
    /// <summary>Never placeable.</summary>
    None = -1,

    /// <summary>Placeable on an empty anvil and onto an existing work item.</summary>
    Normal = 0,

    /// <summary>Placeable only on an empty anvil.</summary>
    Empty = 1,

    /// <summary>Placeable only onto an existing work item.</summary>
    Present = 2
}

public static class PlacementModeExtensions
{
    /// <summary>Whether this mode allows placing onto an anvil that already holds a work item.</summary>
    public static bool AllowsPresent(this AnvilPlacementMode mode)
    {
        return mode is AnvilPlacementMode.Normal or AnvilPlacementMode.Present;
    }

    /// <summary>Whether this mode allows placing onto an empty anvil.</summary>
    public static bool AllowsEmpty(this AnvilPlacementMode mode)
    {
        return mode is AnvilPlacementMode.Normal or AnvilPlacementMode.Empty;
    }
}
