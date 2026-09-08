const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('event long polling has a timeout, hidden-tab cancellation and bounded reconnect backoff', () => {
  const source = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  assert.match(source, /AbortController\(\)/);
  assert.match(source, /updateController\.abort\(\), 30000/);
  assert.match(source, /visibilitychange/);
  assert.match(source, /Math\.min\(10000, 500 \* Math\.pow\(2, updateFailures\)\)/);
  assert.match(source, /updateFailures = 0/);
});

test('server transport is local by default and settings migrate across releases', () => {
  const root = path.join(__dirname, '..');
  const server = fs.readFileSync(path.join(root, 'HttpServer.cs'), 'utf8');
  const settings = fs.readFileSync(path.join(root, 'Settings.cs'), 'utf8');
  const startup = fs.readFileSync(path.join(root, 'Main.cs'), 'utf8');
  assert.match(settings, /allowRemoteConnections = false/);
  assert.match(server, /remote \? "\*" : "localhost"/);
  assert.match(server, /allowRemoteConnections && !string\.IsNullOrEmpty\(Main\.settings\.serverPassword\)/);
  assert.doesNotMatch(startup, /loaded\.version ==/);
});
