# Island ↔ browser add-on protocol — version 1

Written once, in phase A of the night (2026-10-06), by the main session. Both sides' tests read the examples
at the bottom of this file: the island's tests (`tests/Island.Tests/Tabs/`) and the add-on's tests
(`extension/tests/`). A change to this file is made only by the main session; an agent that needs one writes the
request into its report.

Nothing in this protocol is ever written to a file on the island's side. Titles, hosts and track names live in
memory only.

## Transport

- A WebSocket. The island listens on **`127.0.0.1` only**, port **47653** (`Island.Core` constant
  `TabProtocol.Port`; if it is taken the listener tries 47654 to 47657 and the add-on tries the same five in turn).
  Path: `/island`. The loopback address is never anything else; a guard test fails if the listener could bind elsewhere.
- Text frames, one JSON object per frame, UTF-8 (a half character left by cutting a title is replaced, never fatal). A frame larger than **1 MB** is dropped (2000 tabs with full titles are about 600 KB) and the connection closed.
  An icon (`png`) is at most **64 KB** before base64 encoding.
- The add-on connects out; the island never connects to anything.
- **Who may connect.** The WebSocket upgrade request must carry an `Origin` header that is exactly
  `chrome-extension://lmnojmilhkpdhejkanneoogmjldolook` (the add-on's fixed id, from the public key in its manifest;
  a web page cannot forge this Origin; other local programs can, which is why the first message must also be a valid
  `hello`). Any other Origin, or none: refuse with HTTP 403. A shared secret between add-on and island is **not** built
  in version 1 (recorded as an owner decision). A bridge started on ports of its own (a test bridge) also accepts the
  pretend add-on's Origin.
- **Limits** the island enforces: 8 connections at once (a ninth is turned away), 2000 tabs per connection, a repeated
  `hello` on one connection counts as a bad frame, and a new `hello` with the same `profile` replaces the older connection.
  The add-on needs the `storage` permission to keep its `profile` id.
- **Announcing.** The first frame from the add-on must be `hello` within 3 seconds, or the island closes the
  connection. After a good `hello` the island answers `welcome`.
- **Keepalive.** The add-on sends `ping` every 20 seconds (Chrome keeps its service worker alive while WebSocket
  traffic flows); the island answers `pong`. No frame for 60 seconds: the island closes the connection.
- **Reconnect.** When the island is not running the add-on keeps trying quietly: on every tab event and on an
  alarm every 30 seconds, never more than one attempt per 5 seconds, never raising an error to the user.
- **Several browsers or profiles** are several connections. Each `hello` carries a `profile` id the add-on
  invents once and stores. The island keys a tab as `"<profile>:<tabId>"`.

## Messages from the add-on to the island

All have `"type"` and, except `hello`, nothing else is required to be present unless listed.

| type | fields | meaning |
|---|---|---|
| `hello` | `v` (1), `client` ("island-addon"), `browser` ("chrome", "edge", "brave", "opera", other text), `profile` (1–64 chars of `[A-Za-z0-9_-]`), `version` (text) | first frame |
| `snapshot` | `tabs`: list of tab objects | the whole list; replaces everything the island knew about this connection |
| `tab` | `tab`: a tab object | one tab was created or changed (an "upsert") |
| `tab-removed` | `id` (number) | a tab closed |
| `tab-activated` | `id`, `windowId` | a tab became the active tab of its window |
| `icon` | `id`, `png` (base64 of a PNG, ≤ 64 KB) | the browser's stored icon for the tab |
| `media` | `id`, `title`, `artist`, `state` ("playing", "paused", "stopped"), `position` (seconds or null), `length` (seconds or null); optional: `rate` (the page player's playing speed, 0 to 16, 1 is normal; missing means 1), `readAt` (milliseconds since 1970 UTC by the add-on's clock when the position was read; missing means "as it arrived") | what the page of a media tab is playing |
| `result` | `cmd` ("activate", "media-command", "close"), `id`, `ok` (true/false) | the add-on's answer to a command |
| `ping` | — | keepalive |

A **tab object**: `id` (number), `windowId` (number), `title` (text, at most 200 chars), `host` (the host only,
lower case, without a leading `www.`, empty when the tab has no web address), `audible` (true/false),
`active` (true/false), `pinned` (true/false, optional), `incognito` (true/false, optional).
The add-on never sends a full address, a query string or a page's contents.

