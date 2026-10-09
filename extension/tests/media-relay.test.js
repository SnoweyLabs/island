// WORK-ORDER-9 section 1: the relay script. It answers "are you alive?", stays quiet when its add-on was reloaded under it, and leaves a mark in its own
// world so that a second copy does nothing; the mark of a copy that can no longer reach the add-on is not a copy.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const ROOT = path.join(__dirname, '..');
const read = (f) => fs.readFileSync(path.join(ROOT, f), 'utf8');

/** One isolated world of a pretend tab; `run()` runs the relay in it as often as asked. */
function pretendWorld({ runtimeId = 'self' } = {}) {
  const runtimeListeners = [];
  const windowListeners = [];
  const sent = [];
  const chrome = {
    runtime: {
      id: runtimeId,
      sendMessage: (m) => {
        sent.push(JSON.parse(JSON.stringify(m)));
        return Promise.resolve();
      },
      onMessage: { addListener: (f) => runtimeListeners.push(f) },
    },
  };
  const context = { chrome, location: { hostname: 'www.youtube.com', origin: 'https://www.youtube.com' }, Date, setTimeout, clearTimeout };
  context.window = {
    addEventListener: (name, f) => name === 'message' && windowListeners.push(f),
    postMessage() {},
  };
  vm.createContext(context);
  for (const f of ['lib/sites.js', 'lib/media.js']) vm.runInContext(read(f), context, { filename: f });
  return {
    chrome,
    sent,
    run: () => vm.runInContext(read('content/media-relay.js'), context, { filename: 'media-relay.js' }),
    copies: () => runtimeListeners.length,
    ask: (msg) => {
      const answers = [];
      for (const l of runtimeListeners) l(msg, {}, (a) => answers.push(JSON.parse(JSON.stringify(a))));
      return answers;
    },
    pageSays: (data) => windowListeners.forEach((l) => l({ source: context.window, data })),
  };
}

test('a second relay in one world does nothing', () => {
  const w = pretendWorld();
  w.run();
  w.run();
  w.run();
  assert.equal(w.copies(), 1, 'one listener from the worker, one copy');
  assert.deepEqual(w.ask({ island: 'alive' }), [{ alive: true }], 'and one answer');
  w.pageSays({ __island: 'state', raw: { title: 'A', playbackState: 'playing', hasElement: false } });
  assert.equal(w.sent.filter((m) => m.island === 'media').length, 1, 'one report to the worker, not three');
});

test('the mark of a copy left behind by a reload is not a copy: the new one takes its place', () => {
  const w = pretendWorld();
  w.run();
  w.chrome.runtime.id = undefined; // the add-on was reloaded under the first copy
  w.run();
  assert.equal(w.copies(), 2, 'the new copy registered itself');
  w.chrome.runtime.id = 'self'; // (in a real reload the new copy has its own chrome; here one object stands for both)
  assert.deepEqual(w.ask({ island: 'alive' }), [{ alive: true }, { alive: true }].slice(0, 2));
});

test('a copy left behind by a reload answers nothing, acts on nothing, sends nothing and throws nothing', () => {
  const w = pretendWorld();
  w.run();
  w.chrome.runtime.id = undefined;
  w.chrome.runtime.sendMessage = () => {
    throw new Error('Extension context invalidated.');
  };
  assert.deepEqual(w.ask({ island: 'alive' }), []);
  assert.deepEqual(w.ask({ island: 'command', command: 'playpause' }), []);
  assert.doesNotThrow(() => w.pageSays({ __island: 'state', raw: { title: 'A', playbackState: 'playing', hasElement: false } }));
  assert.deepEqual(w.sent, []);
});

test('the relay answers "alive?" and only to that question', () => {
  const w = pretendWorld();
  w.run();
  assert.deepEqual(w.ask({ island: 'alive' }), [{ alive: true }]);
  assert.deepEqual(w.ask({ island: 'nothing' }), []);
  assert.deepEqual(w.ask(null), []);
});
