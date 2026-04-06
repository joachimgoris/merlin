const groupsContainer = document.getElementById('homepage-groups');
const emptyState = document.getElementById('homepage-empty');
const groupTemplate = document.getElementById('homepage-group-template');

/** @type {Map<string, HTMLElement>} */
const groupElements = new Map();

export function initHomepage() {
  // Attach collapse toggle via delegation
  groupsContainer?.addEventListener('click', (e) => {
    const header = e.target.closest('.homepage-group__header');
    if (!header) return;
    const group = header.closest('.homepage-group');
    if (!group) return;
    const collapsed = group.classList.toggle('homepage-group--collapsed');
    header.setAttribute('aria-expanded', String(!collapsed));
  });
}

/**
 * @param {Array<{id: string, name: string, url: string, icon: string, group: string, description: string, status: string}>} services
 */
export function updateHomepageServices(services) {
  if (!groupsContainer) return;

  if (!services || services.length === 0) {
    groupsContainer.innerHTML = '';
    groupElements.clear();
    if (emptyState) emptyState.style.display = '';
    return;
  }

  if (emptyState) emptyState.style.display = 'none';

  // Group services
  /** @type {Map<string, Array>} */
  const grouped = new Map();
  for (const svc of services) {
    const g = svc.group || 'Services';
    if (!grouped.has(g)) grouped.set(g, []);
    grouped.get(g).push(svc);
  }

  // Remove stale groups
  for (const [name, el] of groupElements) {
    if (!grouped.has(name)) {
      el.remove();
      groupElements.delete(name);
    }
  }

  // Create or update groups
  for (const [groupName, groupServices] of grouped) {
    let groupEl = groupElements.get(groupName);

    if (!groupEl) {
      const clone = groupTemplate.content.cloneNode(true);
      groupEl = clone.querySelector('.homepage-group');
      groupEl.dataset.group = groupName;
      groupEl.querySelector('.homepage-group__name').textContent = groupName;
      groupsContainer.appendChild(groupEl);
      groupElements.set(groupName, groupEl);
    }

    groupEl.querySelector('.homepage-group__count').textContent =
      groupServices.length + (groupServices.length === 1 ? ' service' : ' services');

    const grid = groupEl.querySelector('.service-grid');
    updateGrid(grid, groupServices);
  }
}

/**
 * @param {HTMLElement} grid
 * @param {Array} services
 */
function updateGrid(grid, services) {
  /** @type {Map<string, HTMLElement>} */
  const existing = new Map();
  for (const tile of grid.querySelectorAll('.service-tile')) {
    existing.set(tile.dataset.serviceId, tile);
  }

  const ids = new Set(services.map((s) => s.id));

  // Remove stale tiles
  for (const [id, el] of existing) {
    if (!ids.has(id)) el.remove();
  }

  for (const svc of services) {
    let tile = existing.get(svc.id);

    if (!tile) {
      tile = createTile(svc);
      grid.appendChild(tile);
    } else {
      updateTile(tile, svc);
    }
  }
}

/**
 * @param {object} svc
 * @returns {HTMLAnchorElement}
 */
function createTile(svc) {
  const a = document.createElement('a');
  a.className = 'service-tile';
  a.href = svc.url;
  a.target = '_blank';
  a.rel = 'noopener';
  a.dataset.serviceId = svc.id;

  a.innerHTML = `
    <div class="service-tile__icon"></div>
    <div class="service-tile__info">
      <div class="service-tile__name"></div>
      <div class="service-tile__description"></div>
    </div>
    <div class="service-tile__status"></div>`;

  updateTile(a, svc);
  return a;
}

/**
 * @param {HTMLElement} tile
 * @param {object} svc
 */
function updateTile(tile, svc) {
  tile.href = svc.url;

  const iconEl = tile.querySelector('.service-tile__icon');
  if (svc.icon && svc.icon.startsWith('http')) {
    if (!iconEl.querySelector('img') || iconEl.querySelector('img').src !== svc.icon) {
      iconEl.innerHTML = `<img src="${escapeAttr(svc.icon)}" alt="" loading="lazy" width="40" height="40">`;
    }
  } else {
    const text = svc.icon || '🔗';
    if (iconEl.textContent !== text || iconEl.querySelector('img')) {
      iconEl.textContent = text;
    }
  }

  tile.querySelector('.service-tile__name').textContent = svc.name;
  tile.querySelector('.service-tile__description').textContent = svc.description || '';

  const isOffline = svc.status !== 'online';
  tile.classList.toggle('service-tile--offline', isOffline);
}

/**
 * @param {boolean} visible
 */
export function setHomepageVisible(visible) {
  const section = document.getElementById('homepage-section');
  const main = document.querySelector('main');
  if (section) section.hidden = !visible;
  if (main) main.hidden = visible;
}

/**
 * @param {string} str
 * @returns {string}
 */
function escapeAttr(str) {
  return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}
