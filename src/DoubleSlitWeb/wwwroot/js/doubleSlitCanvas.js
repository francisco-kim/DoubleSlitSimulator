// Canvas + animation glue for the double-slit simulator.
// Loaded both via JSHost.ImportAsync (for [JSImport] fast-path rendering) and
// via IJSRuntime module import (for the requestAnimationFrame loop); ES modules
// are singletons per URL, so both share this state.

const canvases = new Map();

function cssVar(name, fallback) {
  const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return value || fallback;
}

export function initCanvas(id, width, height) {
  const canvas = document.getElementById(id);
  if (!canvas) {
    return;
  }
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d');
  canvases.set(id, { ctx, imageData: ctx.createImageData(width, height) });
}

export function renderFrame(id, view) {
  const state = canvases.get(id);
  if (!state) {
    return;
  }
  // view is a MemoryView over WASM memory; slice() copies it out, which must
  // happen synchronously inside this call.
  state.imageData.data.set(view.slice());
  state.ctx.putImageData(state.imageData, 0, 0);
}

export function clearCanvas(id) {
  const state = canvases.get(id);
  if (state) {
    state.ctx.clearRect(0, 0, state.ctx.canvas.width, state.ctx.canvas.height);
  }
}

// --- static top-view scene: gun, barrier with slits, screen, detectors ---

export function drawScene(id, geometryJson) {
  const state = canvases.get(id);
  if (!state) {
    return;
  }
  const g = JSON.parse(geometryJson);
  const ctx = state.ctx;
  const w = ctx.canvas.width;
  const h = ctx.canvas.height;
  const ink = cssVar('--text-primary', '#0b0b0b');
  const muted = cssVar('--text-muted', '#898781');
  const detector = cssVar('--series-3', '#eda100');

  ctx.clearRect(0, 0, w, h);

  // Electron gun: not a pinpoint emitter — the real source is a wide
  // Gaussian (σₓ) spanning much of the grid, so draw a soft bar fading to
  // nothing at ±sourceHalfWidth instead of implying a point, plus a small
  // solid pointer at its centre so it still reads as "the gun".
  const barLeft = Math.max(0, g.gunX - g.sourceHalfWidth);
  const barRight = Math.min(w, g.gunX + g.sourceHalfWidth);
  // Flat-topped, not peaked: a triangular fade reads as "invisible" well
  // before the true ±sourceHalfWidth edge, so electrons sampled out near
  // that edge would look like they spawn outside the bar. A plateau keeps
  // almost the whole declared width visibly solid.
  const gradient = ctx.createLinearGradient(barLeft, 0, barRight, 0);
  gradient.addColorStop(0, 'transparent');
  gradient.addColorStop(0.12, ink);
  gradient.addColorStop(0.88, ink);
  gradient.addColorStop(1, 'transparent');
  ctx.fillStyle = gradient;
  ctx.fillRect(barLeft, g.gunY - 14, barRight - barLeft, 10);

  ctx.fillStyle = ink;
  ctx.beginPath();
  ctx.moveTo(g.gunX - 6, g.gunY - 16);
  ctx.lineTo(g.gunX + 6, g.gunY - 16);
  ctx.lineTo(g.gunX + 3, g.gunY - 4);
  ctx.lineTo(g.gunX - 3, g.gunY - 4);
  ctx.closePath();
  ctx.fill();

  // Barrier: full-width wall with the open slit gaps cut out.
  const barrierTop = g.barrierY - 1;
  const barrierHeight = Math.max(4, g.barrierThickness + 2);
  const half = g.slitWidth / 2;
  ctx.fillStyle = ink;
  ctx.fillRect(0, barrierTop, g.leftSlitCentre - half, barrierHeight);
  ctx.fillRect(
    g.leftSlitCentre + half, barrierTop,
    g.rightSlitCentre - half - (g.leftSlitCentre + half), barrierHeight);
  ctx.fillRect(g.rightSlitCentre + half, barrierTop, w - (g.rightSlitCentre + half), barrierHeight);

  // Detection screen: a line near the bottom of the top view.
  ctx.strokeStyle = muted;
  ctx.lineWidth = 2;
  ctx.setLineDash([6, 4]);
  ctx.beginPath();
  ctx.moveTo(0, g.screenY + 0.5);
  ctx.lineTo(w, g.screenY + 0.5);
  ctx.stroke();
  ctx.setLineDash([]);

  // Which-slit detectors: a glowing "eye" beside each slit when observing.
  if (g.observed) {
    for (const slitX of [g.leftSlitCentre, g.rightSlitCentre]) {
      const y = g.barrierY + 14;
      const gradient = ctx.createRadialGradient(slitX, y, 1, slitX, y, 12);
      gradient.addColorStop(0, detector);
      gradient.addColorStop(1, 'rgba(237, 161, 0, 0)');
      ctx.fillStyle = gradient;
      ctx.beginPath();
      ctx.arc(slitX, y, 12, 0, 2 * Math.PI);
      ctx.fill();

      ctx.fillStyle = detector;
      ctx.beginPath();
      ctx.ellipse(slitX, y, 6, 4, 0, 0, 2 * Math.PI);
      ctx.fill();
      ctx.fillStyle = ink;
      ctx.beginPath();
      ctx.arc(slitX, y, 2, 0, 2 * Math.PI);
      ctx.fill();
    }
  }
}

