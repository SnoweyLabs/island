// WORK-ORDER-9 section 1: tabs that were already open. The service worker against a pretend browser (fake chrome.* APIs, a fake
// WebSocket) inside a Node vm context; and the relay script's "are you alive?" answer and its quiet when its add-on was reloaded.
// No browser is started. Whether Chrome behaves like the fake is not proven here.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');

const ROOT = path.join(__dirname, '..');
const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, 'manifest.json'), 'utf8'));
const flush = () => new Promise((r) => setImmediate(r));

function event() {
  const listeners = [];
  return { addListener: (f) => listeners.push(f), fire: (...args) => listeners.map((f) => f(...args)), listeners };
}

/**
 * `answers` says what each tab's relay does when asked "alive?": 'live' answers, 'none' has no receiver (a tab with no copy, or only a
 * copy left over from before a reload), 'late' answers after `lateMs`, 'never' does not answer. `refuses` lists tab ids that cannot take a script.
 */
function pretendBrowser(tabs, { answers = {}, refuses = [], hangs = [], lateMs = 0 } = {}) {
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
    runtime: { id: 'self', getURL: (p) => 'chrome-extension://self' + p, getManifest: () => manifest, onMessage: event(), onStartup: event(), onInstalled: event() },
    tabs: {
      query: async () => tabs,
      get: async (id) => tabs.find((t) => t.id === id) || Promise.reject(new Error('no tab')),
      sendMessage: (id, msg) => {
        asked.push([id, msg]);
        const how = answers[id] || 'none';
        if (how === 'live') return Promise.resolve({ alive: true, version: manifest.version });
        if (how === 'late') return new Promise((r) => setTimeout(() => r({ alive: true, version: manifest.version }), lateMs));
        if (how === 'never') return new Promise(() => {});
        return Promise.reject(new Error('Could not establish connection. Receiving end does not exist.'));
      },
      onCreated: event(),
      onUpdated: event(),
      onRemoved: event(),
      onActivated: event(),
      onReplaced: event(),
      onAttached: event(),
    },
    scripting: {
      executeScript: async (injection) => {
        if (refuses.includes(injection.target.tabId)) throw new Error('Cannot access contents of the page.');
        if (hangs.includes(injection.target.tabId)) return new Promise(() => {});
        injected.push([injection.target.tabId, injection.world, [...injection.files]]);
        return [{ frameId: 0 }];
      },
    },
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
  context.importScripts = (...files) => files.forEach((f) => vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), context, { filename: f }));
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path.join(ROOT, 'background.js'), 'utf8'), context, { filename: 'background.js' });
  return { chrome, asked, injected };
}

const tab = (id, url, o = {}) => ({ id, windowId: 1, title: 'Tab ' + id, url, audible: false, active: false, status: 'complete', ...o });
const MAIN_FILES = ['content/media-main.js'];
const ISOLATED_FILES = ['lib/sites.js', 'lib/media.js', 'content/media-relay.js'];

test('a tab with a live relay is left alone', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/watch?v=a')], { answers: { 1: 'live' } });
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual(JSON.parse(JSON.stringify(b.asked)), [[1, { island: 'alive' }]]);
  assert.deepEqual(b.injected, []);
});

test("a tab with no answer gets the manifest's files, in order, in both worlds", async () => {
  const b = pretendBrowser([tab(1, 'https://music.youtube.com/watch?v=a')]);
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual(b.injected, [
    [1, 'MAIN', MAIN_FILES],
    [1, 'ISOLATED', ISOLATED_FILES],
  ]);
  // The files come from the manifest, not from this test: a change there is a change here.
  assert.deepEqual(manifest.content_scripts.map((c) => c.js), [MAIN_FILES, ISOLATED_FILES]);
});

