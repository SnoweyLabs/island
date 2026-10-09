// ATTACK9 (WORK-ORDER-9 section 4, item 1): an adversarial pass over the add-on's walk over open tabs, its two content scripts and the guards around them.
// Naming: a test called Defect_<what> FAILS because of the defect it names (a failing test is the report; the code under test was not touched).
// A test called Holds_<what> passes and is the coverage statement. Everything runs against a pretend browser and a pretend page in Node vm contexts;
// no browser is started. Names are invented ("example.org"); nothing here comes from the computer it was written on.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const ROOT = path.join(__dirname, '..');
const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, 'manifest.json'), 'utf8'));
const read = (f) => fs.readFileSync(path.join(ROOT, f), 'utf8');
const BACKGROUND = read('background.js');
const MAIN_SRC = read('content/media-main.js');
const RELAY_SRC = read('content/media-relay.js');
const LEGACY_SRC = fs.readFileSync(path.join(__dirname, 'attack9-legacy-media-main.js'), 'utf8'); // the 1.0.0 page script, as git holds it
const MARK = Symbol.for('island.media-main');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const unhandled = [];
process.on('unhandledRejection', (e) => unhandled.push(e));

// ======================================================================================================================================
// 1. The walk
// ======================================================================================================================================

function event() {
  const listeners = [];
  return { addListener: (f) => listeners.push(f), fire: (...args) => listeners.map((f) => f(...args)), listeners };
}

/**
 * A pretend browser around background.js. Every behaviour is a function so a test can make a call reject, throw at once, hang or answer late.
 * `onAsk(id, msg)` is what a tab's relay does with a message (returns a promise or throws); `onExecute(injection)` is what the browser does with a script.
 */
function pretend(tabs, { onAsk, onExecute, query, scripting, getManifest } = {}) {
  const asked = [];
  const injected = [];
  class FakeSocket {
    static OPEN = 1;
    constructor() {
      this.readyState = 0;
    }
    send() {}
    close() {}
  }
  const chrome = {
    runtime: { id: 'self', getURL: (p) => 'chrome-extension://self' + p, getManifest: getManifest || (() => manifest), onMessage: event(), onStartup: event(), onInstalled: event() },
    tabs: {
      query: query || (async () => tabs),
      get: async () => Promise.reject(new Error('no tab')),
      sendMessage: (id, msg) => {
        asked.push(id);
        return onAsk ? onAsk(id, msg) : Promise.reject(new Error('Receiving end does not exist.'));
      },
      onCreated: event(),
      onUpdated: event(),
      onRemoved: event(),
      onActivated: event(),
      onReplaced: event(),
      onAttached: event(),
    },
    scripting:
      scripting === undefined
        ? {
            executeScript: async (injection) => {
              if (onExecute) await onExecute(injection);
              injected.push([injection.target.tabId, injection.world, [...injection.files]]);
              return [{ frameId: 0 }];
            },
          }
        : scripting,
    windows: { update: async () => {} },
    storage: { local: { get: async () => ({}), set: async () => {} } },
    alarms: { get: async () => undefined, create: async () => {}, onAlarm: event() },
  };
  const context = {
    chrome,
    WebSocket: FakeSocket,
    URL,
    navigator: { userAgent: 'Mozilla/5.0 Chrome/154.0.0.0' },
    crypto: webcrypto,
    btoa,
    Uint8Array,
    setTimeout,
    clearTimeout,
    setInterval: () => 0,
    clearInterval: () => {},
    fetch: async () => ({ arrayBuffer: async () => new ArrayBuffer(0) }),
  };
  context.importScripts = (...files) => files.forEach((f) => vm.runInContext(read(f), context, { filename: f }));
  vm.createContext(context);
  vm.runInContext(BACKGROUND, context, { filename: 'background.js' });
  return {
    chrome,
    asked,
    injected,
    install: () => chrome.runtime.onInstalled.fire({ reason: 'update' }),
    start: () => chrome.runtime.onStartup.fire(),
    /** The walk under way (a promise), or null. */
    walk: () => vm.runInContext('walk', context),
  };
}

