import * as hub from './signalr-client.js';
import * as anim from './animations.js';
import { createSparkline, createAreaChart } from './charts.js';
import { updateContainerList, updateContainerStats } from './containers.js';

// DOM refs
const cpuGaugeFill = document.getElementById('cpu-gauge-fill');
const cpuGaugeText = document.getElementById('cpu-gauge-text');
const cpuFreq = document.getElementById('cpu-freq');
const cpuLoad = document.getElementById('cpu-load');
const cpuCoresGrid = document.getElementById('cpu-cores');

const memGaugeFill = document.getElementById('mem-gauge-fill');
const memGaugeText = document.getElementById('mem-gauge-text');
const memUsed = document.getElementById('mem-used');
const memTotal = document.getElementById('mem-total');
const swapUsage = document.getElementById('swap-usage');

const diskMounts = document.getElementById('disk-mounts');
const netRates = document.getElementById('net-rates');
const tempSection = document.getElementById('temp-section');
const tempSensors = document.getElementById('temp-sensors');

// Charts
const cpuSparkline = createSparkline(document.getElementById('cpu-sparkline'), {
  color: getComputedStyle(document.documentElement).getPropertyValue('--color-accent-cpu').trim() || '#60a5fa',
});
const memSparkline = createSparkline(document.getElementById('mem-sparkline'), {
  color: getComputedStyle(document.documentElement).getPropertyValue('--color-accent-mem').trim() || '#a78bfa',
});
const netChart = createAreaChart(document.getElementById('net-chart'), {
  colorTx: getComputedStyle(document.documentElement).getPropertyValue('--color-accent-net').trim() || '#4ade80',
  colorRx: getComputedStyle(document.documentElement).getPropertyValue('--color-accent-cpu').trim() || '#60a5fa',
});

function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  return (bytes / Math.pow(1024, i)).toFixed(1) + ' ' + units[i];
}

function formatRate(bytesPerSec) {
  return formatBytes(bytesPerSec) + '/s';
}

function updateSystemMetrics(m) {
  // CPU
  anim.animateGaugeRing(cpuGaugeFill, m.cpu.totalUsagePercent);
  anim.animateNumberTo(cpuGaugeText, m.cpu.totalUsagePercent, v => Math.round(v) + '%');
  cpuFreq.textContent = m.cpu.frequencyMhz > 0 ? Math.round(m.cpu.frequencyMhz) + ' MHz' : '';
  cpuLoad.textContent = `${m.cpu.loadAvg1.toFixed(2)} / ${m.cpu.loadAvg5.toFixed(2)} / ${m.cpu.loadAvg15.toFixed(2)}`;
  cpuSparkline.update(m.cpu.totalUsagePercent);

  // Per-core bars
  updateCoreBars(m.cpu.perCoreUsagePercent);

  // Memory
  const memPercent = m.memory.totalBytes > 0 ? (m.memory.usedBytes / m.memory.totalBytes) * 100 : 0;
  anim.animateGaugeRing(memGaugeFill, memPercent);
  anim.animateNumberTo(memGaugeText, memPercent, v => Math.round(v) + '%');
  memUsed.textContent = formatBytes(m.memory.usedBytes);
  memTotal.textContent = formatBytes(m.memory.totalBytes);
  swapUsage.textContent = `${formatBytes(m.memory.swapUsedBytes)} / ${formatBytes(m.memory.swapTotalBytes)}`;
  memSparkline.update(memPercent);

  // Disk
  updateDiskMounts(m.disk.mounts);

  // Network
  let totalTx = 0, totalRx = 0;
  for (const iface of m.network.interfaces) {
    totalTx += iface.txBytesPerSec;
    totalRx += iface.rxBytesPerSec;
  }
  netRates.textContent = `↑ ${formatRate(totalTx)}  ↓ ${formatRate(totalRx)}`;
  netChart.update(totalTx, totalRx);

  // Temperature
  if (m.temperature.sensors.length > 0) {
    tempSection.style.display = '';
    updateTempSensors(m.temperature.sensors);
  }
}