// --- electron flight animation + impact dots ---
// All in-flight electrons and impact flashes are drawn by one shared rAF
// animator so that concurrent shots (auto-fire) never fight over clearRect.

const electrons = [];
const flashes = [];
let animatorRunning = false;

export function animateElectron(shotJson) {
  const shot = JSON.parse(shotJson);
  electrons.push({ ...shot, start: performance.now() + (shot.delay || 0) });
  ensureAnimator();
}

export function clearDots(dotsId, fxId) {
  clearCanvas(dotsId);
  clearCanvas(fxId);
  electrons.length = 0;
  flashes.length = 0;
}

function ensureAnimator() {
  if (!animatorRunning) {
    animatorRunning = true;
    requestAnimationFrame(tickAnimations);
  }
}

function tickAnimations(now) {
  const accent = cssVar('--accent', '#2a78d6');

  // Group by canvas so each overlay/fx canvas is cleared exactly once.
  const overlayIds = new Set(electrons.map((e) => e.overlayId));
  for (const id of overlayIds) {
    clearCanvas(id);
  }

  for (let i = electrons.length - 1; i >= 0; i--) {
    const e = electrons[i];
    const t = (now - e.start) / e.duration;
    if (t < 0) {
      continue;   // staggered launch not due yet
    }
    if (t >= 1) {
      // Blocked electrons are absorbed at the wall: nothing is detected, so
      // no screen dot and nothing counted.
      if (!e.blocked) {
        stampDot(e, accent);
      }
      electrons.splice(i, 1);
      continue;
    }
    drawElectron(e, t, accent);
  }

  const fxIds = new Set(flashes.map((f) => f.fxId));
  for (const id of fxIds) {
    clearCanvas(id);
  }
  for (let i = flashes.length - 1; i >= 0; i--) {
    const f = flashes[i];
    const t = (now - f.start) / 450;
    if (t >= 1) {
      flashes.splice(i, 1);
      continue;
    }
    const state = canvases.get(f.fxId);
    if (state) {
      // Freshly landed stripe lights up briefly, then fades.
      const ctx = state.ctx;
      ctx.fillStyle = accent;
      ctx.globalAlpha = 0.8 * (1 - t);
      ctx.fillRect(f.x - 1.25, 0, 2.5, ctx.canvas.height);
      ctx.globalAlpha = 1;
    }
  }

  if (electrons.length > 0 || flashes.length > 0) {
    requestAnimationFrame(tickAnimations);
  } else {
    // Final clear so the last frame's sprites do not linger.
    for (const id of overlayIds) {
      clearCanvas(id);
    }
    animatorRunning = false;
  }
}