const tab = (id, url, o = {}) => ({ id, windowId: 1, title: 'Tab ' + id, url, audible: false, active: false, status: 'complete', ...o });
const MEDIA_URL = 'https://www.youtube.com/watch?v=a';
const settle = async (b) => {
  await sleep(5);
  const w = b.walk();
  if (w) await w;
};

test('Holds_Walk_A_Thousand_Tabs_Mostly_Not_Eligible', async () => {
  const junk = [
    'https://example.org/',
    'http://www.youtube.com/', // not https
    'chrome://extensions',
    'about:blank',
    'file:///alpha.txt',
    'https://www.youtube.com.example.org/', // a lookalike host
    'https://example.org/www.youtube.com',
    'https://www.youtube.com@example.org/',
    'https://youtube.com.example.org/',
    'https://notyoutube.com/',
    'https://www.youтube.com/', // a letter that only looks like a t
    'https://example.org/' + 'a'.repeat(200000),
    undefined,
    '',
    42,
    {},
  ];
  const tabs = [];
  for (let i = 0; i < 1000; i++) tabs.push(i % 10 === 0 ? tab(i, 'https://music.youtube.com/watch?v=' + i) : tab(i, junk[i % junk.length]));
  const b = pretend(tabs);
  b.install();
  await settle(b);
  assert.equal(b.asked.length, 100, 'only the hundred eligible tabs are asked');
  assert.equal(b.injected.length, 200, 'both worlds, once each');
  assert.deepEqual(unhandled, []);
});

test('Holds_Walk_A_Thousand_Silent_Tabs_Costs_One_Wait_Not_A_Thousand', async () => {
  const tabs = Array.from({ length: 1000 }, (_, i) => tab(i, 'https://open.spotify.com/track/' + i));
  const b = pretend(tabs, { onAsk: () => new Promise(() => {}) });
  const t0 = Date.now();
  b.install();
  await settle(b);
  assert.ok(Date.now() - t0 < 6000, 'every tab waits at the same time');
  assert.equal(b.injected.length, 2000);
});

test('Holds_Walk_Odd_Tab_Records_Never_Stop_The_Walk', async () => {
  const tabs = [null, undefined, 7, 'x', { id: 1 }, { id: 1.5, windowId: 1, url: MEDIA_URL }, { id: -1, windowId: 1, url: MEDIA_URL }, tab(2, MEDIA_URL), { id: 3, windowId: 1, get url() { return MEDIA_URL; } }];
  const b = pretend(tabs);
  b.install();
  await settle(b);
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))].sort(), [2, 3]);
});

test('Holds_Walk_Calls_That_Throw_At_Once_Or_Reject_Are_Skipped_Quietly', async () => {
  const b = pretend([tab(1, MEDIA_URL), tab(2, MEDIA_URL), tab(3, MEDIA_URL), tab(4, MEDIA_URL)], {
    onAsk: (id) => {
      if (id === 1) throw new Error('sendMessage threw at once');
      return Promise.reject(new Error('no receiver'));
    },
    onExecute: (inj) => {
      if (inj.target.tabId === 2) throw new Error('executeScript threw at once');
      if (inj.target.tabId === 3) return Promise.reject(new Error('refused'));
    },
  });
  b.install();
  await settle(b);
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))], [4], 'the one good tab was still served');
  assert.deepEqual(unhandled, []);
});

test('Holds_Walk_Query_Or_Manifest_Or_Scripting_Failing_Ends_The_Walk_Without_An_Error', async () => {
  for (const variant of [
    { query: () => { throw new Error('query threw at once'); } },
    { query: () => Promise.reject(new Error('query rejected')) },
    { query: async () => undefined },
    { query: async () => 'not a list' },
    { scripting: undefined, getManifest: () => { throw new Error('no manifest'); } },
    { getManifest: () => ({}) },
  ]) {
    const b = pretend([tab(1, MEDIA_URL)], variant);
    assert.doesNotThrow(() => b.install());
    await settle(b);
    assert.equal(b.walk(), null, 'the walk lets go of its place');
  }
  const noScripting = pretend([tab(1, MEDIA_URL)], { scripting: null });
  noScripting.install();
  await settle(noScripting);
  assert.deepEqual(unhandled, []);
});

