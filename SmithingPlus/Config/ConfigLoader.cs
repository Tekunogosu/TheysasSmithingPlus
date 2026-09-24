using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SmithingPlus.Config;

[UsedImplicitly]
public class ConfigLoader : ModSystem
{
    private const string ConfigName = "SmithingPlus.json";

    /// <summary>
    ///     World config key the server publishes its whole config under. World config is the one channel that
    ///     reaches the client before its <c>StartPre</c>, which is where patches are chosen and behaviors
    ///     attached; a network message would only arrive after both. The value is Base64, see
    ///     <see cref="EncodeForWorldConfig" />.
    /// </summary>
    private const string ServerConfigKey = "SmithingPlus_ServerConfig";

    private static readonly PropertyInfo[] ClientSettings = typeof(ServerConfig)
        .GetProperties()
        .Where(property => property.IsDefined(typeof(ClientSettingAttribute)))
        .ToArray();

    public static ServerConfig Config { get; private set; }

    public override double ExecuteOrder()
    {
        return 0.03;
    }

    public override void StartPre(ICoreAPI api)
    {
        var localConfig = LoadLocalConfig(api);
        Config = api.Side == EnumAppSide.Client ? AdoptServerConfig(api, localConfig) : localConfig;
    }

    public override void Start(ICoreAPI api)
    {
        // Written by the server alone: the client receives the server's world config, and writing there
        // from the client would replace the server's values with its own.
        if (api.Side != EnumAppSide.Server) return;
        api.World.Config.SetString(ServerConfigKey, EncodeForWorldConfig(Config));
        api.World.Config.SetBool("SmithingPlus_CanRepairForlornHopeEstoc", Config.CanRepairForlornHopeEstoc);
        api.World.Config.SetBool("SmithingPlus_WorkableBits", Config.SmithWithBits || Config.EnableToolRecovery);
    }

    /// <summary>
    ///     Reads this side's own file, migrating it first if it predates <see cref="ServerConfig.CurrentVersion" />.
    ///     A file that cannot be parsed is left untouched and defaults are used, so a typo is never overwritten.
    /// </summary>
    private ServerConfig LoadLocalConfig(ICoreAPI api)
    {
        var path = Path.Combine(GamePaths.ModConfig, ConfigName);
        ServerConfig config;
        try
        {
            if (!File.Exists(path))
            {
                Mod.Logger.VerboseDebug("Config file not found, creating a new one...");
                config = new ServerConfig();
                api.StoreModConfig(config, ConfigName);
            }
            else
            {
                var raw = JObject.Parse(File.ReadAllText(path));
                var fileVersion = raw.Value<int?>(nameof(ServerConfig.ConfigVersion)) ?? 1;
                config = fileVersion < ServerConfig.CurrentVersion
                    ? Migrate(api, path, raw, fileVersion)
                    : raw.ToObject<ServerConfig>() ?? new ServerConfig();
                api.StoreModConfig(config, ConfigName);
            }
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to load config, you probably made a typo: {0}", e);
            config = new ServerConfig();
        }

        Validate(config);
        return config;
    }

    /// <summary>
    ///     Carries an older file's settings onto a fresh config of the current schema. The original is copied
    ///     aside first, so the migration can be undone by restoring the backup. Keys this version no longer
    ///     knows are dropped from the new file and survive only in the backup.
    /// </summary>
    private ServerConfig Migrate(ICoreAPI api, string path, JObject raw, int fromVersion)
    {
        var backupPath = BackupPath(path, fromVersion);
        File.Copy(path, backupPath);

        var config = new ServerConfig();
        JsonConvert.PopulateObject(raw.ToString(), config);
        config.ConfigVersion = ServerConfig.CurrentVersion;

        Mod.Logger.Notification("Migrated {0} from v{1} to v{2}, backup at {3}",
            ConfigName, fromVersion, ServerConfig.CurrentVersion, backupPath);
        return config;
    }

    private static string BackupPath(string path, int fromVersion)
    {
        var backupPath = $"{path}.v{fromVersion}.bak";
        return File.Exists(backupPath) ? $"{path}.v{fromVersion}.{DateTime.Now:yyyyMMdd-HHmmss}.bak" : backupPath;
    }

    /// <summary>
    ///     Replaces the client's config with the server's, keeping the client's own
    ///     <see cref="ClientSettingAttribute" /> settings unless the server overrides those too.
    /// </summary>
    private ServerConfig AdoptServerConfig(ICoreAPI api, ServerConfig localConfig)
    {
        var serialized = api.World.Config?.GetString(ServerConfigKey);
        if (string.IsNullOrEmpty(serialized))
        {
            Mod.Logger.Warning("Server did not publish its config; using this client's own. " +
                               "Settings may differ from the server's.");
            return localConfig;
        }

        ServerConfig serverConfig;
        try
        {
            serverConfig = DecodeFromWorldConfig(serialized);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            Mod.Logger.Error("Could not read the server's config; using this client's own: {0}", e);
            return localConfig;
        }

        if (serverConfig == null) return localConfig;
        if (!serverConfig.ServerOverridesClientSettings)
            foreach (var setting in ClientSettings)
                setting.SetValue(serverConfig, setting.GetValue(localConfig));

        Mod.Logger.Notification("Using the server's config{0}.",
            serverConfig.ServerOverridesClientSettings ? ", including client settings" : "");
        return serverConfig;
    }

    /// <summary>
    ///     The config as JSON, Base64-encoded. World config strings are written back out as JSON by vanilla
    ///     without escaping (<c>StringAttribute.ToJsonToken</c>), and the singleplayer "Modify world" screen parses
    ///     that output: raw JSON, or any setting holding a quote or backslash, made the save crash that screen.
    ///     Base64's alphabet needs no escaping. Reusing the key the raw JSON was stored under overwrites it on
    ///     the next load, which is what repairs a save written by 1.10.4.
    /// </summary>
    public static string EncodeForWorldConfig(ServerConfig config)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(config)));
    }

    public static ServerConfig DecodeFromWorldConfig(string encoded)
    {
        return JsonConvert.DeserializeObject<ServerConfig>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
    }

    private void Validate(ServerConfig config)
    {
        if (config.BrokenToolVoxelPercent < 0.2)
            Mod.Logger.Warning($"[{nameof(ConfigLoader)}] Config setting {nameof(config.BrokenToolVoxelPercent)} " +
                               "has a very low value, your broken tools will be almost or fully empty.");
        if (config.VoxelsPerBit is < 2 or > 3)
        {
            Mod.Logger.Warning($"[{nameof(ConfigLoader)}] Config setting {nameof(config.VoxelsPerBit)} " +
                               "requires a value between 2 and 3. Clamping value.");
            config.VoxelsPerBit = Math.Clamp(config.VoxelsPerBit, 2, 3);
        }
    }

    public override void Dispose()
    {
        Config = null;
        base.Dispose();
    }
}
