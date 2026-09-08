const initialZoom = 20;
const earthCircumference = 40e6;
const metersToDegrees = 360 / earthCircumference;

const movingMarkers = new Map();
function queueMotion(marker, data, render) {
  const previous = marker.lastMotion;
  if (previous && previous[0] === data.position[0] && previous[1] === data.position[1] && previous[2] === data.rotation && previous[3] === data.length) return;
  marker.lastMotion = [data.position[0], data.position[1], data.rotation, data.length];
  if (!marker.motion) marker.motion = new MotionTrack();
  marker.motion.push(data.position, data.rotation, performance.now());
  movingMarkers.set(marker, render);
}
function animateMarkers(now) {
  movingMarkers.forEach((render, marker) => {
    const pose = marker.motion.at(now);
    if (pose) render(pose);
    if (marker.motion.settled(now)) movingMarkers.delete(marker);
  });
  if (markerToFollow && movingMarkers.has(markerToFollow))
    map.panTo(markerToFollow.getBounds().getCenter(), { animate: false });
  requestAnimationFrame(animateMarkers);
}

/////////////////////
// map

const canvasRenderer = L.canvas();
const mapBounds = [[0, 0], [0.15, 0.15]];
const maxBounds = [[-0.02, -0.02], [0.17, 0.17]];
const map = L.map('map', {
  preferCanvas: true,
  minZoom: 13,
  maxBounds: maxBounds,
  tap: false,
  zoomControl: false,
})
.fitBounds(mapBounds);
L.control.scale().addTo(map);
const zoomHome = new L.Control.ZoomHome({
  position: 'topleft',
  zoomInText: '<i class="fas fa-search-plus"></i>',
  zoomHomeText: '<i class="fas fa-user"></i>',
  zoomHomeTitle: 'Zoom to player(s)',
  zoomOutText: '<i class="fas fa-search-minus"></i>',
}).addTo(map);

let markerToFollow;
const viewportOverlays = new Map();
function refreshOverlayVisibility(layer, minZoom) {
  const visible = map.getZoom() >= minZoom && map.getBounds().pad(0.2).intersects(layer.getBounds());
  if (visible && !map.hasLayer(layer)) layer.addTo(map);
  else if (!visible && map.hasLayer(layer)) map.removeLayer(layer);
}
function registerOverlay(layer, minZoom) {
  viewportOverlays.set(layer, minZoom);
  refreshOverlayVisibility(layer, minZoom);
  return layer;
}
map.on('moveend zoomend', () => viewportOverlays.forEach((zoom, layer) => refreshOverlayVisibility(layer, zoom)));
map.addEventListener('mousedown', stopFollowing);

function setMarkerToFollow(marker) {
  markerToFollow = marker;
  map.panTo(marker.getBounds().getCenter());
}

function stopFollowing() {
  markerToFollow = undefined;
}

function zoomToAllPlayers() {
  const bounds = new L.LatLngBounds();
  playerMarkers.forEach(marker => bounds.extend(marker.getBounds()));
  multiplayerView.markers.forEach(marker => bounds.extend(marker.getLatLng()));
  if (bounds.isValid()) map.fitBounds(bounds, { maxZoom: initialZoom, padding:[60,60] });
}

map.addEventListener('zoomhome', () => {
  stopFollowing();
  zoomToAllPlayers();
});

/////////////////////
// settings

document.getElementById('themeDropdown')
  .addEventListener('input', e => {
    if (e.target.value === 'dark') {
      document.getElementById('map').classList.add('dark');
    } else {
      document.getElementById('map').classList.remove('dark');
    }
  });

function getCarColorMode() {
  return document.getElementById('carColorDropdown').value;
}

document.getElementById('carColorDropdown')
  .addEventListener('input', () => {
    updateAllCarColors();
    updateJobListColors();
  });

/////////////////////
// sidebar

const sidebar = L.control.sidebar({ autopan: true, container: 'sidebar' }).addTo(map);

const tablesort = new Tablesort(document.getElementById('carList'));
const carListBody = document.getElementById('carListBody');

function createCarRow(carId) {
  const row = document.createElement('tr');
  row.setAttribute('id', `carList-${carId}`);
  row.classList.add('interactive');
  carListBody.append(row);
  updateCarRow(carId);
  row.addEventListener('click', _ => followCar(carId, false) );
}

function removeCarRow(carId) {
  const row = document.getElementById(`carList-${carId}`);
  if (row)
    row.remove();
}

function updateCarRow(carId) {
  const row = document.getElementById(`carList-${carId}`);
  if (!row)
    return;
  const jobId = carJobIds.has(carId) ? carJobIds.get(carId) : '';
  const destinationYardId = allJobData.has(jobId) ? allJobData.get(jobId).destinationYardId : '';
  const signature = `${carId}|${jobId}|${destinationYardId}`;
  if (row.dataset.signature === signature) return;
  row.dataset.signature = signature;
  row.innerHTML = `<td>${carId}</td><td>${jobId}</td><td>${destinationYardId}</td>`;
  tablesort.refresh();
}

/////////////////////
// jobs

const CarsPerRow = 3;
const allJobData = new Map();
const carJobIds = new Map();
const jobListBody = document.getElementById('jobListBody');

// https://www.npmjs.com/package/string-hash
function stringHash(str) {
  let hash = 5381, i = str.length;
  while(i) {
    hash = (hash * 33) ^ str.charCodeAt(--i);
  }
  return hash >>> 0;
}

// http://vrl.cs.brown.edu/color
const carColors = [
  '#52ef99', '#c95e9f', '#b1e632', '#7574f5', '#799d10', '#fd3fbe', '#2cf52b', '#d130ff', '#21a708', '#fd2b31',
  '#3eeaef', '#ffc4de', '#069668', '#f9793b', '#5884c9', '#e5d75e', '#96ccfe', '#bb8801', '#6a8b7b', '#a8777c',
];

function colorByHashing(str) {
  return carColors[stringHash(str) % carColors.length];
}

function colorForJobDestination(jobId) {
  const jobData = allJobData.get(jobId);
  if (!jobData)
    return 'gray';
  return colorForYardId(jobData.destinationYardId);
}

function colorForJobType(jobId) {
  const segments = jobId.split('-');
  if (segments.length == 2)
    return 'cornflowerblue';
  const jobType = segments[1];
  switch (jobType) {
  case 'FH': return 'lightgreen';
  case 'LH': return 'khaki';
  case 'PC':
  case 'PE': return 'cornflowerblue';
  case 'PR': return 'mediumpurple';
  case 'SL':
  case 'SU': return 'lightcoral';
  }
}