test('Holds_Walk_Moments_During_A_Walk_Make_No_Second_Walk_And_The_Next_One_After_Is_A_New_Walk', async () => {
  const b = pretend([tab(1, MEDIA_URL)], { onAsk: () => new Promise((r) => setTimeout(() => r({ alive: true }), 100)) });
  b.install();
  b.start();
  b.install();
  await settle(b);
  assert.equal(b.asked.length, 1);
  b.start();
  await settle(b);
  assert.equal(b.asked.length, 2);
});

test('Holds_Walk_A_Tab_That_Closes_Or_Moves_Away_Mid_Walk_Is_Skipped', async () => {
  // The tab is gone by the time the script goes in; the browser rejects; the others are served.
  const b = pretend([tab(1, MEDIA_URL), tab(2, 'https://www.twitch.tv/x')], {
    onExecute: async (inj) => {
      await sleep(10);
      if (inj.target.tabId === 1) throw new Error('No tab with id: 1');
    },
  });
  b.install();
  await settle(b);
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))], [2]);
});

test('Holds_Walk_An_Answer_After_The_Wait_Counts_As_No_Answer', async () => {
  // This is what lets a second relay into a tab (see Defect_Second_Relay_*): the live relay answers at 1.7 s, the wait is 1.5 s.
  const b = pretend([tab(1, MEDIA_URL)], { onAsk: () => new Promise((r) => setTimeout(() => r({ alive: true }), 1700)) });
  b.install();
  await settle(b);
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))], [1], 'the tab is given the scripts although a live relay was in it');
});

test('Holds_Walk_Alive_Answers_That_Are_Not_Alive_Are_Not_Believed', async () => {
  for (const answer of [undefined, null, {}, { alive: 'true' }, { alive: 1 }, 'alive', [true]]) {
    const b = pretend([tab(1, MEDIA_URL)], { onAsk: async () => answer });
    b.install();
    await settle(b);
    assert.equal(b.injected.length, 2, JSON.stringify(answer));
  }
});

test('Defect_Frozen_Tab_Is_Not_Skipped', async () => {
  // WORK-ORDER-9 section 1: "Tabs that Chrome marks as discarded or frozen are skipped." canTakeScripts() looks at `discarded` only.
  const b = pretend([tab(1, MEDIA_URL, { frozen: true }), tab(2, MEDIA_URL, { discarded: true })]);
  b.install();
  await settle(b);
  assert.deepEqual(b.asked, [], 'a frozen tab is not even asked, let alone given scripts');
  assert.deepEqual(b.injected, []);
});

test('Defect_Hung_Script_Injection_Holds_The_Walk_Open_For_Ever', async () => {
  // LATENT. giveScripts() has a time limit on the question (1.5 s) and none on executeScript(): one tab whose page never answers keeps `walk` set, and
  // every later install/start moment in the same worker is swallowed as "a walk is under way".
  const b = pretend([tab(1, MEDIA_URL)], { onExecute: () => new Promise(() => {}) });
  b.install();
  await sleep(1800);
  b.start();
  await sleep(1800);
  assert.equal(b.asked.length, 2, 'the second moment must start a new walk');
});

// ======================================================================================================================================
// 2. A pretend page: a page world and an add-on world on one window, joined by message events
// ======================================================================================================================================

