const test = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
function fixture() {
  const elements = new Map(), listeners = {}, calls = [];
  const el = id => {
    if (!elements.has(id)) elements.set(id, {value:'', textContent:'', children:[], hidden:false, disabled:false,
      addEventListener(type, fn) { this[type] = fn; }, replaceChildren() { this.children = []; }, append(x) {this.children.push(x);}, scrollIntoView() {}});
    return elements.get(id);
  };
  const state = {fleet:[{assetId:'a', carGuid:'GUID-A', displayName:'<Wagon>', kind:'FreightWagon', state:'Available', version:3}],
    rollingStockTags:[{assetId:'wrong-first-record', compatibleCargoIds:[], loaded:false},{assetId:'a', compatibleCargoIds:['Logs','Coal'], loaded:false}], cargoChoices:[{id:'Logs',name:'Logs'},{id:'Coal',name:'Coal'}],
    locationChoices:[{id:'FRC',name:'Forest Central'},{id:'CME',name:'Coal Mine East'}], industrial:{sites:[{facilityId:'FRC',providedCargoIds:['Logs']},{facilityId:'CME',providedCargoIds:['Coal']}]}};
  const context = {document:{getElementById:id => id === 'industryDispatchStatus' ? null : el(id), createElement:() => ({})},
    window:{addEventListener:(name, fn) => listeners[name] = fn}, sidebar:{open:id => calls.push({open:id})},
    fetch:async(url, options) => { calls.push({url,options}); return {ok:true,json:async() => state}; }, crypto:{randomUUID:()=> 'test'}, console};
  vm.runInNewContext(fs.readFileSync(require.resolve('../industrial-dispatch.js'), 'utf8'), context);
  async function select(guid='guid-a') { listeners['bdvm:car-selected']({detail:{carId:'W-1',carGuid:guid}}); await new Promise(setImmediate); }
  return {el,state,calls,select};
}
test('select exact camel-case fleet and tag records, then submit supplied compatible cargo', async () => {
  const f = fixture(); await f.select();
  assert.equal(f.el('rollingStockTagTitle').textContent, 'W-1 - <Wagon>');
  assert.deepEqual(f.el('rollingStockIndustry').children.map(x=>x.value), ['', 'FRC', 'CME']);
  assert.deepEqual(f.el('rollingStockCargo').children.map(x=>x.value), ['']);
  f.el('rollingStockIndustry').value = 'FRC'; f.el('rollingStockIndustry').onchange();
  assert.deepEqual(f.el('rollingStockCargo').children.map(x=>x.value), ['', 'Logs']);
  assert.equal(f.el('rollingStockCargo').children.some(x=>x.value === 'Coal'), false);
  f.el('rollingStockCargo').value = 'Logs'; f.el('rollingStockLifetime').value = 'Permanent';
  f.el('rollingStockCargo').onchange();
  await f.el('rollingStockCargoSave').click();
  const payload = JSON.parse(f.calls.find(x=>x.options?.method==='POST').options.body);
  assert.equal(payload.assetId, 'a'); assert.equal(payload.expectedVersion, 3);
  assert.equal(payload.action, 'fleet.set-tag'); assert.equal(payload.sourceFacilityId, 'FRC'); assert.equal(payload.cargoId, 'Logs'); assert.equal(payload.tagLifetime, 'Permanent');
});
test('loaded and unknown wagons cannot submit cargo edits; modes use separate intent', async () => {
  const f = fixture(); f.state.rollingStockTags[1].loaded = true; await f.select();
  assert.equal(f.el('rollingStockCargoSave').disabled, true);
  await f.el('rollingStockCargoSave').click();
  assert.equal(f.calls.some(x=>x.options?.method==='POST'), false);
  f.el('rollingStockMode').value = 'Stored'; await f.el('rollingStockModeSave').click();
  const payload = JSON.parse(f.calls.find(x=>x.options?.method==='POST').options.body);
  assert.equal(payload.action,'fleet.set-dispatch-state'); assert.equal(payload.state,'Stored');
  await f.select('another-guid');
  assert.equal(f.el('rollingStockModeSave').disabled, true);
  assert.match(f.el('rollingStockTagStatus').textContent, /not in your managed fleet/);
});
test('host lock disables both actions', async () => {
  const f = fixture(); f.state.rollingStockTags[1].blockedReason = 'Active dossier'; await f.select();
  assert.deepEqual(f.el('rollingStockIndustry').children.map(x=>x.value), ['', 'FRC', 'CME']);
  assert.equal(f.el('rollingStockCargoSave').disabled, true); assert.equal(f.el('rollingStockModeSave').disabled, true);
  assert.equal(f.el('rollingStockTagStatus').textContent, 'Active dossier');
});
