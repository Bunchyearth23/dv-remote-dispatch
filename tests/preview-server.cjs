const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const stress = process.env.DISPATCH_STRESS === '1';
const fixture = stress ? JSON.parse(fs.readFileSync('D:/tmp/dv-live-infrastructure.json','utf8')) : null;
const geometry = stress ? JSON.parse(fs.readFileSync('D:/tmp/dv-live-track.json','utf8')) : null;
let updates = 0;
const sessions = new Set();
http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  const json = data => {res.setHeader('Content-Type','application/json');res.end(JSON.stringify(data));};
  const t = Date.now() / 1000;
  const loco = {guid:'mock',length:18,position:[0.075,0.075],rotation:0,canBeControlled:true,canCouple:true,carsInFront:0,carsInRear:0,brakePipe:5,forwardSpeed:0,reverser:0.5,trainBrake:1,independentBrake:1,throttle:0};
  if(url.pathname.startsWith('/car/')) return json(url.pathname.endsWith('/control') ? {ok:true} : loco);
  const player = {player:{color:'aqua',position:[0.075 + Math.sin(t/5)*0.0001,0.075],rotation:t*5%360}};
  const junctions = fixture?.junctions || [{id:0,position:[0.075,0.075],branches:['#A','#B'],selectedBranch:Math.floor(t/5)%2}];
  const aiTraffic = {status:'ready',version:'0.2.1',trains:[{id:'mock',carId:'L-001',state:'Driving',origin:'Harbor',destination:'Steel Mill',destinationTrack:'#A',worker:true,speedKmh:36,targetSpeedKmh:40,distanceToSignal:180,signalId:0,distanceToDestination:2500,position:player.player.position,rotation:0,routeTracks:['#A','#B']}],reservations:[{trackId:'#A',ownerId:'mock'}],junctionLocks:[{junctionId:0,ownerId:'mock'}]};
  if(url.pathname==='/track') return json(geometry || {'#A':[[0.075,0.075],[0.076,0.075]],'#B':[[0.075,0.075],[0.076,0.076]]});
  if(url.pathname==='/route/catalog') return json({trains:[{id:'player',name:'L-002',origin:'#A',ai:false},{id:'mock',name:'L-001',origin:'#A',ai:true}],tracks:[{id:'#A',length:111},{id:'#B',length:157}]});
  if(url.pathname==='/route/preview') {
    let body=''; req.on('data',chunk=>body+=chunk); req.on('end',()=>{const input=JSON.parse(body);json({token:'synthetic',origin:'#A',destination:input.destination,via:input.via,ai:input.train==='mock',tracks:['#A','#B'],switches:[{id:0,branch:1,change:true}],distance:268,conflicts:[],expiresAt:Date.now()+60000});});return;
  }
  if(url.pathname==='/route/apply') return json({changed:1,message:'Simulation uniquement : aucune commande envoyée au jeu.'});
  if(url.pathname==='/route/assign') return json({changed:0,message:'Simulation : parcours affecté au conducteur AI. Aucune commande envoyée au jeu.'});
  if(url.pathname==='/route/control-ai') return json({message:'Simulation : commande conducteur reçue. Aucune commande envoyée au jeu.'});
  if(url.pathname==='/junction') return json(junctions);
  if(url.pathname==='/player') return json(player);
  if(url.pathname.startsWith('/updates/')) {
    updates++;
    const first = !sessions.has(url.pathname); sessions.add(url.pathname);
    const cars = {'L-001':{...loco,position:player.player.position},'L-002':{...loco,guid:'player',position:[0.0753,0.0753]},'C-001':{guid:'wagon',length:14,position:[0.0754,0.0753],rotation:0}};
    if (stress && (first || updates%10===0)) for(let id=0;id<800;id++)
      cars['C-'+id] = {guid:'car'+id,length:14,position:[0.01+(id%40)*0.0032,0.01+Math.floor(id/40)*0.006],rotation:0};
    const job = {originYardId:'HB',destinationYardId:'SM',tasks:[{startTrack:'#A',destinationTrack:'#B',cars:['C-001']}],requiredLicenses:[],mass:30,length:14,basePayment:1200,isActive:true,yardMaster:true,carsAssigned:true};
    const jobs = {'HB-DH-01':job,'HB-DH-02':{...job,isActive:false,carsAssigned:false,tasks:[{startTrack:'#A',destinationTrack:'#B',cars:[]}]}};
    return json(first || updates%10===0 ? {cars,...(first?{jobs}:{}),player} : {'trainset-1':cars,player});
  }
  if(url.pathname==='/infrastructure') return json({worldLoaded:true,sampledAt:Date.now(),multiplayer:{status:'host',canCommand:true,players:[{id:'mp-1',name:'Conducteur test',crew:'Harbor',position:[0.075,0.075],rotation:0,car:'L-002'}]},aiTraffic,junctions,signalsStatus:'ready',signals:Array.from({length:1842},(_,id)=>({id,name:'TEST '+id,position:[0.074+(id%43)*0.00005,0.074+Math.floor(id/43)*0.00005],heading:0,aspect:'Stop',disallowPassing:true,isOff:false,operation:'Automatic'}))});
  if (url.pathname === '/performance-probe.js') {res.setHeader('Content-Type','text/javascript');res.end(fs.readFileSync(path.join(__dirname,'performance-probe.js')));return;}
  const baseline = url.pathname.startsWith('/baseline/');
  const assetPath = baseline ? url.pathname.slice('/baseline'.length) : url.pathname;
  const file = assetPath==='/'?'index.html':assetPath.startsWith('/res/')?assetPath.slice(5):'';
  if(!['index.html','main.js','motion.js','ai-traffic.js','route-planner.js','multiplayer.js','signal-canvas.js','style.css','leaflet.rotatedImageOverlay.js','icon.svg'].includes(file)) {res.writeHead(404);res.end();return;}
  res.setHeader('Content-Type',file.endsWith('.js')?'text/javascript':file.endsWith('.css')?'text/css':file.endsWith('.svg')?'image/svg+xml':'text/html');
  let content = fs.readFileSync(path.join(baseline ? 'D:/tmp/dv-dispatch-baseline' : root,file));
  if(file==='index.html') {
    content = content.toString().replace('</body>', '<script src="/performance-probe.js"></script></body>');
    if(baseline) content=content.replaceAll('src="res/', 'src="/baseline/res/').replaceAll('href="res/', 'href="/baseline/res/');
  }
  res.end(content);
}).listen(7246,'127.0.0.1',()=>console.log('Synthetic preview: http://127.0.0.1:7246'));