function pretendPage() {
  const listeners = []; // { win, f }: message listeners of every world
  const queue = [];
  const sent = []; // what the relays handed to the worker
  const intervals = new Map();
  let nextTimer = 1;
  const flush = () => {
    while (queue.length) {
      const { data } = queue.shift();
      for (const l of [...listeners]) l.f({ source: l.win, data: JSON.parse(JSON.stringify(data)) });
    }
  };
  const makeWindow = () => {
    const win = {
      addEventListener: (name, f) => name === 'message' && listeners.push({ win, f }),
      removeEventListener: (name, f) => {
        const at = listeners.findIndex((l) => l.win === win && l.f === f);
        if (at >= 0 && name === 'message') listeners.splice(at, 1);
      },
      postMessage: (m) => queue.push({ data: m }),
    };
    return win;
  };

  // The page's own world.
  const element = {
    paused: false,
    ended: false,
    currentTime: 5,
    duration: 100,
    playbackRate: 1,
    isConnected: true,
    plays: 0,
    pauses: 0,
    play() {
      this.plays++;
      this.paused = false;
      return Promise.resolve();
    },
    pause() {
      this.pauses++;
      this.paused = true;
    },
  };
  class HTMLMediaElement {
    play() {
      return Promise.resolve();
    }
  }
  const registered = [];
  const mainWindow = makeWindow();
  const main = {
    window: mainWindow,
    document: { title: 'A page', addEventListener() {}, removeEventListener() {}, querySelectorAll: () => [element], querySelector: () => null },
    navigator: { mediaSession: { metadata: { title: 'Alpha song', artist: 'Beta' }, playbackState: 'playing', setActionHandler(a, h) { registered.push([a, h]); } } },
    HTMLMediaElement,
    location: { origin: 'https://www.youtube.com', hostname: 'www.youtube.com' },
    Date,
    Function,
    setInterval: (f) => {
      intervals.set(nextTimer, f);
      return nextTimer++;
    },
    clearInterval: (id) => intervals.delete(id),
    setTimeout: () => 0,
  };
  vm.createContext(main);

  // The add-on's world (relay): a chrome object that only knows what the relay uses.
  const relayListeners = [];
  const relayWorld = {
    chrome: {
      runtime: {
        id: 'self',
        sendMessage: (m) => {
          sent.push(JSON.parse(JSON.stringify(m)));
          return Promise.resolve();
        },
        onMessage: { addListener: (f) => relayListeners.push(f) },
      },
    },
    location: { hostname: 'www.youtube.com', origin: 'https://www.youtube.com' },
    Date,
    setTimeout: (f, ms) => setTimeout(f, ms).unref(),
    clearTimeout,
    window: makeWindow(),
  };
  vm.createContext(relayWorld);
  for (const f of ['lib/sites.js', 'lib/media.js']) vm.runInContext(read(f), relayWorld, { filename: f });

  const page = {
    element,
    sent,
    registered,
    mainWindow,
    relayWorld,
    intervals,
    flush,
    messageListeners: () => listeners.filter((l) => l.win === mainWindow).length,
    /** Runs a copy of the page script and lets the messages it posted to itself come back (the probe for a copy from before the mark). */
    runMain: () => {
      vm.runInContext(MAIN_SRC, main, { filename: 'media-main.js' });
      flush();
    },
    runMainRaw: () => vm.runInContext(MAIN_SRC, main, { filename: 'media-main.js' }),
    runLegacy: () => vm.runInContext(LEGACY_SRC, main, { filename: 'legacy-media-main.js' }),
    runRelay: () => vm.runInContext(RELAY_SRC, relayWorld, { filename: 'media-relay.js' }),
    inMain: (code) => vm.runInContext(code, main),
    /** One second of the page's life: every timer fires, and what was posted is delivered. */
    tick: () => {
      [...intervals.values()].forEach((f) => f());
      flush();
    },
    /** The worker sends the relays a message and collects what each answers. */
    ask: (msg) => {
      const replies = [];
      for (const l of relayListeners) l(msg, {}, (a) => replies.push(JSON.parse(JSON.stringify(a))));
      flush();
      return replies;
    },
    mediaSent: () => sent.filter((m) => m.island === 'media').length,
    relayCount: () => relayListeners.length,
  };
  return page;
}

// ---- two relays in one world

test('Defect_Second_Relay_In_One_World_Does_Not_Stand_Down', () => {
  // WORK-ORDER-9 section 1: "The relay sets a mark in its own world when it starts. A second copy that finds the mark does nothing." media-relay.js has no
  // mark. The walk puts a second copy into a tab whose live relay answered after 1.5 s (Holds_Walk_An_Answer_After_The_Wait_Counts_As_No_Answer).
  const p = pretendPage();
  p.runRelay();
  p.runRelay();
  assert.equal(p.relayCount(), 1, 'a second relay in one world registers no second listener');
});