function colorForJobId(jobId) {
  switch (getCarColorMode()) {
    case 'jobId': return colorByHashing(jobId);
    case 'carType':
    case 'jobType': return colorForJobType(jobId);
    case 'destination': return colorForJobDestination(jobId);
  }
}

function yardIdForTrack(trackId) {
  return (trackId || '').split('-')[0];
}

function jobMatchesFilter(jobId, jobData) {
    const testText = document.getElementById('jobSearchText').value.toUpperCase();
    const activeOnly = document.getElementById('jobActiveOnly').checked;
  function taskFields(task) { return [task.startTrack, task.destinationTrack].concat(task.cars); }
  const fields = [jobId].concat(jobData.tasks.flatMap(taskFields));
  return fields.some(field => String(field || '').toUpperCase().includes(testText)) && (!activeOnly || jobData.isActive);
}

function jobElem(jobId, jobData) {
  function replaceHyphens(s) { return (s || '—').replaceAll('-', '\u2011'); }

  const tbody = document.createElement('tbody');
  tbody.setAttribute('id', `jobList-${jobId}`);

  let row = document.createElement('tr');
  const jobIdCell = document.createElement('th'); 
  jobIdCell.setAttribute('colspan', CarsPerRow);
  jobIdCell.classList.add("jobList-jobHeader");
  jobIdCell.style.background = colorForJobId(jobId);
  jobIdCell.textContent = jobId;

  jobLicensesDiv = document.createElement('div');
  jobLicensesDiv.classList.add('jobList-licenses');
  for (const license of jobData.requiredLicenses) {
      jobLicensesDiv.innerHTML += `<span class="jobList-license"><div class="jobList-licenseBackground"></div><img src="res/licenses.${license}.png" title="${license}"></span>`;
  }
  jobIdCell.appendChild(jobLicensesDiv);
  if (jobData.yardMaster || jobData.error) {
    const note = document.createElement('div');
    note.textContent = jobData.error || (jobData.carsAssigned ? 'Yard Master · wagons affectés' : 'Yard Master · wagons à choisir au chargement');
    jobIdCell.appendChild(note);
  }

  row.appendChild(jobIdCell);
  tbody.appendChild(row);

  row = document.createElement('tr');
  jobMassCell = document.createElement('th');
  jobMassCell.textContent = jobData.yardMaster && !jobData.carsAssigned ? 'Selon wagons' : `${jobData.mass.toFixed(0)} t`;
  jobLengthCell = document.createElement('th');
  jobLengthCell.textContent = jobData.yardMaster && !jobData.carsAssigned ? 'À composer' : `${jobData.length.toFixed(0)} m`;
  jobPaymentCell = document.createElement('th');
  jobPaymentCell.textContent =
    new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
    .format(jobData.basePayment);
  row.append(jobMassCell, jobLengthCell, jobPaymentCell);
  tbody.appendChild(row);

  jobData.tasks.forEach(task => {
    row = document.createElement('tr');
    const startTrackCell = document.createElement('th');
    startTrackCell.classList.add('interactive');
    startTrackCell.textContent = replaceHyphens(task.startTrack);
    startTrackCell.style.background = colorForYardId(yardIdForTrack(task.startTrack));
    startTrackCell.addEventListener('click', () => scrollToTrack(task.startTrack));
    row.appendChild(startTrackCell);

    const arrowCell = document.createElement('th');
    arrowCell.textContent = "\u279C";
    arrowCell.classList.add('jobList-trackSeparator');
    row.appendChild(arrowCell);

    const destinationTrackCell = document.createElement('th');
    destinationTrackCell.classList.add('interactive');
    destinationTrackCell.textContent = replaceHyphens(task.destinationTrack);
    destinationTrackCell.style.background = colorForYardId(yardIdForTrack(task.destinationTrack));
    destinationTrackCell.addEventListener('click', () => scrollToTrack(task.destinationTrack));
    row.appendChild(destinationTrackCell);

    for (let carIndex = 0; carIndex < task.cars.length; carIndex++) {
      if (carIndex % CarsPerRow == 0) {
        tbody.appendChild(row);
        row = document.createElement('tr');
      }
      const carId = task.cars[carIndex];
      const carCell = document.createElement('td');
      carCell.classList.add(`jobList-carCell-${carId}`);
      carCell.classList.add('interactive');
      carCell.textContent = carId;
      carCell.addEventListener('click', () => followCar(carId, false));
      row.appendChild(carCell);
    }
    if (row.children.length < CarsPerRow)
      // add filler cells
      for (let i = 0; i < CarsPerRow - (task.cars.length % CarsPerRow); i++)
        row.appendChild(document.createElement('td'));
    tbody.appendChild(row);
  });

  return tbody;
}

function updateCarJobs() {
  carJobIds.clear();
  allJobData.forEach((jobData, jobId) => {
    jobData.tasks.forEach(task => {
      task.cars.forEach(carId => {
        carJobIds.set(carId, jobId);
      });
    })
  });
  for ([carId, _] of allCarData) {
    updateCarRow(carId);
    updateCarMarker(carId);
  }
}

function updateJobListColors() {
  for (const elem of jobListBody.querySelectorAll('th.jobList-jobHeader')) {
    elem.style.background = colorForJobId(elem.textContent);
  }
}

function updateJobList() {
  for (const elem of Array.from(jobListBody.childNodes))
    elem.remove();
  const sortedJobs = Array.from(allJobData.entries()).sort((a, b) => a[0].localeCompare(b[0]));
  sortedJobs
    .filter(([jobId, jobData]) => jobMatchesFilter(jobId, jobData))
    .forEach(([jobId, jobData]) => jobListBody.appendChild(jobElem(jobId, jobData)));
}

function updateAllJobs(jobs) {
  allJobData.clear();
  Object.entries(jobs).forEach(([jobId, jobData]) => allJobData.set(jobId, jobData));
  updateJobList();
  updateCarJobs();
}

let jobSearchTimeoutId;
function queueJobUpdate() {
    if (jobSearchTimeoutId)
        clearTimeout(jobSearchTimeoutId);
    jobSearchTimeoutId = setTimeout(updateJobList, 100);
}
document.getElementById('jobSearchText').addEventListener('input', e => {
    queueJobUpdate();
});
document.getElementById('jobActiveOnly').addEventListener('change', e => {
    queueJobUpdate();
})

/////////////////////
// track

