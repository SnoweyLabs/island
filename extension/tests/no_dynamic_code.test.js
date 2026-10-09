// GUARD (WORK-ORDER-8 section 7, ExtensionGuardTests.No_Eval_Or_Dynamic_Code): the store forbids code that arrives from outside. Nothing the island sends
// is ever run: its messages are parsed as JSON data and read field by field. No eval, no Function constructor, no string given to a timer, no script
// loaded from an address, no unsafe-eval in the manifest's content security policy.
//
// Narrowed by WORK-ORDER-9 section 1: the bare ban on executeScript became the rule in guard.js (one call, in background.js, in its full
// `chrome.scripting.` form, naming the files the manifest's content_scripts list and nothing else). Everything else this test forbade stays forbidden.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { violations } = require('./guard.js');

const ROOT = path.join(__dirname, '..');

function scripts(dir = ROOT) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((e) => {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) return e.name === 'tests' || e.name === 'node_modules' ? [] : scripts(full);
    return e.name.endsWith('.js') ? [full] : [];
  });
}

const rel = (f) => path.relative(ROOT, f).split(path.sep).join('/');

test('No_Eval_Or_Dynamic_Code', async (t) => {
  const files = scripts();
  assert.ok(files.some((f) => rel(f) === 'background.js'), 'the guard must see the service worker');

  await t.test('no way to run text as code in any script, and the one injection call is the narrow one', () => {
    for (const f of files) assert.deepEqual(violations(rel(f), fs.readFileSync(f, 'utf8')), [], rel(f));
    const worker = fs.readFileSync(path.join(ROOT, 'background.js'), 'utf8');
    assert.ok(/\bchrome\.scripting\.executeScript\b/.test(worker), 'the guard must see the one call');
  });

  await t.test('importScripts names only files of the add-on itself', () => {
    for (const f of files) {
      for (const m of fs.readFileSync(f, 'utf8').matchAll(/importScripts\(([^)]*)\)/g)) {
        for (const arg of m[1].split(',').map((s) => s.trim())) {
          assert.match(arg, /^'[A-Za-z0-9_\-/]+\.js'$/, `${rel(f)}: importScripts(${arg})`);
        }
      }
    }
  });

  await t.test('the manifest allows no unsafe-eval and loads no remote script', () => {
    const manifest = fs.readFileSync(path.join(ROOT, 'manifest.json'), 'utf8');
    assert.ok(!/unsafe-eval|unsafe-inline/.test(manifest));
    assert.ok(!/content_security_policy/.test(manifest) || !/https?:/.test(manifest.match(/"content_security_policy"[\s\S]*?\}/)[0]));
  });

  await t.test('what arrives from the island is parsed as data only', () => {
    const protocol = fs.readFileSync(path.join(ROOT, 'lib', 'protocol.js'), 'utf8');
    assert.ok(/JSON\.parse\(/.test(protocol), 'messages are parsed with JSON.parse');
    const worker = fs.readFileSync(path.join(ROOT, 'background.js'), 'utf8');
    assert.ok(!/ws\.onmessage[\s\S]{0,400}\beval\b/.test(worker));
  });
});