test('Defect_Second_Relay_Doubles_Media_Reports', () => {
  const p = pretendPage();
  p.runMain();
  p.runRelay();
  p.runRelay();
  p.tick();
  assert.equal(p.mediaSent(), 1, 'one state from the page is one report to the worker, not two');
});

test('Defect_Second_Relay_Doubles_Every_Command_So_Play_Pause_Cancels_Itself', () => {
  const p = pretendPage();
  p.runMain();
  p.runRelay();
  p.runRelay();
  p.tick();
  p.ask({ island: 'command', command: 'playpause' });
  assert.equal(p.element.pauses + p.element.plays, 1, `the player was toggled ${p.element.pauses + p.element.plays} times by one press (pause ${p.element.pauses}, play ${p.element.plays})`);
});

test('Holds_One_Relay_One_Press_One_Toggle', () => {
  const p = pretendPage();
  p.runMain();
  p.runRelay();
  p.tick();
  const replies = p.ask({ island: 'command', command: 'playpause' });
  assert.equal(p.element.pauses + p.element.plays, 1);
  assert.deepEqual(replies, [{ ok: true }]);
});

// ---- an orphaned relay

test('Defect_Orphan_Relay_Still_Answers_Alive_And_Acts_On_Commands_If_A_Message_Reaches_It', () => {
  // LATENT, and the reach of an orphan is UNVERIFIED (WORK-ORDER-9 "GROUND TRUTH"). The relay checks connected() before it forwards anything, but its onMessage
  // handler answers "alive" and posts commands without asking: if a message did reach it, it would hide the tab from the walk and double the live relay's commands.
  const p = pretendPage();
  p.runMain();
  p.runRelay();
  p.relayWorld.chrome.runtime.id = undefined; // the add-on was reloaded under this copy
  const alive = p.ask({ island: 'alive' });
  assert.ok(!alive.some((a) => a.alive === true), 'an orphan must not say it is alive');
  p.tick();
  p.ask({ island: 'command', command: 'playpause' });
  assert.equal(p.element.pauses + p.element.plays, 0, 'an orphan must not act');
});

test('Holds_Orphan_Relay_Sends_Nothing_And_Throws_Nothing', () => {
  const p = pretendPage();
  p.runMain();
  p.runRelay();
  p.relayWorld.chrome.runtime.id = undefined;
  p.relayWorld.chrome.runtime.sendMessage = () => {
    throw new Error('Extension context invalidated.');
  };
  assert.doesNotThrow(() => p.tick());
  assert.equal(p.mediaSent(), 0);
  // And a live relay beside it, in the same world, is not hurt by the orphan's silence.
  const live = pretendPage();
  live.runMain();
  live.runRelay();
  live.tick();
  assert.equal(live.mediaSent(), 1);
});

// ---- what a page may say to the relay

test('Holds_Forged_Page_Messages_Are_Shaped_Or_Ignored', () => {
  const p = pretendPage();
  p.runRelay();
  const forged = [
    { __island: 'state', raw: { title: 'x'.repeat(5e6), artist: 'y'.repeat(5e6), playbackState: 'playing', hasElement: false } },
    { __island: 'state', raw: { title: 'T', hasElement: true, paused: false, position: NaN, length: -5, rate: 1e99, readAt: 'soon' } },
    { __island: 'state', raw: { title: { a: { b: { c: 1 } } }, artist: [1, 2], playbackState: {} } },
    { __island: 'state', raw: 'text' },
    { __island: 'state' },
    { __island: 'state', raw: null },
    { __island: 'done', nonce: 'nope', ok: true },
    { __island: 'done', nonce: -1 },
    { __island: 'command', nonce: 1, plan: [{ kind: 'button' }], selectors: ['body'] },
    { __island: 7 },
    'a string',
    null,
    42,
  ];
  for (const data of forged) {
    assert.doesNotThrow(() => {
      p.relayWorld.window.postMessage(data);
      p.flush();
    }, JSON.stringify(data).slice(0, 80));
  }
  for (const m of p.sent.filter((s) => s.island === 'media')) {
    assert.ok(Array.from(m.media.title || '').length <= 200 && Array.from(m.media.artist || '').length <= 200, 'text is cut');
  }
});

