<!-- Source pages: https://developer.chrome.com/docs/webstore/cws-dashboard-privacy (the page's own date as the reader's tool reported it: 2020-06-12), the Privacy tab; https://developer.chrome.com/docs/webstore/program-policies/user-data-faq (2016-04-23 as reported), the data categories; https://developer.chrome.com/docs/webstore/program-policies/limited-use (2022-11-01 as reported). Details in ship/SOURCES.md, section 5. -->

# Facts for the store's data questions

These are facts about the add-on, written from its code and from `extension/PROTOCOL.md`. The owner ticks the boxes; this file says what is true.

## What it handles

- **Web browsing activity.** The add-on reads, for each tab, its title (cut to 200 characters), the host of its address (the part before the first slash, never the full address, never a query, never the page's contents), whether the tab makes sound, whether it is the active or a pinned tab, whether it is in a private window (where the browser lets the add-on run there), and the small picture the browser has stored for the site. On five named media sites it also reads what is playing: title, artist, position, length and state.
- **A tab's title can contain personal information** (websites put names and mailbox addresses in titles), so this is not data that is free of it. Nothing else: no health, financial or authentication information, no personal communications, no location, no form data, no cookies, no history, and no page contents.

## What it does with it

- It uses it **only** to show and switch the person's tabs, and to show and control what a media tab is playing, in the Island program on the same computer. That is the add-on's single purpose.
- It sends it **to no server**. It connects out to one address only, `127.0.0.1` (the person's own computer), trying five ports in turn; the program that answers on one of them should be Island, but the add-on cannot check which program answers and the connection has no shared secret (version 1 of the protocol). A guard test (`ExtensionGuardTests.No_Remote_Fetch_And_No_Icon_Service`) fails if any other address appears in the add-on's code, in the source or in the zip.
- It can also close a tab and press a page player's buttons, only when Island asks.
- It does not sell or transfer it, does not use it for advertising, creditworthiness or any purpose that is not the single purpose, and no person reads it. Island, on the other side, keeps none of it in a file: only the things the person chooses to put on the island are kept.
- It stores one invented identifier in the browser's own storage so that several browsers can be told apart; it contains nothing about the person.
- It runs no code that arrives from outside: what Island sends is parsed as data (`ExtensionGuardTests.No_Eval_Or_Dynamic_Code`).

## What the owner decides (not stated by any page that was found)

- **UNVERIFIED — whether a program on the same computer counts as a "third party" in the store's sense.** No page that was opened addresses it. The add-on sends the data only to the person's own computer; whether the store's question about "sharing with third parties" is answered yes or no is the owner's decision.
- **UNVERIFIED — the exact words and the current list of the data-category boxes and of the certification boxes in the dashboard.** The pages describe them only generally, and the dashboard shows the live list after signing in. The owner ticks them against the facts above.
- A privacy policy is required if the product handles user data (the policy page says so): `ship/store/privacy.md` has the text, written for the app and the add-on together; it needs a place on the internet (the owner's page, step about hosting). The contact in it is `__OWNER__`.
