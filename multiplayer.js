'use strict';
class MultiplayerView {
  constructor({map, move, removeMotion}) {
    this.map = map; this.move = move; this.removeMotion = removeMotion;
    this.layer = L.layerGroup().addTo(map); this.markers = new Map(); this.players = new Map();
    this.status = document.getElementById('multiplayerStatus');
    this.list = document.getElementById('multiplayerPlayers');
  }
  update(state) {
    const labels = { unavailable:'Multiplayer non installé ou désactivé', initializing:'Initialisation multiplayer', disconnected:'Multiplayer déconnecté',
      singleplayer:'Partie solo', host:'Hôte multiplayer', client:'Client multiplayer — commandes sur la carte de l’hôte',
      dedicated:'Serveur dédié — consultation', incompatible:'API multiplayer incompatible', 'world-unavailable':'Partie non chargée' };
    const players = Array.isArray(state?.players) ? state.players : [];
    this.status.textContent = `${labels[state?.status] || 'État multiplayer indisponible'} · ${players.length} autre${players.length > 1 ? 's' : ''} joueur${players.length > 1 ? 's' : ''}`;
    this.players = new Map(players.map(p => [p.id,p]));
    for (const [id,marker] of this.markers) if (!this.players.has(id)) {
      this.removeMotion(marker); this.layer.removeLayer(marker); this.markers.delete(id);
    }
    for (const player of players) {
      let marker = this.markers.get(player.id);
      if (!marker) {
        marker = L.marker(player.position,{zIndexOffset:2100,icon:L.divIcon({className:'dispatch-player multiplayer-player',iconSize:[36,36],iconAnchor:[18,18],
          html:'<svg viewBox="-15 -15 30 30"><path d="M0,-11 L10,10 L0,5 L-10,10 Z" fill="#ffc75b" stroke="white" stroke-width="1.5"/></svg>'})}).addTo(this.layer);
        this.markers.set(player.id,marker);
      }
      const label = `${player.crew ? '['+player.crew+'] ' : ''}${player.name}${player.car ? ' · '+player.car : ''}`;
      if (marker.dispatchLabel !== label) {
        const text = document.createElement('span'); text.textContent = label;
        marker.bindTooltip(text,{permanent:true,direction:'right',offset:[20,0],className:'dispatch-player-label'}); marker.dispatchLabel = label;
      }
      this.move(marker, player, pose => {
        marker.setLatLng(pose.position);
        const arrow = marker.getElement()?.querySelector('svg');
        if (arrow) arrow.style.transform = `rotate(${Number(pose.rotation) || 0}deg)`;
      });
    }
    const signature = JSON.stringify(players.map(p => [p.id,p.name,p.crew,p.car]));
    if (signature === this.signature) return;
    this.signature = signature; this.list.replaceChildren();
    for (const player of players) {
      const button = document.createElement('button'); button.type = 'button';
      button.textContent = `${player.crew ? '['+player.crew+'] ' : ''}${player.name}${player.car ? ' · '+player.car : ''}`;
      button.onclick = () => { const current = this.players.get(player.id); if (current) this.map.panTo(current.position); };
      this.list.append(button);
    }
  }
}
