using System;

namespace SmithingPlus.Config;

/// <summary>
///     Marks a <see cref="ServerConfig" /> setting that each player chooses for themselves. Every other setting
///     is taken from the server, so client and server never disagree about gameplay; these keep the client's
///     own value unless the server sets <see cref="ServerConfig.ServerOverridesClientSettings" />.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ClientSettingAttribute : Attribute;
