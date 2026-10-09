const test = require('node:test');
const assert = require('node:assert/strict');
const P = require('../lib/protocol.js');
const { examples, FROM_ISLAND } = require('./examples.js');

const json = (text) => JSON.parse(text);

// A chrome.tabs.Tab as Chrome would hand it over: with the full address, which must never leave.
const chromeTab = (o) => ({ index: 0, highlighted: false, discarded: false, autoDiscardable: true, groupId: -1, ...o });

test('the protocol file has its examples', () => {
  assert.ok(Object.keys(examples).length >= 18);
  for (const label of ['hello', 'snapshot', 'tab', 'icon', 'media', 'activate', 'bad-not-an-object']) assert.ok(label in examples, label);
});

test('the frames the add-on builds are exactly the examples', () => {
  assert.deepEqual(json(P.build.hello('p1', 'chrome', '0.1.0')), json(examples.hello));
  assert.deepEqual(
    json(
      P.build.snapshot([
        chromeTab({ id: 11, windowId: 1, title: 'Lo-fi beats', url: 'https://www.youtube.com/watch?v=abc&t=1', audible: true, active: true }),
        chromeTab({ id: 12, windowId: 1, title: 'Docs', url: 'https://example.org/a/b?secret=1#x', audible: false, active: false, pinned: true }),
      ]),
    ),
    json(examples.snapshot),
  );
  assert.deepEqual(
    json(P.build.tab(chromeTab({ id: 13, windowId: 1, title: 'Mix', url: 'https://music.youtube.com/watch?v=q', audible: false, active: false }))),
    json(examples.tab),
  );
  assert.deepEqual(json(P.build.tabRemoved(12)), json(examples['tab-removed']));
  assert.deepEqual(json(P.build.tabActivated(13, 1)), json(examples['tab-activated']));
  assert.deepEqual(json(P.build.icon(11, json(examples.icon).png)), json(examples.icon));
  assert.deepEqual(
    json(P.build.media(11, { title: 'Lo-fi beats to study to', artist: 'Some Channel', state: 'playing', position: 42.5, length: 3600 })),
    json(examples.media),
  );
  assert.deepEqual(json(P.build.media(11, { title: 'Live stream', artist: null, state: 'playing', position: null, length: null })), json(examples['media-no-length']));
  assert.deepEqual(json(P.build.result('activate', 11, true)), json(examples.result));
  assert.deepEqual(json(P.build.result('close', 11, true)), json(examples['result-close']));
  assert.deepEqual(json(P.build.ping()), json(examples.ping));
});

test('every frame the island sends is understood', () => {
  for (const label of FROM_ISLAND) assert.ok(P.parseIsland(examples[label]), label);
  assert.deepEqual(P.parseIsland(examples.activate), { type: 'activate', id: 11, windowId: 1 });
  assert.deepEqual(P.parseIsland(examples['media-command']), { type: 'media-command', id: 11, command: 'next' });
  assert.deepEqual(P.parseIsland(examples.close), { type: 'close', id: 11 });
});

test('anything else from the island is refused', () => {
  const others = Object.keys(examples).filter((l) => !FROM_ISLAND.includes(l));
  for (const label of others) assert.equal(P.parseIsland(examples[label]), null, label);
  for (const bad of [
    '',
    'null',
    '[]',
    '"welcome"',
    '{"type":"welcome","v":2}',
    '{"type":"activate","id":-1,"windowId":1}',
    '{"type":"close","id":-1}',
    '{"type":"close","id":"11"}',
    '{"type":"close"}',
    '{"type":"activate","id":"11","windowId":1}',
    '{"type":"media-command","id":11,"command":"launch"}',
    '{"type":"activate","id":11}',
    '{' + '"a":'.repeat(3000),
    JSON.stringify({ type: 'pong', pad: 'x'.repeat(5000) }),
  ])
    assert.equal(P.parseIsland(bad), null, bad.slice(0, 40));
});

