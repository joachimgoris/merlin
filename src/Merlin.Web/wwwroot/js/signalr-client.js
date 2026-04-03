const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hub/metrics')
  .withAutomaticReconnect([0, 1000, 5000, 10000, 30000])
  .build();

const listeners = {
  SystemMetrics: [],
  ContainerList: [],
  ContainerStats: [],
};

connection.on('SystemMetrics', data => listeners.SystemMetrics.forEach(cb => cb(data)));
connection.on('ContainerList', data => listeners.ContainerList.forEach(cb => cb(data)));
connection.on('ContainerStats', data => listeners.ContainerStats.forEach(cb => cb(data)));

const dot = document.getElementById('connection-dot');
const text = document.getElementById('connection-text');

function setStatus(state) {
  dot.className = 'connection-dot';
  if (state === 'connected') {
    dot.classList.add('connection-dot--connected');
    text.textContent = 'Live';
  } else if (state === 'reconnecting') {
    dot.classList.add('connection-dot--reconnecting');
    text.textContent = 'Reconnecting...';
  } else {
    dot.classList.add('connection-dot--disconnected');
    text.textContent = 'Disconnected';
  }
}

connection.onreconnecting(() => setStatus('reconnecting'));
connection.onreconnected(() => setStatus('connected'));
connection.onclose(() => setStatus('disconnected'));

export async function start() {
  try {
    await connection.start();
    setStatus('connected');
  } catch (err) {
    console.error('SignalR connection failed:', err);
    setStatus('disconnected');
    setTimeout(() => start(), 5000);
  }
}

export function onSystemMetrics(cb) { listeners.SystemMetrics.push(cb); }
export function onContainerList(cb) { listeners.ContainerList.push(cb); }
export function onContainerStats(cb) { listeners.ContainerStats.push(cb); }

export async function startContainer(id) { await connection.invoke('StartContainer', id); }
export async function stopContainer(id) { await connection.invoke('StopContainer', id); }
export async function restartContainer(id) { await connection.invoke('RestartContainer', id); }

export function streamLogs(containerId, tail, onLine, onComplete) {
  const subject = connection.stream('StreamContainerLogs', containerId, tail);
  const subscription = subject.subscribe({
    next: line => onLine(line),
    error: err => { console.error('Log stream error:', err); onComplete?.(); },
    complete: () => onComplete?.(),
  });
  return () => subscription.dispose();
}
