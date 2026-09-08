'use strict';

class RoutePlannerView {
  constructor({ map, tracks }) {
    this.map = map;
    this.tracks = tracks;
    this.layer = L.layerGroup().addTo(map);
    this.train = document.getElementById('routeTrain');
    this.destination = document.getElementById('routeDestination');
    this.via = document.getElementById('routeVia');
    this.status = document.getElementById('routeStatus');
    this.applyButton = document.getElementById('routeApply');
    this.catalog = { trains: [], tracks: [] };
    this.revision = 0;
    this.busy = false;
    this.plan = null;
    this.pick = null;
    this.recent = [];
    try {
      const saved = JSON.parse(localStorage.getItem('dispatch.routes.recent') || '[]');
      if (Array.isArray(saved)) this.recent = saved.filter(r => typeof r.destination === 'string' && typeof r.via === 'string').slice(0, 8);
    } catch (_) { /* Browser storage is optional. */ }
    document.getElementById('routeRefresh').onclick = () => this.refresh();
    document.getElementById('routePreview').onclick = () => this.preview();
    this.applyButton.onclick = () => this.apply();
    document.getElementById('routeAiStop').onclick = () => this.controlAi('stop');
    document.getElementById('routeAiResume').onclick = () => this.controlAi('resume');
    this.train.onchange = () => { this.invalidate(); this.showOrigin(); };
    this.destination.oninput = this.via.oninput = () => this.invalidate();
    document.getElementById('routePickDestination').onclick = () => this.startPick(this.destination);
    document.getElementById('routePickVia').onclick = () => this.startPick(this.via);
    document.addEventListener('keydown', e => { if (e.key === 'Escape') this.stopPick(); });
    const container = this.map.getContainer();
    container.addEventListener('mousedown', e => { this.pointerStart = [e.clientX,e.clientY]; }, true);
    container.addEventListener('click', e => {
      if (!this.pick || this.busy || e.button !== 0 || e.target.closest('#sidebar, .leaflet-control')) return;
      // Capture before Leaflet overlays: signals, cars and canvases can obscure
      // the track layer. A picking click must never operate a junction or loco.
      e.preventDefault(); e.stopImmediatePropagation();
      if (this.pointerStart && Math.hypot(e.clientX-this.pointerStart[0],e.clientY-this.pointerStart[1])>5) return;
      this.pickAtPoint(this.map.mouseEventToLayerPoint(e));
    }, true);
    this.renderRecent();
  }
  async request(path, body) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 25000);
    try {
    const response = await fetch('/route/' + path, body === undefined ? { cache: 'no-store', signal: controller.signal } : {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body), cache: 'no-store', signal: controller.signal
    });
    const data = await response.json();
    if (!response.ok) throw new Error(data.error || 'Commande refusée (' + response.status + ').');
    return data;
    } catch (e) {
      if (e.name === 'AbortError') throw new Error('Le serveur ne répond pas. Vérifiez que la partie est chargée puis réessayez.');
      throw e;
    } finally { clearTimeout(timeout); }
  }
  invalidate() {
    this.revision++;
    this.plan = null;
    this.applyButton.disabled = true;
    clearTimeout(this.expiry);
    this.layer.clearLayers();
    document.getElementById('routeSummary').replaceChildren();
    document.getElementById('routeConflicts').replaceChildren();
    this.status.textContent = 'Prévisualisez pour recalculer le parcours et ses conflits.';
  }
  setBusy(value) {
    this.busy = value;
    for (const id of ['routeRefresh', 'routePreview', 'routeTrain', 'routeDestination', 'routeVia', 'routePickDestination', 'routePickVia', 'routeAiStop', 'routeAiResume'])
      document.getElementById(id).disabled = value;
    this.applyButton.disabled = value || !this.plan || this.plan.conflicts.length > 0 || this.plan.expiresAt <= Date.now();
  }
  async refresh() {
    if (this.busy) return;
    this.stopPick();
    this.invalidate();
    this.setBusy(true);
    this.status.textContent = 'Chargement des locomotives…';
    const selected = this.train.value;
    try {
      this.catalog = await this.request('catalog');
      this.train.replaceChildren(new Option('Sélectionner un train…', ''));
      for (const train of this.catalog.trains)
        this.train.add(new Option(`${train.name} · ${train.ai ? 'AI' : 'Joueur'} · ${train.origin}`, train.id));
      if (this.catalog.trains.some(t => t.id === selected)) this.train.value = selected;
      const options = document.getElementById('routeTrackOptions');
      options.replaceChildren();
      const fragment = document.createDocumentFragment();
      for (const track of this.catalog.tracks) fragment.append(new Option(`${track.id} · ${track.length} m`, track.id));
      options.append(fragment);
      this.showOrigin();
      this.status.textContent = `${this.catalog.trains.length} locomotives · ${this.catalog.tracks.length} voies. Choisissez le train puis la destination.${this.catalog.warning ? ' ' + this.catalog.warning : ''}`;
    } catch (e) { this.status.textContent = e.message; }
    finally { this.setBusy(false); }
  }
  showOrigin() {
    const train = this.catalog.trains.find(t => t.id === this.train.value);
    document.getElementById('routeOrigin').textContent = train ? `Départ : ${train.origin} (actualisé lors du calcul).` : '';
    document.getElementById('routeAiControls').hidden = !train?.ai;
    this.applyButton.textContent = train?.ai ? '4. Affecter et démarrer le conducteur AI' : '4. Positionner les aiguillages';
  }
  startPick(input) {
    if (this.busy) return;
    if (this.pick === input) { this.stopPick(); return; }
    this.pick = input;
    this.map.getContainer().classList.add('route-picking');
    this.status.textContent = `Cliquez une voie pour ${input === this.via ? 'le passage intermédiaire' : 'la destination'}. Échap pour annuler.`;
  }
  stopPick() {
    this.pick = null;
    this.tracks.forEach(track => { track.options.interactive = false; });
    this.map.getContainer().classList.remove('route-picking');
  }
  pickTrack(id) {
    if (!this.pick || this.busy) return;
    this.pick.value = id;
    this.stopPick();
    this.invalidate();
    this.status.textContent = `Voie ${id} sélectionnée. Prévisualisez le parcours.`;
  }
  pickAtPoint(point) {
    if (!this.pick || this.busy) return;
    let nearest = null, best = 100; // 10 screen pixels, independent of zoom.
    this.tracks.forEach((track,id) => {
      const points = track.getLatLngs();
      for (let i=1;i<points.length;i++) {
        const a = this.map.latLngToLayerPoint(points[i-1]), b = this.map.latLngToLayerPoint(points[i]);
        const dx=b.x-a.x,dy=b.y-a.y,len=dx*dx+dy*dy;
        const t=len ? Math.max(0,Math.min(1,((point.x-a.x)*dx+(point.y-a.y)*dy)/len)) : 0;
        const distance=(point.x-a.x-t*dx)**2+(point.y-a.y-t*dy)**2;
        if(distance<best) {best=distance;nearest=id;}
      }
    });
    if(nearest !== null) this.pickTrack(nearest);
    else this.status.textContent = 'Aucune voie sous le clic. Zoomez et cliquez plus près du rail. Échap pour annuler.';
  }
  async preview() {
    if (this.busy) return;
    this.stopPick();
    this.invalidate();
    if (!this.train.value || !this.destination.value.trim()) { this.status.textContent = 'Sélectionnez un train et une voie de destination.'; return; }
    const revision = this.revision;
    this.setBusy(true);
    this.status.textContent = 'Calcul du parcours et vérification des conflits…';
    try {
      const plan = await this.request('preview', { train: this.train.value, destination: this.destination.value.trim(), via: this.via.value.trim() });
      if (revision !== this.revision) return;
      this.plan = plan;
      const missing = [];
      const bounds = L.latLngBounds([]);
      for (const id of plan.tracks) {
        const source = this.tracks.get(id);
        if (!source) { missing.push(id); continue; }
        const line = L.polyline(source.getLatLngs(), { color: '#5ce6ba', weight: 7, opacity: 0.85, interactive: false }).addTo(this.layer);
        bounds.extend(line.getBounds());
      }
      if (bounds.isValid()) this.map.fitBounds(bounds.pad(0.15));
      const summary = document.getElementById('routeSummary');
      const description = document.createElement('p');
      description.textContent = `${plan.origin} → ${plan.destination}${plan.via ? ' via ' + plan.via : ''} · ${(plan.distance / 1000).toFixed(2)} km de voies · ${plan.switches.filter(s => s.change).length} aiguillages à changer.`;
      const direction = document.createElement('p');
      direction.textContent = plan.tracks.length > 1 ? `Départ vers la voie ${plan.tracks[1]}. Vérifiez ce sens avant de conduire.` : 'Départ et destination sur la même voie.';
      summary.replaceChildren(description, direction);
      const conflicts = document.getElementById('routeConflicts');
      for (const reason of plan.conflicts) { const li = document.createElement('li'); li.textContent = reason; conflicts.append(li); }
      if (missing.length) { const p = document.createElement('p'); p.textContent = `${missing.length} voies absentes du fond de carte. Rechargez la page.`; summary.append(p); }
      this.applyButton.textContent = plan.ai ? '4. Affecter et démarrer le conducteur AI' : '4. Positionner les aiguillages';
      this.status.textContent = plan.conflicts.length ? 'Parcours préparé, commande bloquée par les conflits ci-dessous.' : plan.ai ? 'Parcours AI prêt. L’affectation remplacera sa destination et demandera le départ. Préparation valable 60 secondes.' : 'Aucun conflit détecté lors du contrôle. Préparation valable 60 secondes.';
      this.expiry = setTimeout(() => { this.applyButton.disabled = true; this.status.textContent = 'Préparation expirée. Recalculez avant de commander.'; }, Math.max(0, plan.expiresAt - Date.now()));
      this.recent = [{ destination: plan.destination, via: plan.via || '' }, ...this.recent.filter(r => r.destination !== plan.destination || r.via !== (plan.via || ''))].slice(0, 8);
      try { localStorage.setItem('dispatch.routes.recent', JSON.stringify(this.recent)); } catch (_) { }
      this.renderRecent();
    } catch (e) { this.status.textContent = e.message; }
    finally { this.setBusy(false); }
  }
  async apply() {
    if (this.busy || this.applyButton.disabled || !this.plan) return;
    const token = this.plan.token;
    const ai = this.plan.ai;
    this.setBusy(true);
    clearTimeout(this.expiry);
    try {
      const result = await this.request(ai ? 'assign' : 'apply', { token });
      this.status.textContent = ai ? result.message : `${result.changed} aiguillages modifiés. ${result.message}`;
    } catch (e) { this.status.textContent = e.message; }
    finally { this.plan = null; this.setBusy(false); }
  }
  async controlAi(action) {
    if (this.busy || !this.train.value) return;
    this.stopPick(); this.invalidate(); this.setBusy(true);
    try {
      const result = await this.request('control-ai', { train: this.train.value, action });
      this.status.textContent = result.message;
    } catch (e) { this.status.textContent = e.message; }
    finally { this.setBusy(false); }
  }
  renderRecent() {
    const container = document.getElementById('routeRecent');
    container.replaceChildren();
    for (const route of this.recent) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = route.destination + (route.via ? ' via ' + route.via : '');
      button.onclick = () => {
        if (this.busy) return;
        this.destination.value = route.destination; this.via.value = route.via; this.invalidate();
      };
      container.append(button);
    }
  }
}
