# c4-progress — report

Piece C4, WORK-ORDER-7.md section 2 (the share that is left). Branch `c4-progress`, from 6033be4. Pure Island.Core: no drawing, no timer, no clock inside, no outside call.

## 1. What I built

`src/Island.Core/Progress/`
- `ProgressReport.cs` — `readonly record struct ProgressReport(double? PositionSeconds, double? LengthSeconds, DateTimeOffset ReportedAt, bool IsPlaying, double Speed = 1)`; the constant `WindowsZeroDate` (1601-01-01 UTC); `FromView(NowPlayingView, now)`, a bridge from what `NowPlaying` already gives (position as of `now`, so it stands still).
- `LitShare.cs` — `LitShare.Of(report, now)` returns `double?`: the lit share 0..1, or null = "no progress" (the pill shows the approved moving light).
- `RedrawGate.cs` — `RedrawGate.ShouldRedraw(double? drawn, double? next, double outlinePixels)` and the constant `MinVisibleChangePixels = 1.0`.
- `TabMediaTiming.cs` — `TabMediaTiming.ToReport(position, length, isPlaying, rate, readAtMs, arrivedAt)`: turns the extended add-on `media` message into a report. Its doc comment holds the proposed fields and the example frame.

`tests/Island.Tests/Progress/`
- `ProgressTests.cs` — the five named tests plus extras (see 3).
- `NowPlayingProgressTests.cs` — `Unknown_Length_Shows_No_Progress` (see below).

Rules chosen (all documented in the code):
- Position between reports = `position + max(0, now - reportedAt) x speed`, only while `IsPlaying`; clamped to 0..length; share = `1 - position/length`, clamped.
- No progress (null) when: position or length null; length <= 0 or not finite; `ReportedAt <= WindowsZeroDate` (also catches `default`); position < 0, not finite, or `> length` (exactly equal to the length is a finished item, share 0); speed not finite.
- Speed 0: stands still. Negative speed: the literal formula runs backwards and stops at the start (a rewind). Huge speed: clamps. A report in the future or a clock that goes backwards: elapsed counts as 0.
- Redraw rule: redraw when `|new - last| x outlinePixels >= 1.0` device pixel (only the end of the lit part moves, so 1 px of outline = 1 px on screen). Always redraw on a switch between ring and moving light (null <-> number), and when the share reaches exactly 0 or 1 from another value (so a finished ring is really empty). Unusable outline length: any change redraws. Non-finite share counts as null. Worked example in a test: a 1 hour item on a 400 px outline redraws about every 9 s (6 redraws in 60 s at 60 fps, not 3600).

Confirmed on Microsoft Learn (fetched today): `GlobalSystemMediaTransportControlsSessionTimelineProperties` — `Position` "current as of LastUpdatedTime", `LastUpdatedTime` "the UTC time at which the timeline properties were last updated"; `PlaybackInfo.PlaybackRate` is `Nullable<double>`, "1 being normal playback"; `Windows.Foundation.DateTime.UniversalTime` counts 100-ns intervals from midnight 1 January 1601 and projects to `DateTimeOffset` in .NET; `FILETIME` also from 1 January 1601 UTC. So the zero value is 1601-01-01T00:00:00Z (the test also checks `DateTimeOffset.FromFileTime(0)` equals it). UNVERIFIED on Learn (research only): that a player without a timeline really reports this value (SimpMusic issue 2590); Learn says nothing about it.

## 2. Commands and results

- `"C:\Program Files\dotnet\dotnet.exe" test "<worktree>\tests\Island.Tests"` — first run: 513 passed, 1 failed (my own wrong expected number in `Follows_A_Seek`, test arithmetic, fixed in the test; code unchanged). Second run: **Passed 514, Failed 0, Skipped 0** (12 of those are mine: 11 in `ProgressTests`, 1 in `NowPlayingProgressTests`; so 502 existed before). The guard tests (`OutsideGuardTests`, `GuardTests`) are in that green run.
- No app was started, no window shown, nothing built outside Island.Core and Island.Tests.

## 3. What is proven, by which test

