// GUARD (EVALS I3, ExtensionGuardTests.No_Remote_Fetch_And_No_Icon_Service): the add-on asks nothing of the
// internet. It dials 127.0.0.1 only, fetches only the browser's own stored icons, and asks for no host but
// the island's and, for its content scripts, the five media sites.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..');
const MEDIA_MATCHES = [
  'https://www.youtube.com/*',
  'https://music.youtube.com/*',
  'https://www.twitch.tv/*',
  'https://soundcloud.com/*',
  'https://open.spotify.com/*',
];
const ICON_SERVICES = ['s2/favicons', 'favicon.ico', 'icons.duckduckgo', 'icon.horse', 'besticon', 'faviconkit', 'favicone', 'clearbit', 'gstatic.com', 'faviconV2'];

/** Every file the browser loads: everything except the tests and the documents. */
function sourceFiles(dir = ROOT) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) return e.name === 'tests' || e.name === 'node_modules' ? [] : sourceFiles(full);
    return /\.(js|json|html|css)$/.test(e.name) ? [full] : [];
  });
}

const read = (f) => fs.readFileSync(f, 'utf8');
const rel = (f) => path.relative(ROOT, f).split(path.sep).join('/');

test('No_Remote_Fetch_And_No_Icon_Service', async (t) => {
  const files = sourceFiles();
  const scripts = files.filter((f) => f.endsWith('.js'));
  assert.ok(scripts.some((f) => rel(f) === 'background.js'), 'the guard must see the service worker');
  const manifest = JSON.parse(read(path.join(ROOT, 'manifest.json')));

  await t.test('no address in the scripts but this computer', () => {
    for (const f of scripts) {
      const addresses = read(f).match(/\b(?:https?|wss?|ftp):\/\/[^\s'"`]*/g) || [];
      for (const a of addresses) assert.ok(a.startsWith('ws://127.0.0.1:'), `${rel(f)}: ${a}`);
    }
  });

  await t.test('nothing fetched but the browser\'s own icon store', () => {
    for (const f of scripts) {
      const code = read(f);
      for (const word of ['XMLHttpRequest', 'sendBeacon', 'EventSource', 'WebTransport', 'RTCPeerConnection', 'import(', '<all_urls>'])
        assert.ok(!code.includes(word), `${rel(f)}: ${word}`);
      const fetches = code.match(/\bfetch\s*\(/g) || [];
      const allowed = code.match(/\bfetch\s*\(\s*faviconUrl\s*\(/g) || [];
      assert.equal(fetches.length, allowed.length, `${rel(f)}: a fetch that is not the icon store`);
      const sockets = code.match(/new\s+WebSocket\s*\(/g) || [];
      const toIsland = code.match(/new\s+WebSocket\s*\(\s*P\.address\s*\(/g) || [];
      assert.equal(sockets.length, toIsland.length, `${rel(f)}: a WebSocket not built by address()`);
    }
    const worker = read(path.join(ROOT, 'background.js'));
    assert.match(worker, /function faviconUrl\(pageUrl\) \{\s*const u = new URL\(chrome\.runtime\.getURL\('\/_favicon\/'\)\);/);
    for (const f of scripts.filter((s) => /importScripts/.test(read(s))))
      for (const m of read(f).matchAll(/importScripts\(([^)]*)\)/g)) assert.ok(!/:\/\//.test(m[1]), `${rel(f)}: remote importScripts`);
  });

  await t.test('no icon service anywhere', () => {
    for (const f of files) for (const s of ICON_SERVICES) assert.ok(!read(f).includes(s), `${rel(f)}: ${s}`);
  });

  await t.test('the manifest asks for this computer and the five media sites only', () => {
    // WORK-ORDER-9 section 1: the five sites became hosts the add-on may act on (it puts its own files into tabs that were already open); still no other.
    assert.deepEqual([...manifest.host_permissions].sort(), ['http://127.0.0.1/*', ...MEDIA_MATCHES].sort());
    assert.ok(!('optional_host_permissions' in manifest));
    assert.ok(!JSON.stringify(manifest).includes('<all_urls>'));
    assert.ok(!JSON.stringify(manifest).includes('*://'));
    for (const cs of manifest.content_scripts) assert.deepEqual(cs.matches, MEDIA_MATCHES);
    assert.ok(!('externally_connectable' in manifest), 'no web page may talk to the add-on');
    assert.ok(!('web_accessible_resources' in manifest));
  });
});
