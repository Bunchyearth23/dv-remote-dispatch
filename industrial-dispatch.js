(function () {
  'use strict';
  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  let byGuid=new Map(), contracts=[], receivedAt=0, pending=null;
  const status=document.getElementById('industryDispatchStatus');
  function accept(state){
    const tags=new Map((state.rollingStockTags||[]).map(t=>[t.assetId,t])), assigned=new Map();
    const names=new Map((state.locationChoices||[]).map(x=>[x.id,x.name]));
    contracts=(state.industrial?.contracts||[]).filter(c=>!['Completed','Cancelled','Expired'].includes(c.state));
    for(const c of contracts)for(const w of c.assignedWagons||[])assigned.set(w.assetId,c);
    byGuid=new Map((state.fleet||[]).filter(w=>w.carGuid).map(w=>{const tag=tags.get(w.assetId)||{},dossier=assigned.get(w.assetId);return [w.carGuid.toLowerCase(),{...tag,operatingState:w.state,dossierId:dossier?.dossierId||dossier?.contractId,displayName:dossier?.displayName,destination:dossier?.destinationFacilityId,destinationName:names.get(dossier?.destinationFacilityId)||dossier?.destinationFacilityId}]}));
    receivedAt=Date.now();
    if(status)status.textContent=`Tags and dossiers read at ${new Date(receivedAt).toLocaleTimeString()}. Cargo fill follows train updates. Refresh tags after changes in Management.`;
    const list=document.getElementById('industryDispatchDossiers');
    if(list)list.innerHTML=contracts.map(c=>`<p><a target="_top" href="/management?tab=contracts&dossier=${encodeURIComponent(c.dossierId||c.contractId)}">${esc(c.displayName||c.dossierId||c.contractId)}</a><br>${esc(c.state)} · ${(c.assignedWagons||[]).length} wagons · delivered ${esc(c.deliveredQuantity||0)} / ${esc(c.quantity)}</p>`).join('')||'<p>No active dossier.</p>';
    if(typeof window.dispatchEvent==='function')window.dispatchEvent(new CustomEvent('bdvm:industrial-display'));
  }
  function describe(car={}){
    const tag=byGuid.get(String(car.guid||'').toLowerCase())||{};
    const amount=car.loadedAmount,capacity=car.cargoCapacity,known=typeof amount==='number'&&Number.isFinite(amount)&&amount>=0;
    const ratio=known&&capacity>0?Math.max(0,Math.min(1,amount/capacity)):null;
    const fill=!known?'Unknown':amount<=0.01?'Empty':ratio===null?'Loaded':ratio>=0.99?'Full':'Partial';
    const mismatch=known&&amount>0.01&&tag.cargoId&&car.cargoId&&tag.cargoId!==car.cargoId;
    const color=mismatch?'#ec7777':tag.operatingState==='Maintenance'?'#ce97f2':tag.operatingState==='Stored'?'#a5a6b0':fill==='Full'?'#72c69b':fill==='Partial'||fill==='Loaded'?'#ebba69':tag.cargoId?'#7bb8e6':'#a7a7a7';
    const short=`${mismatch?'! ':''}${fill}${ratio===null?'':` ${Math.round(ratio*100)}%`}`;
    const detail=[short,car.cargoId||tag.cargoId||'No cargo tag',tag.sourceFacilityId?`Tag: ${tag.sourceFacilityId} (${tag.lifetime||''})`:'',tag.destination?`→ ${tag.destinationName}`:'',tag.displayName||tag.dossierId||'',tag.operatingState||'',mismatch?'Cargo differs from tag':''].filter(Boolean).join(' · ');
    return {color,short,detail,dossierId:tag.dossierId,destination:tag.destination||'',tagCargo:tag.cargoId||'',receivedAt};
  }
  async function refresh(){
    if(pending)return pending;
    pending=(async()=>{try{const response=await fetch('/bdvm',{cache:'no-store',credentials:'same-origin'});const state=await response.json();if(!response.ok)throw new Error(state.error||`HTTP ${response.status}`);accept(state)}catch(error){byGuid.clear();contracts=[];receivedAt=0;if(status)status.textContent=`Tags unavailable: ${error.message}`;window.dispatchEvent?.(new CustomEvent('bdvm:industrial-display'))}finally{pending=null}})();return pending;
  }
  globalThis.BdvmIndustrialDisplay=Object.freeze({accept,describe,refresh,escape:esc});
  if(!status)return;
  document.getElementById('industryDispatchRefresh')?.addEventListener('click',refresh);
  document.querySelector('a[href="#industryDispatchTab"]')?.addEventListener('click',refresh);
  // Once per opening, and on explicit actions. Never poll the complete economy.
  document.addEventListener('DOMContentLoaded',refresh,{once:true});
  document.addEventListener('visibilitychange',()=>{if(!document.hidden&&Date.now()-receivedAt>1000)refresh()});
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
      globalThis.BdvmIndustrialDisplay?.accept(state);
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
