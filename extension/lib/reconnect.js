// When to dial the island again (extension/PROTOCOL.md "Reconnect"): on tab events and on a 30-second alarm,
// but never more than one attempt per 5 seconds, and never while a connection is open or being made.
// One attempt tries the five ports in turn. Classic script (see sites.js).
(function (root) {
  'use strict';

  const MIN_GAP_MS = 5000;
  const ALARM_MINUTES = 0.5;

  function createRedialer(minGapMs = MIN_GAP_MS) {
    let last = -Infinity;
    return {
      /** True (and remembered) when a new attempt may start now. */
      tryStart(now, busy) {
        if (busy || now - last < minGapMs) return false;
        last = now;
        return true;
      },
    };
  }

  /** The port to try after `index` in one attempt, or null when the attempt has tried them all. */
  function nextPortIndex(index, portCount) {
    return index + 1 < portCount ? index + 1 : null;
  }

  const api = { MIN_GAP_MS, ALARM_MINUTES, createRedialer, nextPortIndex };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.IslandReconnect = api;
})(globalThis);
