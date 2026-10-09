// The service worker against a pretend browser: fake chrome.* APIs and a fake WebSocket, inside a Node vm
// context. No browser is started and no socket is opened. Proves the wiring of background.js; whether Chrome
// itself behaves like the fake is not proven here (see the report).
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { webcrypto } = require('node:crypto');
const { examples } = require('./examples.js');

const ROOT = path.join(__dirname, '..');
const PNG = Buffer.from(JSON.parse(examples.icon).png, 'base64');
const flush = () => new Promise((r) => setImmediate(r));
// Objects made inside the vm have that realm's prototypes; a JSON copy makes them comparable here.
const record = (list, item) => list.push(JSON.parse(JSON.stringify(item)));

function event() {
  const listeners = [];
  return { addListener: (f) => listeners.push(f), fire: (...args) => listeners.map((f) => f(...args)), listeners };
}

function pretendBrowser(tabs) {
  let now = 1_000_000;
  const sockets = [];
  const calls = [];
  const fetched = [];
  const storage = {};

  class FakeSocket {
    static OPEN = 1;
    constructor(url) {
      this.url = url;
      this.readyState = 0;
      this.sent = [];
      sockets.push(this);
    }
    send(frame) {
      if (this.readyState !== 1) throw new Error('send before open');
      this.sent.push(JSON.parse(frame));
    }
    close() {
      this.readyState = 3;
      this.onclose?.();
    }
    // Driven by the test:
    open() {
      this.readyState = 1;
      this.onopen?.();
    }
    refuse() {
      this.onerror?.();
      this.readyState = 3;
      this.onclose?.();
    }
    receive(text) {
      this.onmessage?.({ data: text });
    }
    framesOfType(type) {
      return this.sent.filter((f) => f.type === type);
    }
  }

  class FakeDate extends Date {
    static now() {
      return now;
    }
  }

  const chrome = {
    runtime: {
      id: 'self',
      getURL: (p) => 'chrome-extension://self' + p,
      getManifest: () => ({ version: '0.1.0' }),
      onMessage: event(),
      onStartup: event(),
      onInstalled: event(),
    },
    tabs: {
      query: async () => tabs,
      get: async (id) => tabs.find((t) => t.id === id) || Promise.reject(new Error('no tab')),
      update: async (id, props) => {
        record(calls, ['tabs.update', id, props]);
        if (!tabs.some((t) => t.id === id)) throw new Error('no tab');
      },
      remove: async (id) => {
        record(calls, ['tabs.remove', id]);
        const at = tabs.findIndex((t) => t.id === id);
        if (at < 0) throw new Error('no tab');
        tabs.splice(at, 1);
      },
      sendMessage: async (id, msg) => {
        record(calls, ['tabs.sendMessage', id, msg]);
        if (!tabs.some((t) => t.id === id)) throw new Error('no receiver');
        return { ok: true };
      },
      onCreated: event(),
      onUpdated: event(),
      onRemoved: event(),
      onActivated: event(),
      onReplaced: event(),
      onAttached: event(),
    },
    windows: {
      update: async (id, props) => record(calls, ['windows.update', id, props]),
    },
    storage: {
      local: {
        get: async (key) => (key in storage ? { [key]: storage[key] } : {}),
        set: async (o) => Object.assign(storage, o),
      },
    },
    alarms: {
      created: [],
      get: async () => undefined,
      create: async (name, info) => record(chrome.alarms.created, [name, info]),
      onAlarm: event(),
    },
  };

  const context = {
    chrome,
    WebSocket: FakeSocket,
    Date: FakeDate,
    URL,
    navigator: { userAgent: 'Mozilla/5.0 Chrome/154.0.0.0' },
    crypto: webcrypto,
    btoa,
    Uint8Array,
    setTimeout,
    clearTimeout,
    setInterval: () => 0,
    clearInterval: () => {},
    fetch: async (url) => {
      fetched.push(url);
      return { arrayBuffer: async () => PNG.buffer.slice(PNG.byteOffset, PNG.byteOffset + PNG.length) };
    },
  };
  context.importScripts = (...files) => files.forEach((f) => vm.runInContext(fs.readFileSync(path.join(ROOT, f), 'utf8'), context, { filename: f }));
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(path.join(ROOT, 'background.js'), 'utf8'), context, { filename: 'background.js' });

  return {
    chrome,
    sockets,
    calls,
    fetched,
    storage,
    advance: (ms) => (now += ms),
    last: () => sockets[sockets.length - 1],
    async connect() {
      const ws = sockets[sockets.length - 1];
      ws.open();
      await flush();
      ws.receive(examples.welcome);
      await flush();
      await flush();
      return ws;
    },
  };
}

