# Research — signals of Codex, Gemini and Antigravity's terminal program (WORK-ORDER-11.md section 5)

Written 2026-10-07 by agent T3. Every page below was fetched on 2026-10-07 and read as its own text (the page's markdown source, or its rendered text);
tagged [raw] = read word for word on that page, [UNVERIFIED] = the page does not say, nothing is built on it. The notes in `Research/agent-sessions.md`
were used as leads only. No real helper, folder or settings file was opened; nothing was started.

## Codex — https://learn.chatgpt.com/docs/hooks (page source: https://learn.chatgpt.com/docs/hooks.md; https://developers.openai.com/codex/hooks answers 200 on the same page)

Built from (the shapes below were read there; the island's entries are written from them, not from memory):
- Events [raw]: `PreToolUse`, `PermissionRequest`, `PostToolUse`, `PreCompact`, `PostCompact`, `UserPromptSubmit`, `SubagentStop`, `Stop` (during a turn); `Interrupt`
  ("when you interrupt an active turn", not for subagents); `SessionStart`, `SubagentStart`; `SessionEnd` (main thread only).
- File [raw]: `hooks.json`, or inline `[hooks]` tables in `config.toml`; the four useful places are `~/.codex/hooks.json`, `~/.codex/config.toml`,
  `<repo>/.codex/hooks.json`, `<repo>/.codex/config.toml`. A layer that holds both `hooks.json` and `[hooks]` is merged and Codex "warns at startup".
- Shape [raw], the page's own example, abbreviated here: top-level `{ "description"?: …, "hooks": { "<Event>": [ { "matcher"?: "<regex>", "hooks": [ { "type": "command",
  "command": "<one string>", "timeout"?: <seconds>, "async"?: true, "statusMessage"?: … } ] } ] } }`.
- Run in the background [raw]: "Set `async` to true to run a command hook in the background." Background hooks "can't block, approve, rewrite, or otherwise control the
  operation that triggered them"; up to eight run at once per session. **"SessionEnd hooks always run synchronously, even when async is true."** (so no island entry
  for SessionEnd). `Interrupt` keeps a one-second default timeout (three at most) "including when they run in the background".
- No `args` field is documented for a command hook: `command` is one string; `commandWindows` is "an optional Windows-only command override" [raw]. `command`
  and `mcp_tool` handlers run; `prompt` and `agent` handlers are parsed but skipped [raw]. The page does not say which shell runs `command` on Windows [UNVERIFIED]:
  the entry is the shell form, the program's path in double quotes, then `--agent codex`.
- Limiting an entry [raw]: `matcher` is a regex on the tool name for `PermissionRequest`, `PostToolUse`, `PreToolUse`; on the start source for `SessionStart`
  (`startup`, `resume`, `clear`, `compact`); on the reason for `SessionEnd` (always `other`). For `UserPromptSubmit`, `Stop` and `Interrupt` "any configured matcher is
  ignored". Codex has no Notification event, so no entry needs limiting by kind; the island writes no matcher.
- What goes in [raw]: one JSON object on stdin: `session_id`, `transcript_path`, `cwd`, `hook_event_name`, `model`; turn events also `turn_id`; `permission_mode`
  on SessionStart, tool, prompt, Stop and Interrupt events; `PermissionRequest` / `PostToolUse` carry `tool_name`, `tool_input`; `SessionStart` carries `source`.
- Output [raw]: "Exit 0 with no output is treated as success". `Stop` "expects JSON on stdout when it exits 0. Plain text output is invalid for this event" — only for
  a hook that is not in the background; the island's entries are background and print nothing.
- Trust [raw]: "Non-managed hooks must be reviewed and trusted before they run"; "new or changed hooks are marked for review and skipped until trusted"; `/hooks` in
  the CLI reviews and trusts them; at startup Codex warns if hooks need review. Trust is recorded against the hook's hash: a changed entry needs review again.
- An event name the page does not list, in a hooks file: the page does not say what Codex does [UNVERIFIED]. The island writes only events the page lists with no
  version note.

## Gemini CLI — https://github.com/google-gemini/gemini-cli/blob/main/docs/hooks/index.md and reference.md (read as https://raw.githubusercontent.com/google-gemini/gemini-cli/main/docs/hooks/index.md and …/reference.md, then writing-hooks.md and best-practices.md)

- Events [raw]: `SessionStart`, `SessionEnd`, `BeforeAgent`, `AfterAgent`, `BeforeModel`, `AfterModel`, `BeforeToolSelection`, `BeforeTool`, `AfterTool`, `PreCompress`, `Notification`.
- File [raw]: `settings.json`, `hooks` object, event -> array of `{ "matcher"?, "sequential"?, "hooks": [ { "type": "command", "command": "<shell command>", "name"?, "timeout"? (ms, default 60000), "description"? } ] }`;
  user file `~/.gemini/settings.json`, project file `.gemini/settings.json`, system file `/etc/gemini-cli/settings.json`.
- Input [raw]: `session_id`, `transcript_path`, `cwd`, `hook_event_name`, `timestamp`; `Notification` has `notification_type` (only `"ToolPermission"`), `message`, `details`.
- **Waiting [raw]:** "Hooks run synchronously as part of the agent loop—when a hook event fires, Gemini CLI waits for all matching hooks to complete before continuing."
  The configuration fields are `type`, `command`, `name`, `timeout`, `description` (and `sequential` on the group): **there is no `async` or background field, and no `args`.**
  Only `SessionEnd` ("The CLI will not wait for this hook") and `PreCompress` ("Fired asynchronously") are said not to hold the helper, and neither carries "working", "needs you"
  or "finished". (`best-practices.md` says once, loosely, "hooks run in the background"; the two pages that specify it say the opposite.)
- Unknown event in a settings file: not said [UNVERIFIED].
- Result: **BLOCKED** — no documented way to run a hook without making Gemini wait; nothing built.

## Antigravity's terminal program — https://antigravity.google/docs/hooks (page source: https://antigravity.google/docs/hooks.md)

- Files [raw] (CLI tab): `.agents/hooks.json` at the project root; global `~/.gemini/config/hooks.json` "or inside your primary `~/.gemini/antigravity-cli/settings.json`"; plugin `hooks.json`.
  `/hooks` in its TUI lists the loaded hooks.
- Shape [raw]: `hooks.json` maps a hook name to its event configuration: `{ "<hook-name>": { "enabled"?: false, "PreToolUse"|"PostToolUse": [ { "matcher": "<regex>", "hooks": [ {handler} ] } ],
  "PreInvocation"|"PostInvocation"|"Stop": [ {handler} ] } }`; the handler is `type` (only `"command"`), `command` ("The shell command to execute"), `timeout` (seconds, default 30).
- Events [raw]: `PreToolUse`, `PostToolUse`, `PreInvocation`, `PostInvocation`, `Stop`. No session start, no prompt event, no permission event, no session end.
- Input [raw], camelCase on stdin: `conversationId`, `workspacePaths` (list), `transcriptPath`, `artifactDirectoryPath`, `modelName`; `PreToolUse`/`PostToolUse` add `toolCall.name`, `toolCall.args`, `stepIdx`
  (and `error` on PostToolUse); `PreInvocation`/`PostInvocation` add `invocationNum`, `initialNumSteps`; `Stop` adds `executionNum`, `terminationReason`, `error`, `fullyIdle`.
  **No field names the event** (so an entry would have to pass `--event`), and there is no `cwd`.
- **Waiting [raw]:** the handler has three fields only (`type`, `command`, `timeout`); there is **no async or background field and no `args`**. `PostToolUse` output is "an empty JSON object {}"
  and `Stop` has a required `decision`, i.e. the page describes hooks whose output the program reads.
- Unknown event in a hooks file: not said [UNVERIFIED].
- Result: **BLOCKED** — no documented way to run a hook without making the program wait; nothing built.

## Not used

- Codex's older `notify = [...]` setting is a lead from `Research/agent-sessions.md` that was not re-read on its own page by T3 [UNVERIFIED here]; it is TOML, and a TOML settings format is BLOCKED rather than written with a parser made here. The inline `[hooks]` tables of `config.toml` are likewise not written: only `hooks.json` is.
