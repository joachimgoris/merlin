const MAX_TOASTS = 5;
const TOAST_DURATION = 5000;

const container = document.getElementById('toast-container');

/**
 * Show a toast notification.
 * @param {string} message
 * @param {'info' | 'warning' | 'error'} type
 */
export function showToast(message, type = 'info') {
  const toast = document.createElement('div');
  toast.className = `toast toast--${type}`;
  toast.textContent = message;
  container.appendChild(toast);

  // Enforce max visible toasts — remove the oldest if exceeded
  const toasts = container.querySelectorAll('.toast');
  if (toasts.length > MAX_TOASTS) {
    removeToast(toasts[0]);
  }

  // Trigger slide-in on next frame so the browser registers the initial transform
  requestAnimationFrame(() => {
    toast.classList.add('toast--visible');
  });

  // Auto-remove after duration
  setTimeout(() => {
    removeToast(toast);
  }, TOAST_DURATION);
}

/**
 * Remove a toast with a fade-out transition.
 * @param {HTMLElement} toast
 */
function removeToast(toast) {
  if (!toast.parentNode) return;

  toast.classList.remove('toast--visible');

  toast.addEventListener('transitionend', () => {
    toast.remove();
  }, { once: true });

  // Fallback removal in case transitionend does not fire
  setTimeout(() => {
    toast.remove();
  }, 400);
}
