using JetBrains.Annotations;
using SmithingPlus.Config;
using SmithingPlus.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SmithingPlus.HammerTweaks;

[UsedImplicitly(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public class HammerTweaksNetwork : ModSystem
{
    private const string ChannelName = $"{Core.ModId}:{Core.HammerTweaksCategory}";

    public override bool ShouldLoad(ICoreAPI api)
    {
        return ConfigLoader.Config?.HammerTweaks ?? false;
    }

    public override double ExecuteOrder()
    {
        return base.ExecuteOrder() + 0.01;
    }

    public override void Start(ICoreAPI api)
    {
        api.Network
            .RegisterChannel(ChannelName)
            .RegisterMessageType(typeof(FlipToolModePacket));
    }

    public override void Dispose()
    {
        ClientChannel = null;
        base.Dispose();
    }

    #region Client

    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private IClientNetworkChannel ClientChannel { get; set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Local
    private ICoreClientAPI Capi { get; set; }

    public override void StartClientSide(ICoreClientAPI api)
    {
        Capi = api;
        ClientChannel = api.Network.GetChannel(ChannelName);
    }

    public static void SendFlipToolMode(ICoreClientAPI capi, int flipToolModeIndex)
    {
        var response = new FlipToolModePacket
        {
            ToolMode = flipToolModeIndex
        };
        capi.Network.GetChannel(ChannelName)?.SendPacket(response);
    }

    #endregion

    #region Server

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Network
            .GetChannel(ChannelName)
            .SetMessageHandler<FlipToolModePacket>(ReceiveFlipToolMode);
    }

    /// <summary>
    ///     Records which mode index means "flip" on the server's copy of the hammer.
    ///     <para>
    ///         Only a hammer is written to. The packet names no slot, so it lands on whatever the player is
    ///         holding when it arrives; without this check, switching hotbar slots between opening the tool
    ///         mode menu and the packet arriving stamped the index onto an unrelated item.
    ///     </para>
    /// </summary>
    private static void ReceiveFlipToolMode(IServerPlayer fromPlayer, FlipToolModePacket packet)
    {
        var stack = fromPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        if (stack?.Collectible is not ItemHammer) return;
        if (packet.ToolMode < 0) return;
        stack.Attributes.SetInt(ModStackAttributes.FlipToolModeIndex, packet.ToolMode);
    }

    #endregion
}