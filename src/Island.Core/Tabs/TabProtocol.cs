namespace Island.Core;

/// <summary>The fixed numbers of extension/PROTOCOL.md (version 1). Both the listener and its tests read them from here.</summary>
public static class TabProtocol
{
    public const int Version = 1;

    /// <summary>The first port tried; if it is taken, the next ones in <see cref="Ports"/>.</summary>
    public const int Port = 47653;

    /// <summary>47653 to 47657, in the order both the listener and the add-on try them.</summary>
    public static IReadOnlyList<int> Ports { get; } = [47653, 47654, 47655, 47656, 47657];

    public const string Path = "/island";

    /// <summary>What a browser puts in front of an add-on's id in the Origin header; a web page cannot send it.</summary>
    public const string OriginPrefix = "chrome-extension://";

    /// <summary>The fixed id of the add-on (from the public key in its manifest). The real bridge accepts only this Origin.</summary>
    public const string AddonId = "lmnojmilhkpdhejkanneoogmjldolook";

    public const string AddonOrigin = OriginPrefix + AddonId;

    public const string ClientName = "island-addon";

    /// <summary>A frame larger than this (in UTF-8 bytes) is dropped and the connection closed.</summary>
    public const int MaxFrameBytes = 1024 * 1024;

    /// <summary>A tab icon's PNG bytes, before base64.</summary>
    public const int MaxIconBytes = 64 * 1024;

    public const int MaxTitleChars = 200;

    /// <summary>Hostnames are at most 253 characters (RFC 1035).</summary>
    public const int MaxHostChars = 253;

    /// <summary>Ten bad frames in a row close the connection.</summary>
    public const int MaxBadFramesInARow = 10;

    public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan HelloDeadline = TimeSpan.FromSeconds(3);

    /// <summary>Chosen by the B4 agent: a hostile local program cannot grow the island's memory without bound.</summary>
    public const int MaxTabsPerConnection = 2000;

    /// <summary>The five sites whose pages report what they play and take play/pause, next and previous.</summary>
    public static IReadOnlyList<string> MediaHosts { get; } =
        ["youtube.com", "music.youtube.com", "twitch.tv", "soundcloud.com", "open.spotify.com"];

    public static bool IsMediaHost(string? host) => host is not null && MediaHosts.Contains(SiteMatch.Normalize(host));
}
