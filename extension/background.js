// The add-on's service worker. It tells the Island app on this computer which tabs are open, and carries out
// the app's commands: switch to a tab, press play/pause, next or previous in a media page. It dials
// 127.0.0.1 only (lib/protocol.js address()), fetches nothing from the internet, and never sends a full
// address or a page's contents: tabs leave as an id, a title and a host (extension/PROTOCOL.md).
importScripts('lib/sites.js', 'lib/protocol.js', 'lib/reconnect.js');

const P = globalThis.IslandProtocol;
const S = globalThis.IslandSites;
const R = globalThis.IslandReconnect;

const ALARM = 'island-redial';
const WELCOME_WAIT_MS = 3000;
const ALIVE_WAIT_MS = 1500;
const INJECT_WAIT_MS = 1500; // one tab whose page never lets a script in must not hold the walk open

const redialer = R.createRedialer();
const lastMedia = new Map(); // tab id -> what its page last reported; memory only
const iconSentFor = new Map(); // tab id -> "host|favIconUrl" of the icon that was sent
let socket = null; // open or being opened
let ready = false; // the island said welcome
let keepalive = null;

// ---- connection ---------------------------------------------------------------------------------------

/** Starts one attempt (the five ports in turn) unless one is under way, or one started less than 5 s ago. */
function dial() {
  if (!redialer.tryStart(Date.now(), socket !== null)) return;
  tryPort(0);
}

function tryPort(index) {
  let ws;
  try {
    ws = new WebSocket(P.address(P.PORTS[index]));
  } catch {
    return;
  }
  socket = ws;
  let opened = false;
  let welcomeTimer = null;

  ws.onopen = () => {
    opened = true;
    welcomeTimer = setTimeout(() => ws.close(), WELCOME_WAIT_MS); // something else is on this port
    profileId()
      .then((profile) => ws.send(P.build.hello(profile, browserName(), chrome.runtime.getManifest().version)))
      .catch(() => ws.close());
  };
  ws.onmessage = (e) => {
    const m = P.parseIsland(typeof e.data === 'string' ? e.data : '');
    if (m && m.type === 'welcome') clearTimeout(welcomeTimer);
    if (m) onIsland(m);
  };
  ws.onerror = () => {}; // quiet: the island is simply not running
  ws.onclose = () => {
    clearTimeout(welcomeTimer);
    if (socket !== ws) return;
    socket = null;
    ready = false;
    clearInterval(keepalive);
    if (!opened) {
      const next = R.nextPortIndex(index, P.PORTS.length);
      if (next !== null) tryPort(next);
    }
  };
}

function send(frame) {
  if (ready && socket && socket.readyState === WebSocket.OPEN) socket.send(frame);
}

function onIsland(m) {
  switch (m.type) {
    case 'welcome':
      ready = true;
      clearInterval(keepalive);
      // WebSocket traffic keeps the service worker awake (Chrome 116+); the island answers pong.
      keepalive = setInterval(() => send(P.build.ping()), P.KEEPALIVE_MS);
      sendSnapshot();
      break;
    case 'resync':
      sendSnapshot();
      break;
    case 'activate':
      chrome.tabs
        .update(m.id, { active: true })
        .then(() => chrome.windows.update(m.windowId, { focused: true }))
        .then(() => send(P.build.result('activate', m.id, true)))
        .catch(() => send(P.build.result('activate', m.id, false)));
      break;
    case 'close':
      chrome.tabs
        .remove(m.id)
        .then(() => send(P.build.result('close', m.id, true)))
        .catch(() => send(P.build.result('close', m.id, false)));
      break;
    case 'media-command':
      chrome.tabs
        .sendMessage(m.id, { island: 'command', command: m.command })
        .then((answer) => send(P.build.result('media-command', m.id, !!answer && answer.ok === true)))
        .catch(() => send(P.build.result('media-command', m.id, false)));
      break;
    default:
      break; // pong
  }
}

async function profileId() {
  const stored = await chrome.storage.local.get('islandProfile');
  if (P.isProfileId(stored.islandProfile)) return stored.islandProfile;
  const id = P.newProfileId((n) => crypto.getRandomValues(new Uint8Array(n)));
  await chrome.storage.local.set({ islandProfile: id });
  return id;
}