function drawElectron(e, t, accent) {
  const state = canvases.get(e.overlayId);
  if (!state) {
    return;
  }
  const ctx = state.ctx;

  if (e.blocked) {
    // Straight line from gun height to the barrier only — absorbed there,
    // rendered muted grey (not the accent blue used for detected electrons),
    // fading out just before impact.
    const x = e.gunX;
    const y = e.gunY + (e.barrierY - e.gunY) * t;
    const alpha = t < 0.75 ? 1 : Math.max(0, (1 - t) / 0.25);
    const gradient = ctx.createRadialGradient(x, y, 0.5, x, y, 5);
    gradient.addColorStop(0, 'rgba(140, 140, 140, 0.85)');
    gradient.addColorStop(1, 'rgba(140, 140, 140, 0)');
    ctx.fillStyle = gradient;
    ctx.globalAlpha = alpha;
    ctx.beginPath();
    ctx.arc(x, y, 5, 0, 2 * Math.PI);
    ctx.fill();
    ctx.globalAlpha = 1;
    return;
  }

  if (!e.showPath) {
    // No trajectory to draw. The source is a wide gun, not a point, so
    // flash its actual width (matching the drawn gun bar) rather than one
    // pixel — and not the whole canvas, which is wider than the source.
    if (t < 0.35) {
      const ft = t / 0.35;
      const left = Math.max(0, e.gunX - e.sourceHalfWidth);
      const right = Math.min(ctx.canvas.width, e.gunX + e.sourceHalfWidth);
      ctx.fillStyle = accent;
      ctx.globalAlpha = 0.35 * (1 - ft);
      ctx.fillRect(left, e.gunY - 6, right - left, 12);
      ctx.globalAlpha = 1;
    }
    return;
  }

  // Real which-path measurement: particle path gun → slit → impact point.
  let x;
  let y;
  if (t < 0.5) {
    const s = t / 0.5;
    x = e.gunX + (e.slitX - e.gunX) * s;
    y = e.gunY + (e.barrierY - e.gunY) * s;
  } else {
    const s = (t - 0.5) / 0.5;
    x = e.slitX + (e.impactX - e.slitX) * s;
    y = e.barrierY + (e.screenY - e.barrierY) * s;
  }

  const gradient = ctx.createRadialGradient(x, y, 0.5, x, y, 6);
  gradient.addColorStop(0, accent);
  gradient.addColorStop(1, 'rgba(0, 0, 0, 0)');
  ctx.fillStyle = gradient;
  ctx.beginPath();
  ctx.arc(x, y, 6, 0, 2 * Math.PI);
  ctx.fill();
}

function stampDot(e, accent) {
  const state = canvases.get(e.dotsId);
  if (!state) {
    return;
  }
  // Each electron leaves a faint full-height stripe; stripes accumulate into
  // the interference pattern.
  const ctx = state.ctx;
  ctx.fillStyle = accent;
  ctx.globalAlpha = 0.22;
  ctx.fillRect(e.dotX - 0.75, 0, 1.5, ctx.canvas.height);
  ctx.globalAlpha = 1;

  flashes.push({ fxId: e.fxId, x: e.dotX, start: performance.now() });
  ensureAnimator();
}

// --- intensity curves on the detection tile ---

export function drawCurves(id, theoryView, histView, showTheory, showHistogram) {
  const state = canvases.get(id);
  if (!state) {
    return;
  }
  const ctx = state.ctx;
  const w = ctx.canvas.width;
  const h = ctx.canvas.height;
  ctx.clearRect(0, 0, w, h);

  if (showHistogram) {
    drawCurve(ctx, histView.slice(), w, h, cssVar('--series-2', '#1baf7a'), []);
  }
  if (showTheory) {
    drawCurve(ctx, theoryView.slice(), w, h, cssVar('--series-3', '#eda100'), [5, 3]);
  }
}

function drawCurve(ctx, values, w, h, colour, dash) {
  let max = 0;
  for (const v of values) {
    max = Math.max(max, v);
  }
  if (max <= 0) {
    return;
  }

  ctx.strokeStyle = colour;
  ctx.lineWidth = 1.5;
  ctx.setLineDash(dash);
  ctx.beginPath();
  for (let x = 0; x < values.length; x++) {
    const px = (x + 0.5) * (w / values.length);
    const py = h - 3 - (values[x] / max) * (h - 10);
    if (x === 0) {
      ctx.moveTo(px, py);
    } else {
      ctx.lineTo(px, py);
    }
  }
  ctx.stroke();
  ctx.setLineDash([]);
}

// --- shared requestAnimationFrame loop driving the .NET side ---

let running = false;

export function startLoop(dotNetRef) {
  if (running) {
    return;
  }
  running = true;
  const frame = async () => {
    if (!running) {
      return;
    }
    try {
      await dotNetRef.invokeMethodAsync('OnAnimationFrame');
    } catch {
      running = false;
      return;
    }
    if (running) {
      requestAnimationFrame(frame);
    }
  };
  requestAnimationFrame(frame);
}

export function stopLoop() {
  running = false;
}

function triggerDownload(url, filename) {
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
}

export function downloadCanvasPng(id, filename) {
  const canvas = document.getElementById(id);
  if (!canvas) {
    return;
  }
  canvas.toBlob((blob) => {
    const url = URL.createObjectURL(blob);
    triggerDownload(url, filename);
    URL.revokeObjectURL(url);
  }, 'image/png');
}
