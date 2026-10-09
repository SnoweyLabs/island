const test = require('node:test');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..');
const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, 'manifest.json'), 'utf8'));

// The id written in README.md and in the island's notes. Chrome derives it from the public key: SHA-256 of the
// key, first 32 hex digits, each digit 0-f written as a letter a-p.
const EXTENSION_ID = 'lmnojmilhkpdhejkanneoogmjldolook';

test('the fixed key gives the documented extension id', () => {
  const digest = crypto.createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest('hex');
  const id = [...digest.slice(0, 32)].map((h) => String.fromCharCode(97 + parseInt(h, 16))).join('');
  assert.equal(id, EXTENSION_ID);
  assert.ok(fs.readFileSync(path.join(ROOT, 'README.md'), 'utf8').includes(EXTENSION_ID));
});

test('the key is a public key only', () => {
  const key = crypto.createPublicKey({ key: Buffer.from(manifest.key, 'base64'), format: 'der', type: 'spki' });
  assert.equal(key.asymmetricKeyType, 'rsa');
  for (const f of fs.readdirSync(ROOT)) assert.ok(!/\.pem$/.test(f), 'no private key in the add-on folder');
});

test('Manifest V3, Chrome 116 or newer, the smallest set of permissions', () => {
  assert.equal(manifest.manifest_version, 3);
  assert.equal(manifest.minimum_chrome_version, '116');
  assert.deepEqual([...manifest.permissions].sort(), ['alarms', 'favicon', 'scripting', 'storage', 'tabs']); // scripting: WORK-ORDER-9 section 1
  assert.equal(manifest.background.service_worker, 'background.js');
});

test('the page-world script runs at document start, and every file the manifest names exists', () => {
  const [main, relay] = manifest.content_scripts;
  assert.equal(main.world, 'MAIN');
  assert.equal(main.run_at, 'document_start');
  assert.deepEqual(relay.js, ['lib/sites.js', 'lib/media.js', 'content/media-relay.js']);
  const named = [manifest.background.service_worker, ...manifest.content_scripts.flatMap((c) => c.js)];
  const worker = fs.readFileSync(path.join(ROOT, 'background.js'), 'utf8');
  for (const m of worker.matchAll(/importScripts\(([^)]*)\)/g)) named.push(...m[1].split(',').map((s) => s.trim().replace(/^'|'$/g, '')));
  for (const f of named) assert.ok(fs.existsSync(path.join(ROOT, f)), f);
});

const MEDIA_MATCHES = [
  'https://www.youtube.com/*',
  'https://music.youtube.com/*',
  'https://www.twitch.tv/*',
  'https://soundcloud.com/*',
  'https://open.spotify.com/*',
];

test('the hosts are exactly the five sites and 127.0.0.1', () => {
  assert.deepEqual([...manifest.host_permissions].sort(), ['http://127.0.0.1/*', ...MEDIA_MATCHES].sort());
  for (const entry of manifest.content_scripts) assert.deepEqual([...entry.matches].sort(), [...MEDIA_MATCHES].sort(), 'the content scripts run on the same five sites');
});

test('every script parses', () => {
  const vm = require('node:vm');
  const scripts = ['background.js', 'content/media-main.js', 'content/media-relay.js', 'lib/sites.js', 'lib/protocol.js', 'lib/reconnect.js', 'lib/media.js'];
  for (const f of scripts) assert.doesNotThrow(() => new vm.Script(fs.readFileSync(path.join(ROOT, f), 'utf8'), { filename: f }), f);
});