const trackPolyLines = new Map();

function colorForYardId(yardId) {
  switch (yardId) {
    case 'CME': return '#686868';
    case 'CMS': return '#4e554e';
    case 'CP': return '#583d3d';
    case 'CS': return '#97adc2';
    case 'CW': return '#a7a7a7';
    case 'FF': return '#77a6e3';
    case 'FM': return '#ddaa4d';
    case 'FRC': return '#92b66a';
    case 'FRS': return '#609161';
    case 'GF': return '#c97fa2';
    case 'HB': return '#816c94';
    case 'HMB': return '#816c94';
    case 'IME': return '#b66861';
    case 'IMW': return '#9a5847';
    case 'MB': return '#988c5f';
    case 'MF': return '#dc885b';
    case 'MFMB': return '#dc885b';
    case 'OR': return '#935478';
    case 'OWC': return '#555a62';
    case 'OWN': return '#625d55';
    case 'SM': return '#7b8394';
    case 'SW': return '#cda888';
  }
}

function createTrackLabel(trackId, position, angle) {
  const size = 0.0002;
  const bounds = [[position[0] - size, position[1] - size], [position[0] + size, position[1] + size]];
  const rotation = `rotate(${-angle})`;

  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', trackId)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', '-50 -10 100 20');
  svg.innerHTML =
    `<text text-anchor="middle" dominant-baseline="central" transform="${rotation}" font-family="Arial" font-weight="bold" fill="steelblue" stroke="black" stroke-width="0.25px">${trackId.slice(trackId.indexOf('-') + 1)}</text>`;
  registerOverlay(L.svgOverlay(svg, bounds, { renderer: canvasRenderer })
  .addTo(map)
  .setZIndex(1000), 17);
}

function pointDistance(p1, p2) {
  const d0 = p1[0] - p2[0];
  const d1 = p1[1] - p2[1];
  return Math.sqrt(d0 * d0 + d1 * d1);
}

function pointLerp(p1, p2, a) {
  return [
    (p2[0] - p1[0]) * a + p1[0],
    (p2[1] - p1[1]) * a + p1[1]
  ];
}

function createLocation(start, end, mid, a) {
  return [
    (end[0] - start[0]) * a + mid[0],
    (end[1] - start[1]) * a + mid[1]
  ];
}

function createTrackLabels(trackId, coords) {
  const length = pointDistance(coords[0], coords[coords.length - 1]);
  const midIndex = Math.floor(coords.length / 2); 
  const beforeMid = (midIndex % 2 == 1) ? coords[midIndex] : coords[midIndex - 1];
  const mid = (midIndex % 2 == 1) ? coords[midIndex] : pointLerp(coords[midIndex - 1], coords[midIndex], 0.5);
  const afterMid = (midIndex % 2 == 1) ? coords[midIndex + 1] : coords[midIndex];
  const midGap = pointDistance(beforeMid, afterMid);

  const angle = ((Math.atan2(afterMid[0] - beforeMid[0], afterMid[1] - beforeMid[1]) * 180 / Math.PI) + 270) % 180 - 90;

  if (coords.length > 5) {
    createTrackLabel(trackId, createLocation(beforeMid, afterMid, mid, length / midGap *  0.3), angle);
    createTrackLabel(trackId, createLocation(beforeMid, afterMid, mid, length / midGap * -0.3), angle);
  } else {
    createTrackLabel(trackId, mid, angle);
  }
}

const tracksReady = fetch(new URL('/track', location))
.then(resp => resp.json())
.then(tracks => {
  Object.entries(tracks).forEach(([trackId, coords]) => {
    const isSiding = !trackId.includes('#');
    const polyline = L.polyline(coords, {
      color: isSiding ? 'slategray' : 'lightsteelblue',
      interactive: false,
      renderer: canvasRenderer,
    }).addTo(map);
    polyline.on('click', () => routePlannerView.pickTrack(trackId));
    trackPolyLines.set(trackId, polyline);
    if (isSiding)
      createTrackLabels(trackId, coords)
  });
});

/////////////////////
// junctions

let junctions = [];
const junctionsReady = tracksReady
.then(_ => fetch(new URL('/junction', location)))
.then(resp => resp.json())
.then(allJunctionData =>
  junctions = allJunctionData.map((data, index) => ({
    marker: createJunctionMarker(data.position, index),
    branches: data.branches,
  }))
);

function toggleJunction(junctionId) {
  if (!infrastructureFresh() || !multiplayerCanCommand) return;
  if (aiJunctionLocks.has(junctionId)) {
    infrastructureStatus.textContent = 'Aiguillage verrouillé par AITraffic';
    return;
  }
  fetch(new URL(`/junction/${junctionId}/toggle`, location), { method: 'POST' })
  .then(resp => { if (!resp.ok) throw new Error(resp.status === 409 ? 'Verrou AITraffic actif ou indisponible' : `HTTP ${resp.status}`); return resp.json(); })
  .then(selectedBranch => updateJunctionOverlay(junctionId, selectedBranch))
  .catch(err => { infrastructureStatus.textContent = `Commande refusée : ${err.message}`; });
}

const junctionCanvasSize = 30;

function createJunctionShape(selectedBranch) {
  return `<g opacity="70%"><rect x="${-junctionCanvasSize/2}" y="${-junctionCanvasSize}" width="${junctionCanvasSize}" height="${junctionCanvasSize*2}" fill="red"/>` +
    (
      selectedBranch == 0 ? `<line x1="${junctionCanvasSize/2}" y1="${junctionCanvasSize}" x2="${-junctionCanvasSize/2}" y2="${-junctionCanvasSize}" stroke="white" stroke-width="10"/>` :
      selectedBranch == 1 ? `<line x1="${-junctionCanvasSize/2}" y1="${junctionCanvasSize}" x2="${junctionCanvasSize/2}" y2="${-junctionCanvasSize}" stroke="white" stroke-width="10"/>`
      : ''
    ) +
    `<rect x="${-junctionCanvasSize/2}" y="${-junctionCanvasSize}" width="${junctionCanvasSize}" height="${junctionCanvasSize*2}" fill="none" stroke="black" stroke-width="2%"/></g>`;
}

function createJunctionLabel(junctionId) {
  return `<span class="dispatch-junction-label">J-${junctionId}</span>`;
}

function createJunctionOverlay(junctionId) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', `J-${junctionId}`)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-junctionCanvasSize/2} ${-junctionCanvasSize} ${junctionCanvasSize} ${junctionCanvasSize*2}`);
  svg.innerHTML = createJunctionShape(null) + createJunctionLabel(junctionId);
  return svg;
}

