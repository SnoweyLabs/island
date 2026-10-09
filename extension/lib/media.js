// What a media page is playing, shaped for the protocol's "media" frame, and how a command is carried out.
// Re-implemented from the idea of WebNowPlaying (MIT, per-site media scripts); no code was copied, and none
// of the five sites was opened while this was written: the button selectors below are UNVERIFIED guesses.
// Classic script (see sites.js).
(function (root) {
  'use strict';

  const MAX_TEXT = 200;
  const MIN_GAP_MS = 500; // at most twice a second per tab
  const HEARTBEAT_MS = 5000; // while playing, the position is refreshed this often
  const SEEK_SLACK_S = 2; // a position this far from where it should be is a jump

  // Last resort, after the page's own media-session handlers and the media element. UNVERIFIED.
  const BUTTONS = {
    'youtube.com': { playpause: ['.ytp-play-button'], next: ['.ytp-next-button'], previous: ['.ytp-prev-button'] },
    'music.youtube.com': { playpause: ['#play-pause-button'], next: ['.next-button'], previous: ['.previous-button'] },
    'twitch.tv': { playpause: ['[data-a-target="player-play-pause-button"]'], next: [], previous: [] },
    'soundcloud.com': { playpause: ['.playControls__play'], next: ['.playControls__next'], previous: ['.playControls__prev'] },
    'open.spotify.com': {
      playpause: ['[data-testid="control-button-playpause"]'],
      next: ['[data-testid="control-button-skip-forward"]'],
      previous: ['[data-testid="control-button-skip-back"]'],
    },
  };

  function text(value) {
    if (typeof value !== 'string') return null;
    const t = Array.from(value.trim()).slice(0, MAX_TEXT).join('');
    return t === '' ? null : t;
  }

  function seconds(value) {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1e9 ? Math.round(value * 10) / 10 : null;
  }

  /**
   * `raw` is what the page's own script read: metadata title and artist, navigator.mediaSession.playbackState,
   * and, when the page has a media element, whether it is paused or ended and its position and length.
   * Returns null when the page has nothing to report.
   */
  function shapeMedia(raw) {
    if (!raw || typeof raw !== 'object') return null;
    const title = text(raw.title);
    const artist = text(raw.artist);
    let state;
    if (raw.hasElement) state = raw.ended ? 'stopped' : raw.paused ? 'paused' : 'playing';
    else if (raw.playbackState === 'playing' || raw.playbackState === 'paused') state = raw.playbackState;
    else if (title === null) return null;
    else state = 'stopped';
    const shaped = { title, artist, state, position: seconds(raw.position), length: seconds(raw.length) };
    // Where the position will be between two reports: the player's speed, and when the position was read. Only added when the page gave them.
    const speed = rate(raw.rate);
    const readAt = readTime(raw.readAt);
    if (speed !== null) shaped.rate = speed;
    if (readAt !== null) shaped.readAt = readAt;
    return shaped;
  }

  /** The playing speed of the page's player: a number from 0 to 16, else null. */
  function rate(value) {
    return typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 16 ? Math.round(value * 100) / 100 : null;
  }

  /** The moment the position was read, in milliseconds since 1970 UTC: a positive whole number, else null. */
  function readTime(value) {
    return typeof value === 'number' && Number.isFinite(value) && value > 0 && value < 253402300799000 ? Math.round(value) : null;
  }

  /** Decides which shaped reports go to the island: changes only, at most twice a second, a heartbeat while playing. */
  function createMediaGate() {
    let sent = null;
    let sentAt = -Infinity;
    return {
      shouldSend(next, now) {
        if (next === null) return false;
        if (now - sentAt < MIN_GAP_MS) return false;
        const changed =
          sent === null ||
          next.title !== sent.title ||
          next.artist !== sent.artist ||
          next.state !== sent.state ||
          next.length !== sent.length ||
          next.rate !== sent.rate ||
          positionJumped(sent, next, now - sentAt) ||
          (next.state === 'playing' && now - sentAt >= HEARTBEAT_MS);
        if (changed) {
          sent = next;
          sentAt = now;
        }
        return changed;
      },
    };
  }

  function positionJumped(before, after, elapsedMs) {
    if (before.position === null || after.position === null) return before.position !== after.position;
    const expected = before.state === 'playing' ? before.position + elapsedMs / 1000 : before.position;
    return Math.abs(after.position - expected) > SEEK_SLACK_S;
  }

  /**
   * The order in which a command is tried in the page: its own media-session handler, then the media
   * element (play/pause only: an element cannot skip a track), then a button on the page.
   */
  function controlPlan(command, playing) {
    switch (command) {
      case 'playpause':
        return [{ kind: 'handler', action: playing ? 'pause' : 'play' }, { kind: 'element' }, { kind: 'button' }];
      case 'next':
        return [{ kind: 'handler', action: 'nexttrack' }, { kind: 'button' }];
      case 'previous':
        return [{ kind: 'handler', action: 'previoustrack' }, { kind: 'button' }];
      default:
        return [];
    }
  }

  function buttonsFor(host, command) {
    const site = BUTTONS[host];
    return site && site[command] ? site[command].slice() : [];
  }

  const api = { MIN_GAP_MS, HEARTBEAT_MS, shapeMedia, createMediaGate, controlPlan, buttonsFor, BUTTONS };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.IslandMedia = api;
})(globalThis);
