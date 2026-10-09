namespace Island.Core;

public enum HookCommandKind
{
    /// <summary>Unpackaged: a copy of Island.Notify in the island's own folder, named by its path.</summary>
    CopiedProgram,

    /// <summary>Packaged: the package's execution alias, a stable command name.</summary>
    Alias,

    /// <summary>Packaged without a declared alias: "Connect" is not offered.</summary>
    NotOffered,
}

/// <summary>What the hook names, by how the app runs (WORK-ORDER-8 section 3). A packaged app's %LOCALAPPDATA% is private and its own folder changes with every version, so a copy placed there would not be found.</summary>
public static class HookCommand
{
    public static HookCommandKind For(IPackageFacts package, bool aliasDeclared) =>
        !package.IsPackaged ? HookCommandKind.CopiedProgram : aliasDeclared ? HookCommandKind.Alias : HookCommandKind.NotOffered;

    /// <summary>What the "Coding agents" row says in place of the button when connecting is not offered.</summary>
    public const string NotOfferedText = "Connecting Claude Code is not offered in this build: it needs the program that tells Island to be reachable by a stable name, and this package does not declare one.";
}
