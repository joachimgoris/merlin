import { startContainer, stopContainer, restartContainer, streamLogs } from './signalr-client.js';
import { animateCardIn, animateCardOut, animateNumberTo } from './animations.js';

const grid = document.getElementById('container-grid');
const emptyState = document.getElementById('container-empty');
const template = document.getElementById('container-card-template');
const drawer = document.getElementById('detail-drawer');
const overlay = document.getElementById('drawer-overlay');
const drawerName = document.getElementById('drawer-name');
const drawerImage = document.getElementById('drawer-image');
const drawerActions = document.getElementById('drawer-actions');
const drawerLogs = document.getElementById('drawer-logs');
const drawerClose = document.getElementById('drawer-close');
const countEl = document.getElementById('container-count');

let currentContainers = new Map();
let openContainerId = null;
let cancelLogStream = null;

export function updateContainerList(containers) {
  const incoming = new Map(containers.map(c => [c.id, c]));
  const runningCount = containers.filter(c => c.state === 'running').length;
  countEl.textContent = `${runningCount} running / ${containers.length} total`;

  if (containers.length === 0) {
    emptyState.style.display = '';
    grid.style.display = 'none';
    return;
  }

  emptyState.style.display = 'none';
  grid.style.display = '';

  // Remove cards for containers that disappeared
  for (const [id] of currentContainers) {
    if (!incoming.has(id)) {
      const card = grid.querySelector(`[data-container-id="${id}"]`);
      if (card) animateCardOut(card);
      currentContainers.delete(id);
    }
  }

  // Add or update cards
  for (const [id, container] of incoming) {
    let card = grid.querySelector(`[data-container-id="${id}"]`);

    if (!card) {
      card = template.content.cloneNode(true).querySelector('.container-card');
      card.dataset.containerId = id;
      card.addEventListener('click', () => openDrawer(container));
      grid.appendChild(card);
      animateCardIn(card);
    }

    const dot = card.querySelector('.status-dot');
    dot.className = `status-dot status-dot--${container.state}`;

    card.querySelector('.container-card__name').textContent = container.name;
    card.querySelector('.container-card__image').textContent = container.image;

    currentContainers.set(id, container);
  }
}

export function updateContainerStats(stats) {
  for (const stat of stats) {
    const card = grid.querySelector(`[data-container-id="${stat.containerId}"]`);
    if (!card) continue;

    const cpuEl = card.querySelector('[data-stat="cpu"]');
    const memEl = card.querySelector('[data-stat="mem"]');

    animateNumberTo(cpuEl, stat.cpuPercent, v => v.toFixed(1) + '%');
    animateNumberTo(memEl, stat.memoryPercent, v => v.toFixed(1) + '%');
  }
}

function openDrawer(container) {
  openContainerId = container.id;
  drawerName.textContent = container.name;
  drawerImage.textContent = container.image;
  drawerLogs.innerHTML = '';

  // Build actions
  drawerActions.innerHTML = '';
  if (container.state === 'running') {
    drawerActions.appendChild(makeButton('Stop', 'btn btn--danger', () => stopContainer(container.id)));
    drawerActions.appendChild(makeButton('Restart', 'btn', () => restartContainer(container.id)));
  } else {
    drawerActions.appendChild(makeButton('Start', 'btn btn--success', () => startContainer(container.id)));
  }

  // Start log streaming
  if (cancelLogStream) cancelLogStream();
  cancelLogStream = streamLogs(container.id, 200,
    line => {
      const el = document.createElement('div');
      el.textContent = line;
      drawerLogs.appendChild(el);
      drawerLogs.scrollTop = drawerLogs.scrollHeight;
    },
    () => {
      const el = document.createElement('div');
      el.textContent = '--- stream ended ---';
      el.style.color = 'var(--color-text-muted)';
      drawerLogs.appendChild(el);
    }
  );

  drawer.classList.add('detail-drawer--open');
  overlay.classList.add('overlay--visible');
}

function closeDrawer() {
  openContainerId = null;
  drawer.classList.remove('detail-drawer--open');
  overlay.classList.remove('overlay--visible');
  if (cancelLogStream) { cancelLogStream(); cancelLogStream = null; }
}

function makeButton(text, className, onClick) {
  const btn = document.createElement('button');
  btn.className = className;
  btn.textContent = text;
  btn.addEventListener('click', e => { e.stopPropagation(); onClick(); });
  return btn;
}

drawerClose.addEventListener('click', closeDrawer);
overlay.addEventListener('click', closeDrawer);
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeDrawer(); });
