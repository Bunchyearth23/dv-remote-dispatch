(function () {
  const byId=id=>document.getElementById(id), status=byId('industryDispatchStatus'), origin=byId('industryDispatchOrigin'), destination=byId('industryDispatchDestination'), cargo=byId('industryDispatchCargo'), quantity=byId('industryDispatchQuantity'), wagons=byId('industryDispatchWagons'), dossiers=byId('industryDispatchDossiers');
  if(!status)return;
  let state=null,lastSignature='',refreshing=false,lastRefresh=0;
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const label=(rows,id)=>rows?.find(x=>x.id===id)?.name||id;
  async function json(url,options={}){const response=await fetch(url,{cache:'no-store',credentials:'same-origin',...options});const body=await response.json();if(!response.ok)throw new Error(body.error||`HTTP ${response.status}`);return body}
  function option(select,value,text){const item=document.createElement('option');item.value=value;item.textContent=text;select.append(item)}
  function render(next){state=next;const previous={origin:origin.value,destination:destination.value,cargo:cargo.value};const sites=state.industrial?.sites||[],routes=state.industrial?.routes||[];
    const companyToggle=byId('industryDispatchCompany'),company=(state.companies||[]).find(item=>(item.members||[]).includes(state.authorityActor));
    companyToggle.disabled=!company;if(!company)companyToggle.checked=false;
    byId('industryDispatchOperatorHint').textContent=company?`Personal wagons${companyToggle.checked?' hidden while the company operates.':'; check the box to use company wagons.'}`:'No company membership: this delivery will use and pay your personal fleet.';
    const originIds=[...new Set(routes.map(route=>route.originFacilityId))].sort();origin.replaceChildren(...originIds.map(id=>{const item=document.createElement('option');item.value=id;item.textContent=label(state.locationChoices,id);return item}));if(originIds.includes(previous.origin))origin.value=previous.origin;
    const destinationIds=[...new Set(routes.filter(route=>route.originFacilityId===origin.value).map(route=>route.destinationFacilityId))].sort();destination.replaceChildren(...destinationIds.map(id=>{const item=document.createElement('option');item.value=id;item.textContent=label(state.locationChoices,id);return item}));if(destinationIds.includes(previous.destination))destination.value=previous.destination;
    const route=routes.find(item=>item.originFacilityId===origin.value&&item.destinationFacilityId===destination.value),cargoIds=(route?.cargoIds||[]).slice().sort();cargo.replaceChildren(...cargoIds.map(id=>{const item=document.createElement('option');item.value=id;item.textContent=label(state.cargoChoices,id);return item}));if(cargoIds.includes(previous.cargo))cargo.value=previous.cargo;
    const operatorWagons=new Set(companyToggle.checked?(state.industrial?.pilotCompanyWagons||[]):(state.industrial?.pilotPersonalWagons||[]));
    const tags=new Map((state.rollingStockTags||[]).map(tag=>[tag.assetId,tag]));
    const eligible=(state.fleet||[]).filter(x=>{const tag=tags.get(x.assetId);return x.kind==='FreightWagon'&&x.state==='Available'&&operatorWagons.has(x.assetId)&&tag&&tag.sourceFacilityId===origin.value&&tag.cargoId===cargo.value});
    wagons.innerHTML=eligible.length?eligible.map(x=>{const tag=tags.get(x.assetId),load=tag.loaded?` · loaded ${esc(tag.loadedCargoAmount||'?')}`:' · empty';return `<label><input type="checkbox" value="${esc(x.assetId)}"> ${esc(x.displayName)}${load} · ${esc(tag.lifetime)} · ID ${esc(x.carGuid||x.assetId)} · ${esc(x.lastKnownLocation||'location unknown')}</label>`}).join(''):'<p>No available wagon has a matching industry and cargo tag. Select a wagon on the map and assign its tag from the Cars panel.</p>';
    const active=(state.industrial?.contracts||[]).filter(x=>!['Completed','Cancelled','Expired'].includes(x.state));dossiers.innerHTML=active.length?active.map(x=>{const assigned=(x.assignedWagons||[]).map(w=>state.fleet?.find(f=>f.assetId===w.assetId)?.displayName||w.assetId).join(', ');const aboard=(x.manifests||[]).reduce((sum,m)=>sum+Number(m.onBoardQuantity||0),0);return `<p><strong>${esc(x.dossierId)}</strong><br>${esc(label(state.locationChoices,x.originFacilityId))} → ${esc(label(state.locationChoices,x.destinationFacilityId))} · ${esc(label(state.cargoChoices,x.cargoId))}<br>Delivered ${esc(x.deliveredQuantity)}/${esc(x.quantity)} · aboard ${esc(aboard)} · quoted $${esc(x.quotedUnitValue)}/unit<br>Wagons: ${esc(assigned||'none')} · ${esc(x.state)}</p>`}).join(''):'<p>No active dossier.</p>';
    status.textContent=`Live authoritative state · ${sites.length} company site(s)`;
  }
  async function refresh(force=false){if(refreshing||(!force&&Date.now()-lastRefresh<1000))return;refreshing=true;try{const next=await json('/bdvm'),signature=JSON.stringify([next.industrial?.stocks,next.industrial?.needs,next.industrial?.contracts,next.industrial?.cargoTags,next.fleet]);lastRefresh=Date.now();if(force||signature!==lastSignature){lastSignature=signature;render(next)}}catch(error){status.textContent=`Economy unavailable: ${error.message}`}finally{refreshing=false}}
  async function create(){try{const assetIds=[...wagons.querySelectorAll('input:checked')].map(x=>x.value);if(!origin.value||!destination.value||!cargo.value||origin.value===destination.value||!assetIds.length||!(Number(quantity.value)>0))throw new Error('Choose distinct companies, cargo, a positive quantity, and at least one tagged wagon.');status.textContent='Creating authoritative dossier…';const correlationId=globalThis.crypto?.randomUUID?.()||`${Date.now()}-${Math.random().toString(16).slice(2)}`;await json('/bdvm',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({action:'industry.manage',operation:'start-manual',originFacilityId:origin.value,destinationFacilityId:destination.value,cargoId:cargo.value,quantity:Number(quantity.value),assetIds,forCompany:byId('industryDispatchCompany').checked,correlationId})});quantity.value='';await refresh(true)}catch(error){status.textContent=`Dossier refused: ${error.message}`}}
  origin.addEventListener('change',()=>render(state));destination.addEventListener('change',()=>render(state));byId('industryDispatchCompany').addEventListener('change',()=>render(state));byId('industryDispatchCreate').addEventListener('click',create);byId('industryDispatchRefresh').addEventListener('click',()=>refresh(true));document.querySelector('a[href="#industryDispatchTab"]')?.addEventListener('click',()=>refresh(true));
  // Do not poll the whole authoritative economy while dispatching.  Track and
  // train updates are intentionally frequent; dossier state is refreshed when
  // this panel is opened, explicitly requested, or changed by its own command.
})();

