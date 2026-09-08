// AITraffic telemetry only. Selecting a route never reserves it or moves a switch.
(function(root) {
  class AiTrafficView {
    constructor({ map, tracks, signals, follow, move, removeMotion }) {
      Object.assign(this, { map, tracks, signals, follow, move, removeMotion });
      this.layer = L.layerGroup().addTo(map);
      this.routes = L.layerGroup().addTo(map);
      this.markers = new Map();
      this.state = { status: 'initializing', trains: [], reservations: [], junctionLocks: [] };
      this.selected = null;
      this.routeSignature = '';
      this.listSignature = '';
      this.search = document.getElementById('aiSearch');
      this.list = document.getElementById('aiTrainList');
      this.status = document.getElementById('aiStatus');
      this.detail = document.getElementById('aiDetail');
      this.search.addEventListener('input', () => { this.listSignature = ''; this.renderList(); });
      document.getElementById('aiClearSelection').addEventListener('click', () => this.select(null));
    }
    update(state) {
      this.state = state || { status: 'unavailable', trains: [], reservations: [], junctionLocks: [] };
      const ready = this.state.status === 'ready';
      if (!ready) this.state = { ...this.state, trains: [], reservations: [], junctionLocks: [] };
      this.status.textContent = ready
        ? `${this.state.trains.length} trains · ${this.state.reservations.length} voies réservées · ${this.state.junctionLocks.length} aiguillages verrouillés`
        : `AITraffic : ${this.state.status}`;
      const seen = new Set();
      this.state.trains.forEach(train => {
        seen.add(train.id);
        let marker = this.markers.get(train.id);
        if (!marker) {
          const label = document.createElement('span');
          label.textContent = `AI ${train.carId}`;
          marker = L.marker(train.position, { icon: L.divIcon({ className: 'ai-train-marker', html: label, iconSize: [95, 22], iconAnchor: [48, 30] }) });
          marker.on('click', () => this.select(train.id));
          marker.addTo(this.layer);
          this.markers.set(train.id, marker);
        }
        this.move(marker, train, pose => marker.setLatLng(pose.position));
      });
      this.markers.forEach((marker, id) => {
        if (!seen.has(id)) { this.removeMotion(marker); this.layer.removeLayer(marker); this.markers.delete(id); }
      });
      if (this.selected && !seen.has(this.selected)) this.selected = null;
      this.renderList();
      this.renderRoute();
    }
    select(id) {
      this.selected = id;
      const train = this.state.trains.find(train => train.id === id);
      if (train) this.follow(train);
      this.listSignature = '';
      this.renderList();
      this.renderRoute();
    }
    renderList() {
      const query = this.search.value.trim().toLowerCase();
      const trains = this.state.trains.filter(train => [train.carId, train.origin, train.destination, train.state].join(' ').toLowerCase().includes(query));
      const number = value => Number.isFinite(value) ? Math.round(value) : '—';
      const rows = trains.map(train => ({ id: train.id,
        text: `${train.carId} · ${train.worker ? 'Conducteur engagé' : 'Trafic AI'} · ${train.state}\n${train.origin || '?'} → ${train.destination || '?'} · ${number(train.speedKmh)} / ${number(train.targetSpeedKmh)} km/h` }));
      const signature = JSON.stringify([rows, this.selected]);
      if (signature !== this.listSignature) {
        this.listSignature = signature;
        this.list.replaceChildren();
        rows.forEach(row => {
          const button = document.createElement('button');
          button.type = 'button'; button.className = 'ai-train-row';
          button.textContent = row.text;
          button.setAttribute('aria-pressed', String(row.id === this.selected));
          button.addEventListener('click', () => this.select(row.id));
          this.list.appendChild(button);
        });
      }
      const train = this.state.trains.find(train => train.id === this.selected);
      this.detail.textContent = train
        ? `${train.carId} · Destination : ${train.destinationTrack || '?'} · Distance restante : ${number(train.distanceToDestination)} m · Signal : ${train.signalId ?? '—'} à ${number(train.distanceToSignal)} m`
        : 'Sélectionne un train pour afficher son itinéraire prévu et ses réservations.';
    }
    renderRoute() {
      const train = this.state.trains.find(train => train.id === this.selected);
      const reserved = this.state.reservations.filter(item => !train || item.ownerId === train.id);
      const signature = JSON.stringify([train?.id, train?.routeTracks, reserved]);
      if (signature === this.routeSignature) return;
      this.routeSignature = signature;
      this.routes.clearLayers();
      const draw = (id, color, dashArray, text) => {
        const track = this.tracks.get(id);
        if (!track) return false;
        const label = document.createElement('span'); label.textContent = text;
        L.polyline(track.getLatLngs(), { color, weight: 5, opacity: .8, dashArray, className: 'ai-route' })
          .bindTooltip(label).addTo(this.routes);
        return true;
      };
      let missing = 0;
      new Set(train?.routeTracks || []).forEach(id => { if (!draw(id, '#55bfff', '8 8', `Prévu · ${train.carId} · ${id}`)) missing++; });
      reserved.forEach(item => {
        const owner = this.state.trains.find(train => train.id === item.ownerId);
        if (!draw(item.trackId, '#ffb547', null, `Réservé · ${owner?.carId || 'Propriétaire inconnu'} · ${item.trackId}`)) missing++;
      });
      document.getElementById('aiMissingTracks').textContent = missing ? `${missing} voies sans géométrie : recharge la carte après chargement complet du réseau.` : '';
    }
  }
  root.AiTrafficView = AiTrafficView;
})(typeof globalThis !== 'undefined' ? globalThis : this);