test('a tab never leaves with its full address, and its title is cut at 200 characters', () => {
  const o = P.tabObject(chromeTab({ id: 1, windowId: 2, title: 'x'.repeat(500), url: 'https://user:pw@Sub.Example.org:8443/path/page?q=secret#frag', audible: false, active: true }));
  assert.equal(o.host, 'sub.example.org');
  assert.equal(o.title.length, 200);
  const text = JSON.stringify(o);
  for (const leak of ['path', 'secret', 'frag', 'user', 'pw', '8443', 'https']) assert.ok(!text.includes(leak), leak);
});

test('a tab without a web address has an empty host', () => {
  for (const url of ['chrome://extensions/', 'about:blank', 'file:///C:/x.txt', '', undefined, 'not a url'])
    assert.equal(P.tabObject(chromeTab({ id: 1, windowId: 1, title: 't', url, audible: false, active: false })).host, '');
  assert.equal(P.tabObject(chromeTab({ id: 1, windowId: 1, title: 't', pendingUrl: 'https://www.twitch.tv/x', audible: false, active: false })).host, 'twitch.tv');
});

test('tabs without a usable id are left out of a snapshot', () => {
  const frame = json(P.build.snapshot([chromeTab({ windowId: 1, title: 'devtools' }), chromeTab({ id: 3, windowId: 1, title: 'ok', url: 'https://a.org/', active: false })]));
  assert.deepEqual(frame.tabs.map((t) => t.id), [3]);
});

test('the add-on only ever dials this computer on the five island ports', () => {
  assert.deepEqual(P.PORTS, [47653, 47654, 47655, 47656, 47657]);
  for (const port of P.PORTS) assert.equal(P.address(port), `ws://127.0.0.1:${port}/island`);
  assert.throws(() => P.address(80));
});

test('a profile id is made up once and fits the protocol', () => {
  let n = 0;
  const id = P.newProfileId((count) => Uint8Array.from({ length: count }, () => n++));
  assert.ok(P.isProfileId(id));
  assert.equal(id.length, 24);
  for (const bad of ['', 'a b', 'x'.repeat(65), 'p:1', null, 5]) assert.equal(P.isProfileId(bad), false);
});

test('an icon goes only as a PNG of at most 64 KB', () => {
  const png = Buffer.from(json(examples.icon).png, 'base64');
  assert.equal(P.iconBase64(new Uint8Array(png)), json(examples.icon).png);
  const big = new Uint8Array(64 * 1024 + 1);
  big.set(png.subarray(0, 8));
  assert.equal(P.iconBase64(big), null);
  assert.ok(P.iconBase64(big.subarray(0, 64 * 1024)));
  assert.equal(P.iconBase64(new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9])), null); // not a PNG
  assert.equal(P.iconBase64('iVBOR'), null);
});

test('a media report from a page is checked again before it leaves', () => {
  const good = { title: 'Alpha', artist: null, state: 'paused', position: 3, length: null, extra: 'dropped' };
  assert.deepEqual(P.cleanMedia(good), { title: 'Alpha', artist: null, state: 'paused', position: 3, length: null });
  for (const bad of [null, 'x', { ...good, state: 'rewinding' }, { ...good, title: 'x'.repeat(201) }, { ...good, position: -1 }, { ...good, length: Infinity }, { ...good, artist: 5 }])
    assert.equal(P.cleanMedia(bad), null);
});

test('a media frame carries the speed and the reading time when the page gave them', () => {
  const timing = { title: 'Lo-fi beats to study to', artist: 'Some Channel', state: 'playing', position: 42.5, length: 3600, rate: 1.25, readAt: 1790000000000 };
  assert.deepEqual(json(P.build.media(11, timing)), json(examples['media-timing']));
  // Without them the frame is the one it always was.
  assert.deepEqual(json(P.build.media(11, { ...timing, rate: undefined, readAt: undefined })), json(examples.media));
  // What the page says is cleaned again before it leaves: a speed outside 0 to 16 and a time that is not a time are dropped, the rest stays.
  assert.deepEqual(P.cleanMedia(timing), timing);
  const cleaned = P.cleanMedia({ ...timing, rate: 99, readAt: -4 });
  assert.equal('rate' in cleaned, false);
  assert.equal('readAt' in cleaned, false);
  assert.equal(cleaned.position, 42.5);
});
