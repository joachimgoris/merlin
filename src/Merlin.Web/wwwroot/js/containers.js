import { startContainer, stopContainer, restartContainer, streamLogs } from './signalr-client.js';
import { animateCardIn, animateCardOut, animateNumberTo, animateStaggeredGrid } from './animations.js';
import { createSparkline } from './charts.js';
import { openTerminal, closeTerminal } from './terminal.js';

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
const logSearchInput = document.getElementById('drawer-log-search');
const drawerCpu = document.getElementById('drawer-cpu');
const drawerMem = document.getElementById('drawer-mem');
const drawerNetTx = document.getElementById('drawer-net-tx');
const drawerNetRx = document.getElementById('drawer-net-rx');
const drawerImageId = document.getElementById('drawer-image-id');
const drawerStatus = document.getElementById('drawer-status');
const drawerCreated = document.getElementById('drawer-created');
const drawerUptime = document.getElementById('drawer-uptime');
const countEl = document.getElementById('container-count');
const drawerTerminal = document.getElementById('drawer-terminal');
const drawerSearch = document.querySelector('.detail-drawer__search');

const cpuSparklineColor = getComputedStyle(document.documentElement)
  .getPropertyValue('--color-accent-cpu').trim() || '#60a5fa';
const memSparklineColor = getComputedStyle(document.documentElement)
  .getPropertyValue('--color-accent-mem').trim() || '#a78bfa';

/** @type {Map<string, {cpu: ReturnType<typeof createSparkline>, mem: ReturnType<typeof createSparkline>}>} */
const containerSparklines = new Map();

const LOG_BUFFER_MAX = 5000;

let currentContainers = new Map();
let openContainerId = null;
let cancelLogStream = null;
/** @type {string[]} */
let logBuffer = [];
let logSearchQuery = '';
let logSearchDebounceTimer = null;
/** @type {'logs' | 'terminal'} */
let drawerView = 'logs';

/** @type {Map<string, boolean>} image reference -> update available */
let imageUpdateMap = new Map();

/** Track whether the first batch of container cards has been rendered. */
let containerFirstRender = true;

/**
 * Converts a byte-per-second value to a human-readable rate string.
 * @param {number} bytesPerSec
 * @returns {string}
 */
function formatUptime(uptimeStr) {
  if (!uptimeStr) return '--';
  // Parse .NET TimeSpan format like "1.02:30:45" or "02:30:45"
  const match = uptimeStr.match(/(?:(\d+)\.)?(\d+):(\d+):(\d+)/);
  if (!match) return uptimeStr;
  const [, days, hours, minutes] = match;
  const parts = [];
  if (days && days !== '0') parts.push(`${days}d`);
  if (hours && hours !== '00') parts.push(`${parseInt(hours)}h`);
  parts.push(`${parseInt(minutes)}m`);
  return parts.join(' ') || '< 1m';
}

function formatRate(bytesPerSec) {
  if (bytesPerSec === 0 || !Number.isFinite(bytesPerSec)) return '0 B/s';
  const units = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
  const i = Math.min(Math.floor(Math.log(bytesPerSec) / Math.log(1024)), units.length - 1);
  return (bytesPerSec / Math.pow(1024, i)).toFixed(1) + ' ' + units[i];
}

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
      destroyContainerSparklines(id);
      currentContainers.delete(id);
    }
  }

  // Add or update cards
  const newCards = [];
  for (const [id, container] of incoming) {
    let card = grid.querySelector(`[data-container-id="${id}"]`);

    if (!card) {
      card = template.content.cloneNode(true).querySelector('.container-card');
      card.dataset.containerId = id;
      card.addEventListener('click', () => openDrawer(container));
      grid.appendChild(card);
      initContainerSparklines(id, card);
      newCards.push(card);
    }

    const dot = card.querySelector('.status-dot');
    dot.className = `status-dot status-dot--${container.state}`;

    card.querySelector('.container-card__name').textContent = container.name;
    card.querySelector('.container-card__image').textContent = container.image;

    currentContainers.set(id, container);
  }

  // Staggered entrance for the first batch; individual animation for later additions
  if (containerFirstRender && newCards.length > 0) {
    containerFirstRender = false;
    animateStaggeredGrid('#container-grid');
  } else {
    for (const card of newCards) {
      animateCardIn(card);
    }
  }

  applyUpdateBadges();
}

