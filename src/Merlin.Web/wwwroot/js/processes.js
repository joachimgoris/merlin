const toggleBtn = document.getElementById('process-toggle');
const tableWrapper = document.getElementById('process-table-wrapper');
const tbody = document.getElementById('process-tbody');
const table = document.getElementById('process-table');

let currentProcesses = [];
let sortField = 'cpu';
let sortAsc = false;

function formatMemory(bytes) {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i];
}

function getSortValue(process, field) {
  switch (field) {
    case 'pid': return process.pid;
    case 'name': return process.name.toLowerCase();
    case 'cpu': return process.cpuPercent;
    case 'mem': return process.memoryBytes;
    case 'state': return process.state.toLowerCase();
    default: return 0;
  }
}

function sortProcesses(processes, field, asc) {
  return [...processes].sort((a, b) => {
    const va = getSortValue(a, field);
    const vb = getSortValue(b, field);
    if (va < vb) return asc ? -1 : 1;
    if (va > vb) return asc ? 1 : -1;
    return 0;
  });
}

function renderRows(processes) {
  const sorted = sortProcesses(processes, sortField, sortAsc);
  const fragment = document.createDocumentFragment();

  for (const p of sorted) {
    const tr = document.createElement('tr');

    const tdPid = document.createElement('td');
    tdPid.textContent = p.pid;
    tr.appendChild(tdPid);

    const tdName = document.createElement('td');
    tdName.textContent = p.name;
    tdName.title = p.name;
    tr.appendChild(tdName);

    const tdCpu = document.createElement('td');
    tdCpu.textContent = p.cpuPercent.toFixed(1);
    tr.appendChild(tdCpu);

    const tdMem = document.createElement('td');
    tdMem.textContent = formatMemory(p.memoryBytes);
    tdMem.title = p.memoryPercent.toFixed(1) + '%';
    tr.appendChild(tdMem);

    const tdState = document.createElement('td');
    tdState.textContent = p.state;
    tr.appendChild(tdState);

    fragment.appendChild(tr);
  }

  tbody.replaceChildren(fragment);
}

function updateSortIndicators() {
  const headers = table.querySelectorAll('th[data-sort]');
  for (const th of headers) {
    th.classList.remove('sorted', 'sorted--asc', 'sorted--desc');
    if (th.dataset.sort === sortField) {
      th.classList.add('sorted', sortAsc ? 'sorted--asc' : 'sorted--desc');
    }
  }
}

function setupSorting() {
  const headers = table.querySelectorAll('th[data-sort]');
  for (const th of headers) {
    th.addEventListener('click', () => {
      const field = th.dataset.sort;
      if (sortField === field) {
        sortAsc = !sortAsc;
      } else {
        sortField = field;
        sortAsc = field === 'name' || field === 'state';
      }
      updateSortIndicators();
      renderRows(currentProcesses);
    });
  }
}

export function updateProcessList(processes) {
  currentProcesses = processes;
  renderRows(processes);
}

export async function initProcessList() {
  toggleBtn.addEventListener('click', () => {
    const isHidden = tableWrapper.hidden;
    tableWrapper.hidden = !isHidden;
    toggleBtn.textContent = isHidden ? 'Hide' : 'Show';
  });

  setupSorting();
  updateSortIndicators();

  try {
    const res = await fetch('/api/processes?top=25');
    if (res.ok) {
      const data = await res.json();
      updateProcessList(data);
    }
  } catch (e) {
    // Initial fetch failed — real-time updates will fill in.
  }
}
