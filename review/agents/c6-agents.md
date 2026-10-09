# C6 agents — report (WORK-ORDER-7 section 4, pieces that are logic and reading)

Branch `c6-agents`, from `6033be4`. Written 2026-10-06. No window was shown, `Island.App` was never built or run, no
real settings file or folder of Claude Code was read, written or looked at, no real pipe name was used by any test.

## 1. What I built

**`src/Island.Core/Agents/`** (pure, `namespace Island.Core`)
- `AgentPipe.cs` — the shared numbers (64 KB stdin limit, 300 ms connect, 4096-byte message, 1500 ms read limit, 16 ids of
  chain, 260 characters of folder) and `DefaultName`, the fixed default pipe name. It is the only place its text exists.
  `IsValidName` (plain letters, digits, `.-_`, at most 128) is what Notify checks an argument against.
- `HookInput.cs` — `HookInputReader.Read(string|bytes)` gives `HookInput(Event, NotificationType, Folder)` or null. Null for
  empty, over 64 KB, broken, deeply nested, not an object, wrong type of a present field, missing `hook_event_name`. Tolerates a
  byte-order mark and an escaped lone surrogate in the folder (the escape is dropped, the notice is kept). Never throws.
- `AgentSignals.cs` — `Classify(event, kind)`: `Stop` -> `Finished`; `Notification` + `permission_prompt` ->
  `NeedsYourAnswer`; everything else null (idle_prompt included). Also the two display lines.
- `AgentWire.cs` — the message Notify sends: one compact JSON object `{"v":1,"e":..,"k":..,"f":..,"c":[..]}` + newline, at
  most 4096 bytes, built from the cleaned, bounded four things. `TryParse` accepts exactly that shape (no other or repeated
  property, right types, bounded lengths, positive ids, at most 16) and never throws.
- `ProjectName.cs`, `AgentText.cs` — last part of the folder (both separators, trailing separators, drive roots, empty), cleaned
  of control characters, bidi controls (U+061C, U+200E/F, U+202A-E, U+2066-9), line/paragraph separators and lone surrogates,
  cut to 40 characters without splitting a surrogate pair. `ProjectName.Unknown` ("Agent") is used when nothing is left.
- `AgentNotice.cs` — `AgentNotice.From(AgentMessage)`: signal, project name, session key, chain; null for ignored events.
- `ProcessChain.cs` — parents-map to nearest-first chain; stops at a loop, a gap, the top, 16 ids.
- `NoticeQueue.cs` — the notice rules as a state model (details in 6). `NoticeState.TakesKeyboard` is always false.
- `TerminalChoice.cs` — `Choose(chain, windows, projectName)` over `WindowFact(Handle, OwnerProcessId, Title, RecencyRank)`.
- `HookInstaller.cs` — `Connect(text, notifyPath)`, `Disconnect(text)`, `IsConnected(text)`, `LinesToAdd(notifyPath)`, all text
  in / text out, result `HookEdit(Text, Changed, Reason)`; reasons `HOOKS_FILE_UNREADABLE`, `NOTIFY_PATH_INVALID`.

**`src/Island.Notify/`** (console exe, net10.0, no packages, references Island.Core)
- `Program.cs` — one `try` in `Main`, `return 0` on every path including the `catch`; no other exit call. Reads stdin on a task
  with a 2 s cap (an input that never ends cannot hang it), at most 64 KB; parses; if the event is not one of the two, it does not
  even connect; builds the chain; one connect with a 300 ms limit; writes; then waits (bounded 300 ms) for the island to close its
  end, so that the message cannot be lost when the island is busy (see 4); exits 0. Prints nothing. A first argument names the
  pipe; one that is given but not plain ends the program, it is never replaced by the real name.
- `ProcessSnapshot.cs` — read-only Toolhelp snapshot (`CreateToolhelp32Snapshot` with `TH32CS_SNAPPROCESS`, `Process32FirstW`,
  `Process32NextW`, `CloseHandle`) into a pid-to-parent map. Starts nothing, opens no process.

**`src/Island.Agents/`** (library, `net10.0-windows`, references Island.Core)
- `AgentPipeSecurity.ForCurrentUser()` — `PipeSecurity` with Allow FullControl for the current user's SID and a Deny for the
  network SID (a remote logon of the same user must not reach it).
