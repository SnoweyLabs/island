// The add-on's half of extension/PROTOCOL.md: the frames it builds and the frames it accepts from the island.
// Classic script (see sites.js).
(function (root) {
  'use strict';

  const sites = typeof module === 'object' && module.exports ? require('./sites.js') : root.IslandSites;

  const VERSION = 1;
  const PORTS = [47653, 47654, 47655, 47656, 47657];
  const PATH = '/island';
  const CLIENT = 'island-addon';
  const MAX_TITLE = 200;
  const MAX_ICON_BYTES = 64 * 1024;
  const KEEPALIVE_MS = 20 * 1000;
  const PROFILE_RULE = /^[A-Za-z0-9_-]{1,64}$/;
  const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  const COMMANDS = ['playpause', 'next', 'previous'];

  /** The only address the add-on ever dials: this computer. */
  function address(port) {
    if (!PORTS.includes(port)) throw new Error('not an island port');
    return 'ws://127.0.0.1:' + port + PATH;
  }

  /** A made-up id for this browser profile, stored once. `randomBytes(n)` returns n random bytes. */
  function newProfileId(randomBytes) {
    return Array.from(randomBytes(12), (b) => b.toString(16).padStart(2, '0')).join('');
  }

  function isProfileId(text) {
    return typeof text === 'string' && PROFILE_RULE.test(text);
  }

  function isTabId(n) {
    return Number.isInteger(n) && n >= 0 && n <= 0x7fffffff;
  }

  /** The first n characters, counted as whole characters: a cut never leaves half of an emoji (a lone surrogate) behind. */
  function cut(text, n) {
    return Array.from(text).slice(0, n).join('');
  }

  /** A chrome.tabs.Tab shaped into the protocol's tab object: the host only, the title cut to 200 characters. */
  function tabObject(tab) {
    const o = {
      id: tab.id,
      windowId: tab.windowId,
      title: typeof tab.title === 'string' ? cut(tab.title, MAX_TITLE) : '',
      host: sites.hostOf(tab.url || tab.pendingUrl || ''),
      audible: tab.audible === true,
      active: tab.active === true,
    };
    if (tab.pinned === true) o.pinned = true;
    if (tab.incognito === true) o.incognito = true;
    return o;
  }

  /** Tabs Chrome reports without an id (devtools, some special windows) cannot be addressed and are skipped. */
  function hasUsableId(tab) {
    return tab && isTabId(tab.id) && Number.isInteger(tab.windowId);
  }

  const frame = (o) => JSON.stringify(o);

  const build = {
    hello: (profile, browser, version) => frame({ type: 'hello', v: VERSION, client: CLIENT, browser, profile, version }),
    snapshot: (tabs) => frame({ type: 'snapshot', tabs: tabs.filter(hasUsableId).map(tabObject) }),
    tab: (tab) => frame({ type: 'tab', tab: tabObject(tab) }),
    tabRemoved: (id) => frame({ type: 'tab-removed', id }),
    tabActivated: (id, windowId) => frame({ type: 'tab-activated', id, windowId }),
    icon: (id, base64) => frame({ type: 'icon', id, png: base64 }),
    media: (id, m) => {
      const f = { type: 'media', id, title: m.title, artist: m.artist, state: m.state, position: m.position, length: m.length };
      if (m.rate !== undefined && m.rate !== null) f.rate = m.rate;
      if (m.readAt !== undefined && m.readAt !== null) f.readAt = m.readAt;
      return frame(f);
    },
    result: (cmd, id, ok) => frame({ type: 'result', cmd, id, ok: ok === true }),
    ping: () => frame({ type: 'ping' }),
  };

  /** Reads a frame from the island. Returns the message, or null for anything not in the protocol. */
  function parseIsland(text) {
    if (typeof text !== 'string' || text.length > 4096) return null;
    let m;
    try {
      m = JSON.parse(text);
    } catch {
      return null;
    }
    if (m === null || typeof m !== 'object' || Array.isArray(m)) return null;
    switch (m.type) {
      case 'welcome':
        return m.v === VERSION ? { type: 'welcome' } : null;
      case 'activate':
        return isTabId(m.id) && Number.isInteger(m.windowId) ? { type: 'activate', id: m.id, windowId: m.windowId } : null;
      case 'media-command':
        return isTabId(m.id) && COMMANDS.includes(m.command) ? { type: 'media-command', id: m.id, command: m.command } : null;
      case 'close':
        return isTabId(m.id) ? { type: 'close', id: m.id } : null;
      case 'resync':
      case 'pong':
        return { type: m.type };
      default:
        return null;
    }
  }

  /**
   * A media report from a content script, checked again before it leaves the browser: the page around the
   * content script can post anything. Null when it is not a proper report.
   */
  function cleanMedia(m) {
    if (!m || typeof m !== 'object' || !['playing', 'paused', 'stopped'].includes(m.state)) return null;
    const textOk = (t) => t === null || (typeof t === 'string' && t.length <= MAX_TITLE);
    const secondsOk = (s) => s === null || (typeof s === 'number' && Number.isFinite(s) && s >= 0 && s <= 1e9);
    if (!textOk(m.title) || !textOk(m.artist) || !secondsOk(m.position) || !secondsOk(m.length)) return null;
    const cleaned = { title: m.title, artist: m.artist, state: m.state, position: m.position, length: m.length };
    const rateOk = (r) => typeof r === 'number' && Number.isFinite(r) && r >= 0 && r <= 16;
    const timeOk = (t) => typeof t === 'number' && Number.isFinite(t) && t > 0 && t < 253402300799000;
    if (rateOk(m.rate)) cleaned.rate = m.rate;
    if (timeOk(m.readAt)) cleaned.readAt = m.readAt;
    return cleaned;
  }

  /** PNG bytes as base64, or null when they are not a PNG or are over 64 KB. */
  function iconBase64(bytes) {
    if (!(bytes instanceof Uint8Array) || bytes.length < 8 || bytes.length > MAX_ICON_BYTES) return null;
    if (!PNG_SIGNATURE.every((b, i) => bytes[i] === b)) return null;
    let binary = '';
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    return btoa(binary);
  }

  const api = { VERSION, PORTS, PATH, CLIENT, KEEPALIVE_MS, address, newProfileId, isProfileId, tabObject, hasUsableId, build, parseIsland, cleanMedia, iconBase64 };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.IslandProtocol = api;
})(globalThis);