`position` and `length` are `null` for a live stream (a page that reports a duration of more than 1e9 seconds, as a live Twitch stream does, is null here).
The add-on sends `media` again when the person seeks or changes the speed, not only when the state changes, so the island can work the position
out between two reports without ever inventing it (work order 7 section 2). `rate` and `readAt` were added on 2026-10-06; an older add-on sends
neither and the island then takes 1 and the moment the frame arrived.

`media` is sent only for tabs on the five media sites (`youtube.com`, `music.youtube.com`, `twitch.tv`,
`soundcloud.com`, `open.spotify.com`), and only when something changes (at most twice a second per tab).

## Messages from the island to the add-on

| type | fields | meaning |
|---|---|---|
| `welcome` | `v` (1) | answer to a good `hello` |
| `activate` | `id`, `windowId` | switch to the tab and focus its window |
| `media-command` | `id`, `command` ("playpause", "next", "previous") | press the button in the tab's page |
| `close` | `id` | close the tab (the island's close button, work order 6 section 5; added 2026-10-06). The add-on answers `result` with `cmd` "close"; the browser may ask its own question first |
| `resync` | — | send a fresh `snapshot` |
| `pong` | — | keepalive answer |

## What the island does with them

- Unknown `type`, a missing required field, a wrong type or an out-of-range number: the frame is ignored and
  counted; ten bad frames in a row close the connection. Nothing crashes, nothing is written to a file.
- `tab-removed` and `tab-activated` for an id the island does not know are ignored. A `tab` for an unknown id
  is an upsert. A `snapshot` replaces all of one connection's tabs and drops their media and icons.
- When a connection closes, all of its tabs disappear from the island.
- `LastActiveOrder` (on the island's `TabInfo`) is a number the island assigns, increasing, unique across
  connections: a tab gets the next one when it is created or becomes active. "Newest" means highest.
- "Now playing" counts a tab only when its host is a picked media site; the tab that most recently started
  playing (state became "playing") wins; see EVALS.md N2, N7.

## Examples

The tests of both sides read these blocks by their label (the words after `json example`). Keep them valid JSON.

```json example hello
{"type":"hello","v":1,"client":"island-addon","browser":"chrome","profile":"p1","version":"0.1.0"}
```

```json example welcome
{"type":"welcome","v":1}
```

```json example snapshot
{"type":"snapshot","tabs":[
 {"id":11,"windowId":1,"title":"Lo-fi beats","host":"youtube.com","audible":true,"active":true},
 {"id":12,"windowId":1,"title":"Docs","host":"example.org","audible":false,"active":false,"pinned":true}]}
```

```json example tab
{"type":"tab","tab":{"id":13,"windowId":1,"title":"Mix","host":"music.youtube.com","audible":false,"active":false}}
```

```json example tab-removed
{"type":"tab-removed","id":12}
```

```json example tab-activated
{"type":"tab-activated","id":13,"windowId":1}
```

```json example icon
{"type":"icon","id":11,"png":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="}
```

```json example media
{"type":"media","id":11,"title":"Lo-fi beats to study to","artist":"Some Channel","state":"playing","position":42.5,"length":3600}
```

```json example media-timing
{"type":"media","id":11,"title":"Lo-fi beats to study to","artist":"Some Channel","state":"playing","position":42.5,"length":3600,"rate":1.25,"readAt":1790000000000}
```

```json example media-no-length
{"type":"media","id":11,"title":"Live stream","artist":null,"state":"playing","position":null,"length":null}
```

```json example result
{"type":"result","cmd":"activate","id":11,"ok":true}
```

```json example ping
{"type":"ping"}
```

```json example activate
{"type":"activate","id":11,"windowId":1}
```

```json example media-command
{"type":"media-command","id":11,"command":"next"}
```

```json example close
{"type":"close","id":11}
```

```json example result-close
{"type":"result","cmd":"close","id":11,"ok":true}
```

```json example resync
{"type":"resync"}
```

```json example pong
{"type":"pong"}
```

### Frames the island must reject (each is ignored and counted)

```json example bad-unknown-type
{"type":"launch-missiles"}
```

```json example bad-missing-field
{"type":"tab-removed"}
```

```json example bad-wrong-type
{"type":"tab","tab":{"id":"thirteen","windowId":1,"title":"x","host":"a.b","audible":false,"active":false}}
```

```json example bad-not-an-object
[1,2,3]
```