test('only tabs on the five sites are touched', async () => {
  const tabs = [
    tab(1, 'https://www.youtube.com/watch?v=a'),
    tab(2, 'https://example.org/'),
    tab(3, 'http://www.youtube.com/'),
    tab(4, 'chrome://extensions'),
    tab(5, 'https://www.twitch.tv/x', { discarded: true }),
    tab(6, 'https://soundcloud.com/a', { status: 'loading' }),
    tab(7, 'https://open.spotify.com/'),
    tab(8, 'https://notyoutube.com/'),
    tab(9, undefined),
    { id: 'x', windowId: 1, url: 'https://www.twitch.tv/y' },
  ];
  const b = pretendBrowser(tabs);
  b.chrome.runtime.onStartup.fire();
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))].sort(), [1, 7]);
  assert.deepEqual(b.asked.map((a) => a[0]).sort(), [1, 7], 'a tab that is not eligible is not even asked');
});

test('a tab that refuses is skipped and the walk goes on', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a'), tab(2, 'https://www.twitch.tv/b'), tab(3, 'https://soundcloud.com/c')], { refuses: [1] });
  b.chrome.runtime.onInstalled.fire({ reason: 'install' });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))].sort(), [2, 3]);
});

test('a tab that answers late is not given a second copy if it answers in time, and is if it does not', async () => {
  const quick = pretendBrowser([tab(1, 'https://www.youtube.com/a')], { answers: { 1: 'late' }, lateMs: 200 });
  quick.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 400));
  assert.deepEqual(quick.injected, [], 'an answer within the wait counts');

  const slow = pretendBrowser([tab(1, 'https://www.youtube.com/a'), tab(2, 'https://www.twitch.tv/b')], { answers: { 1: 'never', 2: 'live' } });
  slow.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 1800));
  assert.deepEqual([...new Set(slow.injected.map((i) => i[0]))], [1], 'no answer in 1.5 s means not alive; the other tab was not held up');
});

test('the walk runs on install, update and browser start, and at no other time', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a')], { answers: { 1: 'live' } });
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 0, 'loading the worker is not a moment');

  // Everything else the browser can say.
  b.chrome.tabs.onCreated.fire(tab(2, 'https://www.twitch.tv/b'));
  b.chrome.tabs.onUpdated.fire(1, { title: 'x', status: 'complete' }, tab(1, 'https://www.youtube.com/a'));
  b.chrome.tabs.onActivated.fire({ tabId: 1, windowId: 1 });
  b.chrome.tabs.onReplaced.fire(3, 1);
  b.chrome.tabs.onAttached.fire(1);
  b.chrome.alarms.onAlarm.fire({ name: 'island-redial' });
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 0, 'no timer, no tab event, no alarm walks the tabs');

  b.chrome.runtime.onInstalled.fire({ reason: 'install' });
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 1);
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 2);
  b.chrome.runtime.onStartup.fire();
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 3);
});

test('two moments close together make one walk', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a')], { answers: { 1: 'live' } });
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  b.chrome.runtime.onStartup.fire();
  await new Promise((r) => setTimeout(r, 20));
  assert.equal(b.asked.length, 1);
});

test('a discarded or a frozen tab is skipped', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a', { discarded: true }), tab(2, 'https://www.twitch.tv/b', { frozen: true }), tab(3, 'https://soundcloud.com/c')]);
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual([...new Set(b.injected.map((i) => i[0]))], [3]);
  assert.deepEqual(b.asked.map((a) => a[0]), [3], 'a sleeping tab is not even asked');
});

test("the worker's top-level code starts no walk", async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a')]);
  await new Promise((r) => setTimeout(r, 50));
  assert.deepEqual(b.asked, []);
  assert.deepEqual(b.injected, []);
});

test('a tab whose script never goes in does not hold the walk open: the next moment starts a new walk', async () => {
  const b = pretendBrowser([tab(1, 'https://www.youtube.com/a')], { hangs: [1] });
  b.chrome.runtime.onInstalled.fire({ reason: 'update' });
  await new Promise((r) => setTimeout(r, 1800));
  b.chrome.runtime.onStartup.fire();
  await new Promise((r) => setTimeout(r, 50));
  assert.equal(b.asked.length, 2);
});
