// El deck por la red (ADR 0021). Recibe el estado de FocusWShell por WebSocket y manda comandos (protocolo v1).
'use strict';

const T = {
  es: {
    focus: 'Foco', previous: 'Anterior', toPanel: 'Al panel', freeAll: 'Liberar todas', move: 'Mover…', moving: 'Moviendo…', pin: 'Anclar…', pinning: 'Anclando…', inUse: 'en uso',
    minimize: 'Minimizar…', minimizing: 'Minimizando…', close: 'Cerrar…', closing: 'Cerrando…',
    pickMinimize: 'Toca una app de un panel para minimizarla (o devolverla).', pickClose: 'Toca la app que quieres cerrar. Si tiene algo sin guardar, ella pregunta en el PC.',
    freeAllSure: '¿Liberar? Toca otra vez', layout: 'Disposición del principal', system: 'Sistema', awakeOn: 'Pantalla encendida', awakeOff: 'Pantalla: normal',
    mouse: 'Ratón al foco', rearrange: 'Reordenar todas', resetTip: 'Mantén apretado para restablecer el ancho',
    tiles: 'Cuadrícula', stack: 'Pestañas', list: 'Lista', fill: 'Llenar', prevTab: 'Pestaña anterior', nextTab: 'Pestaña siguiente',
    pickPin: 'Toca una app para anclarla o desanclarla en su panel. Toca Anclar otra vez para terminar.',
    pickApp: 'Toca la app que quieres mover.', pickPanel: 'Toca el panel donde va, o Libres para liberarla.',
    free: 'Libres', noFree: 'Sin ventanas libres.', pages: 'Páginas', nothingInFocus: 'Nada en el foco',
    monitors: n => `${n} ${n === 1 ? 'monitor' : 'monitores'}`, apps: n => `${n} apps`,
    connecting: 'Conectando…', reconnecting: 'Sin conexión con el PC. Reintentando…', sites: 'Sitios', loose: 'Páginas sueltas',
    privateWindows: 'Ventanas privadas', privateWindow: n => `🕶 Ventana privada · ${n} pestañas`, back: '‹ Sitios',
    loading: 'Leyendo las páginas…', noPages: 'No hay pestañas que leer en Chrome ni en Edge.', closeAll: 'Cerrar todas',
  },
  en: {
    focus: 'Focus', previous: 'Previous', toPanel: 'To panel', freeAll: 'Free all', move: 'Move…', moving: 'Moving…', pin: 'Pin…', pinning: 'Pinning…', inUse: 'in use',
    minimize: 'Minimize…', minimizing: 'Minimizing…', close: 'Close…', closing: 'Closing…',
    pickMinimize: 'Tap an app in a panel to minimize it (or bring it back).', pickClose: 'Tap the app you want to close. If it has unsaved work, it asks on the PC.',
    freeAllSure: 'Free all? Tap again', layout: 'Main screen layout', system: 'System', awakeOn: 'Screen kept on', awakeOff: 'Screen: normal',
    mouse: 'Mouse to focus', rearrange: 'Rearrange all', resetTip: 'Press and hold to reset the width',
    tiles: 'Tiles', stack: 'Tabs', list: 'List', fill: 'Fill', prevTab: 'Previous tab', nextTab: 'Next tab',
    pickPin: 'Tap an app to pin or unpin it in its panel. Tap Pin again to finish.',
    pickApp: 'Tap the app you want to move.', pickPanel: 'Tap the panel where it goes, or Free to free it.',
    free: 'Free', noFree: 'No free windows.', pages: 'Pages', nothingInFocus: 'Nothing in focus',
    monitors: n => `${n} ${n === 1 ? 'monitor' : 'monitors'}`, apps: n => `${n} apps`,
    connecting: 'Connecting…', reconnecting: 'No connection to the PC. Retrying…', sites: 'Sites', loose: 'Single pages',
    privateWindows: 'Private windows', privateWindow: n => `🕶 Private window · ${n} tabs`, back: '‹ Sites',
    loading: 'Reading the pages…', noPages: 'No Chrome or Edge tabs to read.', closeAll: 'Close all',
  },
};
const L = T[(navigator.language || 'en').toLowerCase().startsWith('es') ? 'es' : 'en'];
document.querySelectorAll('[data-t]').forEach(e => { e.textContent = L[e.dataset.t]; });