function updateJunctionOverlay(junctionId, selectedBranch) {
  const junction = junctions[junctionId]
  if (!junction) return;
  if (junction.selectedBranch === selectedBranch) return;
  const changed = junction.selectedBranch !== undefined;
  junction.selectedBranch = selectedBranch;
  setJunctionIcon(junction, junctionId, changed);
  junction.routes?.forEach((route, index) => route?.setStyle({
    color: index === selectedBranch ? '#39f1dd' : '#77818f',
    opacity: index === selectedBranch ? 1 : 0.45,
    dashArray: index === selectedBranch ? null : '3 7', weight: index === selectedBranch ? 8 : 3
  }));
  const label = document.createElement('span');
  label.textContent = `J-${junctionId} → ${junction.branches[selectedBranch] ?? 'inconnu'}`;
  junction.marker.bindTooltip(label);
}

function setJunctionIcon(junction, id, changed = false) {
  // Leaflet destroys Marker DOM when viewport culling removes the layer.
  // setIcon also stores the appearance while detached, for its next onAdd.
  junction.marker.setIcon(L.divIcon({
    className: 'dispatch-junction' + (junction.aiLocked ? ' ai-locked' : '') + (changed ? ' junction-changed' : ''),
    iconSize: [72,72], iconAnchor: [36,36],
    html: createJunctionDirection(junction, junction.selectedBranch) + createJunctionLabel(id)
  }));
}

function getJunctionOverlayBounds(position) {
  const size = metersToDegrees * 5;
  return [ [ position[0] - size, position[1] - size/2], [position[0] + size, position[1] + size/2] ];
}

function createJunctionMarker(p, junctionId) {
  const marker = L.marker(p, {icon:L.divIcon({className:'dispatch-junction',iconSize:[72,72],iconAnchor:[36,36],html:'?'}),zIndexOffset:500})
    .addEventListener('click', () => toggleJunction(junctionId) )
    .addTo(map);
  marker.getBounds = () => L.latLngBounds(getJunctionOverlayBounds(p));
  return registerOverlay(marker, 16);
}

function createJunctionDirection(junction, selected) {
  const directions = (junction.routes || []).map(route => {
    if (!route) return null;
    const points = route.getLatLngs();
    const first = points[0], next = points.find(p => p.lat !== first.lat || p.lng !== first.lng);
    if (!next) return null;
    const dx = next.lng-first.lng, dy = first.lat-next.lat, length = Math.hypot(dx,dy);
    return {x:dx/length,y:dy/length};
  });
  const direction = directions[selected];
  if (!direction) return '<span class="junction-side">?</span>';
  const average = directions.reduce((a,d) => d ? {x:a.x+d.x,y:a.y+d.y} : a, {x:0,y:0});
  const cross = average.x*direction.y-average.y*direction.x;
  const side = Math.abs(cross)<0.001 ? 0 : Math.sign(cross);
  const label = directions.length === 2 && directions.every(Boolean) && side ? (side<0?'GAUCHE':'DROITE') : 'BRANCHE '+(selected+1);
  const angle = Math.atan2(direction.y,direction.x)*180/Math.PI;
  // Shift toward the selected side, keeping the arrow parallel to that rail.
  const offsetX = -direction.y*side*12, offsetY = direction.x*side*12;
  return `<svg viewBox="-38 -38 76 76" aria-hidden="true"><circle r="3" fill="white" stroke="#10202c" stroke-width="2"/>
    <g transform="translate(${offsetX} ${offsetY}) rotate(${angle})"><path class="junction-arrow" d="M -24 -6 L 7 -6 L 7 -15 L 27 0 L 7 15 L 7 6 L -24 6 Z" fill="#fff34b" stroke="#10202c" stroke-width="3" stroke-linejoin="round"/></g></svg><span class="junction-side">${label}</span>`;
}

function updateAllJunctions(states) {
  states.forEach((state, index) => updateJunctionOverlay(index, state))
}

/////////////////////
// following

function followCar(carId, shouldScroll) {
  setMarkerToFollow(carMarkers.get(carId));

  for (const row of carListBody.querySelectorAll('.following'))
    row.classList.remove('following');
  const carListRow = document.getElementById(`carList-${carId}`)
  carListRow.classList.add('following');
  if (shouldScroll)
    carListRow.scrollIntoView({ block: 'center' });

  for (const elem of jobListBody.querySelectorAll('.following'))
    elem.classList.remove('following');
  const jobListElems = jobListBody.querySelectorAll(`.jobList-carCell-${carId}`);
  for (const elem of jobListElems) {
    elem.classList.add('following');
    elem.closest('tbody').classList.add('following');
  }
  if (shouldScroll && jobListElems.length > 0)
    jobListElems[0].scrollIntoView({ block: 'center' });
}

/////////////////////
// player

const playerMarkers = new Map();

function getPlayerOverlayBounds(position) {
  const size = metersToDegrees * 2;
  return [ [ position[0] - size, position[1] - size], [position[0] + size, position[1] + size] ];
}

function updatePlayerOverlays(data) {
  const existingPlayerIds = Array.from(playerMarkers.keys());
  // Remove markers from disconnected players
  existingPlayerIds
  .filter(id => !data.hasOwnProperty(id))
  .forEach(id => {
    removePlayerOverlay(id);
  });
  // Add markers for new players
  Object.entries(data)
  .filter(([id]) => !existingPlayerIds.includes(id))
  .forEach(([id, playerData]) => {
    createPlayerMarker(id, playerData);
  });
  Object.entries(data).forEach(([id, playerData]) => {
    const polygonElem = document.getElementById(`playerPolygon-${id}`);
    const marker = playerMarkers.get(id);
    queueMotion(marker, playerData, pose => {
      polygonElem.setAttribute('transform', `rotate(${pose.rotation})`);
      marker.setLatLng(pose.position);
    });
  });
}

function removePlayerOverlay(id) {
  const marker = playerMarkers.get(id);
  movingMarkers.delete(marker);
  if (markerToFollow === marker) stopFollowing();
  marker?.remove();
  playerMarkers.delete(id);
}

