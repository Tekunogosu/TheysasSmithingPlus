using System;
using System.Linq;
using JetBrains.Annotations;
using SmithingPlus.Common.Metal;
using SmithingPlus.Metal;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus.Test;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public partial class TestCommands : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return forSide == EnumAppSide.Server;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var command = api.ChatCommands.GetOrCreate("smithingplus").WithAlias("sp")
            .RequiresPrivilege("root");

        command.BeginSub("crucible")
            .WithAlias("cr")
            .WithDesc("Gives a hot crucible with molten copper")
            .HandleWith(GiveCrucible)
            .RequiresPlayer()
            .EndSub();
        
        command.BeginSub("getSmithingQuality")
            .WithDesc("Get the smithing quality stat of player.")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnGetSmithingQualityCommand(api, args))
            .EndSub();
        
        command.BeginSub("completeHeldWorkItem")
            .WithAlias("cmp")
            .WithDescription("Complete the held work item.")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnCompleteHeldWorkitemCommand(api, args))
            .EndSub();
        
        command.BeginSub("setBoolAttribute")
            .WithAlias("setb")
            .WithDescription("Set a bool attribute to held item stack.")
            .WithArgs(api.ChatCommands.Parsers.Word("attributeKey"), api.ChatCommands.Parsers.Word("attributeValue"),
                api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnSetHeldAttributeCommand(api, args))
            .EndSub();
        
        command.BeginSub("getMetalMaterial")
            .WithDescription("Get the metal material of held item.")
            .WithArgs(api.ChatCommands.Parsers.OptionalWord("playerName"))
            .HandleWith(args => OnGetMetalMaterialCommand(api, args))
            .EndSub();
        
        command.BeginSub("resetMetalMaterialCache")
            .WithDescription("Reset the metal material caches and the grid recipe index.")
            .HandleWith(_ => ResetMetalMaterialCache(api))
            .EndSub();

        RegisterDurabilityCommands(api, command);
    }

    private static TextCommandResult GiveCrucible(TextCommandCallingArgs args)
    {
        var player = args.Caller.Player as IServerPlayer;
        if (player?.Entity.Api is not ICoreServerAPI api)
            return TextCommandResult.Error("Something went wrong. Api is null.");

        if (!player.InventoryManager.GetHotbarInventory().Any(x => x.Empty)) return TextCommandResult.Deferred;

        ReadOnlySpan<string> crucibleColors =
            ["blue", "fire", "black", "brown", "cream", "gray", "orange", "red", "tan"];
        ReadOnlySpan<string> ingotMetals = ["copper", "iron", "steel"];
        var color = api.World.Rand.GetItems(crucibleColors, 1)[0];
        var metal = api.World.Rand.GetItems(ingotMetals, 1)[0];
        var crucibleCode = $"crucible-{color}-smelted";
        var ingotCode = $"ingot-{metal}";
        var ingotItem = api.World.GetItem(new AssetLocation(ingotCode));
        var block = api.World.GetBlock(new AssetLocation(crucibleCode));
        if (block == null || ingotItem == null)
            return TextCommandResult.Error($"Something went wrong. " +
                                           $"Crucible is {crucibleCode}: {block}. " +
                                           $"Ingot is {ingotCode}: {ingotItem}");
        var outputStack = new ItemStack(ingotItem);
        var crucibleStack = new ItemStack(block);
        if (block is not BlockSmeltedContainer smeltedContainer)
            return TextCommandResult.Error("Something went wrong. Crucible is not smelted container.");
        smeltedContainer.SetContents(crucibleStack, outputStack, 1000);
        crucibleStack.Collectible.SetTemperature(api.World, crucibleStack, 1500);
        player.InventoryManager.TryGiveItemstack(crucibleStack, true);
        return TextCommandResult.Deferred;
    }
    
    private static TextCommandResult OnSetHeldAttributeCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var attributeKey = args[0] as string;
        var attributeValue = bool.Parse(args[1] as string ?? string.Empty);
        if (string.IsNullOrEmpty(attributeKey) || string.IsNullOrEmpty(args[1] as string))
            return TextCommandResult.Error("Attribute key or value is missing.");
        var resolved = ResolveHeldStack(api, args[2] as string, args);
        if (resolved.Error != null) return resolved.Error;
        var targetPlayer = resolved.Player;
        var heldStack = resolved.Stack;
        heldStack.Attributes.SetBool(attributeKey, attributeValue);
        resolved.Slot.MarkDirty();
        return TextCommandResult.Success(
            $"Set held stack attribute {attributeKey} to value {attributeValue} for player '{targetPlayer.PlayerName}'.");
    }

    private static TextCommandResult OnCompleteHeldWorkitemCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var resolved = ResolveHeldStack(api, args[0] as string, args);
        if (resolved.Error != null) return resolved.Error;
        var targetPlayer = resolved.Player;
        var heldStack = resolved.Stack;
        if (heldStack.Collectible is not ItemWorkItem)
            return TextCommandResult.Error($"Player '{targetPlayer.PlayerName}' is not holding a work item.");
        var selectedRecipe = api.GetSmithingRecipes().FirstOrDefault(r =>
            r.RecipeId == heldStack.Attributes.GetInt("selectedRecipeId"));
        if (selectedRecipe == null)
            return TextCommandResult.Error(
                $"Player '{targetPlayer.PlayerName}''s held work item has no selected recipe.");
        var recipeVoxels = selectedRecipe.Voxels;
        heldStack.Attributes.SetBytes("voxels", BlockEntityAnvil.serializeVoxels(recipeVoxels.ToByteArray()));
        targetPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
        return TextCommandResult.Success($"Held work item completed for player '{targetPlayer.PlayerName}'.");
    }

    private static TextCommandResult OnGetSmithingQualityCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var targetPlayer = ResolvePlayer(api, args[0] as string, args, out var error);
        if (error != null) return error;
        var smithingQuality = targetPlayer.Entity.Stats.GetBlended("sp:smithingQuality");
        return TextCommandResult.Success(
            $"Smithing quality for player '{targetPlayer.PlayerName}' is {smithingQuality}.");
    }


    private static TextCommandResult OnGetMetalMaterialCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var resolved = ResolveHeldStack(api, args[0] as string, args);
        if (resolved.Error != null) return resolved.Error;
        var heldStack = resolved.Stack;
        var metalMaterial = heldStack.Collectible.GetOrCacheMetalMaterial(api);
        if (metalMaterial == null)
            return TextCommandResult.Error($"Held item '{heldStack.GetName()}' is not a metal item.");
        return TextCommandResult.Success(
            $"Held item '{heldStack.GetName()}' has metal material {metalMaterial.Code} with ingot {metalMaterial.IngotCode}.");
    }

    /// <summary>
    ///     Drops everything derived from the recipe list and the collectibles, so the next lookup rebuilds
    ///     it. The grid recipe index goes with the material caches: it is the source the material lookups
    ///     fall back to, so resetting them while it stands would refill them from the same stale answers.
    /// </summary>
    private static TextCommandResult ResetMetalMaterialCache(ICoreServerAPI api)
    {
        ObjectCacheUtil.Delete(api, Core.MetalMaterialCacheKey);
        ObjectCacheUtil.Delete(api, Core.MetalMaterialProcessedCacheKey);
        GridRecipeIndex.Invalidate(api);
        SmithingRecipeIndex.Invalidate(api);
        return TextCommandResult.Success("Metal material caches and the recipe indexes have been reset.");
    }

    private static IServerPlayer GetPlayerByName(ICoreServerAPI api, string playerName)
    {
        return api.World.AllOnlinePlayers
            .Cast<IServerPlayer>()
            .FirstOrDefault(player => player.PlayerName.Equals(playerName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     A resolved target for a command that acts on someone's held item: the named player's, or the
    ///     caller's when no name was given. <see cref="Error" /> is set when there is nothing to act on, and
    ///     is the result the handler should return.
    /// </summary>
    private readonly struct HeldStack
    {
        public TextCommandResult Error { get; init; }
        public IServerPlayer Player { get; init; }
        public ItemSlot Slot { get; init; }
        public ItemStack Stack { get; init; }
    }

    /// <summary>
    ///     The named player, or the caller when no name is given. <paramref name="error" /> is set, and the
    ///     return is null, when neither resolves.
    /// </summary>
    private static IServerPlayer ResolvePlayer(ICoreServerAPI api, string playerName, TextCommandCallingArgs args,
        out TextCommandResult error)
    {
        error = null;
        if (!string.IsNullOrEmpty(playerName))
        {
            var named = GetPlayerByName(api, playerName);
            if (named == null) error = TextCommandResult.Error($"Player '{playerName}' not found.");
            return named;
        }

        if (args.Caller.Player is IServerPlayer caller) return caller;
        error = TextCommandResult.Error("Player not found.");
        return null;
    }

    /// <summary>The named player's held item, or the caller's when no name is given.</summary>
    private static HeldStack ResolveHeldStack(ICoreServerAPI api, string playerName, TextCommandCallingArgs args)
    {
        var targetPlayer = ResolvePlayer(api, playerName, args, out var error);
        if (error != null) return new HeldStack { Error = error };

        var slot = targetPlayer.InventoryManager.ActiveHotbarSlot;
        var heldStack = slot?.Itemstack;
        if (slot == null || heldStack == null)
            return new HeldStack
            {
                Error = TextCommandResult.Error($"Player '{targetPlayer.PlayerName}' has no held item.")
            };

        return new HeldStack { Player = targetPlayer, Slot = slot, Stack = heldStack };
    }
}