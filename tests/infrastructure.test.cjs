const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const source = fs.readFileSync(require('node:path').join(__dirname, '../main.js'), 'utf8');

function setup() {
  const layer = () => ({ addTo() { return this; }, remove() {}, setStyle(style) { this.style = style; return this; },
    setLatLng() { return this; }, setIcon(icon) { this.icon = icon; return this; },
    bindTooltip(label) { this.label = label; return this; }, getElement() { return {classList:{toggle(){}}}; }, clearLayers() {}, removeLayer() {} });
  const timers = [];
  const context = vm.createContext({
    L: { layerGroup: layer, control: Object.assign(() => ({ addTo() { this.onAdd(); } }), { layers: layer }),
      DomUtil: { create: () => ({}) }, DomEvent: { disableClickPropagation() {} },
      divIcon: options => options, marker: (p, options) => Object.assign(layer(), options), polyline: layer,
      latLng: () => ({ distanceTo: () => 0 }) },
    AiTrafficView: class { constructor() {this.layer = layer(); this.routes = layer();} update() {} },
    SignalCanvas: class {}, SignalSymbol: class { constructor(p, options) { return Object.assign(layer(), {style:options}); } },
    canvasRenderer: {}, viewportOverlays: new Map(),
    RoutePlannerView: class {},
    MultiplayerView: class { constructor(){this.layer=layer();} update(){} },
    queueMotion() {}, movingMarkers: new Map(), carMarkers: new Map(),
    map: {}, animateMarkers() {}, document: { createElement: () => ({}), getElementById: () => ({ classList: { toggle() {} } }) },
    performance: { now: () => 100 }, requestAnimationFrame() {}, setInterval() {}, setTimeout: (f, ms) => timers.push(ms), clearTimeout() {},
    junctions: [], trackPolyLines: new Map(), createJunctionMarker: layer,
    createJunctionShape: () => '', createJunctionLabel: () => '',
    createJunctionDirection: () => '',
    junctionsReady: { then: () => ({ catch() {} }) }, URL, location: 'http://localhost:7245', AbortController,
    fetch: async () => { throw new Error('offline'); }
  });
  vm.runInContext(source.slice(source.indexOf('function updateJunctionOverlay('), source.indexOf('function getJunctionOverlayBounds(')), context);
  vm.runInContext(source.slice(source.indexOf('// Independent snapshots')), context);
  return { context, timers, run: code => vm.runInContext(code, context) };
}

test('snapshots reconcile signals, preserve raw aspect text and clear removed signals', () => {
  const { run } = setup();
  run(`renderInfrastructure({worldLoaded:true, junctions:[], signalsStatus:'ready', sampledAt:0,
    signals:[{id:7,name:'<img onerror=alert(1)>',position:[0,0],heading:90,aspect:'Approach40',disallowPassing:false,isOff:false,operation:'Automatic'}]})`);
  assert.equal(run('signalMarkers.size'), 1);
  assert.equal(run('signalMarkers.get(7).style.fillColor'), '#48b9ff');
  assert.match(run('signalMarkers.get(7).label.textContent'), /Approach40/);
  assert.equal(run('signalMarkers.get(7).label.innerHTML'), undefined);
  run(`renderInfrastructure({worldLoaded:true,junctions:[],signals:[],signalsStatus:'unavailable',sampledAt:0})`);
  assert.equal(run('signalMarkers.size'), 0);
  assert.equal(run('infrastructureFresh()'), true);
  run('renderInfrastructure({worldLoaded:false})');
  assert.equal(run('infrastructureFresh()'), false);
});

test('junctions tolerate missing tracks and more than two branches, then remove obsolete geometry', () => {
  const { run } = setup();
  run(`renderInfrastructure({worldLoaded:true,junctions:[{id:0,position:[0,0],branches:['a','b','c'],selectedBranch:2}],signals:[],signalsStatus:'ready',sampledAt:0})`);
  assert.match(run('junctions[0].marker.label.textContent'), /→ c/);
  run(`renderInfrastructure({worldLoaded:true,junctions:[],signals:[],signalsStatus:'ready',sampledAt:0})`);
  assert.equal(run('junctions.length'), 0);
});

test('network failures mark infrastructure unavailable and schedule another poll', async () => {
  const { run, timers } = setup();
  await run('pollInfrastructure()');
  assert.equal(run('infrastructureFresh()'), false);
  assert.match(run('infrastructureStatus.textContent'), /offline/);
  assert.deepEqual(timers, [5000, 500]);
});

test('culled junctions update their stored icon and AI lock without an attached DOM node', () => {
  const { run } = setup();
  run(`junctions.push({marker:{setIcon(icon){this.icon=icon;return this;},getElement(){return null;},bindTooltip(){}},branches:['a','b'],signature:JSON.stringify([[0,0],['a','b']])})`);
  run(`renderInfrastructure({worldLoaded:true,junctions:[{id:0,position:[0,0],branches:['a','b'],selectedBranch:1}],signals:[],signalsStatus:'ready',sampledAt:0,aiTraffic:{status:'ready',trains:[],junctionLocks:[{junctionId:0,ownerId:'ai'}]}})`);
  assert.equal(run('junctions[0].selectedBranch'),1);
  assert.match(run('junctions[0].marker.icon.className'),/ai-locked/);
  assert.equal(run('infrastructureFresh()'),true);
});
