# Attack on WORK-ORDER-7 section 4, the outside edges (ATTACK7B, 7 Oct 2026)

Adversarial pass over what WO7 section 4 added at the edges of the app: the hook program `Island.Notify` (run as a real child
process), the pipe server `AgentPipeServer` (real pipes, invented names), the wire format (`AgentWire`, `AgentNotice`,
`ProjectName`, `AgentText` through them) and the text logic of `HookInstaller` (`Connect`, `Disconnect`, `IsConnected`,
`LinesToAdd`).

Branch `attack7b`, made from main `8cc1ce8`, in the worktree `.worktrees/attack7b`. Nothing under `src/` and no existing file was
changed. The tests live in `tests/Island.Attack7B.Tests/` (xunit, `net10.0-windows`, references `Island.Core`, `Island.Agents` and
`Island.Notify` (only so that `Island.Notify.exe` is built into the test output, as in `Island.Agents.Tests`), not in `Island.sln`).

Obeyed: no Claude Code settings read, created or changed, no `.claude` folder touched, no file named; `HookInstaller` only ever saw
strings invented in the tests; `OutsideAgentConnector` never used or referenced; "Connect" never pressed. Every pipe name is
`island.attack7b.<guid>` (listener and `Island.Notify` argument), never `AgentPipe.DefaultName`; `Island.Notify` is never started
without a pipe-name argument (the helper throws if it is asked to). `Island.App.exe` (pid 29108, the owner's, running before I
started and still running at the end) was never started, stopped or touched. Nothing was written to any file by the tests (no
temporary folder was needed); nothing made a sound or opened a window. Every child process runs hidden, with a hard timeout and
`Kill(entireProcessTree: true)`; after the full run `tasklist` shows no `Island.Notify.exe` (checked three times, and the 100-at-once
tests compare the count of live `Island.Notify` processes before and after).

Command and result:
- `dotnet test tests/Island.Attack7B.Tests`: **361 tests, 41 failed, 320 passed** (about 1 min 40 s). Every failing test is named
  `Defect_...` and fails because of the defect it names (14 `Defect_` methods; several are theories, hence 41 cases). Every other test
  is named `Holds_...` (89 methods, 318 cases) and passes. In that run no `Holds_` test failed and two `Defect_` cases passed:
  the burst test (it is probabilistic, see defect 5) and one input of the surrogate theory (the property name `{"\ud800":1}`, which
  is refused with the reason instead of throwing; the other three inputs throw).
- No HIGH defect found. No MEDIUM. Eight findings, LOW or LATENT.

## 1. What I added

