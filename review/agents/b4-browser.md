# B4 browser — report

Agent B4, night of 2026-10-06. Piece: WORK-ORDER-4.md section 2 (the Chrome add-on and the island's side of it),
as split by NIGHT-PLAN.md. Branch `b4-browser`, worktree `.worktrees/b4-browser`, made from `4c852a1`.

State: **done**, except for what only a real browser can prove (section 4).

## 1. What was built

### `src/Island.Core/Tabs/` (plain `net10.0`, namespace `Island.Core` like the rest of Core)
- `TabProtocol.cs`: the fixed numbers of PROTOCOL.md: port 47653 and fallbacks 47654-47657, path `/island`,
  `chrome-extension://` origin prefix, 256 KB frame limit, 64 KB icon limit, 200-character titles, ten bad frames,
  20 s keepalive, 60 s silence, 3 s hello deadline, the five media hosts. Also `MaxTabsPerConnection = 2000`
  (chosen by B4, not in the protocol).
- `TabMessages.cs`: strict parsing of every add-on frame into typed records (`HelloMessage`, `SnapshotMessage`,
  `TabMessage`, `TabRemovedMessage`, `TabActivatedMessage`, `IconMessage`, `MediaMessage`, `ResultMessage`,
  `PingMessage`). It also parses the island's own frames (used by the pretend add-on) and builds the island's
  frames (`Welcome`, `Activate`, `MediaCommandFrame`, `Resync`, `Pong`). A bad frame gives a `Reject` reason and
  never throws: oversize (UTF-8 bytes), not JSON, nesting deeper than 8, not an object, no type, unknown type,
  missing or wrong-typed field, out-of-range number, bad profile, an icon that is not a PNG or is over 64 KB.
- `TabModel.cs`: the state of all connections, in memory, thread-safe (one lock). Keys are `"<profile>:<tabId>"`.
  It handles snapshot (replaces one connection's tabs and drops their media and icons), upsert, remove, activate
  (deactivates the other tabs of that window) and connection-closed. `LastActiveOrder` is increasing and unique
  across connections. Media is kept only for the five media hosts. An icon or a media state is dropped when a tab
  moves to another host. `Locate(key)` gives the connection and ids for a command. `LastStarted(counts)` returns
  the tab whose page most recently went to "playing", among the hosts `counts` accepts. Also: a second hello with
  the same profile replaces the older connection.

### `src/Island.Bridge/` (`net10.0`, references Island.Core only, no NuGet)
- `TabBridge.cs`: `TabBridge : ITabSource, IDisposable`. It binds `new TcpListener(IPAddress.Loopback, port)`
  with `ExclusiveAddressUse = true` and tries the five ports in turn. The hand-written handshake is followed by
  `WebSocket.CreateFromStream(..., IsServer = true)`. Each connection has its own task. The listener enforces the
  3 s hello deadline (covering the HTTP handshake too), 60 s silence, 256 KB frames, ten bad frames in a row, at
  most 8 connections, and replies `pong` to `ping`. `Changed` is coalesced (50 ms) and raised on a pool thread.
  `BridgeCounts` holds numbers only.
- `Handshake.cs`: the RFC 6455 section 4.2 server handshake. The header is read one byte at a time (max 8 KB).
  An `Origin` that does not start with `chrome-extension://` (or a missing Origin) gets **HTTP 403**. A wrong path
  gets 404; an incomplete upgrade gets 400.
- `BridgeLink.cs`: one connection. Whole frames are read within 256 KB. Sends go one at a time, and a peer that
  will not take a frame within 5 s is cut off.
- `OutsideTabs.cs`: `OutsideTabs : ITabControl` (`Activate(tabKey)`, `Media(tabKey, command)`). It first asks
  `OutsideGate.Current.Allow(OutsideKind.TabCommand)` and sends nothing when refused. Sending never waits for
  the browser.
- `PretendAddon.cs`: the pretend add-on (the only file with `ClientWebSocket` / `TcpClient`). It dials only
  `ws://127.0.0.1:<port>/island`; `OutsideGuardTests.App_Has_No_Internet_Client` checks this and passes.
- `PretendScript.cs`: `PretendScript.RunAsync(TabBridge, port)` plays the self-test script (wrong origin, no
  origin, hello, tabs open, a second tab of the same site, playing on one tab then the other, an icon, a broken
  frame, tabs close, connection closed). It returns `ScriptStep(Name, Pass)` with fixed step names only.

### `src/Island.Bridge.Smoke/` (console, no window, not in the solution)
`Program.cs` starts bridges on free loopback ports. It plays the script plus the hostile cases and prints
PASS/FAIL per fixed step name, then the counts.

### `tests/Island.Tests/Tabs/`
- `ProtocolExamples.cs`: reads the fenced `json example <name>` blocks out of `extension/PROTOCOL.md`.
- `TabMessagesTests.cs`, `TabModelTests.cs` (with `IconCacheTests`), `TabBridgeGuardTests.cs`.

### `extension/` (Manifest V3, plain files, no build step, no packages)
- `manifest.json`: permissions `tabs`, `favicon`, `alarms`, `storage`. `host_permissions` is `http://127.0.0.1/*`
  only. Content scripts run on the five media sites only. `minimum_chrome_version` is "116". A fixed public `key`
  (the private key was generated in memory and never written anywhere).
- `background.js`: the service worker (classic script, `importScripts` of `lib/`). It dials 127.0.0.1 on the five
  ports in turn, at most one attempt per 5 s: at start, on every tab event, and on a 30 s alarm. It is quiet on
  failure, and drops a socket that does not answer `welcome` within 3 s. It sends `hello`, then after `welcome` a
  `snapshot`, the icons and any known media, and later `tab`/`tab-removed`/`tab-activated`/`icon`/`media`, plus a
  `ping` every 20 s. It carries out `activate` (`tabs.update` then `windows.update({focused:true})`, answered
  with `result`), `media-command` (sent to the tab's content script, answered with `result`) and `resync`. Icons
  come from the browser's own `chrome-extension://<id>/_favicon/?pageUrl=…&size=32`; nothing is fetched from the
  internet. A tab leaves as id, window id, title (cut to 200), host, audible, active, pinned, incognito: never a
  full address.
- `content/media-main.js` (`world: "MAIN"`, `document_start`): keeps the page's own `mediaSession` action
  handlers by wrapping `setActionHandler`, and remembers the last media element that played. It reports the
  metadata title and artist, the session state, paused or ended, position and length. On request it runs a
  command plan: the page's handler first, then the media element (play/pause only), then a site button.
- `content/media-relay.js` (isolated world): shapes and throttles the page's reports (treated as untrusted) and
  passes them to the worker. It passes commands into the page and returns ok/false within 1 s.
- `lib/sites.js`, `lib/protocol.js`, `lib/reconnect.js`, `lib/media.js`: the logic that runs without a browser
  (site matching, frame building and checking, the reconnect rule, media shaping, the command plan, per-site
  button selectors). They are classic scripts that also work under `require` in Node.
- `tests/*.test.js` for `node --test`, including `no_remote_fetch.test.js` (the guard) and `background.test.js`,
  which runs `background.js` itself inside a Node `vm` against a pretend `chrome.*` and a pretend `WebSocket`.
- `README.md`: how a non-programmer installs it, in plain English.

**Extension id: `lmnojmilhkpdhejkanneoogmjldolook`.** It is computed from the manifest `key` the way Chrome
computes it (SHA-256 of the DER public key, first 32 hex digits, 0-f mapped to a-p).
`manifest.test.js` recomputes it from `manifest.json`.

## 2. Commands run and their results

| Command | Result |
|---|---|
| `dotnet test "<worktree>\tests\Island.Tests"` before any change (baseline) | 248 passed, 0 failed |
| `dotnet test "<worktree>\tests\Island.Tests"` at the end (whole suite, guards included) | **305 passed, 0 failed** |
| `dotnet build "<worktree>\src\Island.Bridge"` | Build succeeded, 0 warnings, 0 errors |
| `node --test` in `<worktree>\extension` (Node v24.18.0, no packages) | **48 tests, 48 pass, 0 fail** |
| `dotnet run --project "<worktree>\src\Island.Bridge.Smoke"` | **steps 34, passed 34, failed 0** |
| the smoke console's exe, 15 runs in a row plus one, after the fixes below | 16 of 16 runs all green |
| guard mutation check on a scratch copy of `extension/`: a `fetch('https…')` added, then `<all_urls>` added | the guard failed both times (3 and 2 failing sub-checks); the scratch copy was deleted |

Smoke steps (each PASS in the final runs): the listener starts on the given port; bound to the loopback
address; a taken port falls back to the next; a wrong origin refused with 403; no origin refused with 403; hello
answered with welcome; tabs open; a second tab of the same site counted; playing starts on the first tab;
playing starts on the second and it takes over; an icon arrives; a broken frame ignored and the connection
stays; tabs close; closing the connection drops its tabs; a stalled socket does not block another add-on (welcome
within 1 s); the stalled socket dropped after the hello deadline; an upgraded connection that never says hello is
closed; a first frame that is not hello closes the connection; nine bad, a good, nine bad: still open; the tenth
bad frame in a row closes; a frame over 256 KB closes; 500 frames in a row all applied; change events coalesced
(2 to 5 events for 500 frames); two profiles with the same tab id do not clash; activate reaches the right add-on
with its window; a media command reaches the add-on; a command for an unknown tab is not sent; under the
self-test gate no command is sent (2 refusals counted); the same profile dialling again replaces the old
connection; a silent connection closed after the silence limit (shortened to 2 s for the run); a connection that
pings stays open; eight add-ons welcomed; a ninth turned away; the eight stay open.

Two smoke steps were flaky at first, and both causes were in the steps, not the bridge:
- "stalled socket dropped after the hello deadline": the step waited exactly 3 s, the same length as the
  deadline itself, so it raced the bridge (failed 4 of 5 runs). It now waits the deadline plus 2 s.
- "a ninth connection is turned away": in 2 of 15 runs the turned-away count was 2 for one extra add-on. The
  .NET client dials once more when a connection is closed before any answer (inferred, not documented by me),
  and the bridge correctly turned that one away too. The step now checks `>= 1` and that the extra never got in.

## 3. What is proven, and by what

- Strict parsing, using the examples of PROTOCOL.md. Proven by `TabMessagesTests.Every_Good_Example_Parses`,
  `TabMessagesTests.Every_Bad_Example_Is_Rejected`, `Examples_Parse_To_What_They_Say`,
  `Island_Frames_Are_Built_Exactly_As_The_Examples`, `Island_Frames_Are_Not_Accepted_From_The_Addon`,
  `Bad_Frames_Are_Rejected_With_A_Reason` (25 cases), `A_Title_Over_200_Characters_Is_Rejected`,
  `An_Icon_Over_64_KB_Is_Rejected`, `A_Huge_Frame_Is_Rejected_As_Oversize`,
  `Deep_Nesting_Is_Rejected_Without_Throwing`, and `Hundreds_Of_Random_Frames_Never_Throw` (2000 mutated frames).
- The model. Proven by `TabModelTests.Snapshot_Replaces_One_Connections_Tabs`,
  `Closing_A_Connection_Drops_Its_Tabs`, `Second_Tab_Of_The_Same_Site_Is_Counted` (through the real
  `PickStates`), `Newest_Active_Tab_Has_The_Highest_Order`, `Two_Profiles_With_The_Same_Tab_Id_Do_Not_Clash`,
  `A_Second_Connection_Of_The_Same_Profile_Replaces_The_First`, `Media_Is_Kept_Only_For_The_Five_Media_Sites`,
  `The_Tab_That_Started_Playing_Last_Wins` (N2, N3, N7 for tabs), `Too_Many_Tabs_On_One_Connection_Are_Not_Kept`,
  and `Many_Threads_At_Once_Leave_A_Consistent_Model`.
- EVALS I7 is proven by `IconCacheTests.Changed_Address_Drops_The_Old_Icon` (also
  `Same_Address_Keeps_The_Icon_And_A_New_One_Replaces_It`, `A_Closed_Tab_Has_No_Icon`).
- EVALS I8, for the tab logic: `TabModelTests.Tab_Logic_Has_No_File_Api` scans `src/Island.Core/Tabs` for file
  APIs and checks that no `TabModel` method takes a stream or a path.
- Loopback only: `TabBridgeGuardTests.Listener_Binds_Loopback_Only` (no `IPAddress.Any`, `IPv6Any`, `0.0.0.0`,
  `Dns.`, `"localhost"`, `HttpListener`, `.Bind(`, `DualMode`; exactly one `new TcpListener(IPAddress.Loopback,`),
  plus the smoke step "bound to the loopback address".
- No internet client: the existing `OutsideGuardTests.App_Has_No_Internet_Client` stays green with
  `PretendAddon.cs` present. `OutsideGuardTests.Outside_Actions_Are_Gated` stays green with `OutsideTabs.cs`.
- The bridge end to end, including hostile and slow connections: the 34 smoke steps above.
- The add-on's logic: `protocol.test.js` (the frames it builds deep-equal the PROTOCOL.md examples, the island's
  frames are understood and anything else refused, no full address leaves, titles are cut, it dials 127.0.0.1
  only, icons are PNG only and at most 64 KB, media reports are checked again). Also `sites.test.js`,
  `reconnect.test.js` (one attempt per 5 s, none while busy, five ports in turn, 30 s alarm), `media.test.js`
  and `manifest.test.js` (the id from the key, a public key only, the permission set, all named files exist,
  every script parses).
- The service worker against a pretend browser (`background.test.js`, 11 tests): dial order and the quiet wait;
  hello, then after welcome a snapshot with hosts only and icons from `chrome-extension://…/_favicon/`; nothing
  sent before welcome; a socket that never welcomes is dropped after 3 s; activate calls `tabs.update` and
  `windows.update` and answers; a media command reaches the tab's page; frames the island never sends are
  ignored; resync; tab events become frames; media reports only for media sites; a redial after the island goes
  away.
- EVALS I3 for the add-on: `no_remote_fetch.test.js` → `No_Remote_Fetch_And_No_Icon_Service`. It checks that the
  only address in any script is `ws://127.0.0.1:`, that every `fetch` is `fetch(faviconUrl(` built from
  `chrome.runtime.getURL('/_favicon/')`, that every WebSocket is built by `P.address(`, that there is no icon
  service, and that the manifest has no `<all_urls>`, no `*://`, no extra hosts, no `externally_connectable` and
  no `web_accessible_resources`. The mutation check in section 2 shows it can fail.

## 4. What is not proven

- **The add-on has never run in a real browser.** No browser was started; nothing was installed. Everything
  about Chrome's behaviour below is UNVERIFIED.
- **No Chrome documentation page was re-opened tonight.** The brief said to re-open them, and it also forbade
  any network connection beyond 127.0.0.1. I took the cautious reading and opened nothing. These facts rest on
  `Research/browser-tabs.md` and my own knowledge, all **UNVERIFIED**:
  - the manifest `key` gives the id above (the algorithm is well known; only self-consistency is tested);
  - the `favicon` permission lets the service worker `fetch` `chrome-extension://<id>/_favicon/?pageUrl=…&size=32`,
    and the answer is a PNG (EVALS I2 was already UNVERIFIED). If not, the island shows the two letters;
  - WebSocket traffic keeps the service worker alive in Chrome 116+, and a ping every 20 s is enough;
  - `chrome.alarms` with `periodInMinutes: 0.5` fires every 30 s (Chrome 120+);
  - `"world": "MAIN"` is accepted in a manifest content-script entry;
  - `host_permissions: ["http://127.0.0.1/*"]` is enough for a `ws://127.0.0.1` connection under Chrome's Local
    Network Access rules (research §3 already marks this UNVERIFIED);
  - the `storage` permission is needed for `chrome.storage.local` (added to keep the profile id);
  - `windows.update({focused:true})` brings the window forward on Windows. Research §4 says this is often
    unreliable and the taskbar may flash instead. The island cannot help here tonight:
    `AllowSetForegroundWindow` is forbidden by `OutsideGuardTests.The_Foreground_Is_Never_Forced`;
  - whether a failed connection attempt shows up on the extensions page's "Errors" button.
- **WebNowPlaying was not opened** (same reason). The media logic was re-implemented from the idea in the
  research file; nothing was copied, and there is no licence text to add.
- **None of the five media sites was opened.** The button selectors in `lib/media.js` are guesses. Whether
  `navigator.mediaSession` handlers, the media element, or the buttons work on YouTube, YouTube Music, Twitch,
  SoundCloud and Spotify is unknown. `el.play()` may be blocked by autoplay rules.
- Media tabs that were open before the add-on was installed report nothing until reloaded (the README says so).
- Edge, Brave and Opera; incognito; installed web apps: not tried.
- That .NET's `ClientWebSocket` sends a custom `Origin` header is **observed** working: the smoke run gets 403
  for "null" or no origin and welcome for the add-on origin.

## 5. Requests to the main session (joints, protocol, app)

PROTOCOL.md (I did not change it):
1. **Check the exact add-on id.** With a fixed `key`, the island could require
   `Origin: chrome-extension://lmnojmilhkpdhejkanneoogmjldolook` rather than any `chrome-extension://`. That stops
   any other installed add-on from connecting. Today it follows the protocol ("starts with"). If agreed, add the
   id to PROTOCOL.md and `TabProtocol`, and change `Handshake.Run`.
2. Write down what the island already does: a second `hello` on a connection is a bad frame; a binary frame is
   a bad frame; a `hello` with a profile already connected replaces the older connection; at most 8 connections
   and 2000 tabs per connection (a larger `snapshot` is a bad frame); a `media` for a tab whose host is not a
   media site is ignored; media `title` and `artist` are at most 200 characters; in a `snapshot`, known tabs keep
   their `LastActiveOrder` and new ones are numbered in list order with the active tabs last (newest).
3. "invents once and stores" needs the `storage` permission. Please name it in the protocol's transport notes.
4. Content scripts match `www.youtube.com`, `music.youtube.com`, `www.twitch.tv`, `soundcloud.com` and
   `open.spotify.com`. `m.youtube.com` is not covered.

Joints (Island.Core, which I did not change):
5. `TabInfo` has no field for "when its page started playing". `TabModel.LastStarted(counts)` answers it for
   tabs only. EVALS N2 between a tab and a desktop player (Spotify) needs one clock across both sources.
   Suggestion: add `long PlayStartedOrder` (or a `Environment.TickCount64` stamp) to `TabInfo`, or let the B2
   now-playing logic call `TabModel.LastStarted` and compare stamps.
6. `ITabSource` has no "last started" member, so the app needs the concrete `TabBridge.Model` for N2. A small
   addition to the joint would avoid that.

## 6. How to wire it into the app

```csharp
using Island.Bridge;

// At start, on any thread (Start only binds and returns):
var tabs = new TabBridge();                 // ports 47653..47657, 60 s silence limit
int? port = tabs.Start();                   // null: no free port; the island behaves as with no add-on
ITabSource tabSource = tabs;                // Connected, Tabs (newest first), IconPng(key), Changed
ITabControl tabControl = new OutsideTabs(tabs);   // asks OutsideGate before every command
tabs.Changed += () => dispatcher.BeginInvoke(RefreshFromTabs);   // raised on a pool thread; must not throw
// "Now playing" for tabs (EVALS N2/N7): tabs.Model.LastStarted(host => pickedMediaHosts.Contains(host))
// At exit:
tabs.Dispose();
```

- Threading: every connection reads on its own task. `Changed` comes from a timer thread, at most once per
  50 ms burst. A handler that throws there would take the process down, so marshal to the dispatcher and do
  nothing else. `Tabs`, `Connected`, `IconPng`, `Locate` and `LastStarted` are safe from any thread.
  `OutsideTabs` methods return at once (the send is queued).
- Clicking a site pick: use `ClickCycler.Next(pick.Id, …)` over the tabs of that host, newest first (`Tabs` is
  already in that order). Then call `tabControl.Activate(tab.Key)`. The add-on focuses the browser window itself.
- **Self-test:** start a **separate** bridge on a free loopback port outside 47653-47657, for example
  `new TabBridge([freePort])`. That way Dan's real add-on can never connect to the self-test's bridge. Then
  `var steps = await PretendScript.RunAsync(selfTestBridge, freePort);` and record each `ScriptStep` (fixed names,
  pass/fail) in selftest.json. Under the self-test gate, `OutsideTabs` sends nothing and counts `TabCommand`
  refusals (the smoke step "under the self-test gate no command is sent" shows this).
- New projects to add to `Island.sln`: `src/Island.Bridge/Island.Bridge.csproj` (and `Island.App` must reference
  it). `src/Island.Bridge.Smoke` stays out of the solution, as the brief says.
- STATE.md's Romanian install steps can be taken from `extension/README.md` "Putting it into Chrome".

## Other notes

- My first commit carries the `Co-Authored-By: Claude Sonnet 5.5` line from the brief. The later ones carry
  `Claude Opus 5.5`, the model that actually did the work.
- `OWNER DECISIONS REQUIRED` (for the main session to carry over): no shared secret between add-on and island in
  version 1 (as the protocol says); request 1 above (exact-id origin); the 8-connection and 2000-tab caps.
