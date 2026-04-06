import { startContainer, stopContainer, restartContainer, streamLogs } from './signalr-client.js';
import { animateCardIn, animateCardOut, animateNumberTo, animateStaggeredGrid } from './animations.js';
import { createSparkline } from './charts.js';
import { showToast } from './toasts.js';
const groupsContainer = document.getElementById('container-groups');
const emptyState = document.getElementById('container-empty');
const template = document.getElementById('container-card-template');
const groupTemplate = document.getElementById('compose-group-template');
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
const drawerHealthRow = document.getElementById('drawer-health-row');
const drawerHealth = document.getElementById('drawer-health');
const countEl = document.getElementById('container-count');

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

/** @type {Map<string, boolean>} image reference -> update available */
let imageUpdateMap = new Map();

/** Track whether the first batch of container cards has been rendered. */
let containerFirstRender = true;

/** @type {Map<string, boolean>} group name -> collapsed */
const groupCollapseState = new Map();

/** @type {Map<string, string>} container id -> previous state */
const previousStates = new Map();

/**
 * Find a container card by ID across all group grids.
 * @param {string} containerId
 * @returns {HTMLElement|null}
 */
function findCard(containerId) {
  return groupsContainer.querySelector(`[data-container-id="${containerId}"]`);
}

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
    groupsContainer.style.display = 'none';
    return;
  }

  emptyState.style.display = 'none';
  groupsContainer.style.display = '';

  // Remove cards for containers that disappeared
  for (const [id] of currentContainers) {
    if (!incoming.has(id)) {
      const card = findCard(id);
      if (card) animateCardOut(card);
      destroyContainerSparklines(id);
      currentContainers.delete(id);
    }
  }

  // Group containers by compose project
  /** @type {Map<string, Array<[string, object]>>} */
  const groups = new Map();
  for (const [id, container] of incoming) {
    const groupName = container.composeProject || 'Standalone';
    if (!groups.has(groupName)) groups.set(groupName, []);
    groups.get(groupName).push([id, container]);
  }

  const isSingleGroup = groups.size === 1;

  // Track which group elements are still active
  const activeGroupNames = new Set(groups.keys());

  // Remove group wrappers that no longer have containers
  for (const groupEl of [...groupsContainer.querySelectorAll('.compose-group')]) {
    if (!activeGroupNames.has(groupEl.dataset.group)) {
      groupEl.remove();
    }
  }

  const newCards = [];

  for (const [groupName, entries] of groups) {
    // Find or create group wrapper
    let groupEl = groupsContainer.querySelector(`.compose-group[data-group="${CSS.escape(groupName)}"]`);
    if (!groupEl) {
      groupEl = groupTemplate.content.cloneNode(true).querySelector('.compose-group');
      groupEl.dataset.group = groupName;

      // Restore collapse state
      if (groupCollapseState.get(groupName)) {
        groupEl.classList.add('compose-group--collapsed');
        groupEl.querySelector('.compose-group__header').setAttribute('aria-expanded', 'false');
      }

      // Toggle handler
      const header = groupEl.querySelector('.compose-group__header');
      header.addEventListener('click', () => {
        const collapsed = groupEl.classList.toggle('compose-group--collapsed');
        groupCollapseState.set(groupName, collapsed);
        header.setAttribute('aria-expanded', String(!collapsed));
      });

      groupsContainer.appendChild(groupEl);
    }

    const grid = groupEl.querySelector('.compose-group__grid');

    // Update header info
    const headerEl = groupEl.querySelector('.compose-group__header');
    const groupRunning = entries.filter(([, c]) => c.state === 'running').length;
    groupEl.querySelector('.compose-group__name').textContent = groupName;
    groupEl.querySelector('.compose-group__count').textContent = `${groupRunning} / ${entries.length}`;

    // Hide header when there is only one group
    headerEl.hidden = isSingleGroup;

    // Add or update cards within this group
    for (const [id, container] of entries) {
      let card = findCard(id);

      if (!card) {
        card = template.content.cloneNode(true).querySelector('.container-card');
        card.dataset.containerId = id;
        card.addEventListener('click', () => openDrawer(container));
        grid.appendChild(card);
        initContainerSparklines(id, card);
        newCards.push(card);
      } else if (card.parentElement !== grid) {
        // Container moved to a different group
        grid.appendChild(card);
      }

      const dot = card.querySelector('.status-dot');
      dot.className = `status-dot status-dot--${container.state}`;

      card.querySelector('.container-card__name').textContent = container.name;
      card.querySelector('.container-card__image').textContent = container.image;

      const healthIndicator = card.querySelector('.health-indicator');
      if (healthIndicator) {
        const health = container.health || 'none';
        healthIndicator.dataset.health = health;
        healthIndicator.hidden = health === 'none' || health === 'unknown';
      }

      currentContainers.set(id, container);
    }
  }

  // Detect container state changes and show toasts (skip first render)
  if (!containerFirstRender || !newCards.length) {
    for (const [id, container] of incoming) {
      const prev = previousStates.get(id);
      if (prev && prev !== container.state) {
        const wasRunning = prev === 'running';
        const isRunning = container.state === 'running';
        const isStopped = container.state === 'exited' || container.state === 'stopped';

        if (wasRunning && isStopped) {
          showToast(`${container.name} has stopped`, 'warning');
        } else if (!wasRunning && isRunning) {
          showToast(`${container.name} is now running`, 'info');
        }
      }
    }
  }

  // Update previous states for next comparison
  previousStates.clear();
  for (const [id, container] of incoming) {
    previousStates.set(id, container.state);
  }

  // Staggered entrance for the first batch; individual animation for later additions
  if (containerFirstRender && newCards.length > 0) {
    containerFirstRender = false;
    animateStaggeredGrid('#container-groups .compose-group__grid');
  } else {
    for (const card of newCards) {
      animateCardIn(card);
    }
  }

  applyUpdateBadges();
}

export function updateContainerStats(stats) {
  for (const stat of stats) {
    const card = findCard(stat.containerId);
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

  // Health info
  const health = container.health || 'none';
  if (health !== 'none' && health !== 'unknown') {
    drawerHealthRow.hidden = false;
    drawerHealth.textContent = health;
    drawerHealth.dataset.health = health;

    fetch(`/api/containers/${container.id}/health`)
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data && openContainerId === container.id) {
          const parts = [data.health];
          if (data.lastCheck) {
            parts.push(`checked ${new Date(data.lastCheck).toLocaleString()}`);
          }
          drawerHealth.textContent = parts.join(' \u2014 ');
          if (data.lastOutput) {
            drawerHealth.title = data.lastOutput;
          }
        }
      })
      .catch(() => { /* health detail fetch is best-effort */ });
  } else {
    drawerHealthRow.hidden = true;
    drawerHealth.textContent = '--';
  }

  drawerLogs.innerHTML = '';
  clearLogState();

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
  clearLogState();
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
    const card = findCard(id);
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
