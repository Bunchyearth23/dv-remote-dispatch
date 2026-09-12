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
  assert.match(client, /company\.create/); assert.match(client, /wallet\.transfer/); assert.match(client, /initial-delivery\.place/); assert.match(client, /action:'assignment\.manage', operation:'cancel'/);
  assert.doesNotMatch(client, /Aucun|Candidater|Compagnies|Flotte|Livraisons|Annuler|indisponible|après|actualisez|Montant|Nom de/);
  assert.equal(calls[0].url, '/bdvm');
});

test('BDVM HTTP bridge stays permissioned, same-origin and main-thread dispatched', () => {
  const root = require('node:path').resolve(__dirname, '..');
  const server = fs.readFileSync(require('node:path').join(root, 'HttpServer.cs'), 'utf8');
  const bridge = fs.readFileSync(require('node:path').join(root, 'BDVMIntegration.cs'), 'utf8');
  assert.match(server, /HasCompanyPermission/); assert.match(server, /CheckMutationOrigin/); assert.match(server, /TransportSecurity\.IsSameOrigin/); assert.match(server, /RunOnMainThread/); assert.match(server, /ContentLength64 > 4096/);
  assert.match(bridge, /FindMod\("BDVM\.Full"\)/); assert.doesNotMatch(bridge, /FindMod\("BDVM"\)/); assert.match(bridge, /MaximumPayloadBytes = 4096/); assert.doesNotMatch(bridge, /BDVM\.Domain/);
  assert.match(server, /case "dispatch"/); assert.match(server, /case "management"/); assert.match(server, /case "bdvm-ui"/); assert.match(server, /RedirectLocation = "\/dispatch"/); assert.match(server, /case "legacy-dispatch"/); assert.match(server, /\/api\/web\/shell/); assert.match(server, /\/api\/modules\/bdvm\.management\/snapshot/); assert.match(server, /SubmitManagementIntent/);
  assert.match(bridge, /GetWebAsset/); assert.match(bridge, /GetManagementState/);
  assert.match(server, /IPAddress\.IsLoopback/); assert.match(server, /isLoopbackRequest/);
  assert.match(bridge, /typeof\(bool\)/);
});

test('legacy Dispatch remains an internal operational surface without Management duplication', () => {
  const root = require('node:path').resolve(__dirname, '..');
  const html = fs.readFileSync(require('node:path').join(__dirname, '..', 'index.html'), 'utf8');
  assert.doesNotMatch(html, /bdvmTab|href="\/management"|res\/bdvm\.js/);
  const bootstrap = fs.readFileSync(require('node:path').join(root, '..', 'dv-company', 'src', 'BDVM.Web', 'Assets', 'bootstrap.js'), 'utf8');
  assert.match(bootstrap, /path==='\/dispatch'/);
  assert.match(bootstrap, /frame\.src='\/legacy-dispatch'/);
  const industry = fs.readFileSync(require('node:path').join(root, 'industrial-dispatch.js'), 'utf8');
  assert.match(html, /industryDispatchTab/); assert.match(html, /empty or already loaded wagons tagged for the origin industry and cargo/);
  assert.match(industry, /operation:'start-manual'/); assert.match(industry, /originFacilityId/); assert.match(industry, /sourceFacilityId===origin\.value/); assert.match(industry, /tag\.cargoId===cargo\.value/); assert.match(industry, /industrial\?\.routes/); assert.match(industry, /pilotCompanyWagons/); assert.match(industry, /carGuid/); assert.match(industry, /a\[href="#industryDispatchTab"\].*refresh\(true\)/); assert.doesNotMatch(industry, /industryDispatchLifetime/); assert.doesNotMatch(industry, /setInterval\(/); assert.doesNotMatch(industry, /bdvm:dispatch-update/);
  assert.match(industry, /companyToggle\.disabled=!company/); assert.match(industry, /if\(!company\)companyToggle\.checked=false/);
  assert.doesNotMatch(industry, /!tag\.loaded/); assert.match(industry, /loadedCargoAmount/);
  assert.doesNotMatch(html, /id="industryDispatchLifetime"/);
  assert.doesNotMatch(industry, /\.(?:AssetId|DisplayName|LastKnownLocation|OriginFacilityId|DestinationFacilityId|CargoId|DeliveredQuantity|Quantity|QuotedUnitValue|Version)\b/);
});

test('browser-facing Remote Dispatch sources contain no banned French UI vocabulary', () => {
  const root = require('node:path').resolve(__dirname, '..');
  const files = ['index.html', 'main.js', 'ai-traffic.js', 'route-planner.js', 'multiplayer.js', 'bdvm.js', 'industrial-dispatch.js', 'AiTrafficData.cs', 'AiTrafficCommands.cs', 'MultiplayerData.cs', 'RoutePlanner.cs', 'RouteGraph.cs', 'TrackIdentity.cs'];
  const banned = /[àâçéèêëîïôùûüœ]|\b(?:aucun|aucune|commande|commandes|conducteur|gare|voie|voies|aiguillage|aiguillages|itinéraire|chargement|partie|réseau|propriétaire|introuvable|refusée|départ|sélection|recharge|vérifiez|choisissez|données|éteint|manœuvre|joueur|hôte|serveur|trafic|connexion|actualiser|indisponible|installé|désactivé|affectation|parcours|préparation|gauche|droite|branche)\b/i;
  for (const file of files) assert.doesNotMatch(fs.readFileSync(require('node:path').join(root, file), 'utf8'), banned, file);
});

test('collapsed Dispatch sidebar keeps every navigation tab visible', () => {
  const css = fs.readFileSync(require('node:path').join(__dirname, '..', 'style.css'), 'utf8');
  assert.match(css, /\.leaflet-sidebar\.collapsed\s*\{[^}]*height:calc\(100vh - 1rem\)[^}]*max-height:calc\(100vh - 1rem\)/s);
  assert.doesNotMatch(css, /\.leaflet-sidebar\.collapsed\s*\{[^}]*(?:148px|160px)/s);
});

test('embedded Dispatch assets cannot remain stale across mod updates', () => {
  const root = require('node:path').resolve(__dirname, '..');
  const server = fs.readFileSync(require('node:path').join(root, 'HttpServer.cs'), 'utf8');
  const resourceRenderer = server.slice(server.indexOf('private static void RenderResource(HttpListenerContext context, string resourceName)'), server.indexOf('private static class ContentTypes'));
  assert.match(resourceRenderer, /Cache-Control"\]\s*=\s*"no-store"/);
});
