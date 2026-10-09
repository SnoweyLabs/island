// Which site a tab is on. Only the host leaves this file: never a path, a query or a page's contents.
// Classic script: the service worker loads it with importScripts, content scripts list it in the manifest,
// and Node's test runner loads it with require.
(function (root) {
  'use strict';

  // The five sites whose pages report what they play (extension/PROTOCOL.md).
  const MEDIA_HOSTS = ['youtube.com', 'music.youtube.com', 'twitch.tv', 'soundcloud.com', 'open.spotify.com'];

  /** Lower case, no leading "www.", no trailing dot. */
  function normalizeHost(host) {
    if (typeof host !== 'string') return '';
    let h = host.trim().replace(/\.+$/, '').toLowerCase();
    if (h.startsWith('www.')) h = h.slice(4);
    return h;
  }

  /** The normalized host of a web address, or "" when it has none (chrome://, about:blank, a file). */
  function hostOf(address) {
    if (typeof address !== 'string' || address === '') return '';
    let url;
    try {
      url = new URL(address);
    } catch {
      return '';
    }
    if (url.protocol !== 'http:' && url.protocol !== 'https:') return '';
    return normalizeHost(url.hostname);
  }

  function isMediaHost(host) {
    return MEDIA_HOSTS.includes(normalizeHost(host));
  }

  const api = { MEDIA_HOSTS, normalizeHost, hostOf, isMediaHost };
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.IslandSites = api;
})(globalThis);