// ======================================================================================================================================
// 3. Copies of the page-world script (WORK-ORDER-9 version 2: the first copy stays for the life of the page; no versions, no taking over)
// ======================================================================================================================================

test('Holds_One_Copy_For_The_Life_Of_The_Page_However_Many_Run', () => {
  const p = pretendPage();
  for (let i = 0; i < 6; i++) assert.doesNotThrow(() => p.runMain());
  assert.equal(p.intervals.size, 1, 'one reporter');
  assert.equal(p.messageListeners(), 1, 'one command listener');
  p.runRelay();
  p.tick();
  assert.equal(p.mediaSent(), 1);
});

test('Holds_The_Copy_Starts_Only_After_Its_Two_Messages_Came_Back', () => {
  const p = pretendPage();
  p.runMainRaw();
  assert.equal(p.intervals.size, 0, 'nothing started before the page had its turn');
  p.flush();
  assert.equal(p.intervals.size, 1);
});

test('Holds_Legacy_Copy_Recognised_By_Its_Answer_Is_Left_Alone', () => {
  const p = pretendPage();
  p.runLegacy();
  p.runMain();
  p.runRelay();
  p.tick();
  p.ask({ island: 'command', command: 'playpause' });
  assert.equal(p.element.pauses + p.element.plays, 1, 'one working copy beside the unmarked one');
  assert.equal(p.intervals.size, 1, 'the new copy did not start a reporter of its own');
  assert.equal(p.mediaSent(), 1);
});

test('Defect_Legacy_Copy_Not_Recognised_When_The_Page_Wrapped_Play_After_It', () => {
  const p = pretendPage();
  p.runLegacy();
  p.inMain('(() => { const inner = HTMLMediaElement.prototype.play; HTMLMediaElement.prototype.play = function () { return inner.apply(this, arguments); }; })()');
  p.runMain();
  p.runRelay();
  p.tick();
  p.ask({ island: 'command', command: 'playpause' });
  assert.equal(p.element.pauses + p.element.plays, 1, `one press toggled the player ${p.element.pauses + p.element.plays} times`);
});

test('Defect_Pages_Own_Play_Wrapper_That_Mentions_lastPlayed_Silences_The_Script', () => {
  const p = pretendPage();
  p.inMain('HTMLMediaElement.prototype.play = function () { this.lastPlayed = Date.now(); return Promise.resolve(); }');
  p.runMain();
  assert.equal(p.intervals.size, 1, 'the script starts: no copy of ours was here');
});

test('Defect_Copy_Throws_Into_The_Page_And_Keeps_The_Mark_When_Play_Cannot_Be_Replaced', () => {
  const p = pretendPage();
  p.inMain("Object.defineProperty(HTMLMediaElement.prototype, 'play', { value: HTMLMediaElement.prototype.play, writable: false, configurable: false })");
  assert.doesNotThrow(() => p.runMain());
  assert.equal(p.intervals.size, 1, 'no error in the page, and the script went on without the wrapper');
});

test('Holds_Forged_Marks_Only_Silence_The_Page_They_Are_In', () => {
  for (const forged of [42, 'x', true, {}, { version: 7 }, [], () => 1]) {
    const p = pretendPage();
    p.mainWindow[MARK] = forged;
    assert.doesNotThrow(() => p.runMain());
    assert.equal(p.intervals.size, 0, 'a page can keep this script out of itself: ' + String(forged).slice(0, 20));
  }
  for (const forged of [0, '', null, undefined, false]) {
    const p = pretendPage();
    p.mainWindow[MARK] = forged;
    assert.doesNotThrow(() => p.runMain());
    assert.equal(p.intervals.size, 1, 'a mark that says nothing is no mark: ' + String(forged));
  }
  const fixed = pretendPage();
  fixed.inMain("Object.defineProperty(window, Symbol.for('island.media-main'), { value: 1, configurable: false })");
  assert.doesNotThrow(() => fixed.runMain());
});