function updateCoreBars(cores) {
  // Only rebuild DOM if core count changed
  if (cpuCoresGrid.children.length !== cores.length) {
    cpuCoresGrid.innerHTML = '';
    for (let i = 0; i < cores.length; i++) {
      const bar = document.createElement('div');
      bar.className = 'core-bar';
      bar.innerHTML = `<div class="core-bar__fill" style="background: var(--color-accent-cpu); height: 0%"></div>
                        <span class="core-bar__label">${i}</span>`;
      cpuCoresGrid.appendChild(bar);
    }
  }

  const bars = cpuCoresGrid.querySelectorAll('.core-bar__fill');
  for (let i = 0; i < cores.length && i < bars.length; i++) {
    bars[i].style.height = Math.round(cores[i]) + '%';
  }
}

function updateDiskMounts(mounts) {
  // Rebuild if mount count changed
  if (diskMounts.children.length !== mounts.length) {
    diskMounts.innerHTML = '';
    for (const mount of mounts) {
      const div = document.createElement('div');
      div.className = 'disk-mount';
      div.dataset.mount = mount.mountPoint;
      div.innerHTML = `
        <div class="disk-mount__info">
          <span class="disk-mount__path">${mount.mountPoint}</span>
          <span class="disk-mount__usage"></span>
        </div>
        <div class="progress-bar">
          <div class="progress-bar__fill" style="background: var(--color-accent-disk); width: 0%"></div>
        </div>
        <div class="metric-card__sub" style="font-size: 0.75rem">
          I/O: <span class="disk-io"></span>
        </div>`;
      diskMounts.appendChild(div);
    }
  }

  for (const mount of mounts) {
    const div = diskMounts.querySelector(`[data-mount="${mount.mountPoint}"]`);
    if (!div) continue;

    const percent = mount.totalBytes > 0 ? (mount.usedBytes / mount.totalBytes) * 100 : 0;
    div.querySelector('.disk-mount__usage').textContent =
      `${formatBytes(mount.usedBytes)} / ${formatBytes(mount.totalBytes)} (${Math.round(percent)}%)`;
    div.querySelector('.progress-bar__fill').style.width = percent + '%';
    div.querySelector('.disk-io').textContent =
      `R ${formatRate(mount.readBytesPerSec)} / W ${formatRate(mount.writeBytesPerSec)}`;
  }
}

function updateTempSensors(sensors) {
  if (tempSensors.children.length !== sensors.length) {
    tempSensors.innerHTML = '';
    for (const sensor of sensors) {
      const div = document.createElement('div');
      div.className = 'temp-sensor';
      div.dataset.label = sensor.label;
      div.innerHTML = `
        <span class="temp-sensor__label">${sensor.label}</span>
        <span class="temp-sensor__value"></span>`;
      tempSensors.appendChild(div);
    }
  }

  for (const sensor of sensors) {
    const div = tempSensors.querySelector(`[data-label="${sensor.label}"]`);
    if (!div) continue;

    const valueEl = div.querySelector('.temp-sensor__value');
    valueEl.textContent = sensor.celsiusCurrent.toFixed(0) + '°C';

    valueEl.className = 'temp-sensor__value';
    if (sensor.celsiusCurrent >= 80) valueEl.classList.add('temp-sensor__value--hot');
    else if (sensor.celsiusCurrent >= 60) valueEl.classList.add('temp-sensor__value--warm');
    else valueEl.classList.add('temp-sensor__value--normal');
  }
}

// Initialize
async function main() {
  await anim.init();

  // Entrance animation for metric cards
  anim.animateEntrance(document.querySelectorAll('.metric-card'));

  hub.onSystemMetrics(updateSystemMetrics);
  hub.onContainerList(updateContainerList);
  hub.onContainerStats(updateContainerStats);

  // Fetch initial data
  try {
    const [metricsRes, containersRes] = await Promise.all([
      fetch('/api/metrics/current'),
      fetch('/api/containers'),
    ]);

    if (metricsRes.ok && metricsRes.status !== 204) {
      updateSystemMetrics(await metricsRes.json());
    }

    if (containersRes.ok) {
      const data = await containersRes.json();
      updateContainerList(data.containers);
      updateContainerStats(data.stats);
    }
  } catch (e) {
    console.warn('Failed to load initial data:', e);
  }

  await hub.start();
}

main();