| File | Content |
|---|---|
| `Island.Attack7B.Tests.csproj`, `AssemblyInfo.cs` | project; test parallelism off so that timings are honest |
| `Support.cs` | invented names, a collector for a server, `RunNotify` (hard timeout, tree kill, refuses an empty argument list), streaming writer for 100 MB |
| `NotifyAttackTests.cs` | `Island.Notify` as a child process: nothing on stdin closed at once; stdin that never closes (with and without bytes); 64 KB + 1, 1 MB padding, 100 MB of blanks; a 70 KB message after the three fields that matter; 21 broken or mistyped shapes (broken JSON, array/string/null root, event as number/array/object/null/empty/lowercase/with trailing blank, cwd as number/object, kind as number, Notification without kind, kind in other case, depth 40, UTF-16 text, zero bytes, blanks); invalid UTF-8 in the folder (4 byte pairs); BOM; lone surrogate escape in the folder; escaped field name; 14 hostile pipe-name arguments (`\\.\pipe\<the real invented name>`, `\\.\pipe\x`, `..\name`, `pipe\name`, name with `\..\`, empty string, leading/trailing blank, trailing newline, 129 and 10 000 characters, look-alike dot U+2024, dotted capital I) against a pipe that exists; 10 odd but plain names (`..`, `.`, `...`, `-`, `_`, `CON`, `NUL`, `anonymous`, `a..b`, 128 characters); extra arguments; a pipe canary named in `cwd`, `transcript_path` and another field (an open would connect); 100 started at once with and without a listener (exit code, silence, time, nothing left running); a burst of 100 against a listener, three rounds; 16 idle connections plus a real hook |
| `PipeAttackTests.cs` | 34 garbage payloads (2 KB and 100 KB of seeded random bytes, 5000 bytes with no newline, 4096 without, a valid message padded past 4096 before its newline, a million newlines, newline then valid, binary zero, zero then valid, valid then zero, valid plus junk, BOM, about 22 well-formed JSON objects of the wrong shape: array, null, string, `{}`, version 2/"1", extra or repeated property, chain of 17 / 0 / negative / past int / float / string / nested, folder 261, event 33, lone surrogate escape, event `Foo`, `idle_prompt`, event in other case) each followed by a valid message that must still arrive and nothing before it; half a message then silence; a valid message in single-byte writes; one trickled too slowly; two messages in one connection; CRLF ending; the 4096/4097 boundary; 200 then 64 idle clients while a valid message waits (threads, memory, time); a 300-client storm of rude closers (connect-close, half-close, whole-but-newline, random, zero byte, bare newline) with a count of notices that must all be whole; 2000 garbage connections (threads and memory); Dispose during traffic from 8 senders (time, nothing after, name freed); Dispose twice, Start after Dispose; Start from 16 threads; a second server on the same name (refused, harmless, stays refused); names of 316 and 1016 characters and with `\ * ? " ` and a blank; a handler that disposes the server; a slow handler (40 clients that wait their turn); a throwing handler 40 times; the ACL (rules, SDDL, the running pipe's own ACL); a loopback connection through the machine name and `127.0.0.1` with the server asking for the client's token; a same-user process joining the name as a second server |
| `WireAttackTests.cs` | 2000 random messages (lone surrogates, control and bidi characters, NUL, chain of 0-40 ids with zero, negative and huge values) encoded and decoded (length at most 4096, newline only at the end, fields bounded, clean, no lone surrogate, chain equals the first 16 positive ids); the worst case of escaping (400 times `"`, `<`, an emoji, a Chinese character, `\` in every field); 46 hostile decodes (missing, too many, repeated, wrong types, wrong separators, trailing comma, comment, single quotes, depth, huge, negative, float and duplicate ids, 16+ ids, version as float/bool/string, lone surrogate, BOM, trailing junk, 261/65/33 characters) all returning null; bytes that are not UTF-8; `AgentNotice.From` for 11 event/kind pairs; 25 folders and 1 MB / 500 000-part / 1 000 000-separator folders for `ProjectName`; the cut at 40 around a surrogate pair; lone surrogates; ten invisible characters as the whole name |
| `HookInstallerAttackTests.cs` | 41 unreadable texts (arrays, scalars, truncation, trailing comma, comments of both kinds, YAML, two values, trailing junk, NUL, single quotes, bare keys, bad numbers, bad escapes, `hooks` as array/string/number/null/bool, BOM variants, depth 10 000 and 65) for Connect, Disconnect and IsConnected; 7 `Stop`/`Notification` values that are not lists; depth 55 to 65; empty/blank/null/BOM text; a corpus of 10 originals (user hooks, several events, our look-alikes, comments in names, huge and tiny numbers, escapes, emoji, line separators, 4-space indent, odd members) through Connect then Disconnect, Connect twice, Disconnect twice; line endings; key and order preservation; look-alikes (`node` with `Island.Notify.exe` in `args`, seven shell lines that only mention the name, four shell-form entries that are ours, another `Island.Notify.exe` in another folder, a stale path, ours twice, one of the two missing, ours under another matcher or another event); 12 paths of other programs; awkward real paths (spaces, quote, unicode, forward slashes, no extension, 400+ characters) written exactly and found again; relative, UNC, `//`, `\\?\UNC`, `http://` paths; blanks around the path; `LinesToAdd` against `Connect`; repeated keys; case of keys; lone surrogates; a 10 MB file; 2 x 50 000 user entries; 50 000 groups of ours; 4000 random mutations of a settings text (never throws, never writes into text `JsonDocument` cannot parse, never loses a root key) |

## 2. What I broke

Severity is my judgement: HIGH = normal use shows it; MEDIUM = plausible in normal use, or acts on something not meant; LOW = needs an
odd input or only bends a stated rule; LATENT = nothing in the app produces the input today. Fixes named are suggestions; nothing was changed.

### 1. A settings file with a lone surrogate escape makes Connect and Disconnect throw instead of coming back unchanged (LOW)
- **Input:** `HookInstaller.Connect(@"{""a"":""\ud800""}", path)` (also `"x\udc00y"` and `["\ud83d"]` as a value; the same text goes into `Disconnect` once a hook has been added elsewhere).
- **What happens:** `InvalidOperationException` ("Cannot read incomplete UTF-16 JSON text as string with missing low surrogate", or "invalid surrogate value 0xDC00") escapes from `Connect`. `TryOpen` (which catches it) parses fine, because the parser does not look inside strings; the exception comes later from `Render` (`ToJsonString`), which is outside the try block. A property name with the same escape is refused properly with `HOOKS_FILE_UNREADABLE`.
- **Should:** such a file is legal JSON (Claude Code reads it), and Connect must either keep the value (write the escape back) or return the text unchanged with `HOOKS_FILE_UNREADABLE`; it must never throw. How the caller handles an exception I could not see (the caller is in `Island.App`, which I did not reference); if it does not catch, pressing Connect would take the app down with nothing written.
- **Fix idea:** catch the exceptions of `Render` as `TryOpen` does, and return `FileUnreadable`.
- **Test:** `HookInstallerAttackTests.Defect_A_Lone_Surrogate_Escape_Anywhere_In_The_File_Makes_Connect_And_Disconnect_Throw` (3 of 4 cases fail; the property-name case passes).

### 2. Connect then Disconnect does not give back the original: it leaves `"hooks": {}` and deletes the user's own empty lists (LOW)
- **Input:** `{"model":"x"}`, `{}`, `{"model":"x","env":{...}}`: Connect, then Disconnect. Also `{"hooks":{"Stop":[]}}`, `{"hooks":{"Notification":[]}}` and `{"model":"x","hooks":{"Stop":[],"Notification":[]}}`.
- **What happens:** the first group comes back as `{"model":"x","hooks":{}}` (an empty `hooks` object that Connect had created stays). In the second group, Disconnect deletes the user's own `"Stop": []` because `groups.Count == 0 && before > 0` removes the key once our entry has gone, although the list was the user's before we came.
- **Should:** "removes exactly the entries ... nothing else" and the order's equivalence of Connect then Disconnect: the JSON value comes back the same. The value is harmless to Claude Code, but a person who connects and disconnects now has a changed file.
- **Fix idea:** remember what Connect created (or remove a key or `hooks` only when it was empty after our removal and held only ours); never remove a list that was empty before.
- **Tests:** `Defect_Disconnect_Leaves_An_Empty_Hooks_Object_That_Connect_Had_Created` (3 cases), `Defect_Disconnect_Deletes_A_Users_Own_Empty_Stop_Or_Notification_List` (3 cases). With any other user hook present (10 originals) the round trip is exact: `Holds_Connect_Then_Disconnect_Gives_Back_An_Equivalent_Value`.

### 3. "Connected" is decided by any entry of ours anywhere; a different matcher counts as the permission entry (LOW)
- **Input a:** `{"hooks":{"Stop":[ours],"Notification":[{"matcher":"idle_prompt","hooks":[ours]}]}}` (someone changed or pasted the matcher).
- **What happens a:** `Connect` finds a handler of ours in the `Notification` list and adds nothing (`Changed == false`); there is now no entry for `permission_prompt`, so the "needs your answer" notice can never come, and Connect says it is done.
- **Input b:** only one of the two entries, or ours under `PreToolUse` or `SessionStart` only.
- **What happens b:** `IsConnected` is true, so the settings show "Disconnect" for a half-made (or unrelated) state; the person cannot press Connect to complete it (Disconnect and Connect again works).
- **Should:** connected means both entries, Stop and Notification with `permission_prompt`; Connect checks the matcher before skipping.
- **Tests:** `Defect_Connect_Counts_Our_Program_Under_Another_Matcher_As_Connected_So_Permission_Prompts_Never_Reach_The_Island`, `Defect_Is_Connected_Says_Yes_When_Only_One_Of_The_Two_Entries_Or_An_Entry_Under_Another_Event_Is_There` (4 cases). `Holds_One_Of_Ours_Present_And_The_Other_Missing_Connect_Adds_Only_The_Missing_One` shows Connect itself does complete a half state; only the screen cannot ask for it.

### 4. A hook whose JSON is longer than 64 KB is dropped although the three fields that matter come first (LOW)
- **Input:** `{"session_id":"alpha","cwd":"x/island","hook_event_name":"Stop","last_assistant_message":"<70 000 characters>"}` on `Island.Notify`'s standard input (the hooks page's own order; the long field is the agent's whole last answer).
- **What happens:** `Island.Notify` reads 64 KB, the JSON is cut and does not parse, nothing is sent, exit 0 and no notice. By the letter of the order ("up to 64 KB and no more") this is what was asked; the cost is a silent miss for a long final answer.
- **Should:** read the three fields from what was read (a streaming reader that stops at the first complete `cwd` and event, or a lenient cut) and still send the notice.
- **Test:** `NotifyAttackTests.Defect_A_Stop_Whose_Long_Message_Passes_64_KB_Is_Dropped_Although_Its_Event_And_Folder_Came_First`. Padding placed before the fields is correctly ignored (`Holds_Input_Past_The_Limit_Is_Ignored_Quickly`).

### 5. A notice is lost when the island has no free pipe instance for 300 ms (LOW)
- **Input:** 16 same-user clients that connect and never write, then a real `Island.Notify` start; or a burst of 100 hooks starting together.
- **What happens:** 16 acceptors, each held by an idle connection for the 1.5 s read limit; `Island.Notify` connects once with a 300 ms limit, finds every instance busy, gives up. Deterministic with the 16 idle connections. In the burst of 100 started within a fraction of a second, between 0 and 5 notices were lost in the runs I made (0 of 100 in two runs, 5 of 100 in another; the test makes three rounds and fails when any is lost, so it is probabilistic: it failed in two of four full runs). All 100 processes exited 0 and silent in every run; under that load each took a median 2.1 to 2.3 s and at most 3.2 s (100 .NET processes at once on this machine).
- **Also measured:** behind idle clients the valid message is delayed far more than the 1.5 s of one acceptor cycle: 64 idle clients held a waiting valid message for 33 s in one run and under 10 s in another; 200 idle clients for 87 s and 134 s (the raw client waited with a long connect limit; a real hook would have been lost). Threads and memory stayed flat (server adds no threads; memory +0.1 MB after 2000 garbage connections).
- **Should:** the island should not let a connection that has sent nothing hold an acceptor for 1.5 s (a short first-byte limit, say 200 ms, then the long limit), or have more acceptors, or Notify should retry once. It needs a same-user process that connects and stays silent, so LOW.
- **Tests:** `NotifyAttackTests.Defect_Sixteen_Idle_Same_User_Connections_Make_A_Real_Hook_Lose_Its_Notice` (deterministic), `Defect_A_Burst_Of_A_Hundred_Hooks_Loses_Notices_Because_Notify_Gives_Up_After_300_Ms` (probabilistic), `PipeAttackTests.Holds_Sixty_Four_Idle_Same_User_Clients_Delay_A_Valid_Message_By_An_Unpredictable_Time_But_It_Arrives_And_Nothing_Grows` (measurement only, no tight bound).

### 6. A folder whose name is only invisible characters draws a blank project name (LOW)
- **Input:** `cwd` = `C:\work\` + U+200B, U+2060, U+FEFF, U+00AD, U+3164, U+2800, U+115F, U+180E, U+200D, or three U+200B.
- **What happens:** `AgentText.Clean` removes control and bidirectional characters, line and paragraph separators and lone surrogates, not zero-width or filler characters; `ProjectName.From` returns them, so `AgentNotice.ProjectName` is a string a person cannot see, and the fallback "Agent" is not used. Windows allows such folder names, so it needs a hostile or odd folder only.
- **Should:** treat a name with nothing visible as empty (Unknown, "Agent"); strip format characters (category Cf) and the filler letters.
- **Test:** `WireAttackTests.Defect_A_Folder_Named_Only_With_Invisible_Characters_Draws_A_Blank_Name_Instead_Of_The_Fallback` (10 cases).

### 7. A file that holds only a BOM is "unreadable" where an empty file is fine (LOW)
- **Input:** `"\uFEFF"`, `"\uFEFF\r\n"`, `"\uFEFF   "` as the settings text (an editor that saved an empty file as UTF-8 with BOM).
- **What happens:** the blank check runs before the BOM is stripped; the stripped text is empty, `JsonNode.Parse` throws, the answer is `HOOKS_FILE_UNREADABLE`. An empty or blank file becomes "only our hooks".
- **Should:** strip the BOM first, then the blank check.
- **Test:** `HookInstallerAttackTests.Defect_A_File_That_Holds_Only_A_Bom_Is_Called_Unreadable_Where_An_Empty_File_Is_Fine` (3 cases).

### 8. The path given to Connect is only checked for its file name (LATENT, and one LOW for blanks)
- **Input a:** `Island.Notify.exe`, `.\Island.Notify.exe`, `..\..\Island.Notify.exe`, `\\evil-server\share\Island.Notify.exe`, `//evil-server/share/...`, `\\?\UNC\...`, `http://evil.example/Island.Notify.exe`.
- **What happens a:** accepted; the hook would run whatever of that name the current folder, the PATH or another machine offers. The only caller passes an absolute path under `%LOCALAPPDATA%`, so nothing produces this today.
- **Input b:** `C:\x\Island.Notify.exe ` (trailing blank), leading blank, trailing tab.
- **What happens b:** the check trims, the entry is written untrimmed: the hook then names a file that does not exist (LOW, the same caller never makes it).
- **Input c:** `LinesToAdd(@"C:\x\calc.exe")`.
- **What happens c:** it returns the lines for `calc.exe`, which `Connect` would refuse with `NOTIFY_PATH_INVALID`; the screen's "exactly these lines" can differ from what is written (LATENT).
- **Should:** `Connect` and `LinesToAdd` share one check: an absolute local path, no surrounding blanks.
- **Tests:** `Defect_A_Relative_Or_Network_Path_Is_Accepted_As_The_Hooks_Program` (7 cases), `Defect_A_Path_With_Surrounding_Blanks_Passes_The_Check_Trimmed_And_Is_Written_Untrimmed` (3), `Defect_The_Screen_Is_Shown_Lines_For_A_Program_That_Connect_Would_Refuse`.

### 9. A handler that disposes the server waits two seconds for itself (LATENT)
- **Input:** `server.NoticeReceived += _ => server.Dispose();` then one valid message.
- **What happens:** the handler runs on the acceptor's own task, `Dispose` does `Task.WaitAll` on all acceptors including that one, and returns after the full 2 s (2002 ms measured). The app's handler is `ui.BeginInvoke(...)`, so nothing does this today.
- **Should:** `Dispose` from a handler returns at once (do not wait for the current task), or the event is raised off the acceptor.
- **Test:** `PipeAttackTests.Defect_A_Handler_That_Disposes_The_Server_Stalls_For_The_Whole_Two_Second_Wait_On_Itself`.

## 3. What held

Names are the test methods; counts are cases.

**`Island.Notify` (real child process, invented names)**
- *Exit code 0, nothing on stdout or stderr, always:* every case below ends with `AssertSilentZero`.
- *Nothing on stdin, closed at once:* 146 ms. *Stdin never closed:* ends after 2145 ms (silent and open: 2227 ms), exit 0, nothing sent (`Holds_Stdin_That_Never_Closes_Ends_After_About_Two_Seconds_With_Zero`, `Holds_No_Stdin_Bytes_And_Never_Closed_Also_Ends`). The 2 s is the order's own figure.
- *Larger than the limit:* 64 KB + 1 and 1 MB of padding before the fields: 148 to 149 ms, ignored. 100 MB of blanks streamed in: 178 ms, exit 0, the writer's broken pipe is the only effect.
- *Bad input (21 cases):* every shape listed in part 1 ends silent, exit 0, nothing delivered, under 2 s (`Holds_Bad_Input_Is_Ignored_Silently_Exit_Zero`). Invalid UTF-8 in the folder (4 byte pairs) is not an error: the notice arrives with the fallback name "Agent" and no replacement character (`Holds_Invalid_Utf8_In_The_Folder_Exits_Zero_Silently_And_Never_Leaks_The_Bytes`). A BOM is skipped; a lone surrogate escape in the folder and an escaped field name still give the notice with the name "island".
- *Hostile arguments:* 14 non-plain names, including the full `\\.\pipe\` path of a pipe that exists, never reach any pipe (`Holds_Names_That_Are_Not_Plain_Never_Reach_Any_Pipe_Not_Even_One_That_Exists`); an empty string is refused, not replaced by the real name. 10 odd but plain names: exit 0, silent; `..`, `.`, `...` and `anonymous` end in 180 to 215 ms (the pipe class throws at once), the others in 490 to 510 ms (the full 300 ms connect limit plus start). Extra arguments are ignored.
- *No file named by the input is opened:* a pipe canary named in `cwd`, `transcript_path` and another field was never connected to (`Holds_Paths_Named_In_The_Input_Are_Never_Opened`).
- *A hundred at once:* with nobody listening, 100 of 100 exit 0 and silent, total 3.5 s, median 2.3 s, maximum 3.2 s per process; with a listener, 100 of 100 exit 0 and silent, total 3.2 s, median 2.1 s, maximum 2.8 s, and 95 to 100 notices heard (defect 5). No `Island.Notify` process left running.

**The pipe server (invented names)**
- *Garbage never becomes a notice and never stops the server:* all 34 payloads (part 1) give no notice, and a valid message sent afterwards on the same server arrives (`Holds_Garbage_Delivers_Nothing_And_A_Valid_Notice_Still_Arrives_Afterwards`). The well-formed JSON of the wrong shape (about 22 cases) and the right shape with an ignored event (3 cases) deliver nothing. A million newlines and 100 KB of random bytes are refused after the first read.
- *Time limits:* half a message then silence is dropped (held about 2.2 s as measured by my wait, the limit is 1.5 s) and the server goes on; a valid message written in single-byte flushes arrives; one trickled slower than the limit does not; the size limit counts the newline: 4096 taken, 4097 refused; CRLF endings are taken; two messages in one connection give the first only.
- *Rude clients:* 300 clients closing at six odd moments cause no wrong notice; the whole-but-newline ones are taken (43 to 49 of 50 in two runs: a client that closes before the acceptor has taken the connection loses its data, which the code comment says) and the server works after.
- *Bounded:* 2000 garbage connections: threads 28 to 31, memory +0.1 MB; the server adds no threads while 200 or 64 clients idle (the threads counted in those runs were the test's own tasks).
- *Lifecycle:* Dispose under traffic from 8 senders returns in 1 to 16 ms, nothing is delivered after it, and the name can be taken again at once; Dispose twice and Start after Dispose are harmless; 16 simultaneous Starts give one listener; a second server on the same name is refused (`Start()` false), harms nothing, hears nothing, and does not wake up when the first is gone; names of 316 and 1016 characters and with `\ * ? "` or a blank do not make `Start` throw (it answers true); a throwing handler 40 times is swallowed; a slow handler (300 ms each, 40 waiting clients) loses nothing for clients that wait.
- *Security as the order says:* the rules are exactly one Deny for the network SID, then one Allow for the current user's SID (`Holds_The_Security_Is_The_Current_User_Allowed_And_The_Network_Denied_And_Nobody_Else`); the SDDL of a running pipe, read from a client, is `D:(D;;0x1f019f;;;NU)(A;;0x1f019f;;;<user SID>)`: no Everyone, authenticated users, administrators, SYSTEM, interactive, service or package principal (`Holds_The_Running_Pipe_Has_No_Other_Principal_In_Its_Acl`).

**The wire format and names**
- *Round trip:* 2000 random messages encode to at most 4096 bytes with the newline only at the end and decode to clean, bounded fields; the chain is exactly the first 16 positive ids; no lone surrogate survives. The worst case of escaping (every character six bytes, 400 per field) still fits.
- *Hostile decode:* 46 inputs give null and never throw, including a repeated `e` with `k` missing (five names seen but one missing), a first over-long `e` followed by a good one, 17 ids, ids of 0, -1, 2147483648, 1e2, 1.0, strings and nulls, version 1.0/1.5/true/"1", depth 4, trailing junk, a second object, lone surrogate escapes and a BOM. The decoder keeps what the sender wrote (control characters, bidi); the notice cleans it (`Holds_A_Decoded_But_Uncleaned_Folder_Becomes_A_Clean_Name_In_The_Notice`).
- *Events:* only `Stop` and `Notification` + `permission_prompt`, exact case, no trailing blank; `SubagentStop`, `stop`, `STOP`, `Stop\0`, empty are ignored. The session key is `p<first pid>`, or `f<cleaned folder>` with no chain.
- *`ProjectName`:* trailing and doubled separators, forward and back slashes mixed, `\\server\share`, `\\?\C:\a\b`, a 1 MB folder (cut to 40 characters in a few milliseconds), 500 000 parts, a million separators, only blanks, tabs and newlines, `CON`, `..`, `.`, `...`, a drive root (the name is `C:`), control and bidi characters, lone surrogates, a cut at 40 that never splits a pair and never ends in a blank. A folder whose last part is only control or bidi characters gives the part before it ("a"): not a defect, noted.

**`HookInstaller`**
- *Unreadable text comes back unchanged with `HOOKS_FILE_UNREADABLE`, from all three entry points, never thrown:* the 41 texts in part 1. This includes comments (both kinds) and trailing commas: **Claude Code's own settings are read as strict JSON here, so a settings file with comments is refused, not rewritten**; whether Claude Code accepts such files I did not verify. Nesting of 64 and 65 is refused (62 and 63 are kept); depth 10 000 comes back in under 1.5 s. Repeated keys (`hooks` twice, `env.A` twice, `model` twice) are refused as unreadable, not merged.
- *A `Stop` or `Notification` that is not a list (7 cases):* Connect refuses with the reason before changing anything, Disconnect changes nothing.
- *Round trip with other entries:* for 10 originals (user hooks in several events and groups, user entries that look like ours, numbers such as `1e400`, `-0`, `0.30000000000000004` and 30-digit integers kept as written, escapes, emoji, U+2028, odd members) Connect then Disconnect gives back the same JSON value, keys in the same order. Our two entries come after the user's; a user's group with their own hooks next to ours keeps theirs and its place.
- *Connect twice:* the second time changes nothing and returns the same text, for 20 texts including the empty one and CRLF ones. Disconnect twice and Disconnect without ours change nothing. Line endings of the original (CRLF or LF) are kept. A BOM before an object is accepted and dropped.
- *Look-alikes:* `node` with `Island.Notify.exe` in `args` is not ours (7 shell lines that only mention the name are not ours either); shell-form entries that start with our program, quoted or not, are ours; ours twice are both removed and not added again.
- *Paths:* 12 paths of other programs, `null` and blank are refused unchanged with `NOTIFY_PATH_INVALID`, before the text is looked at; paths with spaces, a quote, unicode (including an emoji), forward slashes, no extension, and over 400 characters are written exactly and found again; `LinesToAdd` equals what Connect writes into an empty file.
- *Size and speed:* 10 MB of valid JSON: Connect 0.7 s, check and Disconnect 1.2 s, value kept; 2 x 50 000 user entries: Connect 0.7 s, Disconnect 0.7 s, value kept; 50 000 groups of ours: Disconnect 0.9 to 1.3 s (quadratic, but bounded); one group of 50 000 handlers of ours: 0.8 s.
- *Fuzz:* 4000 random mutations of a settings text (916 parsed and were written): never an exception, never a change to text the strict parser rejects, no root key lost.
- By-name ownership, as the order says (documented in tests, not defects): a different `Island.Notify.exe` in another folder counts as ours (Connect says "already there", Disconnect removes it); an entry of ours with a stale path is left as it is by Connect (the order says never to repair).

## 4. What I could not test and why

- **The network deny.** Through the machine name and through `127.0.0.1` the same user arrives with an interactive token that holds no NETWORK SID, so a loopback connection cannot exercise the Deny rule (`Holds_A_Loopback_Connection_Is_Not_A_Network_Logon_So_It_Cannot_Exercise_The_Network_Deny`). I read the rule and its order (Deny first, canonical) and checked the SDDL; only a second machine would prove it.
- **Another Windows user.** There is no second account to connect with. That nobody but the current user is in the ACL is proven by the SDDL; that another user is refused in practice is not.
- **A same-user process may also listen.** The Allow rule gives FullControl, which includes creating pipe instances, and the island's own acceptors need that. A second same-user server could therefore join the name (`Holds_Without_FirstPipeInstance_Rule_A_Same_User_Process_Can_Still_Join_The_Name_As_A_Server_Which_Is_Inside_The_Trust` passes) and hear some of the hooks. The order treats the same user as trusted; I list it only so that the main session decides knowingly.
- **A program in another integrity level, an AppContainer process, a service account:** not tried; none is in the ACL.
- **What Claude Code does with the entries.** `args`, `async` and the matcher format are as the order says; I did not check them against the hooks page (no network use) and no real settings file was read, so whether the written JSON is accepted by Claude Code is not proven.
- **How the app calls these.** `HookInstaller` is called by `OutsideAgentConnector` in `Island.App`, which I did not reference or read for behaviour; whether it catches the exception of defect 1 is therefore unknown.
- **The real timings.** The numbers above are from this machine while other programs ran; the 100-at-once figures in particular depend on CPU load.

## 5. Requests to the main session

1. Wrap `Render` in the same exception handling as `TryOpen` (defect 1), strip the BOM before the blank check (7), and decide what Disconnect owes the file's shape (2); then run the `Defect_` tests of `HookInstallerAttackTests` again: the 28 cases flip.
2. Decide whether "connected" means both entries (3). If yes, `IsConnected` and `Connect` both need the matcher and the event.
3. Consider a short first-byte limit in `AgentPipeServer.ReadOne` and, in `Island.Notify`, one retry of the connect (5); `Defect_Sixteen_Idle...` is the deterministic check.
4. `Island.Notify` could read `hook_event_name` and `cwd` from a cut JSON (4); the test `Defect_A_Stop_Whose_Long_Message...` is the check.
5. When these are fixed, the tests named `Defect_` should be renamed `Holds_` in the commit that fixes them; `Defect_A_Burst_...` is probabilistic and should be kept only if made deterministic (it failed in two of four full runs here).

## 6. Fixed (main session)

All nine findings are fixed; every `Defect_` test of this project passes now (361 of 361), the 100-at-once burst test three times in a row. Defects 1, 2, 3, 6, 7, 8 in `febed3f`; 4, 5, 9 in `88fa46b`. Decisions and changes to tests, with reasons:

- Defect 2 cannot be fully met: after Connect, a list that was empty and a list Connect made look the same. The common case (a file without them) comes back exactly; a list the person left empty is taken out with our entry. `Holds_A_List_That_Our_Removal_Emptied...` replaces `Defect_Disconnect_Deletes_A_Users_Own_Empty_...`; one round-trip original changed from `"hooks":{}` to `"hooks":{"Other":[]}` for the same reason.
- Defect 3: connected means both entries; the shell-form test now gives both entries.
- Defect 5: the order says Island.Notify makes one attempt of 300 ms, so Notify keeps its one attempt; the fix is in the island: each connection is read on its own task, 64 acceptors, first byte within 1 s.
- Defect 4: `Island.Agents.Tests` NotifyTests "oversized" pinned that fields inside the first 64 KB were dropped with the rest; the row is now "fields after the limit" (still ignored); fields before the limit are read.
