// Render measured positions with a short buffer. Never predict beyond the last sample.
(function(root) {
  class MotionTrack {
    constructor(delay = 150) { this.delay = delay; this.samples = []; }
    push(position, rotation, time) {
      if (!position || !position.every(Number.isFinite) || !Number.isFinite(rotation)) return;
      const last = this.samples[this.samples.length - 1];
      if (last && time <= last.time) return;
      // Teleports and reconnects snap instead of drawing a fictitious journey.
      if (last && (time - last.time > 1000 || Math.hypot(position[0] - last.position[0], position[1] - last.position[1]) > 0.00225))
        this.samples = [];
      this.samples.push({ position: position.slice(), rotation, time });
      if (this.samples.length > 16) this.samples.shift();
    }
    at(time) {
      if (!this.samples.length) return null;
      const target = time - this.delay;
      while (this.samples.length > 2 && this.samples[1].time <= target) this.samples.shift();
      const a = this.samples[0];
      const b = this.samples.find(sample => sample.time > target) || this.samples[this.samples.length - 1];
      if (target <= a.time || a === b) return a;
      // Find the surrounding pair even if a burst supplied several samples.
      const before = this.samples[this.samples.indexOf(b) - 1] || a;
      const t = Math.max(0, Math.min(1, (target - before.time) / (b.time - before.time)));
      const angle = ((b.rotation - before.rotation + 540) % 360) - 180;
      return { position: before.position.map((v, i) => v + (b.position[i] - v) * t), rotation: before.rotation + angle * t };
    }
    settled(time) { return !this.samples.length || time - this.delay >= this.samples[this.samples.length - 1].time; }
  }
  if (typeof module !== 'undefined') module.exports = MotionTrack;
  else root.MotionTrack = MotionTrack;
})(typeof globalThis !== 'undefined' ? globalThis : this);
