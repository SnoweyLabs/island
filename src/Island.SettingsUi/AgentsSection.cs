using System.Windows;
using System.Windows.Controls;
using Island.Core;
using Island.Core.SettingsEdit;

namespace Island.SettingsUi;

/// <summary>
/// "Coding agents" (WORK-ORDER-7 section 4, WORK-ORDER-11 sections 3 and 5): one row for each helper the island can be connected to, "Claude Code" and "Codex", with the
/// button "Connect" (or "Disconnect"; "Update" and "Disconnect" for a connection made before the Terminals page, which only tells the island when the helper has finished).
/// Pressing Connect or Update shows, before anything is written, every line that will be added and where the file is, written as %USERPROFILE%, then the folder and the file,
/// never expanded; the lines scroll when they do not fit, never a shortened list; a second button confirms. The state is read when the person opens this section, and never at
/// any other time. A helper whose own folder is not on this computer says "Not found on this computer." and has no button. Gemini's and Antigravity's terminal program
/// are listed with the reason they are not connected: their hooks cannot run without making the helper wait.
/// </summary>
internal static class AgentsSection
{
    public const string Sentence = "You can see every hook with /hooks inside Claude Code. Claude Code may need a restart to notice a new hook. This works for Claude Code in a terminal; whether the desktop app and the editor extension use the same file is not known.";

    /// <summary>What the section says to a person who has just updated Claude Code's connection (WORK-ORDER-11 section 3).</summary>
    public const string AfterUpdate = "After Update, start Claude Code: if it complains about its settings, press Disconnect.";

    /// <summary>The row of a helper that cannot be connected, with the reason (the helper's own hooks page, read 2026-10-07).</summary>
    public static string Blocked(string name) => $"Not connected: {name} cannot start a hook without waiting for it, and Island never makes a helper\u00A0wait.";

    public static FrameworkElement Build(ISectionHost host)
    {
        var session = host.Session;
        var stack = new StackPanel();
        var first = true;

        // The helpers the island can be connected to: with their own connector when this session has one, else the same rows with "Not connected." and no button.
        var shown = session.AgentConnectors.Count > 0
            ? session.AgentConnectors.Select(c => (c.Helper, c.DisplayName)).ToList()
            : [("claude", "Claude Code"), ("codex", "Codex")];
        foreach (var (helper, name) in shown)
        {
            var card = HelperRow(host, helper, name);
            card.Margin = new Thickness(0, first ? 0 : 10, 0, 0);
            stack.Children.Add(card);
            first = false;
        }

        foreach (var name in new[] { "Gemini", "Antigravity's terminal program" })
        {
            var card = Card(host, name, Blocked(name), null, connected: false);
            card.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(card);
        }

        var sentence = Look.Label(Sentence, Look.HintSize, brush: Look.Sub);
        sentence.TextWrapping = TextWrapping.Wrap;
        sentence.Margin = new Thickness(4, 12, 4, 0);
        stack.Children.Add(sentence);
        stack.Children.Add(KeySection.NoticeLine(host));
        return stack;
    }

