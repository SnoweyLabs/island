using System.IO;
using Island.Core;
using Island.Core.Agents.Connect;

namespace Island.App;

/// <summary>
/// The one file of the whole program that knows where Codex's hooks file is (WORK-ORDER-11 section 5), as <see cref="OutsideAgentConnector"/> is for Claude Code's: it
/// takes no path from anyone, and <see cref="OutsideGate"/> refuses every use of it unless the app was started for real (the self-test, <c>dotnet test</c> and every
/// helper program get "Not connected." and nothing is read or written). It is called from two places only: when a person opens the "Coding agents" section (to show
/// whether it is connected, and whether Codex's own folder is on this computer at all: that is the only thing looked at before the person presses anything) and when a
/// person presses the confirming button. Never at launch, never on a timer, never "to repair". Before the first write a copy of the file as it was is saved beside it.
/// What goes into the file is decided by <see cref="CodexHooks"/> (text in, text out); this file only reads and writes the text.
/// </summary>
internal sealed class OutsideCodexConnector(IPackageFacts package) : IAgentConnector
{

    private const bool AliasDeclared = true;

    private HookCommandKind Kind => HookCommand.For(package, AliasDeclared);

    public string Helper => CodexSignals.Helper;

    public string DisplayName => "Codex";

    public string? Trust => CodexHooks.TrustSentence;

    public string? NotOffered => Kind == HookCommandKind.NotOffered ? HookCommand.NotOfferedText.Replace("Claude Code", "Codex") : null;

    private string Command => Kind == HookCommandKind.Alias ? PackageNames.NotifyAlias : OutsideAgentConnector.NotifyExe;

    /// <summary>The place of the file as the person reads it: never the expanded path.</summary>
    public string Location => CodexHooks.PlaceText;

    public IReadOnlyList<string> LinesToAdd => CodexHooks.LinesToAdd(Command, Kind == HookCommandKind.Alias);

    private static string CodexFolder() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    private static string HooksFile() => Path.Combine(CodexFolder(), "hooks.json");

    private static bool Allowed() => OutsideGate.Current.Allow(OutsideKind.EditAgentSettings);

    public AgentConnection State()
    {
        if (!Allowed()) return AgentConnection.NotConnected;
        if (!Directory.Exists(CodexFolder())) return AgentConnection.NotFound; // Codex's own folder is not here: the only thing looked at
        var text = ReadText(out var failed);
        if (failed) return AgentConnection.Unreadable;
        return CodexHooks.State(text) switch
        {
            ConnectState.Connected => AgentConnection.Connected,
            ConnectState.ConnectedOlder => AgentConnection.ConnectedOlder,
            ConnectState.Unreadable => AgentConnection.Unreadable,
            _ => AgentConnection.NotConnected,
        };
    }

    public ConnectorResult Connect() => ConnectOrUpdate(update: false);

    public ConnectorResult Update() => ConnectOrUpdate(update: true);

    private ConnectorResult ConnectOrUpdate(bool update)
    {
        if (!Allowed() || Kind == HookCommandKind.NotOffered) return new ConnectorResult(false, Named(AgentRefusals.NotAllowed));
        if (!Directory.Exists(CodexFolder())) return new ConnectorResult(false, Named(AgentRefusals.NotAllowed));
        var text = ReadText(out var failed);
        if (failed) return new ConnectorResult(false, Named(AgentRefusals.HooksFileUnreadable));

        // The copy first: a hook that names a program that is not there would fail every time. Packaged, the alias names the program inside the package: no copy.
        if (Kind == HookCommandKind.CopiedProgram && !OutsideAgentConnector.CopyNotify()) return new ConnectorResult(false, Named(OutsideAgentConnector.CopyRefusal()));

        var edit = CodexHooks.Connect(text, Command, Kind == HookCommandKind.Alias);
        if (edit.Reason == HookInstaller.NotifyPathInvalid) return new ConnectorResult(false, Named(AgentRefusals.NotifyPathInvalid));
        if (edit.Reason is not null) return new ConnectorResult(false, Named(AgentRefusals.HooksFileUnreadable));
        return edit.Changed && !Write(edit.Text, update) ? new ConnectorResult(false, Named(AgentRefusals.HooksFileNotWritten)) : new ConnectorResult(true, null);
    }

    public ConnectorResult Disconnect()
    {
        if (!Allowed()) return new ConnectorResult(false, Named(AgentRefusals.NotAllowedForDisconnect));
        var text = ReadText(out var failed);
        if (failed) return new ConnectorResult(false, Named(AgentRefusals.HooksFileUnreadableForDisconnect));
        var edit = CodexHooks.Disconnect(text);
        if (edit.Reason is not null) return new ConnectorResult(false, Named(AgentRefusals.HooksFileUnreadableForDisconnect));
        return edit.Changed && !Write(edit.Text, freshSecondCopy: false) ? new ConnectorResult(false, Named(AgentRefusals.HooksFileNotWrittenForDisconnect)) : new ConnectorResult(true, null);
    }

    // The refusals keep their codes and say "Codex" where they said "Claude Code" (WORK-ORDER-11 register).
    private static Refusal Named(Refusal refusal) => refusal with
    {
        WhatHappened = refusal.WhatHappened.Replace("Claude Code", "Codex"),
        Why = refusal.Why.Replace("Claude Code", "Codex"),
        NextAction = refusal.NextAction.Replace("Claude Code", "Codex"),
    };

    private static string ReadText(out bool failed) => HelperFileEditor.ReadText(HooksFile(), out failed);

    private static bool Write(string text, bool freshSecondCopy) => HelperFileEditor.Write(HooksFile(), text, freshSecondCopy);
}