const $ = id => document.getElementById(id);
const esc = x => String(x).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const icon = key => `/icon?key=${encodeURIComponent(key)}`;
// Si no hay icono, la inicial de la app en su lugar (no una imagen rota).
const img = (key, name) => `<img src="${icon(key)}" alt="" data-i="${esc((name || '?')[0].toUpperCase())}" onerror="noIcon(this)">`;
function noIcon(el) { const s = document.createElement('span'); s.className = 'ph'; s.textContent = el.dataset.i; el.replaceWith(s); }

let state = null, ws = null, seq = 0, retry = 500;
let moving = null;            // null | 'app' | 'panel'
let chosen = null;            // la app elegida para mover
let pinning = false;          // modo Anclar: tocar alterna el ancla; sigue prendido hasta tocarlo otra vez
let minimizing = false, closing = false;   // Minimizar y Cerrar: se apagan tras un toque, como en el deck del PC
let freeAllArmed = 0;         // «Liberar todas» pide un segundo toque en 4 s
const anyMode = () => !!moving || pinning || minimizing || closing;
function clearModes() { moving = null; chosen = null; pinning = false; minimizing = false; closing = false; status(''); }
let pagesOpen = false, pages = null, site = null;

function status(text, bad) { $('status').textContent = text || ''; $('status').className = 'status' + (bad ? ' bad' : ''); }
function send(cmd, data) { if (ws && ws.readyState === 1) ws.send(JSON.stringify({ cmd, seq: ++seq, ...data })); }

function connect() {
  status(L.connecting);
  ws = new WebSocket(`ws://${location.host}/ws${location.search}`);
  ws.onopen = () => { retry = 500; status(''); };
  ws.onclose = () => { status(L.reconnecting, true); setTimeout(connect, retry); retry = Math.min(retry * 2, 8000); };
  ws.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.type === 'state') { state = m.state; render(); }
    if (m.type === 'pages') { pages = m.pages; renderPages(); }
  };
}

// --- Izquierda ------------------------------------------------------------------------------------------

function renderLeft() {
  const apps = state.monitors.reduce((n, m) => n + m.panels.reduce((k, p) => k + p.apps.filter(a => a.state !== 'Closed').length, 0), 0);
  $('summary').textContent = `${L.monitors(state.monitors.length)} · ${L.apps(apps)}`;
  const f = state.focus, tile = $('focusTile');
  tile.className = 'focusTile' + (f ? '' : ' empty') + (moving === 'panel' && chosen && (!f || chosen.id !== f.id) ? ' pick' : '');
  // Como en el deck del PC: si el teclado está en otra app, se ve a su lado, más pequeña y con borde blanco.
  const use = f && state.foreground && state.foreground !== f.id ? findApp(state.foreground) : null;
  tile.innerHTML = !f ? esc(L.nothingInFocus)
    : !use ? `${img(f.key, f.name)}<span>${esc(f.name)}</span>`
    : `<div class="duo"><div class="main">${img(f.key, f.name)}<span>${esc(f.name)}</span></div><div class="sep"></div>`
      + `<div class="use">${img(use.key, use.name)}<span>${esc(use.name)}</span><small>${esc(L.inUse)}</small></div></div>`;
  $('previous').disabled = !state.hasPrevious;
  $('toPanel').disabled = !f;
  const move = $('move');
  move.classList.toggle('on', !!moving);
  move.querySelector('span').textContent = moving ? L.moving : L.move;
  if (moving) status(moving === 'app' ? L.pickApp : L.pickPanel);
  $('pin').classList.toggle('on', pinning);
  $('pin').querySelector('span').textContent = pinning ? L.pinning : L.pin;
  if (pinning) status(L.pickPin);
  for (const [id, on, text, busy] of [['minimize', minimizing, L.minimize, L.minimizing], ['close', closing, L.close, L.closing]]) {
    $(id).classList.toggle('on', on);
    $(id).querySelector('span').textContent = on ? busy : text;
  }
  if (minimizing) status(L.pickMinimize);
  if (closing) status(L.pickClose);
  const armed = Date.now() - freeAllArmed < 4000;
  $('freeAll').classList.toggle('armed', armed);
  $('freeAll').querySelector('span').textContent = armed ? L.freeAllSure : L.freeAll;

  // La disposición del principal: azul, prendida. Mantener apretado restablece el ancho.
  const lay = $('layout'); lay.innerHTML = '';
  for (const b of state.layout || []) {
    const btn = document.createElement('button');
    btn.textContent = b.label; btn.title = L.resetTip; btn.className = b.on ? 'on' : '';
    btn.onclick = () => send('view', { key: b.key });
    holdToReset(btn);
    lay.appendChild(btn);
  }

  $('awake').classList.toggle('on', !!state.keepAwake);
  $('awake').querySelector('span').textContent = state.keepAwake ? L.awakeOn : L.awakeOff;
  $('rearrange').hidden = !state.canRearrange;
}

