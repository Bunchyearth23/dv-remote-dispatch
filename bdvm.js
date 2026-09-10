(function () {
  const status = document.getElementById('bdvmStatus');
  const escape = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const rows = (items, render) => items.length ? `<ul>${items.map(x => `<li>${render(x)}</li>`).join('')}</ul>` : '<p>None.</p>';
  const button = (label, action, data = {}) => `<button type="button" data-bdvm-action="${escape(action)}" ${Object.entries(data).map(([k,v]) => `data-${k}="${escape(v)}"`).join(' ')}>${escape(label)}</button>`;
  async function request(url, options = {}) {
    const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 10000);
    try { return await fetch(url, {...options, signal: controller.signal}); }
    catch (error) { if (error.name === 'AbortError') throw new Error('Timed out after 10 seconds — authoritative state is unknown; refresh before retrying.'); throw error; }
    finally { clearTimeout(timeout); }
  }
  async function intent(body) {
    status.textContent = 'Intent pending — waiting for the authoritative response…';
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
      const controls = ownCompany ? [] : [button('Apply', 'company.apply', {company:company.CompanyId})];
      if (isLeader) {
        controls.push(button('Invite player', 'company.invite', {company:company.CompanyId}));
        controls.push(button('Applications', 'company.policy', {company:company.CompanyId, policy:'Application'}));
        controls.push(button('Invitations only', 'company.policy', {company:company.CompanyId, policy:'InvitationOnly'}));
        for (const member of members.filter(x => x !== actor)) {
          controls.push(button(`Fleet → ${member}`, 'company.permission', {company:company.CompanyId, member, permission:'ManageFleet', enabled:'true'}));
          controls.push(button(`Funds → ${member}`, 'company.permission', {company:company.CompanyId, member, permission:'ManageFunds', enabled:'true'}));
          controls.push(button(`Leadership → ${member}`, 'company.transfer-leadership', {company:company.CompanyId, member}));
        }
      }
      return `<strong>${escape(company.Name)}</strong> — ${escape(company.MembershipPolicy)} — leader ${escape(company.LeaderId)} — members ${members.map(escape).join(', ')} ${controls.join(' ')}`;
    });
    const requestRows = rows(requests.filter(x => x.state === 'Pending'), request => {
      if (request.kind === 'Invitation' && request.PlayerId === actor) return `${escape(request.CompanyId)} invited you. ${button('Accept', 'company.respond-invitation', {request:request.RequestId, accept:'true'})} ${button('Decline', 'company.respond-invitation', {request:request.RequestId, accept:'false'})}`;
      const company = companies.find(x => x.CompanyId === request.CompanyId);
      if (request.kind === 'Application' && company?.LeaderId === actor) return `${escape(request.PlayerId)} applied to ${escape(company.Name)}. ${button('Accept', 'company.decide-application', {request:request.RequestId, accept:'true'})} ${button('Decline', 'company.decide-application', {request:request.RequestId, accept:'false'})}`;
      return `${escape(request.kind)} — ${escape(request.PlayerId)} — ${escape(request.CompanyId)}`;
    });
    const primaryControls = ownCompany
      ? `${button('Contribute', 'wallet.transfer', {company:'true'})} ${button('Withdraw', 'wallet.transfer', {company:'false'})} ${button('Leave company', 'company.leave')}`
      : button('Create company', 'company.create');
    return `<h3>Companies</h3>${primaryControls}${companyRows}<h4>Requests</h4>${requestRows}`;
  }
  function renderFleet(state) {
    return '<h3>Fleet</h3>' + rows(state.fleet || [], x => `${escape(x.DisplayName)} — ${escape(x.kind)} — ${escape(x.state)} — owner ${escape(x.owner)} — operator ${escape(x.operatorRef)} ` + button('Rename', 'fleet.rename', {asset:x.AssetId, current:x.DisplayName}) + ' ' + button('Available', 'fleet.set-state', {asset:x.AssetId, state:'Available'}) + ' ' + button('Stored', 'fleet.set-state', {asset:x.AssetId, state:'Stored'}));
  }
  function renderMarket(state) {
    const stock = rows(state.marketStock || [], x => `${escape(x.DefinitionId)} @ ${escape(x.LocationId)} — ${escape(x.Available)}/${escape(x.Capacity)}`);
    const listings = rows((state.market || []).filter(x => x.state === 'Available'), x => `${escape(x.DefinitionId)} — ${escape(x.LocationId)} — ${escape(x.Price)} ` + button('Buy personally', 'market.purchase', {listing:x.ListingId, company:'false'}) + ' ' + button('Buy for company', 'market.purchase', {listing:x.ListingId, company:'true'}));
    return `<h3>Catalog and market</h3><h4>Virtual stock</h4>${stock}<h4>Listings</h4>${listings}`;
  }
  function renderDeliveries(state) {
    const tracks = state.deliveryTracks || [];
    return '<h3>Initial deliveries</h3>' + rows((state.initialDeliveries || []).filter(x => x.state !== 'Delivered'), grant => {
      const controls = tracks.map(track => button(`Place on ${track.TrackId}`, 'initial-delivery.place', {grant:grant.GrantId, track:track.TrackId, kind:track.kind})).join(' ');
      return `${(grant.DefinitionIds || []).map(escape).join(' + ')} — ${escape(grant.owner)} — ${escape(grant.state)} — ${escape(grant.ResultCode)} ${controls || '<em>No depot or service track is configured.</em>'}`;
    });
  }
  function bindActions() {
    document.querySelectorAll('[data-bdvm-action]').forEach(control => control.onclick = () => {
      const d = control.dataset; let payload;
      switch (d.bdvmAction) {
        case 'fleet.rename': { const name = globalThis.prompt?.('BDVM rolling-stock name (48 characters maximum)', d.current || ''); if (name == null) return; payload = {action:'fleet.rename', assetId:d.asset, displayName:name}; break; }
        case 'fleet.set-state': payload = {action:'fleet.set-state', assetId:d.asset, state:d.state}; break;
        case 'company.create': { const name = globalThis.prompt?.('Company name', ''); if (!name) return; payload = {action:'company.create', name}; break; }
        case 'company.apply': payload = {action:'company.apply', companyId:d.company}; break;
        case 'company.invite': { const target = globalThis.prompt?.('Persistent identity of the player to invite', ''); if (!target) return; payload = {action:'company.invite', companyId:d.company, targetPlayerId:target}; break; }
        case 'company.decide-application': payload = {action:'company.decide-application', requestId:d.request, accept:d.accept === 'true'}; break;
        case 'company.respond-invitation': payload = {action:'company.respond-invitation', requestId:d.request, accept:d.accept === 'true'}; break;
        case 'company.leave': payload = {action:'company.leave'}; break;
        case 'company.policy': payload = {action:'company.policy', companyId:d.company, policy:d.policy}; break;
        case 'company.permission': payload = {action:'company.permission', companyId:d.company, memberId:d.member, permission:d.permission, enabled:d.enabled === 'true'}; break;
        case 'company.transfer-leadership': payload = {action:'company.transfer-leadership', companyId:d.company, memberId:d.member}; break;
        case 'wallet.transfer': { const raw = globalThis.prompt?.(d.company === 'true' ? 'Personal → company amount' : 'Company → personal amount', '100'); if (!raw) return; const amount = Number(raw); if (!Number.isSafeInteger(amount) || amount <= 0) { status.textContent = 'The amount must be a positive integer.'; return; } payload = {action:'wallet.transfer', amount, toCompany:d.company === 'true'}; break; }
        case 'market.purchase': payload = {action:'market.purchase', listingId:d.listing, forCompany:d.company === 'true'}; break;
        case 'initial-delivery.place': payload = {action:'initial-delivery.place', grantId:d.grant, trackId:d.track, targetKind:d.kind}; break;
        case 'assignment.cancel': payload = {action:'assignment.manage', operation:'cancel', assignmentId:d.assignment}; break;
        default: return;
      }
      intent(payload).catch(error => status.textContent = error.message);
    });
  }
  async function refresh() {
    try {
      const response = await request('/bdvm', { cache: 'no-store' }); if (!response.ok) throw new Error(`HTTP ${response.status}`); const state = await response.json();
      const population = state.worldPopulation ? ` — population ${state.worldPopulation.runtimeState}/${state.worldPopulation.ResultCode}` : '';
      status.textContent = `Release ${state.release} — authority ${state.authorityActor} — transport ${state.transportIdentity}${population}`;
      document.getElementById('bdvmFinances').innerHTML = '<h3>Finances</h3>' + rows(state.wallets || [], x => `${escape(x.account)}: ${escape(x.Balance)} (v${escape(x.Version)})`);
      document.getElementById('bdvmCompanies').innerHTML = renderCompanies(state);
      document.getElementById('bdvmFleet').innerHTML = renderFleet(state);
      document.getElementById('bdvmMarket').innerHTML = renderMarket(state);
      document.getElementById('bdvmDeliveries').innerHTML = renderDeliveries(state);
      document.getElementById('bdvmMaintenance').innerHTML = '<h3>Maintenance</h3>' + rows(state.operatingCosts || [], x => `${escape(x.AssetId)} — ${escape(x.action)} — ${escape(x.payer)} — ${escape(x.state)} — cost ${escape(x.ActualCost)} / limit ${escape(x.MaximumAuthorizedCost)} — ${escape(x.settlement)}`);
      document.getElementById('bdvmLeases').innerHTML = '<h3>Leases</h3>' + rows(state.leases || [], x => `${escape(x.LeaseId)} — ${escape(x.state)} — deposit ${escape(x.HeldDeposit)} — debt ${escape(x.OutstandingDebt)}`);
      document.getElementById('bdvmAssignments').innerHTML = '<h3>Assignments</h3>' + rows(state.assignments || [], x => `${escape(x.MissionId)} — ${escape(x.kind)} — ${escape(x.state)} — ${escape(x.operatorRef)} ${x.state !== 'Completed' && x.state !== 'Cancelled' ? button('Cancel', 'assignment.cancel', {assignment:x.AssignmentId}) : ''}`);
      bindActions();
    } catch (error) { status.textContent = `BDVM unavailable: ${error.message}`; }
  }
  document.getElementById('bdvmRefresh').addEventListener('click', refresh);
  document.querySelector('a[href="#bdvmTab"]').addEventListener('click', refresh);
})();