function createPlayerOverlay(id, playerData) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '-15 -15 30 30');
  const polygon = document.createElementNS(svg.namespaceURI, 'polygon');
  polygon.setAttribute('id', `playerPolygon-${id}`);
  polygon.setAttribute('fill', playerData.color);
  polygon.setAttribute('fill-opacity', '100%');
  polygon.setAttribute('stroke', '#fff');
  polygon.setAttribute('stroke-width', '1.5');
  polygon.setAttribute('points', '0,-10 10,10 0,5 -10,10');
  svg.appendChild(polygon);
  return svg;
}

function createPlayerMarker(id, playerData) {
  const marker = L.marker(playerData.position,
    { icon: L.divIcon({html:createPlayerOverlay(id, playerData),className:'dispatch-player',iconSize:[36,36],iconAnchor:[18,18]}),
      zIndexOffset:2000, interactive: true, bubblingMouseEvents: false })
    .addEventListener('click', e => setMarkerToFollow(e.target))
    .addTo(map);
  marker.getBounds = () => L.latLngBounds([marker.getLatLng(), marker.getLatLng()]);
  const label = document.createElement('span');
  label.textContent = id === 'player' ? 'Vous · joueur local' : id;
  marker.bindTooltip(label, {permanent:true,direction:'right',offset:[20,0],className:'dispatch-player-label'});
  playerMarkers.set(id, marker);
}

function scrollToTrack(trackId) {
  stopFollowing();
  const polyLine = trackPolyLines.get(trackId);
  if (polyLine)
    map.panTo(polyLine.getCenter());
}

fetch(new URL('/player', location))
.then(resp => resp.json())
.then(data => {
  updatePlayerOverlays(data);
  zoomToAllPlayers();
});

/////////////////////
// loco control

const locoIdSelect = document.getElementById('locoControlLocoId');
function updateLocoList() {
  const selected = locoIdSelect.value;
  for (const elem of Array.from(locoIdSelect.children))
    elem.remove();
  const locoIds = Array.from(allCarData.entries())
    .filter(([_, carData]) => carData.canBeControlled)
    .map(([id, _]) => id.slice(2));
  locoIds.sort();
  for (const id of locoIds) {
    const option = document.createElement('option');
    option.textContent = id;
    locoIdSelect.appendChild(option);
  }
  if (locoIds.includes(selected)) locoIdSelect.value = selected;
}

function isReverserButtonActive(faButton) {
  return faButton.querySelector('svg').getAttribute('data-prefix') == 'fas';
}

function updateReverserButtons(reverser) {
  const reverseButton = document.querySelector('#locoControlReverserReverseButton svg');
  const newReverseStyle = reverser < 0.5 ? 'fas' : 'far';
  if (reverseButton.getAttribute('data-prefix') != newReverseStyle)
    reverseButton.setAttribute('data-prefix', newReverseStyle);

  const forwardButton = document.querySelector('#locoControlReverserForwardButton svg');
  const newForwardStyle = reverser > 0.5 ? 'fas' : 'far';
  if (forwardButton.getAttribute('data-prefix') != newForwardStyle)
    forwardButton.setAttribute('data-prefix', newForwardStyle);
}

const locoBrakePipeDisplay = document.getElementById('locoControlBrakePipe');
const locoSpeedDisplay = document.getElementById('locoControlForwardSpeed');
const locoTrainBrakeInput = document.getElementById('locoControlTrainBrakeInput');
const locoIndependentBrakeInput = document.getElementById('locoControlIndependentBrakeInput');
const locoReverserReverseButton = document.getElementById('locoControlReverserReverseButton');
const locoReverserForwardButton = document.getElementById('locoControlReverserForwardButton');
const locoThrottleInput = document.getElementById('locoControlThrottleInput');
const locoControlCoupleButton = document.getElementById('locoControlCoupleButton');
const locoControlUncoupleButton = document.getElementById('locoControlUncoupleButton');
const locoControlUncoupleSelect = document.getElementById('locoControlUncoupleSelect');

function updateCouplingControls(carData) {
  const canCouple = carData.canCouple;
  const carsInFront = carData.carsInFront;
  const carsInRear = carData.carsInRear;

  locoControlCoupleButton.disabled = !canCouple;
  locoControlUncoupleButton.disabled = carsInFront === 0 && carsInRear === 0;

  const signature = `${carsInFront}|${carsInRear}`;
  if (locoControlUncoupleSelect.consistSignature === signature) {
    return;
  }
  locoControlUncoupleSelect.consistSignature = signature;

  const options = [];
  for (let i = carsInFront; i >= 1; i--)
    options.push(i);
  for (let i = 1; i <= carsInRear; i++)
    options.push(-i);
  locoControlUncoupleSelect.replaceChildren(...options.map(i => {
    const option = document.createElement('option');
    option.setAttribute('value', i);
    option.textContent = i >= 0 ? `\u002b${i}` : `\u2212${-i}`;
    return option;
  }));
}

function getControlledLocoGuid() {
  return allCarData.get(`L-${locoIdSelect.value}`)?.guid;
}

function getControlledLocoData() {
  const guid = getControlledLocoGuid();
  if (guid) {
    return fetch(`/car/${guid}`, location)
    .then(resp => resp.json());
  }
}

let locoTrainBrakeEditing = false;
let locoIndependentBrakeEditing = false;
let locoThrottleEditing = false;

function updateLocoTrainBrakeInput(carData) {
  if (locoTrainBrakeEditing)
    return;
  locoTrainBrakeInput.value = carData.trainBrake * 100;
}

function updateLocoIndependentBrakeInput(carData) {
  if (locoIndependentBrakeEditing)
    return;
  locoIndependentBrakeInput.value = carData.independentBrake * 100;
}

function updateLocoThrottleInput(carData) {
  if (locoThrottleEditing)
    return;
  locoThrottleInput.value = carData.throttle * 100;
}

let locoDisplayPending = false;
async function updateLocoDisplay() {
  if (locoDisplayPending || document.hidden) return;
  const guid = getControlledLocoGuid();
  const status = document.getElementById('locoCommandStatus');
  if (!guid) {
    status.textContent = 'Aucune locomotive contrôlable disponible.';
    locoBrakePipeDisplay.textContent = locoSpeedDisplay.textContent = '—';
    return;
  }
  locoDisplayPending = true;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 5000);
  try {
    const response = await fetch(`/car/${guid}`, {signal:controller.signal,cache:'no-store'});
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const carData = await response.json();
    if (guid !== getControlledLocoGuid()) return;
    if (status.textContent.startsWith('Lecture indisponible') || status.textContent.startsWith('Aucune locomotive')) status.textContent = '';
    locoBrakePipeDisplay.textContent = carData.brakePipe.toFixed(1);
    locoSpeedDisplay.textContent = carData.forwardSpeed.toFixed(0);
    updateLocoTrainBrakeInput(carData);
    updateLocoIndependentBrakeInput(carData);
    updateReverserButtons(carData.reverser);
    updateLocoThrottleInput(carData);
    updateCouplingControls(carData);
  } catch (error) { status.textContent = `Lecture indisponible : ${error.message}`; }
  finally { clearTimeout(timeout); locoDisplayPending = false; }
}