All in `tests/Island.Tests/Progress/`, namespace `Island.Tests`:
- `ProgressTests.Lit_Share_Equals_What_Is_Left` — 25% played gives 0.75 (the self-test's figure); steady play shrinks it one second per second.
- `ProgressTests.Follows_A_Seek` — a new report replaces the old one at once, forward and back.
- `ProgressTests.Works_Out_The_Position_Between_Reports_Only_While_Playing` — playing moves; speed 2 moves twice as fast; paused and speed 0 stand still.
- `ProgressTests.Never_Outside_Zero_To_One` — stale report ends at 0; future report and backwards clock count no time; rewind stops at 1; speed +-1e308; length 1e12; `DateTimeOffset.MaxValue`/`MinValue` as now; a sweep of 10 x 10 x 10 x 5 odd inputs (NaN, +-Inf, 0, +-1, 1e308...) always gives null or a finite value in 0..1.
- `ProgressTests.Zero_Date_Or_A_Position_Past_The_End_Shows_No_Progress` — zero date, `default`, position past the length; one tick after the zero date is real; position equal to the length is share 0.
- `NowPlayingProgressTests.Unknown_Length_Shows_No_Progress` — **put in class `NowPlayingProgressTests` (file `NowPlayingProgressTests.cs`) with the method name unchanged, because `NowPlayingTests` already exists and I may not edit it. The main session moves the method into `NowPlayingTests`.** It feeds a real `NowPlaying` a session with no length and checks the view and the share, also later in time; also length 0, null, and a tab that sends null.
- Extra (mine): `ProgressTests.Unusable_Numbers_Show_No_Progress`, `Speed_Zero_And_A_Default_Speed_Of_One`, `Redraw_Only_For_A_Visible_Change`, `A_Playing_Item_Needs_A_Redraw_Roughly_Every_Few_Seconds_Not_Every_Frame`, `A_Tab_Message_Becomes_A_Report`, `A_Now_Playing_View_Gives_A_Report_That_Matches_Its_Own_Progress`.

## 4. What is not proven

- That any real player (Spotify, Chrome tabs, YouTube) actually sends the zero date, a rate, or timely reports: needs a real session and a person's ears and eyes. Nothing here read the machine.
- That the self-test's "lit part covers three quarters of the ring within 2%" holds on screen: that is the drawing, `RoundedPerimeter.Walk(0, share)`; my part only gives the share.
- The CPU figure of the pill (WORK-ORDER-7 section 2, measuring run) is not measured; the redraw gate only makes it possible. The caller still needs a wake-up that is not a 60 fps tick (see 6).
- Strictness of "position beyond the length": any amount, even 0.3 s, gives no ring. Some players overshoot the end by a little at the last moment; the pill then briefly shows the moving light. I took the order's wording literally. If that flickers in practice, a tolerance is one constant in `LitShare.Of`.
- Twitch live streams: the add-on research says WebNowPlaying detects live as `video.duration === 1073741824`. If the add-on forwards that as a length, the ring would be drawn as a nearly full ring for a live stream, which is invented progress. I did not add a special number in Core (the order's list is exact); the add-on must send `null` for live (see section 5).

## 5. Requests to the main session

1. **Protocol extension (do not edit by me; `extension/PROTOCOL.md` section "Messages from the add-on", row `media`).** Add two optional fields to `media`:
   - `rate` — number, the page player's playing speed, 1 is normal; missing means 1.
   - `readAt` — number, milliseconds since 1970-01-01 UTC from the add-on's clock (`Date.now()`) at the moment the position was read; missing means "as it arrived". The island clamps a value in the future to the arrival time.
   Example frame for both sides' tests:
   ```json example media-timing
   {"type":"media","id":11,"title":"Lo-fi beats to study to","artist":"Some Channel","state":"playing","position":42.5,"length":3600,"rate":1.25,"readAt":1790000000000}
   ```
   Also say there: `position` and `length` are `null` for a live stream (Twitch live arrives as duration 1073741824; the add-on must turn that into `null`), and the add-on re-sends `media` when the user seeks or changes speed, not only on a state change. The island side is `TabMediaTiming.ToReport`, tested by `ProgressTests.A_Tab_Message_Becomes_A_Report`. The named test `TabMessageTests.Playing_Message_Carries_Speed_And_Reading_Time` is for the real parser and the listener, which I did not touch.
2. **Joint `TabMedia` (`src/Island.Core/Joints/OpenWorld.cs`)** has no speed or reading time: add `double? Rate` and `DateTimeOffset? ReadAt` (or hand a `ProgressReport` along), and fill them in the tab listener from the new fields.
3. **`MediaSessionInfo` and `MediaSessionReader.ReadTimeline`** (`src/Island.Sources.Media`) pre-extrapolate the position with the age and ignore the rate, and drop a "past the end" position by clamping it. For the pill the reader should hand over the raw `Position`, `LastUpdatedTime` (as `ReportedAt`) and `PlaybackRate` (null -> 1), with no clamping, so `LitShare.Of` applies the zero-date and past-the-end rules (N11). Nothing breaks if this waits: `ProgressReport.FromView` works from what exists, only without the rate and without the past-the-end rule.
4. **`NowPlaying`** could keep a `ProgressReport` per candidate instead of (position, length) and call `LitShare.Of` in `ViewAt`; then `NowPlayingView.Progress` would follow the same N11 rules. I did not touch it.
5. Move `NowPlayingProgressTests.Unknown_Length_Shows_No_Progress` into class `NowPlayingTests` (Media/NowPlayingTests.cs) as the order names it.

## 6. How to wire it

- Per frame or per event, with the current report and the app's own clock value: `var share = LitShare.Of(report, DateTimeOffset.UtcNow);` (the clock is handed in by the caller; use the same `now` the island's other logic uses).
- `share == null` -> draw the approved moving light in the Media colour. Otherwise draw `RoundedPerimeter.Walk(0, share)` as the lit part and the rest of the ring white at 16%.
- Keep `lastDrawn` (a `double?`, null while the moving light is up). Before redrawing: `if (RedrawGate.ShouldRedraw(lastDrawn, share, perimeter.Length * dpiScale)) { draw; lastDrawn = share; }`. `outlinePixels` must be in device pixels, so multiply the perimeter by the DPI scale of the screen.
- Threading: all four types are stateless static functions over immutable values; safe from any thread, including the drawing thread. For the CPU budget do not tick at frame rate while the ring is shown: wake up when the lit end will have moved one pixel, about `length / (outlinePixels x speed)` seconds after the last draw (9 s for the example above), and on every new report (a seek), a state change, or a mode change. Not built here (the order asked for a gate only).
- For a tab: `TabMediaTiming.ToReport(m.Position, m.Length, m.State == Playing, m.Rate, m.ReadAtMs, arrivedAt)` once the joint carries rate and reading time; for a session: `new ProgressReport(position, length, lastUpdatedTime, playing, rate ?? 1)`. Zero date, length 0 and a past-the-end position then give null by themselves.