function holdToReset(btn) {
  let timer = null, held = false;
  btn.addEventListener('pointerdown', () => { held = false; timer = setTimeout(() => { held = true; send('resetWidth'); }, 700); });
  ['pointerup', 'pointerleave', 'pointercancel'].forEach(e => btn.addEventListener(e, () => clearTimeout(timer)));
  btn.addEventListener('click', e => { if (held) { e.stopImmediatePropagation(); held = false; } }, true);
  btn.addEventListener('contextmenu', e => e.preventDefault());
}

const findApp = id => [...state.monitors.flatMap(m => m.panels.flatMap(p => p.apps)), ...state.free].find(a => a.id === id);

$('focusTile').onclick = () => {
  if (moving === 'panel' && chosen) { send('tap', { id: chosen.id }); endMove(); return; } // mover al foco
  if (state && state.focus) send('tap', { id: state.focus.id });
};
$('previous').onclick = () => send('previous');
$('toPanel').onclick = () => send('toPanel');
$('freeAll').onclick = () => {
  if (Date.now() - freeAllArmed < 4000) { freeAllArmed = 0; send('freeAll'); render(); return; }
  freeAllArmed = Date.now(); render(); setTimeout(render, 4100);
};
$('awake').onclick = () => send('keepAwake');
$('mouse').onclick = () => send('mouseToFocus');
$('rearrange').onclick = () => send('rearrange');
$('minimize').onclick = () => { const on = !minimizing; clearModes(); minimizing = on; render(); };
$('close').onclick = () => { const on = !closing; clearModes(); closing = on; render(); };
$('move').onclick = () => { const on = !moving; clearModes(); moving = on ? 'app' : null; render(); };
$('pin').onclick = () => { const on = !pinning; clearModes(); pinning = on; render(); };
// ⛶ entra y sale de pantalla completa; el icono dice qué hará.
const isFull = () => !!(document.fullscreenElement || document.webkitFullscreenElement);
$('full').onclick = () => {
  const d = document.documentElement;
  if (isFull()) (document.exitFullscreen || document.webkitExitFullscreen).call(document);
  else (d.requestFullscreen || d.webkitRequestFullscreen || (() => {})).call(d);
};
const fullIcon = () => { $('full').textContent = isFull() ? '🗗' : '⛶'; };
document.addEventListener('fullscreenchange', fullIcon);
document.addEventListener('webkitfullscreenchange', fullIcon);

function endMove() { moving = null; chosen = null; status(''); render(); }

// --- Paneles ---------------------------------------------------------------------------------------------

function appButton(a) {
  const b = document.createElement('button');
  const away = ['Closed', 'InTray', 'OtherDesktop', 'Unmovable'].includes(a.state);
  const mark = { InFocus: ' ↑', Parked: ' ⏸', Detached: ' ↗', OtherDesktop: ' ⧉', InTray: ' ▴', Unmovable: ' 🛡' }[a.state] || '';
  b.className = 'app' + (away ? ' away' : '') + (a.state === 'InFocus' ? ' infocus' : '')
    + (state.foreground === a.id && a.state !== 'InFocus' && !anyMode() ? ' inuse' : '')
    + (a.tab === 'visible' ? ' tabvisible' : '')
    + (moving === 'app' && canMove(a) ? ' pick' : '') + (chosen && chosen.id === a.id ? ' chosen' : '')
    + (pinning ? (a.canPin ? ' pinnable' + (a.pinned ? ' pinnedOn' : '') : ' away') : '')
    + (minimizing ? (canMinimize(a) ? ' minimizable' + (a.state === 'Parked' ? ' parked' : '') : ' away') : '')
    + (closing ? (canMove(a) ? ' closable' : ' away') : '');
  b.innerHTML = `${a.pinned ? '<span class="pin">📌</span>' : ''}${img(a.key, a.name)}<span>${esc(a.name + mark)}</span>`;
  b.onclick = e => {
    e.stopPropagation();
    if (pinning) { if (a.canPin) send('pin', { id: a.id }); return; }
    if (minimizing) { if (canMinimize(a)) { send('minimize', { id: a.id }); clearModes(); render(); } return; }
    if (closing) { if (canMove(a)) { send('close', { id: a.id }); clearModes(); render(); } return; }
    if (moving) {
      // Elegida la app, todo el panel destino es el destino, también sus iconos: un panel lleno no deja hueco
      // donde tocar. Lo mismo con Libres. Otra app del mismo panel (que no es destino) cambia la elegida.
      if (moving === 'panel' && chosen && a.id !== chosen.id) {
        if (a.panel && a.panel !== chosen.panel) { send('move', { id: chosen.id, panel: a.panel }); endMove(); return; }
        if (!a.panel && chosen.panel) { send('move', { id: chosen.id, panel: 'free' }); endMove(); return; }
      }
      if (!canMove(a)) return;
      if (chosen && chosen.id === a.id) { chosen = null; moving = 'app'; } else { chosen = a; moving = 'panel'; }
      render();
      return;
    }
    send('tap', { id: a.id });
  };
  return b;
}

