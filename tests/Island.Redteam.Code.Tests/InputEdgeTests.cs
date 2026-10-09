using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Island.Agents;
using Island.Core;
using Island.Core.Agents.Sessions;

namespace Island.Redteam.Code.Tests;

/// <summary>
/// WORK-ORDER-12 section 4, CODE: everything that comes in from outside: the pipe's messages and Island.Notify's arguments, a hook's input, the add-on's frames, and the numbers that two
/// sides of a protocol write twice. Garbage, too long, too many, too fast. Invented names only; the pipes are on invented names.
/// </summary>
public class InputEdgeTests
{
    // ---- the two wires ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Wires_Encode_Within_The_Limit_Even_When_Every_Field_Is_Full_Of_The_Widest_Characters()
    {
        var wide = string.Concat(Enumerable.Repeat("\U0001F600", 400)); // 800 UTF-16 units of 4-byte characters
        var quotes = new string('"', 400);
        var chain = Enumerable.Repeat(int.MaxValue, 40).ToArray();
        foreach (var text in new[] { wide, quotes, new string('\u20AC', 400), new string('<', 400) })
        {
            var two = SessionWire.Encode(text, text, text, text, long.MaxValue, text, chain);
            Assert.True(two.Length <= SessionLimits.MaxMessageBytes, $"version 2: {two.Length} bytes");
            Assert.NotNull(SessionWire.TryParse(two.AsSpan(0, two.Length - 1)));
            var one = AgentWire.Encode(text, text, text, chain);
            Assert.True(one.Length <= AgentPipe.MaxMessageBytes, $"version 1: {one.Length} bytes");
            Assert.NotNull(AgentWire.TryParse(one.AsSpan(0, one.Length - 1)));
        }
    }

    [Fact]
    public void Wires_Fuzzed_Bytes_Never_Throw_And_What_They_Accept_Is_Within_Every_Limit()
    {
        var rng = new Random(77);
        var good = new List<byte[]>
        {
            SessionWire.Encode("claude", "Stop", "", "s-1", 1234, "Q:\\Invented\\Alpha", [100, 200]),
            SessionWire.Encode("codex", "PostToolUse", "Bash", "s-2", 99, "", []),
            AgentWire.Encode("Stop", "", "Q:\\Invented\\Alpha", [100, 200, 300]),
        };
        var accepted = 0;
        for (var round = 0; round < 40_000; round++)
        {
            var bytes = (byte[])good[round % good.Count].Clone();
            var edits = rng.Next(1, 6);
            for (var e = 0; e < edits; e++)
            {
                switch (rng.Next(4))
                {
                    case 0: bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256); break;
                    case 1: bytes = bytes[..rng.Next(bytes.Length)]; if (bytes.Length == 0) bytes = [0x7B]; break;
                    case 2: bytes = [.. bytes[..rng.Next(bytes.Length)], .. Encoding.UTF8.GetBytes("\"\\ud800\""), .. bytes]; break;
                    default: bytes = [.. bytes, .. new byte[rng.Next(0, 64)]]; break;
                }
            }

            SessionMessage? m = null;
            var ex = Record.Exception(() => m = SessionWire.TryParseAny(bytes));
            Assert.Null(ex);
            if (m is null) continue;
            accepted++;
            Assert.True(m.Helper.Length <= SessionLimits.MaxHelperChars && m.Event.Length <= SessionLimits.MaxEventChars && m.Kind.Length <= SessionLimits.MaxKindChars
                        && m.SessionId.Length <= SessionLimits.MaxSessionIdChars && m.Folder.Length <= SessionLimits.MaxFolderChars && m.Chain.Count <= SessionLimits.MaxChain && m.Chain.All(p => p > 0));
            Assert.True(m.Time is null or >= 0);
        }

