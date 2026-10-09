const test = require('node:test');
const assert = require('node:assert/strict');
const M = require('../lib/media.js');
const P = require('../lib/protocol.js');

const raw = (o) => ({ title: 'Alpha', artist: 'Beta', playbackState: 'none', hasElement: true, paused: false, ended: false, position: 10, length: 200, ...o });

test('a playing element is reported as playing, with position and length', () => {
  assert.deepEqual(M.shapeMedia(raw()), { title: 'Alpha', artist: 'Beta', state: 'playing', position: 10, length: 200 });
  assert.equal(M.shapeMedia(raw({ paused: true })).state, 'paused');
  assert.equal(M.shapeMedia(raw({ ended: true, paused: true })).state, 'stopped');
});

test('a live stream has no length', () => {
  const live = M.shapeMedia(raw({ length: Infinity, position: 12345.678 }));
  assert.equal(live.length, null);
  assert.equal(live.position, 12345.7);
  assert.equal(M.shapeMedia(raw({ length: NaN })).length, null);
});

test('without an element, the media session state is used', () => {
  assert.equal(M.shapeMedia(raw({ hasElement: false, playbackState: 'paused' })).state, 'paused');
  assert.equal(M.shapeMedia(raw({ hasElement: false, playbackState: 'none' })).state, 'stopped');
  assert.equal(M.shapeMedia(raw({ hasElement: false, playbackState: 'none', title: '  ' })), null);
  assert.equal(M.shapeMedia(null), null);
});

test('long or empty text is cut or dropped, and the result always passes the add-on check', () => {
  const m = M.shapeMedia(raw({ title: 'x'.repeat(1000), artist: '', position: -5 }));
  assert.equal(m.title.length, 200);
  assert.equal(m.artist, null);
  assert.equal(m.position, null);
  assert.ok(P.cleanMedia(m));
  assert.ok(P.cleanMedia(M.shapeMedia(raw({ title: 7, artist: {} }))));
});

test('only changes are sent, at most twice a second', () => {
  const gate = M.createMediaGate();
  const a = M.shapeMedia(raw());
  assert.equal(gate.shouldSend(a, 0), true);
  assert.equal(gate.shouldSend(a, 1000), false); // nothing changed
  const paused = M.shapeMedia(raw({ paused: true }));
  assert.equal(gate.shouldSend(paused, 1100), true);
  const other = M.shapeMedia(raw({ paused: true, title: 'Gamma' }));
  assert.equal(gate.shouldSend(other, 1300), false); // too soon after the last one
  assert.equal(gate.shouldSend(other, 1600), true);
  assert.equal(gate.shouldSend(null, 5000), false);
});

test('normal progress is not a change, a jump is, and a heartbeat comes while playing', () => {
  const gate = M.createMediaGate();
  gate.shouldSend(M.shapeMedia(raw({ position: 10 })), 0);
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ position: 11 })), 1000), false);
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ position: 90 })), 2000), true); // a seek
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ position: 91 })), 3000), false);
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ position: 95 })), 7000), true); // heartbeat
});

test('a command tries the page handler first, then the element, then a button', () => {
  assert.deepEqual(M.controlPlan('playpause', true).map((s) => s.kind + (s.action ? ':' + s.action : '')), ['handler:pause', 'element', 'button']);
  assert.equal(M.controlPlan('playpause', false)[0].action, 'play');
  assert.deepEqual(M.controlPlan('next', true).map((s) => s.kind), ['handler', 'button']); // an element cannot skip
  assert.equal(M.controlPlan('previous', false)[0].action, 'previoustrack');
  assert.deepEqual(M.controlPlan('launch', true), []);
});

test('every media site has buttons to fall back on, and other sites have none', () => {
  for (const host of ['youtube.com', 'music.youtube.com', 'twitch.tv', 'soundcloud.com', 'open.spotify.com'])
    assert.ok(M.buttonsFor(host, 'playpause').length > 0, host);
  assert.deepEqual(M.buttonsFor('example.org', 'playpause'), []);
  assert.deepEqual(Object.keys(M.BUTTONS).sort(), [...require('../lib/sites.js').MEDIA_HOSTS].sort());
});

test('the speed and the reading time are shaped, and a change of speed is a change worth sending', () => {
  const shaped = M.shapeMedia(raw({ rate: 1.25, readAt: 1790000000000.4 }));
  assert.equal(shaped.rate, 1.25);
  assert.equal(shaped.readAt, 1790000000000);
  assert.equal('rate' in M.shapeMedia(raw()), false); // nothing is made up when the page gave nothing
  assert.equal('rate' in M.shapeMedia(raw({ rate: 99 })), false);
  assert.equal('readAt' in M.shapeMedia(raw({ readAt: 'now' })), false);

  const gate = M.createMediaGate();
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ rate: 1 })), 0), true);
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ rate: 1, position: 10.6 })), 1000), false);
  assert.equal(gate.shouldSend(M.shapeMedia(raw({ rate: 2, position: 11.6 })), 2000), true); // the person changed the speed
});