(function () {
  const el = id => document.getElementById(id);
  const panel = el('rollingStockTags');
  if (!panel) return;
  const status = el('rollingStockTagStatus'), mode = el('rollingStockMode'), industry = el('rollingStockIndustry'), cargo = el('rollingStockCargo'), lifetime = el('rollingStockLifetime');
  const modeSave = el('rollingStockModeSave'), cargoSave = el('rollingStockCargoSave');
  let selection = null, asset = null, tagInfo = null, tagState = null, generation = 0, busy = false;
  function disable() { modeSave.disabled = cargoSave.disabled = true; }
  async function request(options) {
    const response = await fetch('/bdvm', {cache:'no-store', credentials:'same-origin', ...options});
    const body = await response.json();
    if (!response.ok || body.status === 'failed') throw new Error(body.error || body.message || `HTTP ${response.status}`);
    return body;
  }
  async function refresh() {
    if (!selection) return;
    const current = ++generation;
    asset = null; disable(); status.textContent = 'Reading rolling stock tags...';
    try {
      const state = await request();
      if (current !== generation) return;
      asset = (state.fleet || []).find(x => x.carGuid && x.carGuid.toLowerCase() === String(selection.carGuid).toLowerCase());
      if (!asset) throw new Error('This rolling stock is not in your managed fleet.');
      const info = (state.rollingStockTags || []).find(x => x.assetId === asset.assetId);
      if (!info) throw new Error('Rolling stock tags are unavailable on this host.');
      tagInfo = info; tagState = state;
      el('rollingStockTagTitle').textContent = `${selection.carId} - ${asset.displayName}`;
      el('rollingStockTagCurrent').textContent = `${asset.state} | Industry: ${info.sourceFacilityId || 'None'} | Cargo: ${info.cargoId || 'No tag'}${info.lifetime ? ` (${info.lifetime})` : ''}`;
      mode.value = asset.state;
      const add = (value, text) => { const option = document.createElement('option'); option.value = value; option.textContent = text; cargo.append(option); };
      const compatible = new Set(info.compatibleCargoIds || []);
      const industries = (state.industrial?.sites || []).map(site => ({id:site.facilityId, cargoIds:(site.providedCargoIds || []).filter(id => compatible.has(id))})).filter(site => site.cargoIds.length);
      industry.replaceChildren();
      const none = document.createElement('option'); none.value = ''; none.textContent = 'Select industry'; industry.append(none);
      for (const site of industries) { const option = document.createElement('option'); option.value = site.id; option.textContent = (state.locationChoices || []).find(x => x.id === site.id)?.name || site.id; industry.append(option); }
      industry.value = industries.some(site => site.id === info.sourceFacilityId) ? info.sourceFacilityId : '';
      const renderCargo = () => {
        const previous = cargo.value;
        cargo.replaceChildren(); add('', industry.value ? 'No cargo tag' : 'Select industry first');
        const selected = industries.find(site => site.id === industry.value);
        for (const id of selected?.cargoIds || []) add(id, (state.cargoChoices || []).find(x => x.id === id)?.name || id);
        cargo.value = selected?.cargoIds.includes(info.cargoId) ? info.cargoId : selected?.cargoIds.includes(previous) ? previous : '';
        cargoSave.disabled = busy || !!info.blockedReason || info.loaded || asset.kind !== 'FreightWagon' || !industry.value || !cargo.value;
      };
      industry.onchange = renderCargo; cargo.onchange = renderCargo; renderCargo();
      lifetime.value = info.lifetime || 'UntilEmpty';
      el('rollingStockCargoFields').hidden = asset.kind !== 'FreightWagon';
      modeSave.disabled = busy || !!info.blockedReason;
      cargoSave.disabled = busy || !!info.blockedReason || info.loaded || asset.kind !== 'FreightWagon' || !industry.value || !cargo.value;
      status.textContent = info.blockedReason || (info.loaded ? 'Unload the wagon before changing its cargo tag.' : asset.state !== 'Available' ? 'Make the wagon available to assign cargo. Its existing cargo tag is retained.' : 'Choose a compatible cargo tag or operating state.');
    } catch (error) { if (current === generation) { asset = null; disable(); status.textContent = error.message; } }
  }
  async function save(kind) {
    if (busy || !asset || (kind === 'cargo' ? cargoSave : modeSave).disabled) return;
    const current = generation;
    const body = {action:kind === 'cargo' ? 'fleet.set-tag' : 'fleet.set-dispatch-state', assetId:asset.assetId, expectedVersion:asset.version,
      correlationId:globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random().toString(16).slice(2)}`};
    if (kind === 'cargo') { body.sourceFacilityId = industry.value; body.cargoId = cargo.value; body.tagLifetime = lifetime.value; } else body.state = mode.value;
    busy = true; disable(); status.textContent = 'Applying tag...';
    try {
      await request({method:'POST', headers:{'Content-Type':'application/json'}, body:JSON.stringify(body)});
      busy = false;
      await refresh();
    } catch (error) {
      busy = false;
      if (current === generation) { asset = null; disable(); status.textContent = `${error.message} Refresh tags before trying again.`; }
      else await refresh();
    }
  }
  window.addEventListener('bdvm:car-selected', event => {
    selection = event.detail; panel.hidden = false;
    sidebar.open('carsTab');
    panel.scrollIntoView({block:'nearest'});
    refresh();
  });
  modeSave.addEventListener('click', () => save('mode'));
  cargoSave.addEventListener('click', () => save('cargo'));
  el('rollingStockTagRefresh').addEventListener('click', refresh);
})();
