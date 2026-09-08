const {test}=require('node:test');
const assert=require('node:assert/strict');
const vm=require('node:vm');
const fs=require('node:fs');
const path=require('node:path');
function setup(){
  const element=()=>({textContent:'',children:[],append(child){this.children.push(child);},replaceChildren(){this.children=[];}});
  const elements=new Map();
  const document={createElement:element,getElementById(id){if(!elements.has(id))elements.set(id,element());return elements.get(id);}};
  const layer={addTo(){return this;},removeLayer(){}};
  const context=vm.createContext({document,L:{layerGroup:()=>layer,divIcon:o=>o,marker:()=>({addTo(){return this;},bindTooltip(text,options){this.tooltip=text;this.tooltipOptions=options;},setLatLng(){}})}});
  vm.runInContext(fs.readFileSync(path.join(__dirname,'../multiplayer.js'),'utf8'),context);
  const View=vm.runInContext('MultiplayerView',context), removed=[],positions=[];
  const view=new View({map:{panTo:p=>positions.push(p)},move(){},removeMotion:m=>removed.push(m)});
  return{view,removed,positions};
}
test('multiplayer roster uses safe labels, follows latest positions and reuses unchanged rows',()=>{
  const {view,positions}=setup();
  const player={id:'mp-1',name:'<img onerror=bad()>',crew:'Crew',position:[1,2],rotation:0};
  view.update({status:'host',players:[player]});
  assert.equal(view.markers.get('mp-1').tooltipOptions.permanent,true);
  const row=view.list.children[0];assert.match(row.textContent,/<img/);assert.equal(row.innerHTML,undefined);
  view.update({status:'host',players:[{...player,position:[3,4]}]});assert.equal(view.list.children[0],row);
  row.onclick();assert.deepEqual(positions,[[3,4]]);
});
test('disconnect and adapter failure remove multiplayer markers and their animation',()=>{
  const {view,removed}=setup();view.update({status:'client',players:[{id:'mp-1',name:'test',position:[1,2]}]});
  assert.match(view.status.textContent,/Client multiplayer/);
  view.update({status:'incompatible'});assert.equal(view.markers.size,0);assert.equal(removed.length,1);assert.equal(view.list.children.length,0);
});