export function updateContainerStats(stats) {
  for (const stat of stats) {
    const card = grid.querySelector(`[data-container-id="${stat.containerId}"]`);
    if (!card) continue;

    const cpuEl = card.querySelector('[data-stat="cpu"]');
    const memEl = card.querySelector('[data-stat="mem"]');
    const netEl = card.querySelector('[data-stat="net"]');

    animateNumberTo(cpuEl, stat.cpuPercent, v => v.toFixed(1) + '%');
    animateNumberTo(memEl, stat.memoryPercent, v => v.toFixed(1) + '%');

    if (netEl) {
      const tx = stat.netTxBytesPerSec ?? 0;
      const rx = stat.netRxBytesPerSec ?? 0;
      netEl.textContent = `\u2191${formatRate(tx)} \u2193${formatRate(rx)}`;
    }

    // Update drawer stats if this container is open
    if (stat.containerId === openContainerId) {
      drawerCpu.textContent = stat.cpuPercent.toFixed(1) + '%';
      drawerMem.textContent = stat.memoryPercent.toFixed(1) + '%';
      drawerNetTx.textContent = formatRate(stat.netTxBytesPerSec ?? 0);
      drawerNetRx.textContent = formatRate(stat.netRxBytesPerSec ?? 0);
    }
  }
}

/**
 * Escape HTML entities to prevent XSS when inserting log text.
 */
