// Runs in the add-on's own world on the five media sites, after lib/sites.js and lib/media.js. It shapes what
// media-main.js reads, passes the changes to the service worker, and passes the island's commands back into
// the page. Anything the page posts is treated as untrusted: it is shaped and cut before it goes anywhere.
// It answers the worker's "are you alive?" ({ island: 'alive' }): only a live copy can, so a tab that was open before
// the add-on was reloaded (its old copy cannot reach the add-on any more) is found and given a new copy (background.js).
// It leaves a mark in its own world when it starts: a second copy that finds the mark of a live one does nothing (a tab that
// was loading, or slow to answer, when the worker asked). The mark of a copy that can no longer reach the add-on is not
// a copy: the new one takes its place.
(() => {
  'use strict';
  const TAG = '__island';
  const MARK = Symbol.for('island.media-relay');
  const COMMAND_WAIT_MS = 1000;

  /** False in a copy that was left behind by a reload of the add-on: it cannot reach the add-on, and then stays quiet. */
  function connected() {
    try {
      return typeof chrome !== 'undefined' && !!chrome.runtime && !!chrome.runtime.id;
    } catch {
      return false;
    }
  }

  const earlier = globalThis[MARK];
  try {
    if (earlier && typeof earlier.live === 'function' && earlier.live() === true) return;
  } catch {
    // a mark that cannot say it is live is not live
  }

  try {
    Object.defineProperty(globalThis, MARK, { value: { live: connected }, configurable: true, writable: true, enumerable: false });
  } catch {
    return;
  }

  const M = globalThis.IslandMedia;
  const host = globalThis.IslandSites.normalizeHost(location.hostname);
  const gate = M.createMediaGate();
  const waiting = new Map();
  let last = null;
  let nonce = 0;

  function toWorker(message) {
    if (!connected()) return;
    try {
      chrome.runtime.sendMessage(message).catch(() => {});
    } catch {
      // The add-on was reloaded after the check above: nothing to talk to.
    }
  }

  window.addEventListener('message', (e) => {
    if (!connected() || e.source !== window || !e.data) return;
    if (e.data[TAG] === 'state') {
      const shaped = M.shapeMedia(e.data.raw);
      if (shaped) last = shaped;
      if (gate.shouldSend(shaped, Date.now())) toWorker({ island: 'media', media: shaped });
    } else if (e.data[TAG] === 'done' && waiting.has(e.data.nonce)) {
      waiting.get(e.data.nonce)(e.data.ok === true);
      waiting.delete(e.data.nonce);
    }
  });

  chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
    if (!connected()) return false; // left behind by a reload: it answers nothing and acts on nothing
    if (msg && msg.island === 'alive') {
      reply({ alive: true });
      return false;
    }
    if (!msg || msg.island !== 'command') return false;
    const plan = M.controlPlan(msg.command, last !== null && last.state === 'playing');
    if (plan.length === 0) {
      reply({ ok: false });
      return false;
    }
    const id = ++nonce;
    const timer = setTimeout(() => {
      waiting.delete(id);
      reply({ ok: false });
    }, COMMAND_WAIT_MS);
    waiting.set(id, (ok) => {
      clearTimeout(timer);
      reply({ ok });
    });
    window.postMessage({ [TAG]: 'command', nonce: id, plan, selectors: M.buttonsFor(host, msg.command) }, location.origin);
    return true; // the answer comes later
  });
})();
