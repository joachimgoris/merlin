import { connection, onTerminalOutput, offTerminalOutput } from './signalr-client.js';

const terminalContainer = document.getElementById('terminal-container');

/** @type {InstanceType<typeof window.Terminal> | null} */
let term = null;
/** @type {InstanceType<typeof window.FitAddon.FitAddon> | null} */
let fitAddon = null;
/** @type {ResizeObserver | null} */
let resizeObserver = null;
/** @type {((data: string) => void) | null} */
let outputHandler = null;

/**
 * Builds an xterm.js theme from the dashboard CSS custom properties.
 */
function getTerminalTheme() {
  const style = getComputedStyle(document.documentElement);
  return {
    background: style.getPropertyValue('--color-bg').trim() || '#1a1a2e',
    foreground: style.getPropertyValue('--color-text').trim() || '#e0e0e0',
    cursor: style.getPropertyValue('--color-accent-container').trim() || '#b388ff',
    cursorAccent: style.getPropertyValue('--color-bg').trim() || '#1a1a2e',
    selectionBackground: 'rgba(179, 136, 255, 0.3)',
  };
}

/**
 * Opens a terminal session for the given container.
 * @param {string} containerId
 */
export async function openTerminal(containerId) {
  closeTerminal();

  const Terminal = window.Terminal;
  const FitAddon = window.FitAddon.FitAddon;

  term = new Terminal({
    theme: getTerminalTheme(),
    fontFamily: "'JetBrains Mono', 'Fira Code', monospace",
    fontSize: 13,
    cursorBlink: true,
    allowProposedApi: true,
  });

  fitAddon = new FitAddon();
  term.loadAddon(fitAddon);
  term.open(terminalContainer);
  fitAddon.fit();

  // Send input keystrokes to the server
  term.onData(data => {
    connection.invoke('TerminalInput', data).catch(() => {});
  });

  // Send resize events to the server
  term.onResize(({ cols, rows }) => {
    connection.invoke('TerminalResize', cols, rows).catch(() => {});
  });

  // Listen for output from the server
  outputHandler = (data) => {
    if (term) {
      term.write(data);
    }
  };
  onTerminalOutput(outputHandler);

  // Observe container resize to refit the terminal
  resizeObserver = new ResizeObserver(() => {
    if (fitAddon) {
      fitAddon.fit();
    }
  });
  resizeObserver.observe(terminalContainer);

  // Start the exec session on the server
  try {
    await connection.invoke('StartTerminal', containerId);
  } catch {
    if (term) {
      term.write('\r\n*** Failed to start terminal session ***\r\n');
    }
  }

  // Send initial resize after session starts
  if (term) {
    connection.invoke('TerminalResize', term.cols, term.rows).catch(() => {});
  }
}

/**
 * Closes the active terminal session and cleans up resources.
 */
export function closeTerminal() {
  if (resizeObserver) {
    resizeObserver.disconnect();
    resizeObserver = null;
  }

  if (outputHandler) {
    offTerminalOutput(outputHandler);
    outputHandler = null;
  }

  if (term) {
    connection.invoke('StopTerminal').catch(() => {});
    term.dispose();
    term = null;
    fitAddon = null;
  }

  terminalContainer.innerHTML = '';
}
