// Runs inside the media page's own world ("world": "MAIN"), from document_start, so it can see the page's
// navigator.mediaSession handlers and media elements. It reads; it acts only when the island asks through
// the relay. It talks to nothing but the relay script in the same tab (window.postMessage).
// None of the five sites was opened while this was written: UNVERIFIED on every one of them.
(() => {
  'use strict';
  const TAG = '__island';
  const REPORT_MS = 1000;
  const handlers = new Map();
  let lastPlayed = null;

  // Keep a copy of the page's own media-session handlers: pressing "next" through them is what the page expects.
  const session = navigator.mediaSession;
  if (session && typeof session.setActionHandler === 'function') {
    const original = session.setActionHandler.bind(session);
    session.setActionHandler = function (action, handler) {
      if (typeof handler === 'function') handlers.set(action, handler);
      else handlers.delete(action);
      return original(action, handler);
    };
  }

  // Some players never put their media element in the page; remember the one that played last.
  const play = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function () {
    lastPlayed = this;
    return play.apply(this, arguments);
  };

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
})();