let locoControlRefreshIntervalId;
locoIdSelect.addEventListener('change', updateLocoDisplay);
sidebar.on("content", e => {
  clearInterval(locoControlRefreshIntervalId);
  if (e.id == "locoControlTab") {
    locoControlRefreshIntervalId = setInterval(updateLocoDisplay, 1000 / 9);
  }
});
sidebar.on("closing", e => {
  clearInterval(locoControlRefreshIntervalId);
  locoControlRefreshIntervalId = undefined;
})

function sendLocoCommand(command) {
  const status = document.getElementById('locoCommandStatus');
  if (!multiplayerCanCommand) { status.textContent = 'Commandes indisponibles ici : vérifiez le panneau Multiplayer et la connexion.'; return; }
  const guid = getControlledLocoGuid();
  if (guid) {
    fetch(new URL(`/car/${guid}/control?${command}`, location), { method: 'POST' })
      .then(response => { status.textContent = response.ok ? '' : response.status === 409 ? 'Commande refusée : autorité réseau indisponible ou train occupé.' : `Commande refusée (${response.status}).`; })
      .catch(() => { status.textContent = 'Connexion perdue ; commande non confirmée.'; });
  }
}

function rangeCommandSender(parameter) {
  return e => sendLocoCommand(`${parameter}=${e.target.value / 100}`);
}

locoTrainBrakeInput.addEventListener('input', rangeCommandSender('trainBrake'));
locoIndependentBrakeInput.addEventListener('input', rangeCommandSender('independentBrake'));
locoReverserReverseButton.addEventListener('click', e =>
  sendLocoCommand(`reverser=${isReverserButtonActive(locoReverserReverseButton) ? 0.5 : 0}`));
locoReverserForwardButton.addEventListener('click', e =>
  sendLocoCommand(`reverser=${isReverserButtonActive(locoReverserForwardButton) ? 0.5 : 1}`));
locoThrottleInput.addEventListener('input', rangeCommandSender('throttle'));
locoControlCoupleButton.addEventListener('click', e =>
  sendLocoCommand('couple=0'));
locoControlUncoupleButton.addEventListener('click', e =>
  sendLocoCommand(`uncouple=${locoControlUncoupleSelect.value}`));

locoTrainBrakeInput.addEventListener("mousedown", () => locoTrainBrakeEditing = true);
locoTrainBrakeInput.addEventListener("mouseup", () => {
  locoTrainBrakeEditing = false;
  updateLocoDisplay();
});
locoIndependentBrakeInput.addEventListener("mousedown", () => locoIndependentBrakeEditing = true);
locoIndependentBrakeInput.addEventListener("mouseup", () => {
  locoIndependentBrakeEditing = false;
  updateLocoDisplay();
});
locoThrottleInput.addEventListener("mousedown", () => locoThrottleEditing = true);
locoThrottleInput.addEventListener("mouseup", () => {
  locoThrottleEditing = false;
  updateLocoDisplay();
});


/////////////////////
// cars

const carWidthMeters = 3;
const carWidthPx = 20;
const svgPixelsPerMeter = carWidthPx / 3;

const allCarData = new Map();
const carMarkers = new Map();

function getCarColor(carId) {
  const jobId = carJobIds.get(carId);

  switch (getCarColorMode()) {
  case 'jobId':
    return jobId ? colorByHashing(jobId) : 'gray';
  case 'jobType':
    return jobId ? colorForJobType(jobId) : 'gray';
  case 'destination':
    return jobId ? colorForJobDestination(jobId) : 'gray';
  case 'carType':
    return colorByHashing(carId.slice(0,3));
  }
}

function updateCarColor(carId) {
  const carMarker = carMarkers.get(carId);
  const rect = carMarker.getElement().querySelector('rect');
  if (rect)
    rect.setAttribute('fill', getCarColor(carId));
}

function updateAllCarColors() {
  carMarkers.forEach((_, carId) => updateCarColor(carId));
}

const locoShapeNoseDepth = 10;

function createCarShape(carId, carData) {
  const isLoco = carId.slice(0,2) == 'L-';
  const lengthPx = carData.length * svgPixelsPerMeter;
  const svg = isLoco
    ? `<polygon points="${-lengthPx/2},-${carWidthPx/2} ${-lengthPx/2},${carWidthPx/2} ${lengthPx/2-locoShapeNoseDepth},${carWidthPx/2} ${lengthPx/2},0 ${lengthPx/2-locoShapeNoseDepth},-${carWidthPx/2}" fill="goldenrod" fill-opacity="70%" stroke="black" stroke-width="1%"/>`
    : `<rect x="${-lengthPx/2}" y="-10" width="${lengthPx}" height="20" fill-opacity="70%" stroke="black" stroke-width="1%"/>`;
  return svg;
}

function createCarLabel(carId, carData) {
  const isLoco = carId.slice(0,2) == 'L-';
  const jobId = carJobIds.get(carId);
  const lengthPx = carData.length * svgPixelsPerMeter;
  const rotation = carData.rotation >= 180 ? 'rotate(180)' : '';
  if (isLoco)
    return `<text transform="translate(-3 0) ${rotation}" text-anchor="middle" dominant-baseline="central" font-size="12" font-weight="bold">${carId}</text>`;
  const jobIdLabel =
    !jobId ? ""
    : jobId.split('-').length == 3 ? jobId.slice(-5,-3) + jobId.slice(-2)
    : jobId.split('-').join('');
  const jobIdText = `<text x="${-lengthPx/2 + 5}" transform="${rotation}" dominant-baseline="central" font-size="16">${jobIdLabel}</text>`
  const carIdText =
    `<text y="-0.5em" y="1" transform="${rotation} translate(${lengthPx/2 - 5})" dominant-baseline="central" text-anchor="end" font-size="8" font-family="monospace" font-weight="bold">` +
      `<tspan x="0">${carId.slice(0,-3).replaceAll('-', '')}</tspan>` +
      `<tspan x="0" dy="1em">${carId.slice(-3)}</tspan>` +
    '</text>';
  return jobIdText + carIdText;
}