function escapeHtml(text) {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

/**
 * Build the inner HTML for a single log line, highlighting query matches.
 */
function renderLogLineHtml(line, query) {
  const escaped = escapeHtml(line);
  if (!query) return escaped;

  const escapedQuery = escapeHtml(query);
  const lowerEscaped = escaped.toLowerCase();
  const lowerQuery = escapedQuery.toLowerCase();
  const parts = [];
  let cursor = 0;

  while (cursor < lowerEscaped.length) {
    const idx = lowerEscaped.indexOf(lowerQuery, cursor);
    if (idx === -1) {
      parts.push(escaped.slice(cursor));
      break;
    }
    if (idx > cursor) parts.push(escaped.slice(cursor, idx));
    parts.push('<mark class="log-highlight">');
    parts.push(escaped.slice(idx, idx + escapedQuery.length));
    parts.push('</mark>');
    cursor = idx + escapedQuery.length;
  }

  return parts.join('');
}

function renderFilteredLogs() {
  const query = logSearchQuery;
  const fragment = document.createDocumentFragment();

  for (const line of logBuffer) {
    if (query && !line.toLowerCase().includes(query)) continue;
    const el = document.createElement('div');
    el.innerHTML = renderLogLineHtml(line, query);
    fragment.appendChild(el);
  }

  drawerLogs.innerHTML = '';
  drawerLogs.appendChild(fragment);
  drawerLogs.scrollTop = drawerLogs.scrollHeight;
}

function clearLogState() {
  logBuffer = [];
  logSearchQuery = '';
  logSearchInput.value = '';
}

/**
 * Switch the drawer to show the logs view.
 */
function showLogsView() {
  drawerView = 'logs';
  closeTerminal();
  drawerLogs.hidden = false;
  drawerSearch.hidden = false;
  drawerTerminal.hidden = true;
}

/**
 * Switch the drawer to show the terminal view.
 * @param {string} containerId
 */
function showTerminalView(containerId) {
  drawerView = 'terminal';
  drawerLogs.hidden = true;
  drawerSearch.hidden = true;
  drawerTerminal.hidden = false;
  openTerminal(containerId);
}

function openDrawer(container) {
  openContainerId = container.id;
  drawerName.textContent = container.name;
  drawerImage.textContent = container.image;
  drawerImageId.textContent = container.version
    ? `${container.version} (${container.imageId || '?'})`
    : container.imageId || '--';
  drawerStatus.textContent = container.status || '--';
  drawerCreated.textContent = container.created
    ? new Date(container.created).toLocaleString()
    : '--';
  drawerUptime.textContent = formatUptime(container.uptime);
  drawerLogs.innerHTML = '';
  clearLogState();

  // Reset to logs view
  drawerView = 'logs';
  drawerLogs.hidden = false;
  drawerSearch.hidden = false;
  drawerTerminal.hidden = true;

  // Build actions
  drawerActions.innerHTML = '';
  if (container.state === 'running') {
    drawerActions.appendChild(makeButton('Stop', 'btn btn--danger', () => stopContainer(container.id)));
    drawerActions.appendChild(makeButton('Restart', 'btn', () => restartContainer(container.id)));
    drawerActions.appendChild(makeButton('Terminal', 'btn btn--terminal', () => showTerminalView(container.id)));
    drawerActions.appendChild(makeButton('Logs', 'btn btn--logs', () => showLogsView()));
  } else {
    drawerActions.appendChild(makeButton('Start', 'btn btn--success', () => startContainer(container.id)));
  }

  // Start log streaming
  if (cancelLogStream) cancelLogStream();
  cancelLogStream = streamLogs(container.id, 200,
    line => {
      logBuffer.push(line);
      if (logBuffer.length > LOG_BUFFER_MAX) {
        logBuffer.splice(0, logBuffer.length - LOG_BUFFER_MAX);
      }

      const query = logSearchQuery;
      if (!query || line.toLowerCase().includes(query)) {
        const el = document.createElement('div');
        el.innerHTML = renderLogLineHtml(line, query);
        drawerLogs.appendChild(el);
        drawerLogs.scrollTop = drawerLogs.scrollHeight;
      }
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
  closeTerminal();
  clearLogState();
  drawerView = 'logs';
}

function makeButton(text, className, onClick) {
  const btn = document.createElement('button');
  btn.className = className;
  btn.textContent = text;
  btn.addEventListener('click', e => { e.stopPropagation(); onClick(); });
  return btn;
}

/**
 * Creates sparkline chart instances for a container card.
 * @param {string} containerId
 * @param {HTMLElement} card
 */
function initContainerSparklines(containerId, card) {
  const cpuCanvas = card.querySelector('[data-sparkline="cpu"]');
  const memCanvas = card.querySelector('[data-sparkline="mem"]');

  if (!cpuCanvas || !memCanvas) return;

  const cpu = createSparkline(cpuCanvas, { color: cpuSparklineColor, maxPoints: 300 });
  const mem = createSparkline(memCanvas, { color: memSparklineColor, maxPoints: 300 });

  containerSparklines.set(containerId, { cpu, mem });
}

/**
 * Destroys sparkline chart instances for a removed container.
 * @param {string} containerId
 */
function destroyContainerSparklines(containerId) {
  const sparklines = containerSparklines.get(containerId);
  if (sparklines) {
    sparklines.cpu.destroy();
    sparklines.mem.destroy();
    containerSparklines.delete(containerId);
  }
}

/**
 * Updates all container sparklines from a sparkline data broadcast.
 * @param {Record<string, {cpu: number[], mem: number[]}>} data
 */
export function updateContainerSparklines(data) {
  for (const [containerId, series] of Object.entries(data)) {
    const sparklines = containerSparklines.get(containerId);
    if (!sparklines) continue;

    // When receiving full history (initial load or reconnect), replay all points
    if (series.cpu.length > 1) {
      for (let i = 0; i < series.cpu.length; i++) {
        sparklines.cpu.update(series.cpu[i]);
        sparklines.mem.update(series.mem[i]);
      }
    } else if (series.cpu.length === 1) {
      sparklines.cpu.update(series.cpu[0]);
      sparklines.mem.update(series.mem[0]);
    }
  }
}

/**
 * Processes image update status data and shows/hides update badges on cards.
 * @param {Array<{imageReference: string, updateAvailable: boolean}>} updates
 */
export function updateImageUpdates(updates) {
  imageUpdateMap.clear();
  for (const update of updates) {
    if (update.updateAvailable) {
      imageUpdateMap.set(update.imageReference, true);
    }
  }
  applyUpdateBadges();
}

/**
 * Shows or hides update badges on all container cards based on current image update data.
 */
function applyUpdateBadges() {
  for (const [id, container] of currentContainers) {
    const card = grid.querySelector(`[data-container-id="${id}"]`);
    if (!card) continue;

    const badge = card.querySelector('.update-badge');
    if (!badge) continue;

    const hasUpdate = imageUpdateMap.has(container.image);
    badge.hidden = !hasUpdate;
  }
}

drawerClose.addEventListener('click', closeDrawer);
overlay.addEventListener('click', closeDrawer);
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeDrawer(); });

logSearchInput.addEventListener('input', () => {
  clearTimeout(logSearchDebounceTimer);
  logSearchDebounceTimer = setTimeout(() => {
    logSearchQuery = logSearchInput.value.toLowerCase();
    renderFilteredLogs();
  }, 150);
});
