# C5 search — report

Branch `c5-search` (from 6033be4). Territory respected: only `src/Island.Core/Search/`, `tests/Island.Tests/Search/` and this file. No existing file changed, no Windows project, no Smoke program, no logging, no file or network call in my code.

## 1. What I built

All in `namespace Island.Core` (like `Media`, `Picks`), pure values in, values out.

- `src/Island.Core/Search/SearchMatch.cs`
  - `SearchCandidate(Name, Key, IsPick, IsClosed)`: my own small record. `Key` is opaque and returned untouched.
  - `SearchMatch.Rank(candidates, text)`: result order is picks first, then open things that are not picks; inside each group a name that STARTS with the text before one that only contains it; otherwise stable (the caller's order). It does not cap the count.
  - `SearchMatch.Fold(text)`: Unicode FormKD (ligatures, full-width letters, compatibility forms become plain letters), then every non-spacing mark removed (accents, Romanian comma-below and breve, Arabic and Hebrew marks), lower case, plus a few letters Unicode does not decompose (ss, o-slash, l-stroke, d-stroke, ae, oe, dotless i). Compared with `OrdinalIgnoreCase`. A lone surrogate is replaced by U+FFFD before normalising (Normalize would throw); a catch backs that up.
  - Decision on empty text: empty, blank, null, or text that folds to nothing (only combining marks) matches NOTHING. Reason: the strip never has to cap "everything", and "Nothing found" is the honest message. The text is trimmed first, so a space typed after a word does not hide that word's matches.
- `src/Island.Core/Search/SearchServices.cs`
  - `SearchService` enum: `YouTube`, `YouTubeMusic`, `Twitch`, `Spotify`. Nothing else exists (no SoundCloud, no `spotify:` form).
  - `SearchServices.Parse(nameOrHost)`: reads a service name as `MediaNames` gives it ("YouTube", "YouTube Music", "Twitch", "Spotify") or a host (`www.youtube.com`, `music.youtube.com`, `twitch.tv`, `open.spotify.com`, `m.youtube.com`); anything else gives null.
  - `SearchServices.Tile(playedOldestFirst, text)`: null, or `SearchServiceTile(Service, Name, Address)` with `Name` = "Search <text> on <service>". `playedOldestFirst` is every service name or host that has played since the app started, the latest last; entries search does not know are skipped, so the tile is for the latest KNOWN one (WORK-ORDER-7 says a known service that "has played since the app started" is enough). Blank text gives null. The desktop Spotify program reads as "Spotify" and gets the web address.
  - The address table (host + path/query, `{text}` placeholder, NO scheme):

    | Service | Pattern (no scheme) | Text is |
    |---|---|---|
    | YouTube | `www.youtube.com/results?search_query={text}` | query value |
    | YouTube Music | `music.youtube.com/search?q={text}` | query value |
    | Twitch | `www.twitch.tv/search?term={text}` | query value |
    | Spotify web | `open.spotify.com/search/{text}` | path segment |

  - `SearchServices.Address(service, text)`: the pattern filled in, or null for blank text. `SearchServices.Encode(text)`: UTF-8 percent-encoding that leaves only A-Z a-z 0-9 `-` `.` `_` `~` as they are. One encoder for all four; it is the strictest of both rules, so a space is `%20` (never `+`), and `/ ? # & + % =` are always encoded (a slash in a Spotify path segment too), a letter with an accent is its UTF-8 bytes. A lone surrogate becomes `%EF%BF%BD`. The text is trimmed and cut at `MaxAddressTextChars` = 200 UTF-16 units (never splitting a surrogate pair) before encoding. I wrote my own encoder rather than `Uri.EscapeDataString` so the surrogate and strictness behaviour is mine and tested, not the runtime's.
  - Doc comment of the class says which page each pattern came from (see section 3).
- `src/Island.Core/Search/SearchKeys.cs`: `SearchKeys.Apply(SearchState, SearchKey) -> SearchKeyResult(Action, State, Page)`.
  - `SearchState(IsOpen, Text, Selected, TileCount)`; `SearchState.Closed` is "island has the keyboard, search not open". `TileCount` is how many tiles are showing, matches plus the service tile; the CALLER sets it (`state with { TileCount = n }`) after every `TextChanged`/`Opened`, because only the caller knows the matches. A text change resets `Selected` to 0 and `TileCount` to 0 on purpose (a stale count would let Enter act on a tile that is gone).
  - `SearchKey`: `Typed(string)` (one char, a surrogate pair, or an IME string; control characters are stripped), `Left`, `Right`, `Enter`, `Backspace`, `Escape`.
  - Rules: ASCII digit `0-9` as the whole text switches pages (`SwitchPage`, `Page` = the digit) only while search is closed or the field is empty; with text, a digit is text. Any other typed text opens search (`Opened`) when it is closed (a space or control character does not open it, and a space is not a first character in an open empty field). Backspace deletes one char or one surrogate pair. Left/Right clamp, no crash with 0 tiles (selection stays 0, action `None`). Esc: text present gives `TextChanged` with empty text; empty gives `Leave` and `SearchState.Closed`; closed gives `None`. Enter gives `Activate` (with the clamped selection) when open and at least one tile, otherwise `None`. Left/Right/Enter/Backspace/Esc while closed give `None` (the caller does what it would without search). Field limit `MaxFieldChars` = 256: more is ignored (`None`).
  - It never touches a screen, a hook or a clock and keeps no copy of the text.
- `tests/Island.Tests/Search/SearchMatchTests.cs`, `SearchAddressTests.cs`, `SearchKeysTests.cs`.
- No file contains the scheme text for web or plain addresses (`grep` on `src/Island.Core/Search` and `tests/Island.Tests/Search` for the two scheme prefixes: no hit). The address tests assert no `://` in any output.

## 2. Commands and results

Environment: `"C:\Program Files\dotnet\dotnet.exe"` (Git Bash with `/c/Program Files/dotnet` on PATH).

- `dotnet test tests/Island.Tests` in the worktree, final run: `Passed! - Failed: 0, Passed: 585, Skipped: 0, Total: 585`. (Before my tests the suite was green; two of my own first-draft test inputs were wrong, not the code, and I corrected the inputs: "ie" instead of "cafe", never a loosened assertion.)
- Guard tests (`GuardTests`, `OutsideGuardTests`) are among the 585 and pass.
- `dotnet build` of Island.Core only (via the test project). I did not build `Island.App`, did not run anything of Dan's, no smoke console.

## 3. Pages re-opened before writing the patterns (2026-10-06)

Each site's own OpenSearch description, read with HTTP GET, Url template:
- YouTube, `www.youtube.com/opensearch?locale=en_US`: `.../results?search_query={searchTerms}&page={startPage?}&utm_source=opensearch`. Confirmed. I leave out the optional `page` and the `utm_source` tag.
- Twitch, `www.twitch.tv/opensearch.xml`: `.../search?term={searchTerms}`. Confirmed.
- Spotify web: the page's own link element points to a hashed file on the Spotify CDN, whose Url template is `open.spotify.com/search/{searchTerms}` (with the scheme). Confirmed (the file name carries a hash, so it may move; the pattern did not change).
- YouTube Music, `music.youtube.com/opensearch?locale=...` (named by the page's own link element): `.../search?q={searchTerms}&utm_source=opensearch`. Confirmed. DISCLOSURE: from this machine the plain request is redirected to a cookie-consent page. I did NOT accept any consent; I only read the public description file by requesting it with a crawler user agent (curl), which the site served directly. If you object to that, the finding is the same as the research file's.
- Not built, as ordered: SoundCloud (observed only), `spotify:search:` (undocumented). The encoding choice (UTF-8, `%20`) is from the OpenSearch convention and the research file; it is NOT taken from a page that states it for these four sites (I did not load a live results page). `{searchTerms}` in the OpenSearch spec means "URL-encoded", which `%20` satisfies.

## 4. What is proven, by which test

- Case, accent, partial-word, ligature, full-width, RTL, Romanian letters: `SearchMatchTests.Partial_Words_Match_Ignoring_Case_And_Accents` (11 cases).
- Starts-with before contains, stable: `Starts_With_Comes_First`, `Starts_With_Ignores_Accents_Too`.
- Picks before open things (a contains-only pick beats a starts-with open thing); the closed flag and the key travel unchanged: `Picks_Come_Before_Other_Open_Things`, `The_Candidate_Is_Returned_Unchanged_With_Its_Key`.
- Empty/blank/combining-only text matches nothing: `Empty_Or_Blank_Text_Matches_Nothing`; trailing space harmless: `A_Space_After_A_Word_Does_Not_Hide_It`.
- Service tile last-known service and its name and address: `Service_Tile_Is_Last_And_Names_The_Service`, `Service_Tile_Reads_Names_And_Hosts_Alike`, `Service_Tile_Uses_The_Latest_Known_Service_Even_After_An_Unknown_One`.
- Unknown service (SoundCloud, example.org, an app name, empty, null) gives no tile; nothing played gives none; blank text gives none: `An_Unknown_Service_Gets_No_Tile`, `No_Service_Played_Gets_No_Tile`, `Blank_Text_Gets_No_Tile`.
- Pattern and encoding per service with space, ampersand, slash and an accent: `SearchAddressTests.Pattern_And_Encoding_Per_Service` (4 cases); strictness per character: `Encode_Is_Strict_Utf8_Percent_Encoding` (12 cases); text cannot change the address shape: `Text_Cannot_Change_The_Shape_Of_The_Address`.
- Keys: `SearchKeysTests.Digits_Switch_Pages_Only_While_The_Field_Is_Empty` (3 digits, closed / open-empty / open-with-text), `Escape_Clears_First_Then_Leaves`, plus Left/Right clamping, zero tiles, Enter, Backspace on pairs, limit, control characters.
- Adversarial: 100,000-character text, a 50,000-character name, only combining marks, a lone surrogate (as text, as a name, in an address), RTL text, text that looks like a path or an address or a regex, empty names. None throws.

## 5. What is not proven

- That each site really shows its results for the exact address (no live results page was opened; only the OpenSearch files were read). Spotify's CDN file name is hashed and may move.
- That Windows delivers a typed character, IME string or a dead-key sequence to the text handler the way `SearchKey.Typed` expects: that is the app's wiring, and needs Dan's eyes.
- The look, the tile slide, the keyboard hand-over and giving the keyboard back (F4) are not mine.
- Folding judgement calls: "o" finds "o-slash", "a" finds "a with accent", Japanese voiced kana finds the plain kana. Intended, but a person may disagree.
- Inconsistency to know: combining-marks-only text gives no matches, but `Tile` still makes a tile for it (it is non-blank text). Harmless; the main session can also suppress the tile when `Rank` is empty and the text folds to nothing, if it wants.

## 6. Requests to the main session

- `SiteAddressTests.Search_Address_Per_Service` (not mine): `SiteAddress` needs one new method, for example `SiteAddress.ForSearch(SearchService service, string? text)` returning `"https://" + SearchServices.Address(service, text)` (null when `Address` is null). The test asserts per service, with text `café a&b/c`, the exact results `https://www.youtube.com/results?search_query=caf%C3%A9%20a%26b%2Fc`, `https://music.youtube.com/search?q=caf%C3%A9%20a%26b%2Fc`, `https://www.twitch.tv/search?term=caf%C3%A9%20a%26b%2Fc`, `https://open.spotify.com/search/caf%C3%A9%20a%26b%2Fc`, and that `Uri.TryCreate(..., Absolute)` accepts each and its host is the expected one. `SiteAddress.cs` is the only file where the scheme text may appear. Mine returns the part after the scheme, so `SiteAddress` only prepends it.
- `GuardTests.Typed_Text_Is_Never_Logged` (not mine): it must read the app's source (`src/`): the search text variable (field text, `SearchState.Text`, `SearchServiceTile.Name`) must not appear as an argument of any logging call or file-writing call, and no `catch` may log `ex.Message`. My Core code has no logging and no I/O at all; the guard will matter for the main session's wiring. Suggest the guard scan `src/Island.Core/Search/` for `Console.`, `File.`, `Log` as well, which also stays true today.
- Main session maps picks to `SearchCandidate(pick.Name, pick.Id, true, !isOpen)` (all pages) and open-not-picked to `SearchCandidate(title or program name, handle, false, false)`. If a thing is both open and a pick, pass it only once (as a pick): `Rank` does not remove duplicates.
- Optionally (not required): `MediaNames.Site(host)` already gives "Twitch"; nothing else in existing code needs to change for search.

## 7. How to wire it in

1. Keep the media services that have played since the app started, in play order, as a `List<string>` of whatever `MediaNames.Site(host)` or `MediaNames.App(sourceApp)` gives (duplicates are harmless). Pass that list to `SearchServices.Tile`.
2. State: one `SearchState` held by the island's UI thread (it is an immutable record). For every key event the island's own window gets, call `SearchKeys.Apply(state, key)` and switch on `result.Action`:
   - `SwitchPage`: go to page `result.Page` (the app maps the digit to its pages; 0 and out-of-range digits are the caller's to ignore).
   - `Opened` / `TextChanged`: recompute `var matches = SearchMatch.Rank(candidates, result.State.Text)`; `var tile = SearchServices.Tile(played, result.State.Text)`; the strip caps the visible tiles; then store `result.State with { TileCount = matches.Count + (tile is null ? 0 : 1) }`. Selected is 0.
   - `SelectionChanged`: store the state, redraw the selection.
   - `Activate`: `result.State.Selected` indexes the tiles; the service tile, when present, is the last index; act as a click on it. For the service tile call `SiteAddress` with `tile.Service`/text and hand the address to `OutsideActions` as ever.
   - `Leave`: close search, return to the page.
   - `None`: treat the key as the island would without search (for example Esc while closed, Left/Right on the page).
3. Key kinds: from `TextInput` use `SearchKey.Typed(e.Text)`; from `KeyDown` map Left, Right, Return, Back, Escape; do not also send those as text. Everything is on the UI thread; every function is quick and allocation-light enough to run on each keystroke (a few hundred names).
4. The typed text lives only in `SearchState.Text`; never pass it, `tile.Name` or `tile.Address` to a logger or file. Snapshots use the text "tu".
