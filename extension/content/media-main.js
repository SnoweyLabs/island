// Runs inside the media page's own world ("world": "MAIN"), from document_start, so it can see the page's
// navigator.mediaSession handlers and media elements. It reads; it acts only when the island asks through
// the relay. It talks to nothing but the relay script in the same tab (window.postMessage).
// None of the five sites was opened while this was written: UNVERIFIED on every one of them.
//
// The add-on may put this file into a tab that is already open (background.js), so a page can end up with more than one copy.
// The first copy stays for the life of the page: there are no versions and no taking over. A copy that starts leaves a mark in
// the page; a copy that finds the mark does nothing. A copy that finds no mark sets it and then asks whether a copy from before
// the mark existed (add-on 1.0.0, which leaves no mark) is in the page: it posts that script's own `command` message with an
// empty plan and a number of its own. That script answers every `command` with a `done` carrying the number, and goes on
// reporting with the page's handlers it collected. An answer: this copy does nothing more. No answer by the time two
// messages it posted to itself have come back (the answer, if there is one, is queued before the second): it starts.
(() => {
  'use strict';
  const MARK = Symbol.for('island.media-main');
  const TAG = '__island';
  const REPORT_MS = 1000;

  if (window[MARK]) return; // a copy is here already
  try {
    Object.defineProperty(window, MARK, { value: true, configurable: false, writable: false, enumerable: false });
  } catch {
    return; // a page that fixed the mark for good: this copy cannot take it, and says nothing
  }

  const handlers = new Map();
  let lastPlayed = null;

  // Keep a copy of the page's own media-session handlers: pressing "next" through them is what the page expects. Each
  // wrapper only passes calls through; a page that froze what is wrapped here leaves the wrapper out and the script goes on.
  const session = navigator.mediaSession;
  try {
    if (session && typeof session.setActionHandler === 'function') {
      const original = session.setActionHandler.bind(session);
      session.setActionHandler = function (action, handler) {
        if (typeof handler === 'function') handlers.set(action, handler);
        else handlers.delete(action);
        return original(action, handler);
      };
    }
  } catch {
    // not wrapped: "next" and "previous" then fall back on the page's buttons
  }

  // Some players never put their media element in the page; remember the one that played last.
  try {
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function () {
      lastPlayed = this;
      return play.apply(this, arguments);
    };
  } catch {
    // not wrapped: the element is found among the page's own
  }

  function element() {
    if (lastPlayed && (lastPlayed.isConnected || !lastPlayed.paused)) return lastPlayed;
    const all = Array.from(document.querySelectorAll('video, audio'));
    return all.find((m) => !m.paused) || all.sort((a, b) => (b.duration || 0) - (a.duration || 0))[0] || null;
  }

  function report() {
    const meta = session && session.metadata;
    const el = element();
    const raw = {
      title: meta ? meta.title : document.title,
      artist: meta ? meta.artist : null,
      playbackState: session ? session.playbackState : 'none',
      hasElement: el !== null,
      paused: el ? el.paused : true,
      ended: el ? el.ended : false,
      position: el ? el.currentTime : null,
      length: el ? el.duration : null,
      rate: el ? el.playbackRate : null,
      readAt: Date.now(),
    };
    window.postMessage({ [TAG]: 'state', raw }, location.origin);
  }

  function run(plan, selectors) {
    for (const step of plan) {
      if (step.kind === 'handler') {
        const h = handlers.get(step.action);
        if (!h) continue;
        try {
          h({ action: step.action });
          return true;
        } catch {
          continue;
        }
      }
      if (step.kind === 'element') {
        const el = element();
        if (!el) continue;
        if (el.paused) el.play().catch(() => {});
        else el.pause();
        return true;
      }
      if (step.kind === 'button') {
        for (const selector of selectors) {
          const button = document.querySelector(selector);
          if (button) {
            button.click();
            return true;
          }
        }
      }
    }
    return false;
  }

  function start() {
    window.addEventListener('message', (e) => {
      if (e.source !== window || !e.data || e.data[TAG] !== 'command' || !Array.isArray(e.data.plan)) return;
      const ok = run(e.data.plan, Array.isArray(e.data.selectors) ? e.data.selectors : []);
      window.postMessage({ [TAG]: 'done', nonce: e.data.nonce, ok }, location.origin);
      setTimeout(report, 300);
    });

    for (const name of ['play', 'pause', 'ended', 'loadedmetadata', 'durationchange', 'seeked', 'emptied']) {
      document.addEventListener(name, report, true); // media events do not bubble, but they can be caught on the way down
    }
    setInterval(report, REPORT_MS);
  }

  // Is a copy from before the mark in this page? Ask it in its own words (see the top of this file).
  const nonce = Math.floor(Math.random() * 2 ** 31) + 2 ** 31;
  let answered = false;
  function onProbe(e) {
    if (e.source !== window || !e.data || e.data.nonce !== nonce) return;
    if (e.data[TAG] === 'done') answered = true;
    else if (e.data[TAG] === 'probe-1') window.postMessage({ [TAG]: 'probe-2', nonce }, location.origin);
    else if (e.data[TAG] === 'probe-2') {
      window.removeEventListener('message', onProbe);
      if (!answered) start();
    }
  }
  window.addEventListener('message', onProbe);
  window.postMessage({ [TAG]: 'command', nonce, plan: [], selectors: [] }, location.origin);
  window.postMessage({ [TAG]: 'probe-1', nonce }, location.origin);
})();
