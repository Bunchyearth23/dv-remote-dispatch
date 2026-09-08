// Served only by the synthetic preview server, never embedded in the mod.
const report = document.createElement('output');
report.id = 'performanceReport';
report.style.cssText = 'position:fixed;right:12px;bottom:40px;z-index:99999;background:#18212c;color:white;padding:12px;max-width:420px';
const measureButton = document.createElement('button');
measureButton.textContent = 'Mesurer navigation 5 s';
report.append(measureButton);
document.body.append(report);
const renderTimes = [];
const renderOriginal = renderInfrastructure;
renderInfrastructure = function(data) {
  const start = performance.now();
  try { return renderOriginal(data); }
  finally { renderTimes.push(performance.now() - start); }
};
measureButton.onclick = () => {
  measureButton.disabled = true;
  renderTimes.length = 0;
  const times = [], start = performance.now();
  let previous = start;
  function frame(now) {
    times.push(now - previous); previous = now;
    map.panBy([Math.sin((now-start)/300)*4, 1], {animate:false});
    if (now-start < 5000) { requestAnimationFrame(frame); return; }
    const sorted = times.slice().sort((a,b)=>a-b);
    report.textContent = JSON.stringify({frames:times.length, frameP95Ms:sorted[Math.floor(sorted.length*.95)],
      frameMaxMs:Math.max(...times), renderMaxMs:Math.max(0,...renderTimes),
      overlayElements:document.querySelectorAll('.leaflet-overlay-pane svg, .leaflet-marker-pane > *').length});
  }
  requestAnimationFrame(frame);
};
