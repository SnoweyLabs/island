<!-- Source pages: https://developer.chrome.com/docs/webstore/cws-dashboard-listing (the page's own date as the reader's tool reported it: 2020-12-07), the Listing tab; https://developer.chrome.com/docs/extensions/reference/manifest/name and /description (name up to 75 characters, description up to 132 characters). Details in ship/SOURCES.md, section 5. -->

# Chrome Web Store listing — the add-on

## Name (the manifest's own `name`, up to 75 characters)

Island tabs

## Short description (the manifest's own `description`, up to 132 characters)

Tells the Island app on this computer which tabs are open and plays or pauses media tabs when asked. Talks to 127.0.0.1 only.

## Detailed description (plain text)

This add-on needs the Island app for Windows. On its own it does nothing: it only tells Island, on the same computer, which tabs you have open.

What it does: it lets the Island app show your browser tabs as things on the island, light up a site when one of its tabs is open, switch to a tab or close one when you ask on the island, and try to show and control what a tab on YouTube, YouTube Music, Twitch, SoundCloud or Spotify is playing. Reading and controlling what plays on those five sites has not yet been tried on the real sites and may not work.

What it reads: for each tab, its title (a title can contain personal information), the host of its address (never the full address and never what is on the page), whether it makes sound, whether it is the active, a pinned or a private-window tab, and the small picture the browser has stored for the site; on those five sites, what is playing. It sends this only to a program listening on your own computer (the Island app), over a connection that never leaves it, and to no server; it cannot check which program answers. On those five sites it wraps two functions of the page to find the player. It runs no code that arrives from outside.

Not yet tried on other computers.

## Category

Productivity.

## Privacy policy

A link to the text of `ship/store/privacy.md`, hosted by the owner (`__OWNER__`).

## Official, homepage and support URLs (optional)

`__OWNER__`

## Single purpose, permission justifications, data use

See `single-purpose.md`, `permissions.md` and `data-use.md` in this folder.

## Pictures

See `images.md`.
