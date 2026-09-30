// Effects for browsers that run ES modules. The page is complete without this file (old Lumia browsers skip it).

const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)').matches;
const finePointer = matchMedia('(hover: hover) and (pointer: fine)').matches;
const lerp = (a, b, t) => a + (b - a) * t;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
const varCache = new WeakMap();
// Sets a CSS variable only when its text changes (each change restyles and repaints what uses it)
function setVar(el, name, value) {
  let vars = varCache.get(el);
  if (!vars) varCache.set(el, (vars = {}));
  if (vars[name] === value) return;
  vars[name] = value;
  el.style.setProperty(name, value);
}

/* ── Hero: a 3D rig that turns toward the pointer ───────────────────────────── */
function initHero() {
  const hero = document.querySelector('.hero');
  if (!hero) return;
  const rig = hero.querySelector('.rig');
  const pano = hero.querySelector('.panorama span');
  const glare = hero.querySelector('.glare');

  const target = { x: 0, y: 0 };
  const cur = { x: 0, y: 0 };
  const scrollFactor = reduceMotion ? 0 : 0.35; // the panorama title slides slower than the page
  let visible = true;
  let running = false;
  let last = 0;

  if (finePointer && !reduceMotion) {
    window.addEventListener('pointermove', (e) => {
      target.x = (e.clientX / innerWidth) * 2 - 1;
      target.y = (e.clientY / innerHeight) * 2 - 1;
      const r = hero.getBoundingClientRect();
      hero.style.setProperty('--mx', `${e.clientX - r.left}px`);
      hero.style.setProperty('--my', `${e.clientY - r.top}px`);
    }, { passive: true });
    document.documentElement.addEventListener('pointerleave', () => { target.x = 0; target.y = 0; });
  }

  function frame(now) {
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0.016;
    last = now;

    // Touch screens have no hover: the rig drifts slowly on its own instead
    const drifting = !finePointer && !reduceMotion;
    if (drifting) {
      target.x = Math.sin(now / 2600) * 0.35;
      target.y = Math.cos(now / 3400) * 0.2;
    }
    cur.x = lerp(cur.x, target.x, 1 - Math.exp(-dt * 5));
    cur.y = lerp(cur.y, target.y, 1 - Math.exp(-dt * 5));

    setVar(rig, 'transform', `rotateY(${(cur.x * 18).toFixed(2)}deg) rotateX(${(-cur.y * 12).toFixed(2)}deg)`);
    setVar(pano, 'transform', `translate3d(${(-scrollY * scrollFactor - cur.x * 60).toFixed(1)}px, 0, 0)`);
    // Rounded: gradients repaint whenever their value changes, and cur only creeps toward the target
    setVar(glare, '--gx', `${((cur.x + 1) * 50).toFixed(1)}%`);
    setVar(glare, '--gy', `${((cur.y + 1) * 40).toFixed(1)}%`);

    const settled = !drifting && Math.abs(cur.x - target.x) < 0.001 && Math.abs(cur.y - target.y) < 0.001;
    if (visible && !document.hidden && !settled) requestAnimationFrame(frame);
    else { running = false; last = 0; }
  }

  function start() {
    if (running || !visible || document.hidden) return;
    running = true;
    requestAnimationFrame(frame);
  }

  new IntersectionObserver(([entry]) => { visible = entry.isIntersecting; start(); }).observe(hero);
  document.addEventListener('visibilitychange', start);
  window.addEventListener('pointermove', start, { passive: true });
  window.addEventListener('scroll', start, { passive: true });
  start();
}

/* ── Scroll reveal (turnstile) ─────────────────────────────────────────────── */
function initReveal() {
  const items = document.querySelectorAll('[data-reveal]');
  if (reduceMotion) { items.forEach((el) => el.classList.add('in')); return; }
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (!e.isIntersecting) continue;
      e.target.classList.add('in');
      io.unobserve(e.target);
    }
  }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
  items.forEach((el) => {
    el.style.setProperty('--i', [...el.parentElement.children].indexOf(el) % 6);
    io.observe(el);
  });
}

