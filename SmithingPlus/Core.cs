using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using SmithingPlus.BitsRecovery;
using SmithingPlus.CastingTweaks;
using SmithingPlus.ClientTweaks;
using SmithingPlus.Common;
using SmithingPlus.Common.Metal;
using SmithingPlus.Config;
using SmithingPlus.SmithWithBits;
using SmithingPlus.StoneSmithing;
using SmithingPlus.ToolRecovery;
using SmithingPlus.Util;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SmithingPlus;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public partial class Core : ModSystem
{
    public const string ModId = "smithingplus";

    // One instance of this mod system is created per side, and in singleplayer both live in the same
    // process. A single static API field would therefore end up holding whichever side loaded last, and
    // everything reached through it -- the caches in Core.Cache.cs above all -- would be read and written
    // across sides. Each side's API is kept separately, and Api hands back the one belonging to the caller.
    private static ICoreAPI? _clientApi;
    private static ICoreAPI? _serverApi;

    /// <summary>The side this particular instance was started for.</summary>
    private EnumAppSide _side;

    public static ILogger Logger { get; private set; }

    /// <summary>
    ///     An API to reach the world through, for the static helpers that hold none of their own.
    ///     <para>
    ///         The server's is preferred where both sides are loaded, so that the two do not each get a
    ///         different answer depending on which loaded last. Anything whose answer differs by side, or
    ///         that writes to a per-side cache, must take the caller's own API as a parameter rather than
    ///         read this: use <see cref="ApiFor" /> when all that is available is a world or an entity.
    ///     </para>
    /// </summary>
    public static ICoreAPI Api => _serverApi ?? _clientApi!;

    /// <summary>This mod's API for the side <paramref name="world" /> belongs to.</summary>
    public static ICoreAPI? ApiFor(IWorldAccessor? world)
    {
        if (world == null) return Api;
        return world.Side == EnumAppSide.Client ? _clientApi ?? _serverApi : _serverApi ?? _clientApi;
    }

    public static Harmony HarmonyInstance { get; private set; }
    public static ServerConfig Config => ConfigLoader.Config;

    public override void StartPre(ICoreAPI api)
    {
        Logger = Mod.Logger;
        _side = api.Side;
        if (_side == EnumAppSide.Client) _clientApi = api;
        else _serverApi = api;
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterCollectibleBehaviorClass($"{ModId}:JsonAnvilWorkable",
            typeof(CollectibleBehaviorJsonAnvilWorkable));
        api.RegisterCollectibleBehaviorClass($"{ModId}:WorkableNugget", typeof(CollectibleBehaviorWorkableNugget));
        api.RegisterCollectibleBehaviorClass($"{ModId}:RepairableTool", typeof(CollectibleBehaviorRepairableTool));
        api.RegisterCollectibleBehaviorClass($"{ModId}:RepairableToolHead",
            typeof(CollectibleBehaviorRepairableToolHead));
        api.RegisterCollectibleBehaviorClass($"{ModId}:BrokenToolHead", typeof(CollectibleBehaviorBrokenToolHead));
        api.RegisterCollectibleBehaviorClass($"{ModId}:DisplayWorkableTemp",
            typeof(CollectibleBehaviorDisplayWorkableTemp));
        api.RegisterCollectibleBehaviorClass($"{ModId}:ScrapeCrucible", typeof(CollectibleBehaviorScrapeCrucible));
        api.RegisterCollectibleBehaviorClass($"{ModId}:CastToolHead", typeof(CollectibleBehaviorCastToolHead));
        api.RegisterCollectibleBehaviorClass($"{ModId}:SmeltedContainer", typeof(CollectibleBehaviorSmeltedContainer));
        api.RegisterCollectibleBehaviorClass($"{ModId}:RecycledBit", typeof(CollectibleBehaviorRecycledBit));

        api.RegisterEntityBehaviorClass($"{ModId}:RecyclableArrow", typeof(RecyclableArrowBehavior));

        api.RegisterItemClass($"{ModId}:ItemStoneHammer", typeof(ItemStoneHammer));
        api.RegisterBlockEntityClass($"{ModId}:StoneAnvil", typeof(BlockEntityStoneAnvil));

        Patch();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.OnEntitySpawn += AddEntityBehaviors;
        api.Event.OnEntityLoaded += AddEntityBehaviors;
    }

    private static void AddEntityBehaviors(Entity entity)
    {
        if (!Config.ArrowsDropBits || entity is not EntityProjectile projectile) return;
        if (!RecyclableArrowBehavior.IsRecyclableArrow(projectile)) return;
        Logger.VerboseDebug("Adding RecyclableArrowBehavior to {0}", entity.Code);
        entity.AddBehavior(new RecyclableArrowBehavior(entity));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        foreach (var collObj in api.World.Collectibles.Where(c => c?.Code != null))
        {
            collObj.AddBehaviorIf<CollectibleBehaviorDisplayWorkableTemp>(
                api.Side == EnumAppSide.Client &&
                Config.ShowWorkableTemperature &&
                collObj.GetCollectibleInterface<IAnvilWorkable>() is not null);
            collObj.AddBehaviorIf<CollectibleBehaviorQuenchableInfo>(
                api.Side == EnumAppSide.Client &&
                Config.ShowWorkableTemperature &&
                collObj.HasBehavior<CollectibleBehaviorQuenchable>());
            collObj.AddBehaviorIf<CollectibleBehaviorScrapeCrucible>(Config.RecoverBitsOnSplit &&
                                                                     collObj is ItemChisel);
            collObj.AddBehaviorIf<CollectibleBehaviorSmeltedContainer>(Config.RecoverBitsOnSplit &&
                                                                       collObj is BlockSmeltedContainer);

            if (Config.MetalCastingTweaks && collObj.MatchesToolHeadSelector())
            {
                collObj.AddBehavior<CollectibleBehaviorCastToolHead>();
                collObj.MakeForgeable();
            }

            collObj.AddBehaviorIf<CollectibleBehaviorRecycledBit>(collObj.Code.ToString().Contains("metalbit"));

            if ((collObj.Tool != null || (collObj.IsRepairableTool() && !collObj.MatchesToolHeadSelector())) &&
                collObj.HasMetalMaterialSimple()) collObj.AddBehavior<CollectibleBehaviorRepairableTool>();
            else if (collObj.MatchesToolHeadSelector()) collObj.AddBehavior<CollectibleBehaviorRepairableToolHead>();
            else if (WildcardUtil.Match(Config.WorkItemSelector, collObj.Code.ToString()))
                collObj.AddBehavior<CollectibleBehaviorBrokenToolHead>();

            // Adds workable-only smithing recipes to make ingots.
            // This is a bit hacky as workable crafting uses the original
            // ingot recipes, so to have these recipes be present we need ingot -> ingot recipes.
            // These won't show up when smithing with
            // ingots, however,
            // since the original ingot recipe (in the smithingplus domain) has "recipeAttributes":
            // { "workableRecipe": true }
            // A better solution would be
            // to define the recipe with code instead of cloning an ingot recipe defined in the assets
            if (api.Side.IsClient()) continue;
            var ingotCode = new AssetLocation("game:ingot-copper");
            var ingotRecipe = api.ModLoader.GetModSystem<RecipeRegistrySystem>().SmithingRecipes
                .FirstOrDefault(r =>
                    r.Ingredient?.Code?.Equals(ingotCode) == true &&
                    r.Output.ResolvedItemstack?.Collectible.Code.Equals(ingotCode) == true);
            if (ingotRecipe?.Ingredient == null) continue;
            if (!WildcardUtil.Match(Config.IngotSelector, collObj.Code.ToString())) continue;
            if (api.ModLoader.GetModSystem<RecipeRegistrySystem>().SmithingRecipes
                .Any(r => r.Ingredient?.Code?.Equals(collObj.Code) == true &&
                          r.Output.ResolvedItemstack?.Collectible.Code.Equals(collObj.Code) == true)) continue;
            Logger.VerboseDebug($"Adding workable-only ingot recipe for {collObj.Code}");
            var newRecipe = new SmithingRecipe
            {
                Code = new AssetLocation(ModId, collObj.Code.Path + "-to-itself"),
                Name = ingotRecipe.Name,
                Pattern = ingotRecipe.Pattern,
                Voxels = ingotRecipe.Voxels,
                Ingredient = new CraftingRecipeIngredient
                {
                    Type = collObj.ItemClass,
                    Code = collObj.Code,
                    RecipeAttributes = ingotRecipe.Ingredient.RecipeAttributes
                },
                Output = new JsonItemStack
                {
                    Type = collObj.ItemClass,
                    Code = collObj.Code,
                    StackSize = 1
                },
                RecipeId = api.ModLoader.GetModSystem<RecipeRegistrySystem>().SmithingRecipes.Count + 1
            };
            newRecipe.Ingredient.Resolve(api.World, $"[{ModId}] add ingot smithing recipe");
            newRecipe.Output.Resolve(api.World, $"[{ModId}] add ingot smithing recipe");
            api.ModLoader.GetModSystem<RecipeRegistrySystem>().SmithingRecipes.Add(newRecipe);
        }
    }

    private static void Patch()
    {
        if (HarmonyInstance != null) return;
        HarmonyInstance = new Harmony(ModId);
        Logger.VerboseDebug("Patching...");
        AlwaysPatchCategory.PatchIfEnabled(true);
        ToolRecoveryCategory.PatchIfEnabled(Config.EnableToolRecovery);
        SmithingRecipeAttributesPatch.PatchIfEnabled(
            Config.SmithWithBits || Config.BitsTopUp || Config.EnableToolRecovery, HarmonyInstance);
        ClientTweaksCategories.RememberHammerToolMode.PatchIfEnabled(Config.RememberHammerToolMode);
        ClientTweaksCategories.AnvilShowRecipeVoxels.PatchIfEnabled(Config.AnvilShowRecipeVoxels);
        ClientTweaksCategories.ShowWorkablePatches.PatchIfEnabled(Config.ShowWorkableTemperature);
        ClientTweaksCategories.HandbookExtraInfo.PatchIfEnabled(Config.HandbookExtraInfo);
        BitsRecoveryCategory.PatchIfEnabled(Config.RecoverBitsOnSplit);
        HelveHammerBitsRecoveryCategory.PatchIfEnabled(Config.HelveHammerBitsRecovery);
        CastingTweaksCategory.PatchIfEnabled(Config.MetalCastingTweaks);
        DynamicMoldsCategory.PatchIfEnabled(Config.DynamicMoldUnits);
        BitSmithingCategory.PatchIfEnabled(Config.SmithWithBits || Config.BitsTopUp);
        HammerTweaksCategory.PatchIfEnabled(Config.HammerTweaks);
        //StoneSmithingCategory.PatchIfEnabled(true);
    }

    private static void Unpatch()
    {
        Logger?.VerboseDebug("Unpatching...");
        HarmonyInstance?.UnpatchAll(ModId);
        HarmonyInstance = null;
    }

    /// <summary>
    ///     Clears this instance's own side. Each side has its own instance and disposes independently, so
    ///     clearing both here would leave the surviving side reaching for an API that has been dropped.
    /// </summary>
    public override void Dispose()
    {
        Unpatch();
        if (_side == EnumAppSide.Client) _clientApi = null;
        else _serverApi = null;
        if (_clientApi == null && _serverApi == null) Logger = null;
        base.Dispose();
    }
}