const tab = (id, url, o = {}) => ({ id, windowId: 1, title: 'Tab ' + id, url, audible: false, active: false, ...o });
const TABS = () => [
  tab(11, 'https://www.youtube.com/watch?v=secret', { active: true, favIconUrl: 'https://www.youtube.com/favicon.ico' }),
  tab(12, 'https://example.org/private?q=1', { favIconUrl: 'https://example.org/icon.png' }),
];

test('on start it dials 127.0.0.1 on the five ports in turn, then waits quietly', async () => {
  const b = pretendBrowser(TABS());
  assert.deepEqual(b.sockets.map((s) => s.url), ['ws://127.0.0.1:47653/island']);
  for (let i = 0; i < 5; i++) b.last().refuse();
  assert.deepEqual(
    b.sockets.map((s) => new URL(s.url).port),
    ['47653', '47654', '47655', '47656', '47657'],
  );
  b.chrome.tabs.onCreated.fire(tab(13, 'https://a.org/'));
  b.chrome.alarms.onAlarm.fire({ name: 'island-redial' });
  assert.equal(b.sockets.length, 5, 'no second attempt within 5 seconds');

  b.advance(5000);
  b.chrome.alarms.onAlarm.fire({ name: 'island-redial' });
  assert.equal(b.sockets.length, 6, 'the alarm starts the next attempt');
  b.advance(5000);
  b.chrome.tabs.onUpdated.fire(13, { title: 'x' }, tab(13, 'https://a.org/'));
  assert.equal(b.sockets.length, 6, 'no attempt while one is under way');
  await flush(); // the alarm is created after chrome.alarms.get answers
  assert.deepEqual(b.chrome.alarms.created, [['island-redial', { periodInMinutes: 0.5 }]]);
});

test('hello first, then after welcome a snapshot with hosts only and the stored icons', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();

  const [hello, snapshot] = ws.sent;
  assert.equal(hello.type, 'hello');
  assert.equal(hello.client, 'island-addon');
  assert.match(hello.profile, /^[0-9a-f]{24}$/);
  assert.equal(b.storage.islandProfile, hello.profile, 'the profile id is stored once');
  assert.equal(snapshot.type, 'snapshot');
  assert.deepEqual(snapshot.tabs.map((t) => t.host), ['youtube.com', 'example.org']);
  assert.ok(!JSON.stringify(ws.sent).includes('secret') && !JSON.stringify(ws.sent).includes('private'), 'no full address leaves');

  assert.deepEqual(ws.framesOfType('icon').map((f) => f.id), [11, 12]);
  assert.ok(b.fetched.every((u) => u.startsWith('chrome-extension://self/_favicon/?')), 'icons come from the browser itself');
});

test('an icon is sent only once the tab has one, and again when it changes (never the store\'s plain globe)', async () => {
  const b = pretendBrowser([]);
  const ws = await b.connect();
  const loading = tab(21, 'https://a.org/');
  b.chrome.tabs.onUpdated.fire(21, { status: 'complete' }, loading);
  await flush();
  assert.deepEqual(ws.framesOfType('icon'), [], 'no favIconUrl yet: no icon');

  const loaded = { ...loading, favIconUrl: 'https://a.org/one.png' };
  b.chrome.tabs.onUpdated.fire(21, { favIconUrl: loaded.favIconUrl }, loaded);
  b.chrome.tabs.onUpdated.fire(21, { status: 'complete' }, loaded);
  await flush();
  assert.equal(ws.framesOfType('icon').length, 1, 'sent once for the same icon');

  const changed = { ...loading, favIconUrl: 'https://a.org/two.png' };
  b.chrome.tabs.onUpdated.fire(21, { favIconUrl: changed.favIconUrl }, changed);
  await flush();
  assert.equal(ws.framesOfType('icon').length, 2, 'sent again for a new icon');
});

test('nothing is sent before the island says welcome', async () => {
  const b = pretendBrowser(TABS());
  const ws = b.last();
  ws.open();
  await flush();
  b.chrome.tabs.onCreated.fire(tab(13, 'https://a.org/'));
  assert.deepEqual(ws.sent.map((f) => f.type), ['hello']);
});

