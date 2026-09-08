(function () {
  const status = document.getElementById('bdvmStatus');
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const rows = (items, render) => items.length ? `<ul>${items.map(x => `<li>${render(x)}</li>`).join('')}</ul>` : '<p>Aucun.</p>';
  async function request(url, options = {}) {
    const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 10000);
    try { return await fetch(url, {...options, signal: controller.signal}); }
    catch (error) { if (error.name === 'AbortError') throw new Error('Timeout après 10 s — state inconnu, actualisez avant retry.'); throw error; }
    finally { clearTimeout(timeout); }
  }
  async function intent(body) {
    status.textContent = 'Intention pending — attente de la réponse autoritaire…';
    const correlationId = globalThis.crypto?.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;
    const response = await request('/bdvm', { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({...body, correlationId}) });
    if (!response.ok) throw new Error((await response.json()).error || `HTTP ${response.status}`);
    await refresh();
  }
  async function refresh() {
    try {
      const response = await request('/bdvm', { cache: 'no-store' }); if (!response.ok) throw new Error(`HTTP ${response.status}`); const state = await response.json();
      status.textContent = `Release ${state.release} — autorité ${state.authorityActor} — transport ${state.transportIdentity}`;
      document.getElementById('bdvmFinances').innerHTML = '<h3>Finances</h3>' + rows(state.wallets || [], x => `${escape(x.account)} : ${escape(x.Balance)} (v${escape(x.Version)})`);
      document.getElementById('bdvmFleet').innerHTML = '<h3>Flotte</h3>' + rows(state.fleet || [], x => `${escape(x.DisplayName)} — ${escape(x.kind)} — ${escape(x.state)} — owner ${escape(x.owner)} — operator ${escape(x.operatorRef)} <button data-fleet="${escape(x.AssetId)}" data-state="Available">Disponible</button> <button data-fleet="${escape(x.AssetId)}" data-state="Stored">Stocké</button>`);
      document.getElementById('bdvmMarket').innerHTML = '<h3>Marché</h3>' + rows(state.market || [], x => `${escape(x.DefinitionId)} — ${escape(x.LocationId)} — ${escape(x.Price)} — ${escape(x.state)}`);
      document.getElementById('bdvmLeases').innerHTML = '<h3>Locations</h3>' + rows(state.leases || [], x => `${escape(x.LeaseId)} — ${escape(x.state)} — caution ${escape(x.HeldDeposit)} — dette ${escape(x.OutstandingDebt)}`);
      document.getElementById('bdvmAssignments').innerHTML = '<h3>Missions</h3>' + rows(state.assignments || [], x => `${escape(x.MissionId)} — ${escape(x.kind)} — ${escape(x.state)} — ${escape(x.operatorRef)} ${x.state !== 'Completed' && x.state !== 'Cancelled' ? `<button data-cancel="${escape(x.AssignmentId)}">Annuler</button>` : ''}`);
      document.querySelectorAll('[data-fleet]').forEach(b => b.onclick = () => intent({action:'fleet.set-state', assetId:b.dataset.fleet, state:b.dataset.state}).catch(e => status.textContent = e.message));
      document.querySelectorAll('[data-cancel]').forEach(b => b.onclick = () => intent({action:'assignment.cancel', assignmentId:b.dataset.cancel}).catch(e => status.textContent = e.message));
    } catch (error) { status.textContent = `BDVM indisponible : ${error.message}`; }
  }
  document.getElementById('bdvmRefresh').addEventListener('click', refresh);
  document.querySelector('a[href="#bdvmTab"]').addEventListener('click', refresh);
})();
