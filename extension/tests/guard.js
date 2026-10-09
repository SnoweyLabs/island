// The rules of the add-on's guard against code that arrives from outside (WORK-ORDER-8 section 7, narrowed by WORK-ORDER-9 section 1), in one place:
// no_dynamic_code.test.js applies them to the add-on's own scripts, attack9.test.js to scripts with something added. The C# guard
// (ExtensionGuardTests.No_Eval_Or_Dynamic_Code and Executes_Script_Only_As_Files_Of_The_Addon) says the same in C#.
//
// What is forbidden, in any script of the add-on (comments are not read):
// - every way to run text as code: eval (the word itself, so also (0, eval)), the Function constructor in any spelling (the word Function, a
//   constructor of a constructor), a string given to a timer, document.write, dynamic import, WebAssembly;
// - userScripts and registerContentScripts (they take code, or put scripts in by other ways than the one below);
// - the word `scripting` anywhere but in `chrome.scripting.executeScript(`: no alias, no computed name, no destructuring;
// - executeScript anywhere but in background.js, and there only as `chrome.scripting.executeScript({ ... files: entry.js ... })`, where `entry`
//   walks the `content_scripts` the manifest lists, and with no spread, no function, no code and no argument list.

const FORBIDDEN = [
  [/\beval\b/, 'eval'],
  [/\bFunction\b/, 'the Function constructor'],
  [/\bconstructor\s*\.\s*constructor\b/, 'the constructor chain'],
  [/\b(?:setTimeout|setInterval)\s*\(\s*['"`]/, 'a string given to a timer'],
  [/\bdocument\.write\s*\(/, 'document.write'],
  [/\bimport\s*\(/, 'dynamic import'],
  [/\bWebAssembly\b/, 'WebAssembly'],
  [/\buserScripts\b/, 'userScripts'],
  [/\bregisterContentScripts\b/, 'registerContentScripts'],
];

const stripComments = (raw) => raw.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');

/** The text between the parentheses of the call whose "(" is at `from`, balanced. */
function callArgument(text, from) {
  let depth = 0;
  for (let i = from; i < text.length; i++) {
    if (text[i] === '(') depth++;
    else if (text[i] === ')' && --depth === 0) return text.slice(from + 1, i);
  }
  throw new Error('an unclosed call');
}

/** What the guard says about one script: a list of violations. Empty means it passes. `rel` is the script's path inside the add-on. */
function violations(rel, raw) {
  const text = stripComments(raw);
  const found = [];
  for (const [pattern, name] of FORBIDDEN) if (pattern.test(text)) found.push(name);

  const full = /\bchrome\.scripting\.executeScript\b/g;
  const calls = [...text.matchAll(full)];
  // The word `scripting` and the word `executeScript` may appear only inside the one full form.
  if (/\bscripting\b/.test(text.replace(/\bchrome\.scripting\.executeScript\b/g, ''))) found.push('scripting reached any way but chrome.scripting.executeScript');
  if (/executeScript/.test(text.replace(/\bchrome\.scripting\.executeScript\b/g, ''))) found.push('executeScript reached any way but by name');
  for (const m of calls) {
    if (rel !== 'background.js') found.push('executeScript outside the service worker');
    let argument;
    try {
      argument = callArgument(text, m.index + m[0].length);
    } catch {
      found.push('executeScript: an unclosed call');
      continue;
    }
    if (!/\bfiles\s*:\s*entry\.js\b/.test(argument)) found.push('executeScript must name files: entry.js (the files the manifest lists)');
    if (/\.\.\./.test(argument) || /\b(func|function|args|code)\b|=>|`/.test(argument)) found.push('executeScript with something other than files');
    if (!/for\s*\(\s*const\s+entry\s+of\s+chrome\.runtime\.getManifest\(\)\.content_scripts\b/.test(text)) found.push('entry must walk the content_scripts the manifest lists');
  }

  return found;
}

module.exports = { violations, stripComments, callArgument, FORBIDDEN };
