'use strict';

// One canvas for every signal. The renderer clips off-screen symbols and Leaflet
// retains tooltip hit testing without thousands of moving HTML markers.
const SignalCanvas = L.Canvas.extend({
  _initContainer() {
    L.Canvas.prototype._initContainer.call(this);
    this._container.classList.add('signal-canvas');
  },
  _updateSignal(layer) {
    if (!this._drawing || layer._empty()) return;
    const ctx = this._ctx, p = layer._point;
    ctx.save();
    ctx.translate(p.x, p.y);
    ctx.rotate((Number(layer.options.heading) || 0) * Math.PI / 180);
    ctx.beginPath();
    ctx.moveTo(0, -8); ctx.lineTo(6, 6); ctx.lineTo(-6, 6); ctx.closePath();
    ctx.fillStyle = layer.options.fillColor; ctx.fill();
    ctx.strokeStyle = '#18212c'; ctx.lineWidth = 1; ctx.stroke();
    ctx.restore();
  }
});
const SignalSymbol = L.CircleMarker.extend({
  _updatePath() { this._renderer._updateSignal(this); }
});
