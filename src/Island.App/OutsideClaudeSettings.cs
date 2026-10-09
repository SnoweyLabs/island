using System.IO;
using Island.Core;

namespace Island.App;

/// <summary>
/// The one file of the whole program that knows where Claude Code's settings file is (WORK-ORDER-7 section 4). It takes no path from anyone, so no caller
/// can aim it anywhere else and no test can aim it at the real file by mistake, and <see cref="OutsideGate"/> refuses every use of it unless the app was
/// started for real: in the self-test, in <c>dotnet test</c>, in any helper program it does nothing. It is called from two places only: when a person opens
/// the "Coding agents" section (to show whether it is connected) and when a person presses the confirming button. Never at launch, never on a timer, never
/// "to repair". Before the first write a copy of the file as it was is saved beside it, in Claude Code's own folder.
/// </summary>
internal sealed class OutsideAgentConnector(IPackageFacts package) : IAgentConnector
{
    /// <summary>Packaged, the package declares the alias (the manifest in ship/package); unpackaged the question does not arise.</summary>
    private const bool AliasDeclared = true;

    private HookCommandKind Kind => HookCommand.For(package, AliasDeclared);

    public string? NotOffered => Kind == HookCommandKind.NotOffered ? HookCommand.NotOfferedText : null;

    // What the hook names: the alias when packaged, else the copy of Island.Notify in the island's own folder.
    private string Command => Kind == HookCommandKind.Alias ? PackageNames.NotifyAlias : NotifyExe;

    /// <summary>The place of the file as the person reads it: never the expanded path.</summary>
    public string Location => @"%USERPROFILE%\.claude\settings.json";

    public IReadOnlyList<string> LinesToAdd => HookInstaller.LinesToAdd(Command, Kind == HookCommandKind.Alias);

    // Where the hooks will name the program: a copy in the island's own folder, so that a rebuild or a moved project folder does not break the hook.
    private static string NotifyFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Island", "notify");

    internal static string NotifyExe => Path.Combine(NotifyFolder, HookInstaller.ProgramName + ".exe");

    /// <summary>The settings file. The only place its location is known.</summary>
    private static string SettingsFile() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static bool Allowed() => OutsideGate.Current.Allow(OutsideKind.EditAgentSettings);

    public AgentConnection State()
    {
        if (!Allowed()) return AgentConnection.NotConnected;
        var text = ReadText(out var failed);
        if (failed) return AgentConnection.Unreadable;
        if (HookInstaller.Disconnect(text).Reason == HookInstaller.FileUnreadable) return AgentConnection.Unreadable;
        return HookInstaller.StateOf(text);
    }

    public ConnectorResult Update() => ConnectOrUpdate(update: true);

    public ConnectorResult Connect() => ConnectOrUpdate(update: false);

    /// <summary>
    /// Adds what is missing. An update first saves a fresh copy of the file as it is now beside it under a second name, whatever copy is already there (the first copy,
    /// made by the first connection, stays as it was).
    /// </summary>
    private ConnectorResult ConnectOrUpdate(bool update)
    {
        if (!Allowed()) return new ConnectorResult(false, AgentRefusals.NotAllowed);
        if (Kind == HookCommandKind.NotOffered) return new ConnectorResult(false, AgentRefusals.NotAllowed);
        var text = ReadText(out var failed);
        if (failed) return new ConnectorResult(false, AgentRefusals.HooksFileUnreadable);

        // The copy first: a hook that names a program that is not there would fail every time. Packaged, the alias names the program inside the package: no copy.
        if (Kind == HookCommandKind.CopiedProgram && !CopyNotify()) return new ConnectorResult(false, CopyRefusal());

        var edit = HookInstaller.Connect(text, Command, Kind == HookCommandKind.Alias);
        if (edit.Reason == HookInstaller.NotifyPathInvalid) return new ConnectorResult(false, AgentRefusals.NotifyPathInvalid);
        if (edit.Reason is not null) return new ConnectorResult(false, AgentRefusals.HooksFileUnreadable);
        return edit.Changed && !Write(edit.Text, update) ? new ConnectorResult(false, AgentRefusals.HooksFileNotWritten) : new ConnectorResult(true, null);
    }

    public ConnectorResult Disconnect()
    {
        if (!Allowed()) return new ConnectorResult(false, AgentRefusals.NotAllowedForDisconnect);
        var text = ReadText(out var failed);
        if (failed) return new ConnectorResult(false, AgentRefusals.HooksFileUnreadableForDisconnect);
        var edit = HookInstaller.Disconnect(text);
        if (edit.Reason is not null) return new ConnectorResult(false, AgentRefusals.HooksFileUnreadableForDisconnect);
        return edit.Changed && !Write(edit.Text) ? new ConnectorResult(false, AgentRefusals.HooksFileNotWrittenForDisconnect) : new ConnectorResult(true, null);
    }

    /// <summary>The text of the file; empty when there is none. <paramref name="failed"/> when it exists and cannot be read.</summary>
    private static string ReadText(out bool failed) => HelperFileEditor.ReadText(SettingsFile(), out failed);

    /// <summary>Saves a copy of the file beside it the first time, then writes the new text whole (to a temporary file, then moved over).</summary>
    private static bool Write(string text, bool freshSecondCopy = false) => HelperFileEditor.Write(SettingsFile(), text, freshSecondCopy);

    /// <summary>Why the copy of Island.Notify failed: the program is not beside the island, or the copy could not be made (a running hook may hold the old one, or the folder does not let Island write).</summary>
    internal static Refusal CopyRefusal() => HelperFileEditor.NotifyIsBeside(AppContext.BaseDirectory) ? AgentRefusals.NotifyNotCopied : AgentRefusals.NotifyMissing;

    /// <summary>Copies the program the hook runs, and the files it needs beside it, from next to the island into the island's own folder.</summary>
    internal static bool CopyNotify() => HelperFileEditor.CopyNotify(AppContext.BaseDirectory, NotifyFolder);
}
