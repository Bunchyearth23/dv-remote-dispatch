const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const context = vm.createContext({ module: { exports: {} } });
vm.runInContext(require('node:fs').readFileSync(require('node:path').join(__dirname, '../motion.js'), 'utf8'), context);
const MotionTrack = context.module.exports;

test('interpolates positions and takes the short arc through north', () => {
  const track = new MotionTrack(150);
  track.push([0, 0], 359, 1000);
  track.push([0.0001, 0], 1, 1100);
  const middle = track.at(1200);
  assert.equal(middle.position[0], 0.00005);
  assert.equal(middle.rotation, 360);
  assert.equal(track.settled(1200), false);
});
test('freezes at the last measurement without extrapolating', () => {
  const track = new MotionTrack();
  track.push([0, 0], 0, 1000);
  track.push([0.0001, 0], 10, 1100);
  assert.equal(track.at(10000).position[0], 0.0001);
  assert.equal(track.settled(10000), true);
});
test('reconnections and teleports reset history', () => {
  const track = new MotionTrack();
  track.push([0, 0], 0, 1000);
  track.push([0.0001, 0], 0, 3000);
  assert.equal(track.at(3000).position[0], 0.0001);
  track.push([0.1, 0], 0, 3100);
  assert.equal(track.at(3100).position[0], 0.1);
});
test('handles bursts and rejects invalid or out of order measurements', () => {
  const track = new MotionTrack(150);
  for (let i = 0; i < 6; i++) track.push([i * 0.0001, 0], 0, 1000 + 100 * i);
  assert.ok(Math.abs(track.at(1500).position[0] - 0.00035) < 1e-10);
  track.push([NaN, 0], 0, 1600);
  track.push([1, 0], 0, 900);
  assert.equal(track.at(1800).position[0], 0.0005);
});
