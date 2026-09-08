const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

test('BDVM panel renders escaped authoritative state and submits bounded server intent', async () => {
  const elements = new Map();
  const element = id => elements.get(id) || elements.set(id, { id, innerHTML:'', textContent:'', dataset:{}, addEventListener(type, fn){ this[type] = fn; } }).get(id);
  ['bdvmStatus','bdvmRefresh','bdvmFinances','bdvmCompanies','bdvmFleet','bdvmMarket','bdvmDeliveries','bdvmMaintenance','bdvmLeases','bdvmAssignments'].forEach(element);
  const tab = element('tab'); const buttons = [];
  const state = { release:'1.0.0', authorityActor:'local', transportIdentity:'dispatcher', worldPopulation:{runtimeState:'Active',ResultCode:'strict-population-control-active'}, wallets:[{account:'Player:<unsafe>',Balance:10,Version:1}], companies:[], membershipRequests:[], fleet:[{AssetId:'a',DisplayName:'Loco <x>',kind:'Locomotive',state:'Available',owner:'Player:p',operatorRef:'Player:p'}], market:[], marketStock:[{DefinitionId:'DE2',LocationId:'<yard>',Available:1,Capacity:2}], initialDeliveries:[], deliveryTracks:[], operatingCosts:[], leases:[], assignments:[] };
  const calls = [];
  const context = { globalThis:{crypto:{randomUUID:()=> 'cid'}}, crypto:{randomUUID:()=> 'cid'}, AbortController, document:{ getElementById:element, querySelector:()=>tab, querySelectorAll(selector){ return buttons.filter(x => selector === '[data-fleet]' ? x.dataset.fleet : x.dataset.cancel); } }, fetch:async (url, options={}) => { calls.push({url,options}); return {ok:true,json:async()=>state}; }, console, setTimeout, clearTimeout, Math, Date };
  vm.runInNewContext(fs.readFileSync(require.resolve('../bdvm.js'),'utf8'), context);
  await elements.get('bdvmRefresh').click();
  assert.match(elements.get('bdvmFinances').innerHTML, /Player:&lt;unsafe&gt;/);
  assert.match(elements.get('bdvmFleet').innerHTML, /Loco &lt;x&gt;/);
  assert.match(elements.get('bdvmMarket').innerHTML, /&lt;yard&gt;/);
  assert.match(elements.get('bdvmStatus').textContent, /population Active\/strict-population-control-active/);
  const client = fs.readFileSync(require.resolve('../bdvm.js'),'utf8');
  assert.match(client, /company\.create/); assert.match(client, /wallet\.transfer/); assert.match(client, /initial-delivery\.place/);
  assert.equal(calls[0].url, '/bdvm');
});

test('BDVM HTTP bridge stays permissioned, same-origin and main-thread dispatched', () => {
  const root = require('node:path').resolve(__dirname, '..');
  const server = fs.readFileSync(require('node:path').join(root, 'HttpServer.cs'), 'utf8');
  const bridge = fs.readFileSync(require('node:path').join(root, 'BDVMIntegration.cs'), 'utf8');
  assert.match(server, /HasCompanyPermission/); assert.match(server, /CheckMutationOrigin/); assert.match(server, /TransportSecurity\.IsSameOrigin/); assert.match(server, /RunOnMainThread/); assert.match(server, /ContentLength64 > 4096/);
  assert.match(bridge, /FindMod\("BDVM"\)/); assert.match(bridge, /MaximumPayloadBytes = 4096/); assert.doesNotMatch(bridge, /BDVM\.Domain/);
});
