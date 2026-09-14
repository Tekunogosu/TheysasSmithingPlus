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
public class TestCommands : ModSystem
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
        var playerName = args[2] as string;
        IServerPlayer targetPlayer;
        if (string.IsNullOrEmpty(playerName))
        {
            targetPlayer = args.Caller.Player as IServerPlayer;
        }
        else
        {
            targetPlayer = GetPlayerByName(api, playerName);
            if (targetPlayer == null) return TextCommandResult.Error($"Player '{playerName}' not found.");
        }

        if (targetPlayer == null) return TextCommandResult.Error("Player not found.");
        var heldStack = targetPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return TextCommandResult.Error($"Player '{targetPlayer.PlayerName}' has no held item.");
        heldStack.Attributes.SetBool(attributeKey, attributeValue);
        targetPlayer.InventoryManager.ActiveHotbarSlot.MarkDirty();
        return TextCommandResult.Success(
            $"Set held stack attribute {attributeKey} to value {attributeValue} for player '{targetPlayer.PlayerName}'.");
    }

    private static TextCommandResult OnCompleteHeldWorkitemCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var playerName = args[0] as string;
        IServerPlayer targetPlayer;
        if (string.IsNullOrEmpty(playerName))
        {
            targetPlayer = args.Caller.Player as IServerPlayer;
        }
        else
        {
            targetPlayer = GetPlayerByName(api, playerName);
            if (targetPlayer == null) return TextCommandResult.Error($"Player '{playerName}' not found.");
        }

        if (targetPlayer == null) return TextCommandResult.Error("Player not found.");
        var heldStack = targetPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return TextCommandResult.Error($"Player '{targetPlayer.PlayerName}' has no held item.");
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
        var playerName = args[0] as string;
        IServerPlayer targetPlayer;
        if (string.IsNullOrEmpty(playerName))
        {
            targetPlayer = args.Caller.Player as IServerPlayer;
        }
        else
        {
            targetPlayer = GetPlayerByName(api, playerName);
            if (targetPlayer == null) return TextCommandResult.Error($"Player '{playerName}' not found.");
        }

        if (targetPlayer == null) return TextCommandResult.Error("Player not found.");
        var smithingQuality = targetPlayer.Entity.Stats.GetBlended("sp:smithingQuality");
        return TextCommandResult.Success(
            $"Smithing quality for player '{targetPlayer.PlayerName}' is {smithingQuality}.");
    }


    private static TextCommandResult OnGetMetalMaterialCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        var playerName = args[0] as string;
        IServerPlayer targetPlayer;
        if (string.IsNullOrEmpty(playerName))
        {
            targetPlayer = args.Caller.Player as IServerPlayer;
        }
        else
        {
            targetPlayer = GetPlayerByName(api, playerName);
            if (targetPlayer == null) return TextCommandResult.Error($"Player '{playerName}' not found.");
        }

        if (targetPlayer == null) return TextCommandResult.Error("Player not found.");
        var heldStack = targetPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
        if (heldStack == null) return TextCommandResult.Error($"Player '{targetPlayer.PlayerName}' has no held item.");
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
}