function browserName() {
  const ua = navigator.userAgent;
  if (/\bEdg\//.test(ua)) return 'edge';
  if (/\bOPR\//.test(ua)) return 'opera';
  if (navigator.brave) return 'brave';
  return 'chrome';
}

// ---- what is sent -------------------------------------------------------------------------------------

async function sendSnapshot() {
  let tabs;
  try {
    tabs = await chrome.tabs.query({});
  } catch {
    return;
  }
  send(P.build.snapshot(tabs));
  iconSentFor.clear();
  for (const tab of tabs.filter(P.hasUsableId)) {
    sendIcon(tab);
    if (lastMedia.has(tab.id)) send(P.build.media(tab.id, lastMedia.get(tab.id)));
  }
}

/** The browser's own stored icon for the page, from its favicon cache; never asked of any website. */
function faviconUrl(pageUrl) {
  const u = new URL(chrome.runtime.getURL('/_favicon/'));
  u.searchParams.set('pageUrl', pageUrl);
  u.searchParams.set('size', '32');
  return u.toString();
}

async function sendIcon(tab) {
  const host = S.hostOf(tab.url || '');
  // A tab with no favIconUrl has no icon yet (still loading): the store would hand back its plain globe, which
  // must not be sent. The icon is sent again when the tab reports a new favIconUrl.
  if (!host || !ready || !tab.favIconUrl) return;
  const sentKey = host + '|' + tab.favIconUrl;
  if (iconSentFor.get(tab.id) === sentKey) return;
  iconSentFor.set(tab.id, sentKey);
  try {
    const response = await fetch(faviconUrl(tab.url));
    const png = P.iconBase64(new Uint8Array(await response.arrayBuffer()));
    if (png) send(P.build.icon(tab.id, png));
  } catch {
    // No stored icon: the island shows the two-letter tile.
  }
}

// ---- browser events: each one also redials when the island is not connected --------------------------

chrome.tabs.onCreated.addListener((tab) => {
  dial();
  if (P.hasUsableId(tab)) send(P.build.tab(tab));
});

chrome.tabs.onUpdated.addListener((id, change, tab) => {
  dial();
  if (!P.hasUsableId(tab)) return;
  if ('url' in change) lastMedia.delete(id);
  if ('title' in change || 'url' in change || 'audible' in change || 'pinned' in change || 'status' in change) send(P.build.tab(tab));
  if ('favIconUrl' in change || change.status === 'complete') sendIcon(tab);
});

chrome.tabs.onRemoved.addListener((id) => {
  dial();
  lastMedia.delete(id);
  iconSentFor.delete(id);
  send(P.build.tabRemoved(id));
});

chrome.tabs.onActivated.addListener(({ tabId, windowId }) => {
  dial();
  send(P.build.tabActivated(tabId, windowId));
});

chrome.tabs.onReplaced.addListener((addedId, removedId) => {
  lastMedia.delete(removedId);
  iconSentFor.delete(removedId);
  send(P.build.tabRemoved(removedId));
  chrome.tabs.get(addedId).then((tab) => {
    send(P.build.tab(tab));
    sendIcon(tab);
  }, () => {});
});

chrome.tabs.onAttached.addListener((id) => {
  chrome.tabs.get(id).then((tab) => send(P.build.tab(tab)), () => {});
});

// What a media page is playing, from content/media-relay.js on the five media sites only.
chrome.runtime.onMessage.addListener((msg, sender) => {
  if (!msg || msg.island !== 'media' || sender.id !== chrome.runtime.id || !sender.tab || !P.hasUsableId(sender.tab)) return;
  if (!S.isMediaHost(S.hostOf(sender.tab.url || ''))) return;
  const media = P.cleanMedia(msg.media);
  if (!media) return;
  lastMedia.set(sender.tab.id, media);
  send(P.build.media(sender.tab.id, media));
});

// ---- tabs that were already open ----------------------------------------------------------------------
// A tab that was open before the add-on was installed, updated or reloaded has none of the add-on's scripts in it (or has
// a copy that can no longer reach the add-on). Once, at those moments and when the browser starts, each open tab on the five
// media sites is asked whether the relay script is alive in it; a tab that does not answer is given the files the manifest
// lists for these sites, in the same two worlds and the same order. Files of the add-on only: never text, never from outside.

let walk = null; // the walk under way, so two moments close together make one walk

function wakeTabs() {
  if (!walk) {
    walk = walkTabs()
      .catch(() => {})
      .finally(() => {
        walk = null;
      });
  }
  return walk;
}

async function walkTabs() {
  let tabs;
  try {
    tabs = await chrome.tabs.query({});
  } catch {
    return;
  }
  await Promise.allSettled(tabs.filter(canTakeScripts).map((tab) => within(giveScripts(tab), INJECT_WAIT_MS)));
}

/** The promise's own outcome, or a refusal after `ms`: a tab that never answers is given up on, the rest are not held up. */
function within(promise, ms) {
  let timer;
  const gaveUp = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error('gave up')), ms);
  });
  return Promise.race([promise, gaveUp]).finally(() => clearTimeout(timer));
}

/** A tab on a media site, loaded, awake. One that is still loading gets the manifest's scripts by itself when its page starts. */
function canTakeScripts(tab) {
  return (
    P.hasUsableId(tab) &&
    !tab.discarded &&
    !tab.frozen &&
    tab.status !== 'loading' &&
    S.isMediaHost(S.hostOf(tab.url || '')) &&
    new URL(tab.url).protocol === 'https:' // the manifest's patterns are https only
  );
}

async function giveScripts(tab) {
  if (await relayAlive(tab.id)) return;
  for (const entry of chrome.runtime.getManifest().content_scripts || []) {
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: entry.js, world: entry.world === 'MAIN' ? 'MAIN' : 'ISOLATED' });
  }
}

/** Only a live relay script answers; no receiver, an error or no answer in time all mean "not alive". */
function relayAlive(tabId) {
  let timer;
  const answer = chrome.tabs.sendMessage(tabId, { island: 'alive' }).then(
    (a) => !!a && a.alive === true,
    () => false,
  );
  const late = new Promise((resolve) => {
    timer = setTimeout(() => resolve(false), ALIVE_WAIT_MS);
  });
  return Promise.race([answer, late]).finally(() => clearTimeout(timer));
}

// ---- waking up ----------------------------------------------------------------------------------------

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === ALARM) dial();
});
chrome.alarms
  .get(ALARM)
  .then((existing) => existing || chrome.alarms.create(ALARM, { periodInMinutes: R.ALARM_MINUTES }))
  .catch(() => {});
chrome.runtime.onStartup.addListener(() => {
  dial();
  wakeTabs();
});
chrome.runtime.onInstalled.addListener(() => {
  dial();
  wakeTabs();
});
dial();