test('a socket that never says welcome is given up after 3 seconds', async () => {
  const b = pretendBrowser(TABS());
  const ws = b.last();
  ws.open();
  await flush();
  await new Promise((r) => setTimeout(r, 3100));
  assert.equal(ws.readyState, 3);
});

test('activate switches to the tab, focuses its window and answers', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  ws.receive(examples.activate);
  await flush();
  await flush();
  assert.deepEqual(b.calls, [
    ['tabs.update', 11, { active: true }],
    ['windows.update', 1, { focused: true }],
  ]);
  assert.deepEqual(ws.framesOfType('result').at(-1), { type: 'result', cmd: 'activate', id: 11, ok: true });

  ws.receive('{"type":"activate","id":99,"windowId":1}');
  await flush();
  await flush();
  assert.deepEqual(ws.framesOfType('result').at(-1), { type: 'result', cmd: 'activate', id: 99, ok: false });
});

test('a media command goes to the page of the tab and the answer comes back', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  ws.receive(examples['media-command']);
  await flush();
  await flush();
  assert.deepEqual(b.calls.at(-1), ['tabs.sendMessage', 11, { island: 'command', command: 'next' }]);
  assert.deepEqual(ws.framesOfType('result').at(-1), { type: 'result', cmd: 'media-command', id: 11, ok: true });
});

test('frames the island never sends are ignored', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  const before = ws.sent.length;
  for (const bad of ['{"type":"launch"}', 'not json', examples.hello, examples['bad-not-an-object']]) ws.receive(bad);
  await flush();
  assert.equal(ws.sent.length, before);
  assert.deepEqual(b.calls, []);
});

test('resync sends a fresh snapshot', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  ws.receive(examples.resync);
  await flush();
  assert.equal(ws.framesOfType('snapshot').length, 2);
});

test('tab events become protocol frames', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  b.chrome.tabs.onCreated.fire(tab(13, 'https://music.youtube.com/watch?v=x'));
  b.chrome.tabs.onUpdated.fire(13, { title: 'Mix' }, tab(13, 'https://music.youtube.com/watch?v=x', { title: 'Mix' }));
  b.chrome.tabs.onActivated.fire({ tabId: 13, windowId: 1 });
  b.chrome.tabs.onRemoved.fire(12, { windowId: 1, isWindowClosing: false });
  const frames = ws.sent.slice(-4);
  assert.deepEqual(frames.map((f) => f.type), ['tab', 'tab', 'tab-activated', 'tab-removed']);
  assert.equal(frames[1].tab.title, 'Mix');
  assert.equal(frames[1].tab.host, 'music.youtube.com');
});

test('a media report goes out only for a tab on a media site, and only when it is a proper report', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  const report = { title: 'Alpha', artist: null, state: 'playing', position: 1, length: 10 };
  b.chrome.runtime.onMessage.fire({ island: 'media', media: report }, { id: 'self', tab: TABS()[0] });
  b.chrome.runtime.onMessage.fire({ island: 'media', media: report }, { id: 'self', tab: TABS()[1] });
  b.chrome.runtime.onMessage.fire({ island: 'media', media: { ...report, state: 'x' } }, { id: 'self', tab: TABS()[0] });
  b.chrome.runtime.onMessage.fire({ island: 'media', media: report }, { id: 'someone-else', tab: TABS()[0] });
  assert.deepEqual(ws.framesOfType('media'), [{ type: 'media', id: 11, ...report }]);
});

test('when the island goes away the add-on stays quiet and dials again later', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  ws.close();
  b.chrome.tabs.onCreated.fire(tab(14, 'https://a.org/'));
  assert.equal(b.sockets.length, 1, 'within 5 seconds of the last attempt: no new one');
  b.advance(5000);
  b.chrome.tabs.onCreated.fire(tab(15, 'https://a.org/'));
  assert.equal(b.sockets.length, 2);
  assert.equal(b.last().url, 'ws://127.0.0.1:47653/island');
});

test('close closes the tab and answers; a tab that is gone answers no', async () => {
  const b = pretendBrowser(TABS());
  const ws = await b.connect();
  ws.receive(examples.close);
  await flush();
  await flush();
  assert.deepEqual(b.calls, [['tabs.remove', 11]]);
  assert.deepEqual(ws.framesOfType('result').at(-1), { type: 'result', cmd: 'close', id: 11, ok: true });

  ws.receive('{"type":"close","id":99}');
  await flush();
  await flush();
  assert.deepEqual(ws.framesOfType('result').at(-1), { type: 'result', cmd: 'close', id: 99, ok: false });
});