const canMove = a => !['Closed', 'InTray', 'OtherDesktop', 'Unmovable'].includes(a.state);
const canMinimize = a => canMove(a) && !!a.panel;

// Los iconos no cambian de lugar: el PC ordena las pestañas con la visible primero, y al tocar una oculta se
// movería bajo el dedo. Como en el deck del PC (StableOrder), cada panel recuerda el orden en que vio sus apps;
// las nuevas van al final.
const panelOrder = {};
function stable(p) {
  const known = panelOrder[p.id] || [];
  const ordered = [...p.apps].sort((a, b) => {
    const ia = known.indexOf(a.id), ib = known.indexOf(b.id);
    return (ia < 0 ? Infinity : ia) - (ib < 0 ? Infinity : ib);
  });
  panelOrder[p.id] = ordered.map(a => a.id);
  return ordered;
}

// La cabecera de un panel, como en el deck del PC: ◀ ▶ si está en pestañas, y su presentación (la activa en azul).
function panelTools(p) {
  const tools = document.createElement('div'); tools.className = 'tools';
  const add = (text, title, on, click) => {
    const b = document.createElement('button'); b.textContent = text; b.title = title; b.setAttribute('aria-label', title);
    if (on) b.className = 'on';
    b.onclick = e => { e.stopPropagation(); if (!anyMode()) click(); };
    tools.appendChild(b);
  };
  if (p.tabs > 1) {
    add('◀', L.prevTab, false, () => send('tabPrev', { panel: p.id }));
    add('▶', L.nextTab, false, () => send('tabNext', { panel: p.id }));
    tools.insertAdjacentHTML('beforeend', '<span class="gap"></span>');
  }
  const templates = [['Tiles', '▦', L.tiles], ['Stack', '⧉', L.stack], ['List', '☰', L.list]];
  if (p.canFill) templates.push(['Fill', '⛶', L.fill]);
  for (const [t, icon, name] of templates) add(icon, name, p.template === t, () => send('presentation', { panel: p.id, template: t }));
  return tools;
}

function panelCard(p) {
  const card = document.createElement('section');
  const target = moving === 'panel' && chosen && chosen.panel !== p.id;
  card.className = 'panel' + (p.off ? ' off' : '') + (target ? ' target' : '');
  const side = { Left: { es: 'izquierda', en: 'left' }, Right: { es: 'derecha', en: 'right' } }[p.side];
  const h3 = document.createElement('h3');
  h3.innerHTML = `<span class="title"><b>${esc(p.name.toUpperCase())}</b> <span>${side ? side[L === T.es ? 'es' : 'en'] : ''}</span></span>`;
  h3.appendChild(panelTools(p));
  card.appendChild(h3);
  const apps = document.createElement('div'); apps.className = 'apps';
  stable(p).forEach(a => apps.appendChild(appButton(a)));
  card.appendChild(apps);
  if (target) card.onclick = () => { send('move', { id: chosen.id, panel: p.id }); endMove(); };
  return card;
}

