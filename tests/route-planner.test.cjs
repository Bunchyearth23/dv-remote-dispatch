const {test} = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
function setup() {
  const element = () => ({value:'',children:[],disabled:false,textContent:'',classList:{add(){},remove(){}},addEventListener(){},
    replaceChildren(...items){this.children=items;},append(...items){this.children.push(...items);},add(item){this.children.push(item);}});
  const elements = new Map();
  const document = {getElementById(id){if(!elements.has(id)) elements.set(id,element());return elements.get(id);},
    createElement:element,createDocumentFragment:element,addEventListener(){}};
  const layer = () => ({addTo(){return this;},clearLayers(){},getBounds(){return {};}});
  const timers = [];
  const context = vm.createContext({document,Date,console,Option:function(label,value){this.textContent=label;this.value=value;},
    localStorage:{getItem:()=>null,setItem(){}},setTimeout:f=>{timers.push(f);return timers.length;},clearTimeout(){},
    L:{layerGroup:layer,polyline:layer,latLngBounds:()=>({extend(){},isValid:()=>false})},fetch(){throw new Error('Unexpected fetch');}});
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../route-planner.js'),'utf8'),context);
  const View = vm.runInContext('RoutePlannerView',context);
  const view = new View({map:{getContainer:element},tracks:new Map()});
  view.train.value='train';view.destination.value='B';
  const requests=[];
  let result={token:'token',origin:'A',destination:'B',via:'',ai:false,tracks:['A','B'],switches:[],distance:200,conflicts:[],expiresAt:Date.now()+60000};
  view.request=async (endpoint,body)=>{requests.push({endpoint,body});return result;};
  return {view,requests,timers,elements,setResult:value=>result=value};
}
test('preview is read-only, editing invalidates command, explicit apply uses only server token',async()=>{
  const {view,requests}=setup();
  await view.preview();
  assert.equal(requests.length,1);assert.equal(requests[0].endpoint,'preview');assert.equal(view.applyButton.disabled,false);
  view.destination.oninput();assert.equal(view.applyButton.disabled,true);await view.apply();assert.equal(requests.length,1);
  await view.preview();await view.apply();
  assert.equal(requests[2].endpoint,'apply');assert.equal(JSON.stringify(requests[2].body),'\{"token":"token"\}');
  assert.equal(view.applyButton.disabled,true);
});
test('AI uses the assignment endpoint; conflicting plans cannot send a command',async()=>{
  for(const ai of [true,false]){
    const {view,requests,setResult}=setup();
    setResult({token:'token',origin:'A',destination:'B',tracks:[],switches:[],distance:0,ai,conflicts:ai?[]:['occupied'],expiresAt:Date.now()+60000});
    await view.preview();assert.equal(view.applyButton.disabled,!ai);await view.apply();assert.equal(requests.length,ai?2:1);
    if(ai) assert.equal(requests[1].endpoint,'assign');
  }
});
test('AI stop invalidates a prepared route and requires its own explicit action',async()=>{
  const {view,requests}=setup();await view.preview();await view.controlAi('stop');
  assert.equal(view.plan,null);assert.equal(requests[1].endpoint,'control-ai');assert.equal(requests[1].body.action,'stop');
});
test('expired preparation cannot be submitted and selecting a map track invalidates the preview',async()=>{
  const {view,requests,timers}=setup();await view.preview();timers.at(-1)();await view.apply();assert.equal(requests.length,1);
  view.startPick(view.via);view.pickTrack('via');assert.equal(view.via.value,'via');assert.equal(view.plan,null);
});

test('map picking selects the closest parallel rail and fills the requested field without commands',()=>{
  const {view,requests}=setup();
  view.map.latLngToLayerPoint=p=>p;
  for(const [id,y] of [['A',0],['B',8]]) view.tracks.set(id,{options:{},getLatLngs:()=>[{x:0,y},{x:100,y}]});
  view.startPick(view.destination);view.pickAtPoint({x:40,y:7});
  assert.equal(view.destination.value,'B');assert.equal(view.pick,null);
  view.startPick(view.via);view.pickAtPoint({x:40,y:1});
  assert.equal(view.via.value,'A');assert.equal(requests.length,0);
});
test('off-track clicks retain pick mode and a busy planner ignores selection',()=>{
  const {view}=setup();view.map.latLngToLayerPoint=p=>p;
  view.tracks.set('A',{options:{},getLatLngs:()=>[{x:0,y:0},{x:100,y:0}]});
  view.startPick(view.destination);view.pickAtPoint({x:40,y:20});
  assert.equal(view.pick,view.destination);assert.match(view.status.textContent,/No track/);
  view.busy=true;view.pickAtPoint({x:40,y:0});assert.equal(view.destination.value,'B');
});
