namespace Island.Core;

/// <summary>
/// The numbers and the default name shared by Island.Notify (the hook's program) and the island's side of the pipe.
/// The default name is used by one thing only: the app started for real, which passes it to the pipe server.
/// The self-test, every test and every helper use an invented name.
/// </summary>
public static class AgentPipe
{
    /// <summary>The fixed default pipe name. Never written in a test.</summary>
    public const string DefaultName = "island.agents.notice.9b1d7c4e";

    /// <summary>
    /// The pipe name of this Windows user (Dan's P26, WORK-ORDER-13): the fixed name with a short hash of the user's name and domain, so that two people logged in at once on one computer each have their own pipe
    /// and a helper of one never reaches the island of the other. The app and Island.Notify both ask for it, and both run as the same user.
    /// </summary>
    public static string ForThisUser() => ForUser(Environment.UserDomainName, Environment.UserName);

    public static string ForUser(string domain, string user)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((domain + "\\" + user).ToLowerInvariant()));
        return DefaultName + "." + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Island.Notify reads at most this much of the hook's JSON from standard input; more is ignored.</summary>
    public const int StdinLimitBytes = 64 * 1024;

    /// <summary>Island.Notify makes one attempt to connect and gives up after this long.</summary>
    public const int ConnectTimeoutMs = 300;

    /// <summary>The message between Island.Notify and the island is never longer than this (a newline included).</summary>
    public const int MaxMessageBytes = 4096;

    /// <summary>A connection that has sent nothing at all is let go after this long (Island.Notify writes at once).</summary>
    public const int FirstByteTimeoutMs = 1000;

    /// <summary>The island gives one connection this long to deliver its message.</summary>
    public const int ReadTimeoutMs = 1500;

    /// <summary>How many process ids of the chain Island.Notify was started from are carried.</summary>
    public const int MaxChain = 16;

    /// <summary>Only the end of the folder matters (the project's name is its last part), so only this much is carried.</summary>
    public const int MaxFolderChars = 260;

    public const int MaxEventChars = 32;

    public const int MaxKindChars = 64;

    /// <summary>A pipe name handed in as an argument: plain, short, no separators (so it can never name another machine or path).</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 128
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
}
