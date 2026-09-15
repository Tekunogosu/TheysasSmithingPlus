#nullable enable
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;

namespace SmithingPlus.Test;

/// <summary>
///     Commands for driving a tool to the point of breaking, so the broken-head drop can be exercised
///     without waiting out a tool's durability.
///     <para>
///         Toolsmith's own <c>/cta</c> does this for tools it has taken over, but only for those: with
///         Toolsmith uninstalled there is no way to damage a tool on demand, which is exactly the
///         configuration the drop has to be tested in as well.
///     </para>
/// </summary>
public partial class TestCommands
{
    private static void RegisterDurabilityCommands(ICoreServerAPI api, IChatCommand command)
    {
        command.BeginSub("setDurability")
            .WithAlias("dur")
            .WithDescription("Set the remaining durability of the held item. Use 1 to leave it one hit from breaking.")
            .WithArgs(api.ChatCommands.Parsers.Int("durability"),
                api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnSetDurabilityCommand(api, args))
            .EndSub();

        command.BeginSub("breakHeld")
            .WithAlias("brk")
            .WithDescription("Damage the held item by its whole remaining durability, breaking it outright.")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnBreakHeldCommand(api, args))
            .EndSub();
    }

    private static TextCommandResult OnSetDurabilityCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var durability = args[0] as int? ?? 1;
        var resolved = ResolveHeldStack(api, args[1] as string, args);
        if (resolved.Error != null) return resolved.Error;
        var slot = resolved.Slot!;
        var heldStack = resolved.Stack!;

        var maxDurability = heldStack.Collectible.GetMaxDurability(heldStack);
        if (maxDurability <= 0)
            return TextCommandResult.Error($"Held item '{heldStack.GetName()}' has no durability to set.");
        if (durability < 1)
            return TextCommandResult.Error(
                "Durability must be at least 1. Use 'breakHeld' to break the item instead; setting it to " +
                "zero here would leave a spent tool sitting in the slot rather than destroying it.");
        if (durability > maxDurability)
            return TextCommandResult.Error(
                $"Held item '{heldStack.GetName()}' has a max durability of {maxDurability}.");

        heldStack.Collectible.SetDurability(heldStack, durability);
        slot.MarkDirty();
        return TextCommandResult.Success(
            $"Set durability of '{heldStack.GetName()}' to {durability}/{maxDurability}.");
    }

    /// <summary>
    ///     Breaks the item through <see cref="CollectibleObject.DamageItem" /> rather than by emptying the
    ///     slot, so everything that hangs off a tool breaking -- this mod's broken-head drop included --
    ///     runs exactly as it would in play.
    /// </summary>
    private static TextCommandResult OnBreakHeldCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var resolved = ResolveHeldStack(api, args[0] as string, args);
        if (resolved.Error != null) return resolved.Error;
        var slot = resolved.Slot!;
        var heldStack = resolved.Stack!;
        var player = resolved.Player!;

        var remaining = heldStack.Collectible.GetRemainingDurability(heldStack);
        if (heldStack.Collectible.GetMaxDurability(heldStack) <= 0)
            return TextCommandResult.Error($"Held item '{heldStack.GetName()}' has no durability to spend.");

        var name = heldStack.GetName();
        heldStack.Collectible.DamageItem(api.World, player.Entity, slot, remaining);
        return TextCommandResult.Success($"Damaged '{name}' by its remaining {remaining} durability.");
    }
}