    private static Border HelperRow(ISectionHost host, string helper, string name)
    {
        var session = host.Session;
        var available = session.AgentConnectors.Any(c => c.Helper == helper);
        var state = available ? session.AgentState(helper) : AgentConnection.NotConnected; // read now, by the person's own opening of this section
        var connected = state is AgentConnection.Connected or AgentConnection.ConnectedOlder;

        var status = state switch
        {
            AgentConnection.Connected => $"Connected. Island shows what {name} is doing in the Terminals page, and says so when {name} has finished or needs your answer.",
            AgentConnection.ConnectedOlder => $"Connected, but only tells Island when {name} has finished. Update to see what it is doing.",
            AgentConnection.NotFound => "Not found on this computer.",
            AgentConnection.Unreadable => AgentRefusals.HooksFileUnreadable.Message.Replace("Claude Code", name),
            _ => "Not connected.",
        };
        if (session.AgentNotOfferedOf(helper) is { } why && available) status = why; // a package without the stable name: the row says why there is no button

        var buttons = new List<FrameworkElement>();
        if (available && state != AgentConnection.NotFound && session.AgentNotOfferedOf(helper) is null)
        {
            if (state == AgentConnection.ConnectedOlder)
            {
                buttons.Add(Button(host, "Update", true, () => Ask(host, helper, name, update: true), $"agents:{helper}:update"));
                buttons.Add(Button(host, "Disconnect", false, () => Disconnect(host, helper, name), $"agents:{helper}"));
            }
            else if (connected) buttons.Add(Button(host, "Disconnect", false, () => Disconnect(host, helper, name), $"agents:{helper}"));
            else buttons.Add(Button(host, "Connect", true, () => Ask(host, helper, name, update: false), $"agents:{helper}"));
        }

        return Card(host, name, status, buttons, connected, warn: state == AgentConnection.Unreadable);
    }

    private static FrameworkElement Button(ISectionHost host, string text, bool filled, Action action, string key)
    {
        var button = Parts.Pill(text, filled, action, key);
        button.Margin = new Thickness(8, 0, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        return button;
    }

    private static Border Card(ISectionHost host, string name, string status, IReadOnlyList<FrameworkElement>? buttons, bool connected, bool warn = false)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Look.Label(name, 15, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 0, 0, 4);
        text.Children.Add(title);
        var line = Look.Label(status, Look.HintSize, brush: warn ? Look.Warn : Look.Sub);
        line.TextWrapping = TextWrapping.Wrap;
        text.Children.Add(line);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        if (buttons is { Count: > 0 })
        {
            var holder = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var button in buttons) holder.Children.Add(button);
            Grid.SetColumn(holder, 1);
            row.Children.Add(holder);
        }

        return new Border
        {
            Background = Look.Group,
            BorderBrush = connected ? Look.BrushOf(host.Accent) : Look.Line,
            BorderThickness = new Thickness(connected ? 2 : 1),
            CornerRadius = new CornerRadius(Look.GroupRadius),
            Padding = new Thickness(connected ? 15 : 16),
            Child = row,
        };
    }

    /// <summary>The question before anything is written: every line, the place of the file, and what is saved first. Codex adds its own sentence about trusting new hooks.</summary>
    private static void Ask(ISectionHost host, string helper, string name, bool update)
    {
        var session = host.Session;
        var trust = session.AgentConnectors.FirstOrDefault(c => c.Helper == helper)?.Trust;
        var saved = update
            ? "A copy of the file as it is now is saved beside it first, under a second name, and the small program the hook runs is copied again into Island's own folder."
            : "A copy of the file as it is now is saved beside it first, and the small program the hook runs is copied into Island's own folder.";
        var question = $"Island will add these lines to {name}'s {(helper == "claude" ? "settings" : "hooks")} file, {session.AgentLocationOf(helper)}:\n\n{string.Join("\n", session.AgentLinesOf(helper))}\n\n{saved}"
                       + (trust is null ? string.Empty : $"\n\n{trust}")
                       + (helper == "claude" && update ? $"\n\n{AfterUpdate}" : string.Empty);
        host.Ask(question, update ? $"Yes, update {name}" : $"Yes, connect {name}", () =>
        {
            host.Report(update ? session.UpdateAgent(helper) : session.ConnectAgent(helper));
            host.Refresh($"agents:{helper}");
        }, update ? "No, do not update" : "No, do not connect");
    }

    private static void Disconnect(ISectionHost host, string helper, string name)
    {
        host.Ask($"Island will take its own lines out of {name}'s file and nothing else.", "Yes, disconnect", () =>
        {
            host.Report(host.Session.DisconnectAgent(helper));
            host.Refresh($"agents:{helper}");
        }, "No, keep it connected");
    }
}
