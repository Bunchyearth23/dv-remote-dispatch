const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs'),vm=require('node:vm');
const source=fs.readFileSync(require('node:path').join(__dirname,'../main.js'),'utf8');
function setup(){
  const status={},speed={},pressure={};
  const ctx=vm.createContext({guid:null,AbortController,setTimeout,clearTimeout,document:{getElementById:()=>status,createElement:()=>({setAttribute(){}})},
    locoSpeedDisplay:speed,locoBrakePipeDisplay:pressure,getControlledLocoGuid:()=>ctx.guid,
    fetch:async()=>{throw Error('offline');},updateLocoTrainBrakeInput(){},updateLocoIndependentBrakeInput(){},updateReverserButtons(){},updateLocoThrottleInput(){},
    locoControlCoupleButton:{},locoControlUncoupleButton:{},locoControlUncoupleSelect:{replaceChildren(...items){this.items=items;}}});
  vm.runInContext(source.slice(source.indexOf('function updateCouplingControls('),source.indexOf('function getControlledLocoGuid(')),ctx);
  vm.runInContext(source.slice(source.indexOf('let locoDisplayPending'),source.indexOf('let locoControlRefreshIntervalId')),ctx);
  return{ctx,status,speed,pressure,run:()=>vm.runInContext('updateLocoDisplay()',ctx)};
}
test('empty catalog and HTTP failure produce a status, not an unhandled error',async()=>{
  const t=setup();await t.run();assert.match(t.status.textContent,/No controllable locomotive/);
  t.ctx.guid='one';await t.run();assert.match(t.status.textContent,/offline/);
});
test('polling cannot overlap and ignores a response for the previous selected locomotive',async()=>{
  const t=setup();t.ctx.guid='one';let finish,count=0;
  t.ctx.fetch=()=>{count++;return new Promise(resolve=>finish=resolve);};
  const pending=t.run();await t.run();assert.equal(count,1);
  t.ctx.guid='two';finish({ok:true,json:async()=>({forwardSpeed:99})});await pending;
  assert.equal(t.speed.textContent,undefined);
});
test('uncoupling is disabled for an isolated loco and choices follow the actual front/rear split',()=>{
  const t=setup();
  vm.runInContext('updateCouplingControls({canCouple:false,carsInFront:0,carsInRear:0})',t.ctx);
  assert.equal(t.ctx.locoControlUncoupleButton.disabled,true);
  vm.runInContext('updateCouplingControls({canCouple:true,carsInFront:1,carsInRear:0})',t.ctx);
  assert.equal(t.ctx.locoControlUncoupleSelect.items[0].textContent,'+1');
  vm.runInContext('updateCouplingControls({canCouple:true,carsInFront:0,carsInRear:1})',t.ctx);
  assert.equal(t.ctx.locoControlUncoupleSelect.items[0].textContent,'−1');
});
