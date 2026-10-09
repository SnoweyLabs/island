// WORK-ORDER-9 section 1: the page-world script when a page may hold more than one copy of it (the add-on puts it into tabs that were already open).
// The first copy stays for the life of the page; a copy that finds the mark does nothing; a copy that finds no mark asks whether an older copy (add-on
// 1.0.0, which leaves no mark) is there by posting that script's own `command` message with an empty plan and a number, and does nothing more when it
// is answered. A pretend page inside a Node vm context whose postMessage is a queue, delivered in order, as the browser's is.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const SOURCE = fs.readFileSync(path.join(__dirname, '..', 'content', 'media-main.js'), 'utf8');
const MARK = Symbol.for('island.media-main');

/** The script of add-on 1.0.0 as the page holds it in tabs that were open before: it answers every `command` with a `done` carrying the same number. */
const OLDER_COPY = `
(() => {
  const TAG = '__island';
  window.addEventListener('message', (e) => {
    if (e.source !== window || !e.data || e.data[TAG] !== 'command' || !Array.isArray(e.data.plan)) return;
    window.postMessage({ [TAG]: 'done', nonce: e.data.nonce, ok: false }, location.origin);
  });
  setInterval(() => window.postMessage({ [TAG]: 'state', raw: {} }, location.origin), 1000);
})();`;

function pretendPage() {
  const queue = [];
  const listeners = [];
  const intervals = [];
  const window = {
    addEventListener: (name, f) => name === 'message' && listeners.push(f),
    removeEventListener: (name, f) => {
      const at = listeners.indexOf(f);
      if (name === 'message' && at >= 0) listeners.splice(at, 1);
    },
    postMessage: (m) => queue.push(JSON.parse(JSON.stringify(m))),
  };
  const context = {
    window,
    document: { title: 'A page', addEventListener() {}, removeEventListener() {}, querySelectorAll: () => [] },
    navigator: { mediaSession: { metadata: null, playbackState: 'none', setActionHandler() {} } },
    HTMLMediaElement: class {
      play() {}
    },
    location: { origin: 'https://www.youtube.com' },
    Date,
    setInterval: (f) => intervals.push(f),
    clearInterval() {},
    setTimeout: () => 0,
  };
  vm.createContext(context);
  const flush = () => {
    while (queue.length) {
      const data = queue.shift();
      for (const f of [...listeners]) f({ source: window, data });
    }
  };
  return {
    window,
    intervals,
    flush,
    posted: queue,
    run: (code = SOURCE) => vm.runInContext(code, context),
    commandListeners: () => listeners.length,
  };
}

test('a copy that finds the mark does nothing', () => {
  const p = pretendPage();
  p.run();
  p.flush();
  assert.equal(p.intervals.length, 1, 'the first copy started');
  const listeners = p.commandListeners();
  p.run();
  p.flush();
  p.run();
  p.flush();
  assert.equal(p.intervals.length, 1, 'no second reporter');
  assert.equal(p.commandListeners(), listeners, 'no second set of listeners');
  assert.equal(p.posted.length, 0);
});

test('a copy that gets an answer from an unmarked older copy does nothing more', () => {
  const p = pretendPage();
  p.run(OLDER_COPY);
  assert.equal(p.intervals.length, 1, 'the older copy reports');
  p.run();
  p.flush();
  assert.equal(p.intervals.length, 1, 'the new copy started no reporter of its own: the older one keeps the page');
  assert.equal(p.window[MARK], true, 'the new copy keeps its mark, so a third copy stays out too');
  p.run();
  p.flush();
  assert.equal(p.intervals.length, 1);
});

test('a copy that gets no answer starts, after the two messages it posted to itself came back', () => {
  const p = pretendPage();
  p.run();
  assert.equal(p.intervals.length, 0, 'nothing started before the page had its turn');
  p.flush();
  assert.equal(p.intervals.length, 1);
});

test('the empty-plan command it posts is the older script\'s own shape, and carries a number of its own', () => {
  const p = pretendPage();
  p.run();
  const [command, second] = p.posted;
  assert.equal(command.__island, 'command');
  assert.deepEqual(command.plan, []);
  assert.ok(Number.isFinite(command.nonce) && command.nonce > 2 ** 30, 'a number, not a guessable small one');
  assert.equal(second.nonce, command.nonce);
});

test('a mark that says nothing is no mark, and a page that fixed the mark keeps the script out without an error', () => {
  for (const forged of [0, '', null, undefined, false]) {
    const p = pretendPage();
    p.window[MARK] = forged;
    p.run();
    p.flush();
    assert.equal(p.intervals.length, 1, String(forged));
  }
  const fixed = pretendPage();
  fixed.run("Object.defineProperty(window, Symbol.for('island.media-main'), { value: 1, configurable: false })");
  assert.doesNotThrow(() => fixed.run());
  fixed.flush();
  assert.equal(fixed.intervals.length, 0);
});
