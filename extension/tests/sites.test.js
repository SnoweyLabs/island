const test = require('node:test');
const assert = require('node:assert/strict');
const S = require('../lib/sites.js');

test('hosts are normalized the way the island normalizes them', () => {
  assert.equal(S.normalizeHost(' WWW.YouTube.com. '), 'youtube.com');
  assert.equal(S.normalizeHost('music.youtube.com'), 'music.youtube.com');
  assert.equal(S.normalizeHost(42), '');
});

test('the host of an address is the host only', () => {
  assert.equal(S.hostOf('https://www.youtube.com/watch?v=1'), 'youtube.com');
  assert.equal(S.hostOf('http://127.0.0.1:8080/x'), '127.0.0.1');
  assert.equal(S.hostOf('chrome://newtab/'), '');
  assert.equal(S.hostOf('nonsense'), '');
});

test('the five media sites, and music.youtube.com is not youtube.com', () => {
  assert.deepEqual(S.MEDIA_HOSTS, ['youtube.com', 'music.youtube.com', 'twitch.tv', 'soundcloud.com', 'open.spotify.com']);
  for (const h of ['www.youtube.com', 'music.youtube.com', 'www.twitch.tv', 'soundcloud.com', 'open.spotify.com']) assert.ok(S.isMediaHost(h), h);
  for (const h of ['spotify.com', 'm.youtube.com', 'youtube.com.example.org', 'example.org', '']) assert.equal(S.isMediaHost(h), false, h);
});
