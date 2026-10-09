// The labelled examples of ../PROTOCOL.md: the same blocks the island's C# tests read.
const fs = require('node:fs');
const path = require('node:path');

function readExamples() {
  const text = fs.readFileSync(path.join(__dirname, '..', 'PROTOCOL.md'), 'utf8').replace(/\r\n/g, '\n');
  const examples = {};
  for (const m of text.matchAll(/^```json example (\S+)\n([\s\S]*?)^```/gm)) examples[m[1]] = m[2].trim();
  return examples;
}

module.exports = { examples: readExamples(), FROM_ISLAND: ['welcome', 'activate', 'media-command', 'close', 'resync', 'pong'] };
