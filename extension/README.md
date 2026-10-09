# Island tabs: the Chrome add-on

This small add-on tells the Island app which browser tabs you have open, so the island can light up a site
like YouTube when one of its tabs is open, jump to that tab when you click it, and show and control what a
YouTube, YouTube Music, Twitch, SoundCloud or Spotify tab is playing.

It talks only to the Island app on this same computer (the address `127.0.0.1`). It never downloads anything,
and it never sends a full web address or what is on a page: only each tab's title, its site name (for
example `youtube.com`) and whether it is making sound. The island keeps that in memory and writes none of it
to disk.

**Tried so far:** in a real Chrome, a freshly opened tab of one of the five sites reported what it played to
the island. Everything else was checked only against a pretend browser, among it the part that gives the
add-on's scripts to tabs that were already open (version 1.1.0).

## Putting it into Chrome (about two minutes)

1. Open Chrome. In the address bar type `chrome://extensions` and press Enter.
2. In the top right corner of that page, turn on **Developer mode**.
3. Three buttons appear on the left. Click **Load unpacked**.
4. In the folder window that opens, go to the Island project folder, click the folder named **`extension`**
   once, and press **Select Folder**.
5. A card named **Island tabs** appears in the list. Its ID should read:
   `lmnojmilhkpdhejkanneoogmjldolook`
6. Leave **Developer mode turned on.** If it is turned off, Chrome quietly switches this add-on off too.

The same steps work in Edge (`edge://extensions`, "Developer mode" is on the left), Brave and Opera.
Firefox is not supported.

## What you should see

- Nothing changes in Chrome itself: the add-on has no button and no window.
- With the Island app running, open a YouTube tab and call the island: the YouTube tile is bright, and shows
  a small number when you have more than one YouTube tab.
- Click the tile: Chrome jumps to the newest YouTube tab. Click again: the next one.
- Play something on YouTube: the island's "Now playing" line shows it, and its buttons pause it and skip.

If the Island app is not running, the add-on waits quietly and connects by itself within about half a minute
of the app starting. You do not need to restart Chrome.

## If something does not work

- **A media tab that was already open when you installed, updated or reloaded the add-on** picks it up by
  itself within a moment: you do not reload the tab. (After you press the reload arrow on the add-on's card,
  the add-on goes through the open tabs of the five media sites once and gives each one its scripts.) A tab
  that Chrome had put to sleep picks it up when you open it. If a tab still says nothing, reload it once (F5).
  An add-on that is switched off and on again on the extensions page gets no signal to go through the open
  tabs: those tabs report after a reload, as before. In a tab that gets the script late, "next" and "previous"
  may fall back on pressing the page's buttons, whose names were guessed; play and pause work through the player.
- **Pause or next does nothing on one site:** that site may have changed its page. Tell Claude which site.
- **On the extensions page, Island tabs shows an "Errors" button:** click it, take a picture of the
  first lines, and show Claude. Lines saying a connection to `127.0.0.1` failed only mean the Island app was
  not running at that moment.

## Updating it

After the files of the `extension` folder change, press the round **reload arrow** on the Island tabs card on
`chrome://extensions` once. That is all: the open media tabs are given the new scripts by the add-on itself.

## Removing it

On `chrome://extensions`, click **Remove** on the Island tabs card.
