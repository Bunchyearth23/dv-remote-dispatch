const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function setup() {
  const element = () => ({ children: [], value: '', textContent: '', attributes: {},
    addEventListener() {}, appendChild(child) { this.children.push(child); },
    replaceChildren() { this.children = []; }, setAttribute(key, value) { this.attributes[key] = value; } });
  const elements = new Map();
  const document = { createElement: element, getElementById(id) { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); } };
  const layer = () => ({ items: [], addTo(parent) { parent.items?.push(this); return this; },
    clearLayers() { this.items = []; }, removeLayer(item) { this.items = this.items.filter(x => x !== item); } });
  const shape = options => ({ options, addTo(parent) {parent.items.push(this);return this;}, on() {return this;}, setLatLng() {},
    bindTooltip(label) {this.label = label;return this;} });
  const context = vm.createContext({ document, L: { layerGroup: layer, divIcon: options => options,
    marker: (p, options) => shape(options), polyline: (points, options) => shape(options) } });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../ai-traffic.js'), 'utf8'), context);
  const followed = [];
  const view = new context.AiTrafficView({ map: {}, tracks: new Map(['planned','reserved','other'].map(id => [id,{getLatLngs:()=>[[0,0],[1,1]]}])),
    signals: new Map(), follow: train => followed.push(train.id), move() {}, removeMotion() {} });
  return { view, followed };
}
const train = {id:'guid', carId:'<img onerror=bad()>', state:'Driving', origin:'A',destination:'B',destinationTrack:'reserved',
  position:[0,0],rotation:0,worker:true,speedKmh:30,targetSpeedKmh:40,signalId:7,distanceToSignal:null,routeTracks:['planned','reserved','missing']};
const snapshot = () => ({status:'ready',trains:[train],reservations:[{trackId:'reserved',ownerId:'guid'},{trackId:'other',ownerId:null}],junctionLocks:[]});

test('AI selection distinguishes planned route from owned reservations without issuing commands', () => {
  const { view, followed } = setup();
  view.update(snapshot());
  assert.equal(view.routes.items.length,2);
  view.select('guid');
  assert.deepEqual(followed,['guid']);
  assert.equal(view.routes.items.length,3);
  assert.equal(view.routes.items.filter(line=>line.options.dashArray===null).length,1);
  assert.match(view.list.children[0].textContent, /<img onerror=bad\(\)>/);
  assert.equal(view.list.children[0].innerHTML,undefined);
  assert.match(view.detail.textContent,/Signal : 7 à — m/);
});
test('AI disappearance and optional adapter errors clear obsolete routes and selection', () => {
  const {view} = setup(); view.update(snapshot()); view.select('guid');
  view.update({status:'ready',trains:[],reservations:[],junctionLocks:[]});
  assert.equal(view.selected,null); assert.equal(view.markers.size,0); assert.equal(view.routes.items.length,0);
  view.update(snapshot()); view.update({status:'incompatible'});
  assert.equal(view.markers.size,0); assert.equal(view.routes.items.length,0);
  assert.match(view.status.textContent,/incompatible/);
});
test('unchanged AI data reuses route layers and list rows', () => {
  const {view} = setup(); view.update(snapshot()); const row = view.list.children[0]; const route = view.routes.items[0];
  view.update(snapshot());
  assert.equal(view.list.children[0],row); assert.equal(view.routes.items[0],route);
});