/* ── Live Tiles: flip on their own, tilt under the pointer, tap to flip ────── */
function initTiles() {
  const tiles = [...document.querySelectorAll('.tile')];
  if (!tiles.length) return;

  // Each tile's face is a <button>: its name is the text of both faces, so screen readers read the description too
  const autoTimers = new Map(); // tiles flipped by the timer, and the timeout that turns them back
  const setFlipped = (tile, on) => {
    tile.classList.toggle('flipped', on);
    tile.querySelector('.tile-in').setAttribute('aria-pressed', String(on));
  };

  tiles.forEach((tile) => {
    setFlipped(tile, false);
    tile.querySelector('.tile-in').addEventListener('click', () => {
      // A tile the user flipped stays as they left it: the timer that would turn it back is cancelled
      clearTimeout(autoTimers.get(tile));
      autoTimers.delete(tile);
      tile.dataset.manual = '1';
      setFlipped(tile, !tile.classList.contains('flipped'));
    });

    if (finePointer && !reduceMotion) {
      // WP tilt effect: the tile leans away from where the pointer presses
      tile.addEventListener('pointermove', (e) => {
        const r = tile.getBoundingClientRect();
        const px = (e.clientX - r.left) / r.width - 0.5;
        const py = (e.clientY - r.top) / r.height - 0.5;
        tile.style.setProperty('--ty', `${px * 12}deg`);
        tile.style.setProperty('--tx', `${-py * 12}deg`);
      });
      tile.addEventListener('pointerleave', () => {
        tile.style.setProperty('--ty', '0deg');
        tile.style.setProperty('--tx', '0deg');
      });
    }
  });

  if (reduceMotion) return;
  const inView = new Set();
  const io = new IntersectionObserver((entries) => {
    for (const e of entries) {
      if (e.isIntersecting) inView.add(e.target);
      else inView.delete(e.target);
    }
  }, { threshold: 0.6 });
  tiles.forEach((t) => io.observe(t));

  setInterval(() => {
    if (document.hidden) return;
    // Never under the pointer or keyboard focus, and never one the user has flipped
    const candidates = [...inView].filter((t) => !t.matches(':hover') && !t.contains(document.activeElement)
      && !t.dataset.manual && !autoTimers.has(t) && !t.classList.contains('flipped'));
    if (!candidates.length) return;
    const tile = candidates[Math.floor(Math.random() * candidates.length)];
    setFlipped(tile, true);
    autoTimers.set(tile, setTimeout(() => { setFlipped(tile, false); autoTimers.delete(tile); }, 3800));
  }, 2400);
}