- `AgentPipeServer(pipeName)` — `Start()` (false if the name is taken or refused, never throws), `NoticeReceived` event raised on
  a thread-pool thread, `IsListening`, `Dispose()`. 16 acceptors, each takes one connection at a time, reads one message (to the
  first newline or the client's close, 4096 bytes, 1500 ms), ignores anything not exactly the shape, closes its end. Created with
  `NamedPipeServerStreamAcl.Create`, first instance with `FirstPipeInstance`.

**Tests**
- `tests/Island.Tests/Agents/` — `HookInstallTests`, `AgentEventTests`, `NoticeTests`, `TerminalChoiceTests` (all the named ones,
  plus extra cases).
- `tests/Island.Agents.Tests/` (NEW test project, not in the solution) — `PipeServerTests` (in-process clients) and `NotifyTests`
  (runs the real `Island.Notify.exe` as a hidden child process with the hooks page's Stop shape on stdin). Test parallelism is off
  there (`AssemblyInfo.cs`) because the tests time real pipes. References Island.Core, Island.Agents, Island.Notify; the normal
  project reference puts `Island.Notify.exe` and its runtimeconfig in the test output.

## 2. Commands run and results

(`"C:\Program Files\dotnet\dotnet.exe"`, from the worktree)
- `dotnet build src/Island.Core`, `src/Island.Agents`, `src/Island.Notify`: 0 warnings, 0 errors (Agents is `net10.0-windows`
  because the pipe-security types are Windows-only; as `net10.0` they gave CA1416 warnings).
- `dotnet test tests/Island.Tests`: **Passed 623, Failed 0** (existing tests plus mine, guards included).
- `dotnet test tests/Island.Agents.Tests`: **Passed 46, Failed 0**.
- Measured by those tests (this laptop, Debug build, no real island): Notify with a listener, whole child process 189 ms;
  Notify with an invented pipe nobody listens on: exit 0 after **552 ms** (about 250 ms is the runtime starting, 300 ms the
  connect limit; below the 1 s mark of the self-test); Notify with an input that never ends: exit 0 after 2.1 s; 100 clients at
  once on dedicated threads: 100 heard, 0 errors, 41 ms; Dispose with a client waiting: 0 ms.
- Guard-readiness greps over my files: the text of the user folder name of Claude Code (a dot, then the word) appears 0 times;
  the settings file name 0 times; the default pipe name only in `AgentPipe.cs`, never in a test; `Island.Notify` has exactly one
  `try`, no exit call (no `Environment.Exit`, no exit-code property), no `throw`; no `http`, `Process.Start`, `CreateProcess`.

## 3. What is proven, and by what

- Existing hooks kept, ours added beside them, order and unicode and number text kept: `HookInstallTests.Existing_Hooks_Are_Kept`.
- Nothing added twice, byte-identical text: `Installing_Twice_Adds_Nothing`.
- Disconnect removes exactly programs named `Island.Notify` (exec form and shell form, any case, with or without `.exe`), keeps a
  lookalike (`Island.Notify.Smoke.exe`) and other tools' handlers in the same group; connect then disconnect gives back the
  original meaning: `Disconnect_Removes_Only_Ours`, `Disconnect_After_Connect_Gives_Back_The_Original_Meaning`.
- Unreadable text (broken, array, `null`, hooks not an object, repeated key, comments, trailing text) returned unchanged with
  `HOOKS_FILE_UNREADABLE`; an event that is not a list refuses without half a change: `Unreadable_Text_Is_Returned_Unchanged_With_A_Reason`,
  `An_Event_That_Is_Not_A_List_Makes_Connecting_Refuse_...`.
- Empty or blank text becomes only our hooks: `Empty_Text_Becomes_Only_Our_Hooks`. CRLF kept, BOM tolerated, other path refused.
- Event meanings, ignored kinds and events, project-name rules (separators, trailing, root, empty, 40 cut, surrogate pair not
  split, lone surrogate, bidi), broken/oversized/binary/deep input, exact-shape wire parsing (19 wrong shapes): `AgentEventTests.*`.
- Notice rules (replace, same-session Stop changes nothing, waits while the capsule is open, never replaces the capsule, pill
  gives way and returns, 3-30 s clamp, hover keeps it longer but capped at 60 s, a waiting notice goes stale after 5 minutes,
  `TakesKeyboard` false in every state): `NoticeTests.*`.
- Terminal choice (nearest program with a window, one title match, two matches, empty name, nothing found, a nearer program
  wins over a farther title match): `TerminalChoiceTests.*`.
- Real pipe and real Notify: Stop shape on stdin with a folder ending in `island` arrives reading "island" and "Agent finished -
  waiting for you", chain starts with the test process, nothing printed (`NotifyTests.Stop_Reaches_...`); permission_prompt;
  idle_prompt/auth_success/elicitation send nothing; no listener -> exit 0 quickly; 9 bad inputs (empty, whitespace, garbage,
  truncated, binary, array, wrong types, 200 KB, deep nesting) -> exit 0, nothing printed, nothing sent; never-ending stdin ->
  exit 0; non-plain pipe names -> exit 0; lone surrogate and 6 KB path with trailing slash still give "island".
- Server: garbage of 9 kinds ignored and the server goes on; an idle client does not block others; 20 idle clients past the
  limit do not starve it; a slow trickle is ignored after the limit; 100 clients all heard; 50 rude clients (write and close at
  once) cannot hurt it; a throwing handler does not stop it; Dispose is clean, repeatable, nothing arrives after it; a taken name
  gives `Start() == false`; the pipe's DACL (the code's, and the running pipe's, read back from a client handle) is the current
  user allowed plus network denied, nobody else.

## 4. What is not proven, and caveats found on the way

- **Nothing was tried with a real Claude Code.** Not proven: that Claude Code starts the exec form on Windows as the page says,
  that the parent of `Island.Notify` is the process that runs the agent (the chain and the session key assume it; UNVERIFIED), that
  `async` behaves as the page says, that the desktop app and editor extension read the same file (UNVERIFIED, the page does not say).
- The window choice was tested on plain facts only. Not proven with a real terminal. A classic console window is owned by the
  console host, which is not in the chain; Windows Terminal's window is owned by its own process, which should be. UNVERIFIED.
- Hooks page differences from the research file (page read 6 Oct 2026): the Stop example no longer shows `stop_hook_active`
  (it lists `session_id`, `prompt_id`, `transcript_path`, `cwd`, `permission_mode`, `hook_event_name`, `last_assistant_message`, ...);
  Notification examples carry `notification_type` and `message`; the page lists more notification types than the research file
  (`elicitation_url_dialog`, `agent_needs_input`, `agent_completed`, `quota_auto_resume_*`), none used. Confirmed on the page: events
  `Stop` (no matcher support) and `Notification` (matcher filters the notification type; `permission_prompt` is a plain exact
  value); command hooks get the JSON on stdin; exec form (`command` + `args`, "no shell", on Windows needs a real executable,
  an absolute path with spaces is fine); `async: true`; exit 0 is success.
- Learn confirmed: `NamedPipeServerStreamAcl.Create` (System.IO.Pipes.AccessControl, .NET 10; takes `PipeSecurity`; with
  `CurrentUserOnly` it ignores the security argument, so I did NOT use that flag and the explicit SID rules apply),
  `PipeOptions` (`FirstPipeInstance`, `Asynchronous`), `CreateToolhelp32Snapshot` (`TH32CS_SNAPPROCESS` = 2, failure =
  `INVALID_HANDLE_VALUE`, `CloseHandle` to free), `PROCESSENTRY32W` fields, `Process32FirstW`. `Process32NextW` was not opened on its
  own page; it is named on the First page as the call that continues the list (confirmed by name only).
- **Found and fixed: lost messages under a burst.** With a one-way pipe and a client that closes right after writing, about half
  of 100 simultaneous messages were lost: .NET's connect call fails with an I/O error when the client has already come and gone
  (50 of 100 in my first run), and nothing can be read from that instance. Fix: the pipe is two-way for one reason only, the island
  closes its end after reading, and Island.Notify waits (bounded 300 ms) for that close before it ends. With that, 100 of 100 are
  heard. A client that does not wait (the rude clients test) can still lose a message in a burst; that is allowed and tested not
  to hurt. Cost: after the island has the message, Notify ends within a few ms; if the island is stuck it ends 300 ms later.
- A hook payload over 64 KB is ignored (the order says so); a Stop whose `last_assistant_message` is that long would give no notice.
- Session key: the order names four things, none a session id, so "the same session" is the first program of the chain (the
  agent's own process), or the folder when there is no chain. If Claude Code starts the hook through a short-lived wrapper this key
  changes every time and a repeated Stop would show again instead of being dropped. A fifth field `session_id` would fix it
  (it is on the hooks page); I did not add it because the order lists four things.
- `Connect` does not repair a stale entry: if an `Island.Notify` entry already exists for an event, even with another path, nothing
  is added (so a moved copy is not fixed by pressing Connect again). The copy lives in a fixed folder, so this should not occur.
- Rewriting reformats the file with two-space indentation and writes non-ASCII as itself; a comment in the file makes it
  "unreadable" on purpose (it would be lost on rewrite). The backup the order asks for is the main session's job.
- The pipe is secured by the DACL of the user's SID. A different Windows user who creates the same pipe name first would make
  `Start()` return false for the island, or (if the island is first) cannot take it. On a machine with two users running the island
  at once, the second one's `Start()` is false (fixed default name). Single-user laptop: not a problem; noted.
- Notify is a console-subsystem exe (as the order says). If Claude Code does not hide a console window for hooks, one may
  flash on every Stop; switching `OutputType` to `WinExe` removes that and stdin/stdout redirection still works. Not tried; I did
  not want to depart from the order. Decide after Dan's first real run.
- Notify is framework-dependent: it needs the .NET 10 runtime that the app needs, and its `Island.Core.dll`, `.deps.json` and
  `.runtimeconfig.json` beside it. The copy into the notify folder must take the whole output folder, not the exe alone.
- No sound, no screen, no registry, no network, no settings file was touched by anything here.

## 5. Requests to the main session

1. Add `src/Island.Notify`, `src/Island.Agents` and `tests/Island.Agents.Tests` to `Island.sln` (a test project that runs the
   exe: build Notify first; the project reference does that).
2. `Refusals.cs` (existing file): an entry for `HOOKS_FILE_UNREADABLE` with the three parts (what happened, why refusing is right,
   what to do), and one for `NOTIFY_PATH_INVALID` if the screen should show it. The installer only returns the code.
3. `OutsideGate` kinds already exist (agent settings file, sound, foreground); the one `Outside` file that knows Claude Code's real
   settings location and the one that brings the window forward are yours; I wrote neither.
4. `GuardTests` to add: `Claude_Settings_Location_Is_In_One_Outside_File`, `Real_Pipe_Name_Is_Not_In_Tests` (look for the text of
   `AgentPipe.DefaultName`, built from two halves), `Notify_Always_Exits_Zero` (one `try` in `src/Island.Notify/Program.cs`, `return 0`
   everywhere). My files satisfy all three as written today; `tests/Island.Agents.Tests` also never contains the name.
5. `Island.Tests` already picks up `tests/Island.Tests/Agents/*` with no project change.

## 6. How to wire it in

- **Reference**: `Island.App` references `Island.Agents` (it is `net10.0-windows`, compatible with the app's target) and
  `Island.Notify` (a normal project reference puts `Island.Notify.exe`, `.dll`, `.runtimeconfig.json`, `.deps.json` and
  `Island.Core.dll` in the app's output, as it did in the test project's output).
- **Listening** (only when the app is started for real, never under `--selftest`):
  `var pipe = new AgentPipeServer(AgentPipe.DefaultName); pipe.NoticeReceived += n => dispatcher.BeginInvoke(() => queue.Post(n, DateTimeOffset.UtcNow)); pipe.Start();`
  The event comes on a pool thread; do nothing there but marshal. `Start()` false means notices are off for this run. `Dispose()`
  at shutdown. The self-test builds its own `AgentPipeServer(invented name)` and runs `Island.Notify.exe <invented name>` with the
  hooks page's Stop shape on stdin (the helper in `tests/Island.Agents.Tests/PipeTestSupport.cs` shows how, with a hidden window
  and redirected streams).
- **Drawing**: one `NoticeQueue` (`new NoticeQueue(settingsSeconds)`, or set `Seconds`, clamped 3-30). On every tick of the app call
  `var s = queue.Update(DateTimeOffset.UtcNow, capsuleOpen, smallPillUp, pointerOverNotice);` Draw `s.Showing` (`ProjectName` and
  `Line`) when not null; hide the small pill while `s.HidesPill`; leave the notice out while `s.Waiting`. Never activate the notice's
  window (`s.TakesKeyboard` is the flag, always false). A click: choose the window, ask the foreground door, then `queue.Dismiss()`.
- **Click**: `TerminalChoice.Choose(notice.Chain, facts, notice.ProjectName)` where `facts` are the visible top-level windows as
  `WindowFact(handle, ownerPid, title, rank)`, rank 0 for the top of the z-order (the order `EnumWindows` gives is most recent first).
  Titles stay in memory. Null means do nothing.
- **Connect button**: `HookInstaller.Connect(textOfSettingsFile, pathOfCopiedNotifyExe)`; show `HookInstaller.LinesToAdd(path)` before
  anything is written; write `edit.Text` only when `edit.Changed`; show the refusal for `edit.Reason` otherwise. Disconnect:
  `HookInstaller.Disconnect(text)`. Row state: `HookInstaller.IsConnected(text)`. The function that reads the real file, makes the
  backup and writes is the one `Outside` file and takes no path.