function createCarOverlay(carId, carData) {
  const lengthPx = carData.length * svgPixelsPerMeter;
  const carCanvasMajor = Math.sqrt(lengthPx / 2 * lengthPx / 2 + carWidthPx / 2 * carWidthPx / 2);
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', carId);
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-carCanvasMajor} ${-carWidthPx/2} ${carCanvasMajor*2} ${carWidthPx}`);
  return svg
}

function updateCarMarker(carId) {
  const marker = carMarkers.get(carId);
  if (!marker)
    return;
  const carData = allCarData.get(carId);
  queueMotion(marker, carData, pose => {
    marker.options.rotationAngle = pose.rotation - 90;
    marker.setBounds(getCarOverlayBounds({ ...pose, length: carData.length }));
    refreshOverlayVisibility(marker, carId.startsWith('L-') ? 0 : 16);
  });
  const signature = `${carData.length}|${carData.rotation >= 180}|${carJobIds.get(carId)}|${getCarColor(carId)}`;
  if (marker.appearance !== signature) {
    marker.appearance = signature;
    marker.getElement().innerHTML = createCarShape(carId, carData) + createCarLabel(carId, carData);
    updateCarColor(carId);
  }
}

function getCarOverlayBounds(carData) {
  const position = carData.position;
  const length = metersToDegrees * carData.length;
  const width = metersToDegrees * carWidthMeters;
  return [ [ position[0] - width/2, position[1] - length/2], [position[0] + width/2, position[1] + length/2] ];
}

function createNewCar(carId, carData) {
  allCarData.set(carId, carData);
  createCarRow(carId);
  const overlay = L.svgOverlay(
    createCarOverlay(carId, carData),
    getCarOverlayBounds(carData),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('mouseup', e => followCar(carId, true))
    .addTo(map);
  carMarkers.set(carId, overlay);
  registerOverlay(overlay, carId.startsWith('L-') ? 0 : 16);
  updateCarMarker(carId);
}

function updateCar(carId, carData) {
  allCarData.set(carId, carData);
  updateCarRow(carId);
  updateCarMarker(carId);
}

function removeCar(carId) {
  removeCarRow(carId);
  const marker = carMarkers.get(carId);
  if (marker) {
    movingMarkers.delete(marker);
    viewportOverlays.delete(marker);
    if (markerToFollow === marker) stopFollowing();
    marker.remove();
    carMarkers.delete(carId);
  }
  allCarData.delete(carId);
}

function updateAllCars(updateCarData) {
  Object.entries(updateCarData).forEach(([carId, carData]) => {
    if (!carMarkers.has(carId))
      createNewCar(carId, carData);
    else
      updateCar(carId, carData);
  });
  for ([carId, _] of carMarkers)
    if (!updateCarData[carId])
      removeCar(carId);
  updateLocoList();
}

function updateCars(cars) {
  Object.entries(cars).forEach(([carId, carData]) =>
    updateCar(carId, carData));
}

/////////////////////
// events

function uuidv4() {
  return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, c =>
    (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
  );
}
const sessionId = uuidv4();
const updateInterval = 100;
let updateStart;
let updateFailures = 0;
let updateController;

function updateOnce() {
  updateStart = performance.now();
  updateController = new AbortController();
  const timeout = setTimeout(() => updateController.abort(), 30000);
  return fetch(new URL(`/updates/${sessionId}`, location), { cache: 'no-store', signal: updateController.signal })
  .then(resp => { if (!resp.ok) throw new Error(`HTTP ${resp.status}`); return resp.json(); })
  .then(updateData => {
    Object.entries(updateData).forEach(([tag, data]) => {
      switch (tag) {
      case 'cars':
        updateAllCars(data);
        break;
      case 'jobs':
        updateAllJobs(data);
        break;
      case 'junctions':
        updateAllJunctions(data);
        break;
      case 'player':
        updatePlayerOverlays(data);
        break;
      default:
        const segments = tag.split('-');
        switch (segments[0]) {
        case 'trainset': updateCars(data); break;
        case 'carguid': updateCar(data.id, data); break;
        }
      }
    });
  }).finally(() => { clearTimeout(timeout); updateController = undefined; });
}

function updateLoop() {
  if (document.hidden) { setTimeout(updateLoop, 1000); return; }
  updateOnce()
  .then(_ => {
    updateFailures = 0;
    const timeToNextUpdate = (updateStart + updateInterval) - performance.now();
    setTimeout(updateLoop, timeToNextUpdate);
  }).catch(() => {
    updateFailures++;
    const backoff = Math.min(10000, 500 * Math.pow(2, updateFailures));
    setTimeout(updateLoop, backoff + Math.random() * 250);
  });
}

document.addEventListener('visibilitychange', () => {
  if (document.hidden && updateController) updateController.abort();
});

junctionsReady.then(_ => {
  updateLoop();
});

// Independent snapshots keep infrastructure live even when no game events fire.
const signalLayer = L.layerGroup().addTo(map);
const signalRenderer = new SignalCanvas({ padding: 0.2 });
const signalMarkers = new Map();
const aiJunctionLocks = new Map();
let multiplayerCanCommand = false;
const multiplayerView = new MultiplayerView({ map,
  move: (marker, player, render) => {
    if (!marker.motion) marker.motion = new MotionTrack(550);
    queueMotion(marker, player, render);
  }, removeMotion: marker => movingMarkers.delete(marker) });
const routePlannerView = new RoutePlannerView({ map, tracks: trackPolyLines });
const aiTrafficView = new AiTrafficView({ map, tracks: trackPolyLines, signals: signalMarkers,
  follow: train => carMarkers.has(train.carId) ? followCar(train.carId, false) : map.panTo(train.position),
  move: (marker, train, render) => {
    if (!marker.motion) marker.motion = new MotionTrack(550);
    queueMotion(marker, train, render);
  }, removeMotion: marker => movingMarkers.delete(marker) });
L.control.layers(null, { 'Signaux DVSignals': signalLayer, 'Trains AI': aiTrafficView.layer, 'Réservations / itinéraire AI': aiTrafficView.routes, 'Joueurs multiplayer': multiplayerView.layer }).addTo(map);
const infrastructureControl = L.control({ position: 'bottomleft' });
let infrastructureStatus;
infrastructureControl.onAdd = () => {
  const panel = L.DomUtil.create('div', 'infrastructure-status');
  infrastructureStatus = L.DomUtil.create('div', '', panel);
  infrastructureStatus.textContent = 'Connexion à l’infrastructure…';
  const legend = L.DomUtil.create('div', 'infrastructure-legend', panel);
  legend.textContent = 'Rouge : passage interdit • Bleu : passage non interdit • Gris : éteint / inconnu';
  L.DomEvent.disableClickPropagation(panel);
  return panel;
};
infrastructureControl.addTo(map);
let lastInfrastructureUpdate = 0;
let infrastructureAvailable = false;
function infrastructureFresh() {
  return infrastructureAvailable && performance.now() - lastInfrastructureUpdate < 3000;
}
setInterval(() => {
  const stale = !infrastructureFresh();
  document.getElementById('map').classList.toggle('infrastructure-stale', stale);
  document.getElementById('map').classList.toggle('multiplayer-readonly', !multiplayerCanCommand);
  if (lastInfrastructureUpdate && stale) infrastructureStatus.textContent = 'Données périmées — reconnexion en cours';
}, 500);

function renderInfrastructure(data) {
  if (!data.worldLoaded) {
    infrastructureAvailable = false;
    infrastructureStatus.textContent = 'En attente du chargement de la partie';
    signalLayer.clearLayers();
    signalMarkers.clear();
    aiTrafficView.update({status:'world-unavailable'});
    multiplayerCanCommand = false;
    multiplayerView.update({status:'world-unavailable'});
    aiJunctionLocks.clear();
    return;
  }
  data.junctions.forEach(data => {
    let junction = junctions[data.id];
    const signature = JSON.stringify([data.position, data.branches]);
    if (!junction || junction.signature !== signature) {
      junction?.marker.remove();
      if (junction) viewportOverlays.delete(junction.marker);
      junction?.routes?.forEach(route => route?.remove());
      junction = junctions[data.id] = {
        marker: createJunctionMarker(data.position, data.id), branches: data.branches, signature,
        routes: data.branches.map(id => {
          const track = trackPolyLines.get(id);
          if (!track) return null;
          let points = track.getLatLngs().slice();
          const position = L.latLng(data.position);
          if (position.distanceTo(points[0]) > position.distanceTo(points[points.length - 1])) points.reverse();
          // Highlight only the turnout end; the other end can belong to another junction.
          return L.polyline(points.slice(0, 2), { renderer: canvasRenderer, interactive: false, className: 'junction-route' }).addTo(map);
        })
      };
    }
    updateJunctionOverlay(data.id, data.selectedBranch);
  });
  while (junctions.length > data.junctions.length) {
    const junction = junctions.pop();
    junction.marker.remove();
    viewportOverlays.delete(junction.marker);
    junction.routes?.forEach(route => route?.remove());
  }
  const seen = new Set();
  data.signals.forEach(signal => {
    seen.add(signal.id);
    const signature = JSON.stringify(signal);
    if (signalMarkers.get(signal.id)?.signature === signature) return;
    // Blue deliberately means only that DVSignals does not prohibit passing.
    // A custom aspect may still impose speed restrictions; its exact ID is shown.
    const color = signal.isOff || signal.disallowPassing == null ? '#939ba6' :
      signal.disallowPassing ? '#ff4e55' : '#48b9ff';
    const style = { fillColor: color, heading: signal.heading };
    let marker = signalMarkers.get(signal.id);
    if (!marker) {
      marker = new SignalSymbol(signal.position, { renderer: signalRenderer, radius: 9, ...style }).addTo(signalLayer);
      signalMarkers.set(signal.id, marker);
    } else marker.setLatLng(signal.position).setStyle(style);
    marker.signature = signature;
    const label = document.createElement('div');
    label.textContent = `${signal.name} · ${signal.isOff ? 'Éteint' : signal.aspect ?? 'Inconnu'} · ${signal.operation}${signal.shunting ? ' · Manœuvre' : ''}`;
    marker.bindTooltip(label);
  });
  signalMarkers.forEach((marker, id) => {
    if (!seen.has(id)) { signalLayer.removeLayer(marker); signalMarkers.delete(id); }
  });
  aiTrafficView.update(data.aiTraffic);
  multiplayerCanCommand = data.multiplayer?.canCommand === true;
  multiplayerView.update(data.multiplayer);
  aiJunctionLocks.clear();
  if (data.aiTraffic?.status === 'ready') (data.aiTraffic.junctionLocks || []).forEach(item => aiJunctionLocks.set(item.junctionId, item));
  junctions.forEach((junction, id) => {
    const lock = aiJunctionLocks.get(id);
    const owner = lock && data.aiTraffic.trains.find(train => train.id === lock.ownerId)?.carId;
    const signature = `${junction.selectedBranch}|${Boolean(lock)}|${owner}`;
    if (junction.lockSignature === signature) return;
    junction.lockSignature = signature;
    if (junction.aiLocked !== Boolean(lock)) {
      junction.aiLocked = Boolean(lock);
      setJunctionIcon(junction, id);
    }
    const label = document.createElement('span');
    label.textContent = `J-${id} → ${junction.branches[junction.selectedBranch] ?? '?'}${lock ? ` · Verrou AI : ${owner || 'propriétaire inconnu'}` : ''}`;
    junction.marker.bindTooltip(label);
  });
  lastInfrastructureUpdate = performance.now() - (Number(data.captureWallMs) || 0);
  infrastructureAvailable = true;
  infrastructureStatus.textContent = `${data.junctions.length} jonctions · ${data.signals.length} signaux · ${data.signalsStatus} · ${new Date(data.sampledAt).toLocaleTimeString()}`;
  infrastructureStatus.title = `Lecture Unity : ${Number(data.captureMainThreadMs || 0).toFixed(2)} ms au total, plus longue tranche : ${Number(data.maxSliceMs || 0).toFixed(2)} ms`;
}

async function pollInfrastructure() {
  if (document.hidden) { setTimeout(pollInfrastructure, 1000); return; }
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 5000);
  try {
    const response = await fetch(new URL('/infrastructure', location), { cache: 'no-store', signal: controller.signal });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    renderInfrastructure(await response.json());
  } catch (error) {
    infrastructureAvailable = false;
    infrastructureStatus.textContent = `Infrastructure indisponible · ${error.message}`;
  } finally {
    clearTimeout(timeout);
    setTimeout(pollInfrastructure, 500);
  }
}
junctionsReady.then(pollInfrastructure).catch(() => {
  infrastructureStatus.textContent = 'Carte indisponible — recharge la page après le chargement de la partie';
});
requestAnimationFrame(animateMarkers);
