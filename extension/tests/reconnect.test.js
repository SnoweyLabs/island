const test = require('node:test');
const assert = require('node:assert/strict');
const R = require('../lib/reconnect.js');

test('never more than one attempt per 5 seconds', () => {
  const r = R.createRedialer();
  assert.equal(r.tryStart(1000, false), true);
  assert.equal(r.tryStart(1001, false), false);
  assert.equal(r.tryStart(5999, false), false);
  assert.equal(r.tryStart(6000, false), true);
});

test('no attempt while a connection is open or being made', () => {
  const r = R.createRedialer();
  assert.equal(r.tryStart(0, true), false);
  assert.equal(r.tryStart(0, false), true); // a refused try does not count as an attempt
});

test('a burst of tab events makes one attempt', () => {
  const r = R.createRedialer();
  let attempts = 0;
  for (let t = 0; t < 4000; t += 10) if (r.tryStart(t, false)) attempts++;
  assert.equal(attempts, 1);
});

test('one attempt tries the five ports in turn and then stops', () => {
  const tried = [0];
  let i = 0;
  while ((i = R.nextPortIndex(i, 5)) !== null) tried.push(i);
  assert.deepEqual(tried, [0, 1, 2, 3, 4]);
});

test('the alarm wakes the add-on every 30 seconds', () => {
  assert.equal(R.ALARM_MINUTES * 60, 30);
  assert.equal(R.MIN_GAP_MS, 5000);
});