test('Holds_A_Copy_Never_Answers_For_Another_Page_Nonce_Or_Throws_On_Odd_Probe_Messages', () => {
  const p = pretendPage();
  p.runMainRaw();
  // The page posts probe-like messages of its own, with other numbers and in other orders: nothing throws, and the copy still starts once.
  for (const data of [{ __island: 'done', nonce: 1 }, { __island: 'probe-2', nonce: 'x' }, { __island: 'probe-1' }, null, 5, { __island: 'done' }]) p.mainWindow.postMessage(data);
  assert.doesNotThrow(() => p.flush());
  assert.equal(p.intervals.size, 1);
});

// ======================================================================================================================================
// 4. The guards (extension/tests/guard.js, the rules of no_dynamic_code.test.js; the C# guard says the same): text that does what they exist to forbid
// ======================================================================================================================================

const { violations } = require('./guard.js');
const guardsSay = (raw) => violations('background.js', raw);

test('Holds_The_Real_Worker_Passes_The_Guard', () => {
  assert.deepEqual(guardsSay(BACKGROUND), []);
});

test('Holds_Guards_Catch_The_Plain_Ways', () => {
  for (const snippet of [
    "eval('1')",
    "new Function('return 1')",
    "Function('return 1')()",
    "setTimeout('x', 1)",
    "chrome.scripting.executeScript({ target: { tabId: 1 }, func: () => 1 })",
    "chrome.scripting.executeScript({ target: { tabId: 1 }, files: ['a.js'], args: [1] })",
    "chrome.scripting.executeScript({ target: { tabId: 1 }, code: 'x' })",
    "const { executeScript } = chrome.scripting;",
    "chrome.scripting['executeScript']({ files: ['a.js'] })",
    "import('https://example.org/x.js')",
  ]) {
    assert.notDeepEqual(guardsSay(BACKGROUND + '\n' + snippet), [], snippet);
  }
});

test('Holds_The_Guard_Allows_The_Call_Only_In_The_Worker', () => {
  assert.notDeepEqual(violations('content/media-relay.js', BACKGROUND), [], 'the worker text, in a content script, is refused');
});

const BYPASSES = {
  Defect_Guard_Misses_A_Computed_Name_For_ExecuteScript: "chrome.scripting['exec' + 'uteScript']({ target: { tabId: 1 }, func: () => 1 });",
  Defect_Guard_Misses_Options_Spread_Into_ExecuteScript: "chrome.scripting.executeScript({ target: { tabId: 1 }, files: entry.js, ...extra });",
  Defect_Guard_Misses_A_Namespace_Alias: "const s = chrome.scripting; s.executeScript({ target: { tabId: 1 }, files: entry.js });",
  Defect_Guard_Does_Not_Check_That_Files_Are_The_Manifests: "chrome.scripting.executeScript({ target: { tabId: 1 }, files: ['lib/protocol.js'], world: 'MAIN' });",
  Defect_Guard_Misses_Function_Called_Through_A_Global: "globalThis.Function('return 1')();",
  Defect_Guard_Misses_Indirect_Eval: "(0, eval)('1');",
  Defect_Guard_Misses_The_Constructor_Chain: "[].constructor.constructor('return 1')();",
  Defect_Guard_Does_Not_Mention_UserScripts: "chrome.userScripts.register([{ id: 'x', matches: ['https://example.org/*'], js: [{ code: 'x' }] }]);",
  Defect_Guard_Does_Not_Mention_RegisterContentScripts: "chrome.scripting.registerContentScripts([{ id: 'x', matches: ['https://example.org/*'], js: ['lib/protocol.js'] }]);",
};
for (const [name, snippet] of Object.entries(BYPASSES)) {
  test(name, () => {
    // These were Defect_ tests of the attack: the guard of the first version let them through. The guard is stricter now, and they pass.
    assert.notDeepEqual(guardsSay(BACKGROUND + '\n' + snippet), [], 'the guards pass this text: ' + snippet);
  });
}