        Assert.True(accepted > 0);
    }

    [Fact]
    public void NotifyArguments_Random_Argument_Lists_Never_Throw_And_Never_Name_A_Pipe_That_Is_Not_Plain()
    {
        var rng = new Random(5);
        string[] pieces = ["--agent", "--event", "claude", "Codex", "--", "-x", "pipe.name", "..\\evil", "\\\\.\\pipe\\x", "a/b", "", " ", new string('a', 200), "\u0130", "name-with-hyphen", "--agent=claude", "invented.pipe.name.0001"];
        for (var i = 0; i < 30_000; i++)
        {
            var args = Enumerable.Range(0, rng.Next(0, 7)).Select(_ => pieces[rng.Next(pieces.Length)]).ToArray();
            NotifyArguments? parsed = null;
            Assert.Null(Record.Exception(() => parsed = NotifyArguments.Parse(args)));
            if (parsed is null) continue;
            Assert.True(parsed.PipeName is null || AgentPipe.IsValidName(parsed.PipeName) && !parsed.PipeName.StartsWith('-'));
            Assert.True(parsed.Agent.Length is > 0 and <= NotifyArguments.MaxAgentChars && parsed.Agent == parsed.Agent.ToLowerInvariant());
            Assert.True(parsed.Event is null || parsed.Event.Length is > 0 and <= NotifyArguments.MaxEventChars);
        }
    }

    [Fact]
    public void HookInput_Fuzzed_Bytes_Never_Throw_And_Boundaries_Of_The_Stdin_Limit_Hold()
    {
        var rng = new Random(9);
        var valid = Encoding.UTF8.GetBytes("{\"hook_event_name\":\"Stop\",\"cwd\":\"Q:\\\\Invented\\\\Alpha\",\"session_id\":\"s-1\",\"extra\":[1,2,{\"a\":\"b\"}]}");
        for (var i = 0; i < 30_000; i++)
        {
            var bytes = (byte[])valid.Clone();
            for (var e = rng.Next(1, 5); e > 0; e--) bytes[rng.Next(bytes.Length)] = (byte)rng.Next(256);
            if (rng.Next(3) == 0) bytes = bytes[..rng.Next(bytes.Length)];
            Assert.Null(Record.Exception(() => HookInputReader.Read(bytes)));
            Assert.Null(Record.Exception(() => HookInputReader.Read(bytes, "Stop")));
        }

        // exactly at, and one over, the limit; a cut inside the last field keeps the first three
        var filler = new string('x', AgentPipe.StdinLimitBytes);
        var big = Encoding.UTF8.GetBytes("{\"hook_event_name\":\"Stop\",\"cwd\":\"Q:\\\\Invented\\\\Beta\",\"pad\":\"" + filler + "\"}");
        Assert.True(big.Length > AgentPipe.StdinLimitBytes);
        Assert.Null(HookInputReader.Read(big));
        var cut = big[..AgentPipe.StdinLimitBytes];
        var read = HookInputReader.Read(cut);
        Assert.Equal(new HookInput("Stop", "", "Q:\\Invented\\Beta"), read);
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("{\"hook_event_name\":\"Stop\"}")).ToArray();
        Assert.Equal("Stop", HookInputReader.Read(withBom)?.Event);
    }

    [Fact]
    public void ProjectName_Odd_Folders_Give_A_Short_Clean_Name_Or_Nothing()
    {
        foreach (var folder in new[] { "", "\\", "C:\\", "C:", "\\\\server\\share\\", "..", "...\\", "a\0b", new string('x', 5000), "\u202Eevil", "\U0001F600\\", "Q:\\Invented\\Alpha\\\\\\", "/", "q:/x/y/", "  \t  " })
        {
            var name = ProjectName.From(folder);
            Assert.True(name.Length <= ProjectName.MaxChars, folder.Length > 50 ? "long" : folder);
            Assert.DoesNotContain(name, c => char.IsControl(c));
        }

        Assert.Equal("Alpha", ProjectName.From("Q:\\Invented\\Alpha\\\\"));
        Assert.Equal("", ProjectName.From("\u200B"));
    }

    // ---- the add-on's frames -------------------------------------------------------------------------------------------------------------------------------------

    private static string TabFrame(string title, string type = "tab") =>
        type == "tab"
            ? "{\"type\":\"tab\",\"tab\":{\"id\":1,\"windowId\":1,\"title\":" + System.Text.Json.JsonSerializer.Serialize(title, Raw) + ",\"host\":\"example.org\",\"audible\":false,\"active\":false}}"
            : "{\"type\":\"snapshot\",\"tabs\":[{\"id\":2,\"windowId\":1,\"title\":\"Fine\",\"host\":\"example.org\",\"audible\":false,\"active\":true},{\"id\":1,\"windowId\":1,\"title\":" + System.Text.Json.JsonSerializer.Serialize(title, Raw) + ",\"host\":\"example.org\",\"audible\":false,\"active\":false}]}";

    // JSON.stringify writes non-ASCII text as it is, as UTF-8: no \u escapes.
    private static readonly System.Text.Json.JsonSerializerOptions Raw = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void A_Title_Of_Two_Hundred_Plain_Characters_Is_Taken_And_One_Of_Two_Hundred_And_One_Is_Not()
    {
        Assert.True(TabMessages.ParseAddon(TabFrame(new string('a', 200))).Ok);
        Assert.False(TabMessages.ParseAddon(TabFrame(new string('a', 201))).Ok);
    }

    [Fact]
    public void Defect_A_Title_The_Addon_Cut_To_Two_Hundred_Characters_Is_Refused_When_It_Holds_An_Emoji_And_Spoils_The_Whole_Snapshot()
    {
        // code-1-7 (MEDIUM): extension/lib/protocol.js cut() counts whole characters (Array.from(text).slice(0, 200): code points) and PROTOCOL.md says "title (text, at most 200 chars)";
        // TabMessages.Str counts UTF-16 units (s.Length <= 200), where an emoji is two. A page title of 200 or more characters with one emoji in it (a long post's or article's title) is
        // sent by the add-on as exactly 200 code points = 201 units, and the island refuses it as "bad field": for a snapshot the WHOLE list of tabs is refused ("one bad tab spoils the
        // frame"), for a tab frame that tab is never known; ten such frames in a row close the connection. The same two counts are in media.js (MAX_TEXT = 200 code points) and
        // TabMessages.NullableStr (the media title and artist). Expected: the island takes what the add-on sends (count code points, or allow twice the units, or cut instead of refuse).
        var title = new string('a', 199) + "\U0001F600"; // 200 code points, as cut() leaves it
        Assert.Equal(200, title.EnumerateRunes().Count());
        var tab = TabMessages.ParseAddon(TabFrame(title));
        var snapshot = TabMessages.ParseAddon(TabFrame(title, "snapshot"));
        Assert.True(tab.Ok && snapshot.Ok, $"tab: {tab.Reject}; snapshot: {snapshot.Reject}");
    }

    [Fact]
    public void Defect_A_Media_Title_The_Addon_Cut_To_Two_Hundred_Characters_Is_Refused_When_It_Holds_An_Emoji()
    {
        // code-1-7 (second input of the same finding): the media frame, with the title cut by media.js to 200 code points.
        var title = new string('b', 199) + "\U0001F3B5";
        var frame = "{\"type\":\"media\",\"id\":1,\"title\":\"" + title + "\",\"artist\":null,\"state\":\"playing\",\"position\":1,\"length\":100}";
        var parsed = TabMessages.ParseAddon(frame);
        Assert.True(parsed.Ok, parsed.Reject);
    }

    [Fact]
    public void TabMessages_Fuzzed_Frames_Never_Throw_And_What_Is_Taken_Is_Within_The_Limits()
    {
        var rng = new Random(21);
        var seeds = new[]
        {
            TabFrame("Alpha"), TabFrame("Alpha", "snapshot"),
            "{\"type\":\"hello\",\"v\":1,\"client\":\"island-addon\",\"browser\":\"Chrome\",\"profile\":\"abc-1\",\"version\":\"1.1.0\"}",
            "{\"type\":\"media\",\"id\":1,\"title\":\"T\",\"artist\":\"A\",\"state\":\"playing\",\"position\":1.5,\"length\":100,\"rate\":1,\"readAt\":1790000000000}",
            "{\"type\":\"icon\",\"id\":3,\"png\":\"iVBORw0KGgo=\"}", "{\"type\":\"result\",\"cmd\":\"close\",\"id\":3,\"ok\":true}", "{\"type\":\"tab-activated\",\"id\":1,\"windowId\":2}",
        };
        for (var i = 0; i < 40_000; i++)
        {
            var chars = seeds[i % seeds.Length].ToCharArray();
            for (var e = rng.Next(1, 5); e > 0; e--) chars[rng.Next(chars.Length)] = (char)rng.Next(rng.Next(3) == 0 ? 0xFFFF : 128);
            var frame = new string(chars);
            if (rng.Next(4) == 0) frame = frame[..rng.Next(frame.Length)];
            Parsed<AddonMessage> parsed = default;
            Assert.Null(Record.Exception(() => parsed = TabMessages.ParseAddon(frame)));
            Assert.True(parsed.Ok ^ parsed.Reject is not null);
            if (parsed.Message is SnapshotMessage s) Assert.True(s.Tabs.Count <= TabProtocol.MaxTabsPerConnection && s.Tabs.All(t => t.Title.Length <= TabProtocol.MaxTitleChars && t.Host.Length <= TabProtocol.MaxHostChars));
            if (parsed.Message is IconMessage icon) Assert.True(icon.Png.Length <= TabProtocol.MaxIconBytes);
        }
    }

    [Fact]
    public void A_Snapshot_Of_The_Most_Tabs_At_Their_Widest_Fits_The_Frame_Limit_Or_The_Two_Numbers_Disagree()
    {
        // TabProtocol.MaxTabsPerConnection (2000) and MaxFrameBytes (1 MiB): the widest honest tab (a 200-character title, a 253-character host, as the add-on writes it) is about 560 bytes;
        // 2000 of them are 1.1 MB. PROTOCOL.md says "2000 tabs with full titles are about 600 KB" (an average one). The honest worst case is over the frame limit by about 8 %.
        var tab = "{\"id\":1234567,\"windowId\":123,\"title\":\"" + new string('t', 200) + "\",\"host\":\"" + new string('h', 253) + "\",\"audible\":false,\"active\":false,\"pinned\":true,\"incognito\":true}";
        var bytes = Encoding.UTF8.GetByteCount("{\"type\":\"snapshot\",\"tabs\":[") + 2000 * (tab.Length + 1) + 2;
        Assert.True(bytes > TabProtocol.MaxFrameBytes, $"worst case is {bytes} bytes against {TabProtocol.MaxFrameBytes}"); // the numbers differ in that corner; nobody has 2000 such tabs: LATENT, recorded in the report
    }

    [Fact]
    public void The_Constants_Of_The_Island_And_The_Addon_Agree_Where_Both_Write_Them()
    {
        var js = Repo.Text("extension", "lib", "protocol.js");
        Assert.Contains($"const VERSION = {TabProtocol.Version};", js);
        Assert.Contains("const PORTS = [" + string.Join(", ", TabProtocol.Ports) + "];", js);
        Assert.Contains($"const PATH = '{TabProtocol.Path}';", js);
        Assert.Contains($"const CLIENT = '{TabProtocol.ClientName}';", js);
        Assert.Contains($"const MAX_TITLE = {TabProtocol.MaxTitleChars};", js);
        Assert.Contains($"const MAX_ICON_BYTES = {TabProtocol.MaxIconBytes / 1024} * 1024;", js);
        Assert.Contains($"const KEEPALIVE_MS = {TabProtocol.KeepaliveInterval.TotalSeconds:0} * 1000;", js);
        var sites = Repo.Text("extension", "lib", "sites.js");
        var hosts = Regex.Match(sites, @"MEDIA_HOSTS = \[(.*?)\]").Groups[1].Value.Split(',').Select(h => h.Trim().Trim('\'')).ToArray();
        Assert.Equal(TabProtocol.MediaHosts.OrderBy(h => h), hosts.OrderBy(h => h));
        var doc = Repo.Text("extension", "PROTOCOL.md");
        Assert.Contains("1 MB", doc);
        Assert.Contains("2000 tabs", doc);
        Assert.Contains("8 connections", doc);
    }

    [Fact]
    public void The_Accepted_Origin_Is_The_Id_Chrome_Derives_From_The_Manifest_Key()
    {
        // An add-on's id is the first 128 bits of the SHA-256 of its public key (DER), each hex digit written as a letter a to p (Chromium's documented way of deriving an id from "key").
        var manifest = Repo.Text("extension", "manifest.json");
        var key = Regex.Match(manifest, "\"key\":\\s*\"([^\"]+)\"").Groups[1].Value;
        var hash = SHA256.HashData(Convert.FromBase64String(key));
        var id = string.Concat(hash.Take(16).Select(b => (char)('a' + (b >> 4)) + "" + (char)('a' + (b & 15))));
        Assert.Equal(TabProtocol.AddonId, id);
        Assert.Equal("chrome-extension://" + id, TabProtocol.AddonOrigin);
        Assert.Contains($"\"version\": \"{Regex.Match(manifest, "\"version\":\\s*\"([^\"]+)\"").Groups[1].Value}\"", manifest);
    }

    // ---- the pipe --------------------------------------------------------------------------------------------------------------------------------------------------

    private static string NewName() => "island-redteam-code-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Pipe_A_Burst_Of_Valid_Messages_From_Many_Clients_Is_Taken_Whole()
    {
        var name = NewName();
        var seen = new System.Collections.Concurrent.ConcurrentBag<SessionMessage>();
        using var server = new AgentPipeServer(name);
        server.MessageReceived += seen.Add;
        Assert.True(server.Start());
        var tasks = Enumerable.Range(0, 150).Select(i => Task.Factory.StartNew(() =>
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            client.Connect(5000);
            client.Write(SessionWire.Encode("claude", "Stop", "", "s" + i, 1, "Q:\\Invented\\Alpha", [100]));
            client.Flush();
            client.ReadAsync(new byte[1]).AsTask().Wait(3000);
        }, TaskCreationOptions.LongRunning)).ToArray();
        await Task.WhenAll(tasks);
        await Task.Delay(300);
        Assert.Equal(150, seen.Select(m => m.SessionId).Distinct().Count());
    }

    [Fact]
    public async Task Pipe_Garbage_Too_Long_And_Silent_Clients_Do_Not_Stop_A_Good_One()
    {
        var name = NewName();
        var seen = new System.Collections.Concurrent.ConcurrentBag<SessionMessage>();
        using var server = new AgentPipeServer(name);
        server.MessageReceived += seen.Add;
        Assert.True(server.Start());
        var silent = Enumerable.Range(0, 20).Select(_ =>
        {
            var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            c.Connect(2000);
            return c;
        }).ToList();
        try
        {
            foreach (var bytes in new[] { new byte[] { 0xFF, 0xFE, 0x00 }, new byte[10_000], Encoding.UTF8.GetBytes(new string('{', 5000)), Encoding.UTF8.GetBytes("{\"v\":2}\n"), "\n"u8.ToArray() })
            {
                using var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
                c.Connect(2000);
                try
                {
                    c.Write(bytes);
                    c.Flush();
                    await c.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (Exception e) when (e is IOException or TimeoutException)
                {
                    // the island may close its end of an oversized message before it has been read to the end
                }
            }

            using var good = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
            good.Connect(2000);
            good.Write(SessionWire.Encode("claude", "Stop", "", "good", 1, "Q:\\Invented\\Alpha", [100]));
            good.Flush();
            await good.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(200);
            Assert.Contains(seen, m => m.SessionId == "good");
            Assert.Single(seen);
        }
        finally
        {
            foreach (var c in silent) c.Dispose();
        }
    }

    [Fact]
    public async Task Defect_Pipe_The_Number_Of_Connections_Held_At_Once_Is_Not_Bounded_As_The_Class_Says()
    {
        // code-1-8 (LOW): AgentPipeServer's own text: "a fixed number of acceptors each take one connection at a time and are free again within the read limit, so later clients wait their turn
        // ... and nothing grows". Accept() now hands every connection to Task.Run(Receive) and makes the next pipe instance at once, so a client that connects and says nothing costs the server
        // a pipe instance, a task and a 4 KB buffer for the first-byte limit (1 s), and there is no limit on how many at a time. Failing input: 400 clients that connect and stay silent: all 400
        // connect at once (Acceptors is 64). Expected, by the class's own text: at most about 64 held; the rest wait their turn or are turned away. Only the same Windows user can open the
        // pipe (a deny for the network and an allow for the user's SID), hence LOW; the repair is a semaphore around Receive (or a count of live Receive tasks).
        var name = NewName();
        using var server = new AgentPipeServer(name);
        Assert.True(server.Start());
        var held = new List<NamedPipeClientStream>();
        var connected = 0;
        var stamps = new List<long>(); // when each client connected: a silent client is let go after the first-byte limit, and later clients then find a free slot, so only a short window counts as "at once"
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var tasks = Enumerable.Range(0, 400).Select(_ => Task.Factory.StartNew(() =>
            {
                var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.None);
                try
                {
                    c.Connect(400);
                    lock (held) held.Add(c);
                    lock (stamps) stamps.Add(clock.ElapsedMilliseconds);
                    Interlocked.Increment(ref connected);
                }
                catch (Exception e) when (e is TimeoutException or IOException)
                {
                    c.Dispose();
                }
            }, TaskCreationOptions.LongRunning)).ToArray();
            await Task.WhenAll(tasks);
            long[] when;
            lock (stamps) when = [.. stamps.Order()];
            var atOnce = when.Select((t, i) => when.Skip(i).TakeWhile(u => u - t < 500).Count()).DefaultIfEmpty(0).Max(); // the most that connected inside any half second
            Assert.True(atOnce <= 80, $"{atOnce} of 400 silent clients were all held at once ({connected} in all; the class says a fixed number, 64)");
        }
        finally
        {
            lock (held) foreach (var c in held) c.Dispose();
        }
    }

    // ---- the notice and the wall clock ------------------------------------------------------------------------------------------------------------------------------

    private static AgentNotice Notice() => new(AgentSignal.Finished, "Alpha", "p100", [100]);

    [Fact]
    public void NoticeQueue_Odd_Times_Never_Throw()
    {
        foreach (var t in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, DateTimeOffset.UnixEpoch, DateTimeOffset.MaxValue.AddTicks(-1) })
        {
            var q = new NoticeQueue();
            Assert.Null(Record.Exception(() => q.Post(Notice(), t)));
            foreach (var u in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, t, t == DateTimeOffset.MaxValue ? t : t.AddTicks(1) })
                Assert.Null(Record.Exception(() => q.Update(u, capsuleOpen: false, pillUp: false, pointerOver: true)));
        }
    }

    [Fact]
    public void NoticeQueue_Leaves_After_Its_Seconds_And_Waits_While_The_Capsule_Is_Open()
    {
        var q = new NoticeQueue(6);
        var t0 = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        q.Post(Notice(), t0);
        Assert.NotNull(q.Update(t0, false, false, false).Showing);
        Assert.NotNull(q.Update(t0.AddSeconds(5.9), false, false, false).Showing);
        Assert.Null(q.Update(t0.AddSeconds(6), false, false, false).Showing);
    }

    [Fact]
    public void Defect_NoticeQueue_A_Wall_Clock_That_Steps_Back_Keeps_The_Notice_On_Screen_For_As_Long_As_It_Stepped()
    {
        // code-1-9 (LOW): AgentNoticeHost times a notice with DateTimeOffset.UtcNow (the wall clock, which a time sync, a manual change or a resumed laptop with a wrong clock can step back; every
        // other timer of the island uses a steady clock). The deadline is a wall-clock moment, so after a step back of one hour the notice that should leave in 6 s is still shown 59 minutes
        // later, over whatever is open, until it is clicked; NoticeFallback says "a clock that went backwards is never stale" and a waiting notice waits for the clock to come back too.
        // Expected: a notice leaves within about its seconds of real time (the queue treats a time earlier than the one it started from as a restart, or the host passes a steady clock).
        var q = new NoticeQueue(6);
        var t0 = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        q.Post(Notice(), t0);
        Assert.NotNull(q.Update(t0, false, false, false).Showing);
        var stepped = t0.AddHours(-1);
        q.Update(stepped, false, false, false); // the clock went back one hour
        var afterStep = q.Update(stepped.AddSeconds(10), false, false, false); // ten real seconds later: longer than its 6 seconds
        Assert.Null(afterStep.Showing);
    }
}