/* ── Screens: a reel with depth, drag to scroll, pivot headers follow ─────── */
function initReel() {
  const reel = document.querySelector('.reel');
  if (!reel) return;
  const shots = [...reel.querySelectorAll('.shot')];
  const pivot = document.querySelector('.pivot');
  const pivotLinks = [...pivot.querySelectorAll('a')];

  // Scroll snapping measures each screen's transformed box: turning the snap targets themselves moved the snap
  // points while the reel scrolled, and smooth scrolls stopped a screen short. The figure stays put as the snap
  // target and an inner layer turns (opacity too: on the figure it would flatten the 3D)
  const layers = shots.map((shot) => {
    const inner = document.createElement('div');
    inner.className = 'shot-in';
    while (shot.firstChild) inner.appendChild(shot.firstChild);
    shot.appendChild(inner);
    return inner;
  });

  let queued = false;
  let lastActive = null;
  function update() {
    queued = false;
    const box = reel.getBoundingClientRect();
    const mid = box.left + box.width / 2;
    let nearest = null;
    let best = Infinity;
    shots.forEach((shot, i) => {
      const r = shot.getBoundingClientRect();
      const d = clamp((r.left + r.width / 2 - mid) / (box.width / 2), -1.4, 1.4);
      if (!reduceMotion) {
        const a = Math.abs(d);
        layers[i].style.transform = `rotateY(${(-d * 24).toFixed(2)}deg) translateZ(${(-a * 110).toFixed(1)}px)`;
        layers[i].style.opacity = (1 - Math.min(a, 1) * 0.45).toFixed(3);
      }
      if (Math.abs(d) < best) { best = Math.abs(d); nearest = shot; }
    });
    // The pivot header of the screen group in the middle lights up
    const idx = shots.indexOf(nearest);
    let active = null;
    for (const link of pivotLinks) {
      const target = document.querySelector(link.getAttribute('href'));
      if (shots.indexOf(target) <= idx) active = link;
    }
    pivotLinks.forEach((l) => l.classList.toggle('on', l === active));
    // On a phone the headers don't all fit: keep the lit one in view, like a WP pivot
    if (active && active !== lastActive) {
      lastActive = active;
      const left = active.offsetLeft - pivot.offsetLeft;
      if (left < pivot.scrollLeft || left + active.offsetWidth > pivot.scrollLeft + pivot.clientWidth) {
        pivot.scrollTo({ left: left - 16, behavior: reduceMotion ? 'auto' : 'smooth' });
      }
    }
  }
  const schedule = () => { if (!queued) { queued = true; requestAnimationFrame(update); } };
  reel.addEventListener('scroll', schedule, { passive: true });
  window.addEventListener('resize', schedule);
  update();

  const centre = (shot, smooth) => reel.scrollTo({
    left: shot.offsetLeft - (reel.clientWidth - shot.offsetWidth) / 2,
    behavior: smooth && !reduceMotion ? 'smooth' : 'auto',
  });
  pivotLinks.forEach((link) => link.addEventListener('click', (e) => {
    e.preventDefault();
    centre(document.querySelector(link.getAttribute('href')), true);
  }));

  // Mouse drag with momentum (touch already scrolls natively)
  let down = false;
  let startX = 0;
  let startLeft = 0;
  let lastX = 0;
  let lastT = 0;
  let v = 0;
  reel.addEventListener('pointerdown', (e) => {
    if (e.pointerType !== 'mouse' || e.button !== 0) return;
    down = true;
    startX = lastX = e.clientX;
    startLeft = reel.scrollLeft;
    lastT = performance.now();
    v = 0;
    reel.classList.add('dragging');
    reel.setPointerCapture(e.pointerId);
  });
  reel.addEventListener('pointermove', (e) => {
    if (!down) return;
    reel.scrollLeft = startLeft - (e.clientX - startX);
    const now = performance.now();
    v = lerp(v, (lastX - e.clientX) / Math.max(1, now - lastT), 0.4); // px per ms
    lastX = e.clientX;
    lastT = now;
  });
  const end = () => {
    if (!down) return;
    down = false;
    if (reduceMotion || performance.now() - lastT > 80) v = 0;
    // A hard flick glides two or three screens at most (the glide covers about 200 × v pixels)
    v = clamp(v, -2.5, 2.5);
    const glide = () => {
      if (Math.abs(v) < 0.05) {
        // Hand back to scroll-snap, which settles on the nearest screen
        reel.classList.remove('dragging');
        let nearest = shots[0];
        let best = Infinity;
        const mid = reel.scrollLeft + reel.clientWidth / 2;
        for (const s of shots) {
          const d = Math.abs(s.offsetLeft + s.offsetWidth / 2 - mid);
          if (d < best) { best = d; nearest = s; }
        }
        centre(nearest, true);
        return;
      }
      reel.scrollLeft += v * 16;
      v *= 0.92;
      requestAnimationFrame(glide);
    };
    glide();
  };
  reel.addEventListener('pointerup', end);
  reel.addEventListener('pointercancel', end);
}

/* ── Copy the bank account number ──────────────────────────────────────────── */
function initCopy() {
  if (!navigator.clipboard) return;
  document.querySelectorAll('[data-copy]').forEach((btn) => {
    btn.hidden = false;
    btn.addEventListener('click', async () => {
      try {
        await navigator.clipboard.writeText(btn.dataset.copy);
        btn.textContent = 'copied';
        setTimeout(() => { btn.textContent = 'copy'; }, 2000);
      } catch { /* clipboard refused: the number is on screen anyway */ }
    });
  });
}

initHero();
initReveal();
initTiles();
initReel();
initCopy();