function render() {
  if (!state) return;
  renderLeft();
  const box = $('monitors');
  box.hidden = pagesOpen; $('pagesView').hidden = !pagesOpen;
  box.innerHTML = '';
  for (const m of state.monitors) {
    const row = document.createElement('div'); row.className = 'monitor';
    if (state.monitors.length > 1) row.innerHTML = `<div class="mname${m.connected ? '' : ' gone'}">${esc(m.name)}</div>`;
    const panels = document.createElement('div'); panels.className = 'panels';
    m.panels.forEach(p => panels.appendChild(panelCard(p)));
    row.appendChild(panels); box.appendChild(row);
  }

  const free = $('free'); free.innerHTML = state.free.length ? '' : `<small>${esc(L.noFree)}</small>`;
  state.free.forEach(a => free.appendChild(appButton(a)));
  const freeCard = $('freeCard');
  const releasing = moving === 'panel' && chosen && chosen.panel;
  freeCard.classList.toggle('target', !!releasing);
  freeCard.onclick = releasing ? () => { send('move', { id: chosen.id, panel: 'free' }); endMove(); } : null;

  const pb = $('pagesBtn');
  pb.hidden = !state.pages && !pagesOpen;
  pb.classList.toggle('on', pagesOpen);
}

// --- Páginas ---------------------------------------------------------------------------------------------

$('pagesBtn').onclick = () => {
  pagesOpen = !pagesOpen; site = null;
  if (pagesOpen) { pages = null; renderPages(); send('pages'); }
  render();
};

function pageRow(p, label) {
  const row = document.createElement('div'); row.className = 'pageRow';
  const go = document.createElement('button'); go.className = 'go' + (p.active ? ' active' : '');
  go.textContent = label; go.title = p.where;
  go.onclick = () => { send('selectPage', { id: p.id }); pagesOpen = false; render(); };
  const x = document.createElement('button'); x.className = 'x'; x.textContent = '✕';
  x.onclick = () => { send('closePage', { id: p.id }); pages.list = pages.list.filter(q => q.id !== p.id); renderPages(); setTimeout(() => send('pages'), 600); };
  row.append(go, x);
  return row;
}

function renderPages() {
  const v = $('pagesView'); v.innerHTML = '';
  if (!pages) { v.innerHTML = `<small>${esc(L.loading)}</small>`; return; }
  if (!pages.list.length && !pages.privateWindows.length) { v.innerHTML = `<small>${esc(L.noPages)}</small>`; return; }
  if (site) {
    const head = document.createElement('div'); head.className = 'pagesHead';
    const back = document.createElement('button'); back.textContent = L.back; back.style.padding = '0 12px';
    back.onclick = () => { site = null; renderPages(); };
    const inSite = pages.list.filter(p => p.site === site);
    const all = document.createElement('button'); all.textContent = L.closeAll; all.style.padding = '0 12px'; all.style.color = '#e57373';
    all.onclick = () => { inSite.forEach(p => send('closePage', { id: p.id })); pages.list = pages.list.filter(p => p.site !== site); site = null; renderPages(); setTimeout(() => send('pages'), 900); };
    const title = document.createElement('b'); title.textContent = `${site} · ${inSite.length}`;
    head.append(back, title, all); v.appendChild(head);
    inSite.forEach(p => v.appendChild(pageRow(p, p.title)));
    return;
  }

  const groups = {};
  pages.list.filter(p => p.site).forEach(p => { (groups[p.site] = groups[p.site] || []).push(p); });
  const names = Object.keys(groups).sort((a, b) => groups[b].length - groups[a].length);
  if (names.length) {
    v.insertAdjacentHTML('beforeend', `<div class="section">${esc(L.sites)}</div>`);
    const grid = document.createElement('div'); grid.className = 'sites';
    names.forEach(n => {
      const b = document.createElement('button'); b.style.padding = '0 12px'; b.style.textAlign = 'left';
      b.innerHTML = `<b>${esc(n)}</b> <small>${groups[n].length} ›</small>`;
      b.onclick = () => { site = n; renderPages(); };
      grid.appendChild(b);
    });
    v.appendChild(grid);
  }

  const loose = pages.list.filter(p => !p.site);
  if (loose.length) {
    v.insertAdjacentHTML('beforeend', `<div class="section">${esc(L.loose)}</div>`);
    loose.forEach(p => v.appendChild(pageRow(p, p.title)));
  }

  if (pages.privateWindows.length) {
    v.insertAdjacentHTML('beforeend', `<div class="section">${esc(L.privateWindows)}</div>`);
    pages.privateWindows.forEach(w => {
      const b = document.createElement('button'); b.style.padding = '0 12px'; b.style.textAlign = 'left';
      b.textContent = `${L.privateWindow(w.tabs)} · ${w.where}`;
      b.onclick = () => { send('selectPage', { id: w.id }); pagesOpen = false; render(); };
      v.appendChild(b);
    });
  }
}

connect();
