(function () {
  const status = document.getElementById('bdvmStatus');
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const rows = (items, render) => items.length ? `<ul>${items.map(x => `<li>${render(x)}</li>`).join('')}</ul>` : '<p>Aucun.</p>';
  const button = (label, action, data = {}) => `<button type="button" data-bdvm-action="${escape(action)}" ${Object.entries(data).map(([k,v]) => `data-${k}="${escape(v)}"`).join(' ')}>${escape(label)}</button>`;
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
  function renderCompanies(state) {
    const actor = state.authorityActor; const companies = state.companies || []; const requests = state.membershipRequests || [];
    const ownCompany = companies.find(company => (company.members || []).includes(actor));
    const companyRows = rows(companies, company => {
      const members = company.members || []; const isLeader = company.LeaderId === actor;
      const controls = ownCompany ? [] : [button('Candidater', 'company.apply', {company:company.CompanyId})];
      if (isLeader) {
        controls.push(button('Inviter un joueur', 'company.invite', {company:company.CompanyId}));
        controls.push(button('Sur candidature', 'company.policy', {company:company.CompanyId, policy:'Application'}));
        controls.push(button('Sur invitation', 'company.policy', {company:company.CompanyId, policy:'InvitationOnly'}));
        for (const member of members.filter(x => x !== actor)) {
          controls.push(button(`Fleet → ${member}`, 'company.permission', {company:company.CompanyId, member, permission:'ManageFleet', enabled:'true'}));
          controls.push(button(`Funds → ${member}`, 'company.permission', {company:company.CompanyId, member, permission:'ManageFunds', enabled:'true'}));
          controls.push(button(`Direction → ${member}`, 'company.transfer-leadership', {company:company.CompanyId, member}));
        }
      }
      return `<strong>${escape(company.Name)}</strong> — ${escape(company.MembershipPolicy)} — chef ${escape(company.LeaderId)} — membres ${members.map(escape).join(', ')} ${controls.join(' ')}`;
    });
    const requestRows = rows(requests.filter(x => x.state === 'Pending'), request => {
      if (request.kind === 'Invitation' && request.PlayerId === actor) return `${escape(request.CompanyId)} vous invite. ${button('Accepter', 'company.respond-invitation', {request:request.RequestId, accept:'true'})} ${button('Refuser', 'company.respond-invitation', {request:request.RequestId, accept:'false'})}`;
      const company = companies.find(x => x.CompanyId === request.CompanyId);
      if (request.kind === 'Application' && company?.LeaderId === actor) return `${escape(request.PlayerId)} candidate chez ${escape(company.Name)}. ${button('Accepter', 'company.decide-application', {request:request.RequestId, accept:'true'})} ${button('Refuser', 'company.decide-application', {request:request.RequestId, accept:'false'})}`;
      return `${escape(request.kind)} — ${escape(request.PlayerId)} — ${escape(request.CompanyId)}`;
    });
    const primaryControls = ownCompany
      ? `${button('Contribuer', 'wallet.transfer', {company:'true'})} ${button('Retirer', 'wallet.transfer', {company:'false'})} ${button('Quitter ma compagnie', 'company.leave')}`
      : button('Créer une compagnie', 'company.create');
    return `<h3>Compagnies</h3>${primaryControls}${companyRows}<h4>Demandes</h4>${requestRows}`;
  }
  function renderFleet(state) {
    return '<h3>Flotte</h3>' + rows(state.fleet || [], x => `${escape(x.DisplayName)} — ${escape(x.kind)} — ${escape(x.state)} — owner ${escape(x.owner)} — operator ${escape(x.operatorRef)} ` + button('Renommer', 'fleet.rename', {asset:x.AssetId, current:x.DisplayName}) + ' ' + button('Disponible', 'fleet.set-state', {asset:x.AssetId, state:'Available'}) + ' ' + button('Stocké', 'fleet.set-state', {asset:x.AssetId, state:'Stored'}));
  }
  function renderMarket(state) {
    const stock = rows(state.marketStock || [], x => `${escape(x.DefinitionId)} @ ${escape(x.LocationId)} — ${escape(x.Available)}/${escape(x.Capacity)}`);
    const listings = rows((state.market || []).filter(x => x.state === 'Available'), x => `${escape(x.DefinitionId)} — ${escape(x.LocationId)} — ${escape(x.Price)} ` + button('Acheter personnellement', 'market.purchase', {listing:x.ListingId, company:'false'}) + ' ' + button('Acheter pour la compagnie', 'market.purchase', {listing:x.ListingId, company:'true'}));
    return `<h3>Catalogue et marché</h3><h4>Stock virtuel</h4>${stock}<h4>Offres</h4>${listings}`;
  }
  function renderDeliveries(state) {
    const tracks = state.deliveryTracks || [];
    return '<h3>Livraisons initiales</h3>' + rows((state.initialDeliveries || []).filter(x => x.state !== 'Delivered'), grant => {
      const controls = tracks.map(track => button(`Placer sur ${track.TrackId}`, 'initial-delivery.place', {grant:grant.GrantId, track:track.TrackId, kind:track.kind})).join(' ');
      return `${(grant.DefinitionIds || []).map(escape).join(' + ')} — ${escape(grant.owner)} — ${escape(grant.state)} — ${escape(grant.ResultCode)} ${controls || '<em>Aucune voie de dépôt/service configurée.</em>'}`;
    });
  }
  function bindActions() {
    document.querySelectorAll('[data-bdvm-action]').forEach(control => control.onclick = () => {
      const d = control.dataset; let payload;
      switch (d.bdvmAction) {
        case 'fleet.rename': { const name = globalThis.prompt?.('Nom BDVM du matériel (48 caractères maximum)', d.current || ''); if (name == null) return; payload = {action:'fleet.rename', assetId:d.asset, displayName:name}; break; }
        case 'fleet.set-state': payload = {action:'fleet.set-state', assetId:d.asset, state:d.state}; break;
        case 'company.create': { const name = globalThis.prompt?.('Nom de la compagnie', ''); if (!name) return; payload = {action:'company.create', name}; break; }
        case 'company.apply': payload = {action:'company.apply', companyId:d.company}; break;
        case 'company.invite': { const target = globalThis.prompt?.('Identité persistante du joueur à inviter', ''); if (!target) return; payload = {action:'company.invite', companyId:d.company, targetPlayerId:target}; break; }
        case 'company.decide-application': payload = {action:'company.decide-application', requestId:d.request, accept:d.accept === 'true'}; break;
        case 'company.respond-invitation': payload = {action:'company.respond-invitation', requestId:d.request, accept:d.accept === 'true'}; break;
        case 'company.leave': payload = {action:'company.leave'}; break;
        case 'company.policy': payload = {action:'company.policy', companyId:d.company, policy:d.policy}; break;
        case 'company.permission': payload = {action:'company.permission', companyId:d.company, memberId:d.member, permission:d.permission, enabled:d.enabled === 'true'}; break;
        case 'company.transfer-leadership': payload = {action:'company.transfer-leadership', companyId:d.company, memberId:d.member}; break;
        case 'wallet.transfer': { const raw = globalThis.prompt?.(d.company === 'true' ? 'Montant personnel → compagnie' : 'Montant compagnie → personnel', '100'); if (!raw) return; const amount = Number(raw); if (!Number.isSafeInteger(amount) || amount <= 0) { status.textContent = 'Le montant doit être un entier positif.'; return; } payload = {action:'wallet.transfer', amount, toCompany:d.company === 'true'}; break; }
        case 'market.purchase': payload = {action:'market.purchase', listingId:d.listing, forCompany:d.company === 'true'}; break;
        case 'initial-delivery.place': payload = {action:'initial-delivery.place', grantId:d.grant, trackId:d.track, targetKind:d.kind}; break;
        case 'assignment.cancel': payload = {action:'assignment.cancel', assignmentId:d.assignment}; break;
        default: return;
      }
      intent(payload).catch(error => status.textContent = error.message);
    });
  }
  async function refresh() {
    try {
      const response = await request('/bdvm', { cache: 'no-store' }); if (!response.ok) throw new Error(`HTTP ${response.status}`); const state = await response.json();
      status.textContent = `Release ${state.release} — autorité ${state.authorityActor} — transport ${state.transportIdentity}`;
      document.getElementById('bdvmFinances').innerHTML = '<h3>Finances</h3>' + rows(state.wallets || [], x => `${escape(x.account)} : ${escape(x.Balance)} (v${escape(x.Version)})`);
      document.getElementById('bdvmCompanies').innerHTML = renderCompanies(state);
      document.getElementById('bdvmFleet').innerHTML = renderFleet(state);
      document.getElementById('bdvmMarket').innerHTML = renderMarket(state);
      document.getElementById('bdvmDeliveries').innerHTML = renderDeliveries(state);
      document.getElementById('bdvmMaintenance').innerHTML = '<h3>Maintenance</h3>' + rows(state.operatingCosts || [], x => `${escape(x.AssetId)} — ${escape(x.action)} — ${escape(x.payer)} — ${escape(x.state)} — coût ${escape(x.ActualCost)} / plafond ${escape(x.MaximumAuthorizedCost)} — ${escape(x.settlement)}`);
      document.getElementById('bdvmLeases').innerHTML = '<h3>Locations</h3>' + rows(state.leases || [], x => `${escape(x.LeaseId)} — ${escape(x.state)} — caution ${escape(x.HeldDeposit)} — dette ${escape(x.OutstandingDebt)}`);
      document.getElementById('bdvmAssignments').innerHTML = '<h3>Missions</h3>' + rows(state.assignments || [], x => `${escape(x.MissionId)} — ${escape(x.kind)} — ${escape(x.state)} — ${escape(x.operatorRef)} ${x.state !== 'Completed' && x.state !== 'Cancelled' ? button('Annuler', 'assignment.cancel', {assignment:x.AssignmentId}) : ''}`);
      bindActions();
    } catch (error) { status.textContent = `BDVM indisponible : ${error.message}`; }
  }
  document.getElementById('bdvmRefresh').addEventListener('click', refresh);
  document.querySelector('a[href="#bdvmTab"]').addEventListener('click', refresh);
})();
