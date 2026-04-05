let gsap = null;

/** Shared flag — true when the user prefers reduced motion. */
export const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export async function init() {
  if (prefersReducedMotion) return;
  try {
    const mod = await import('https://cdn.jsdelivr.net/npm/gsap@3.12.5/+esm');
    gsap = mod.gsap || mod.default;
  } catch (e) {
    console.warn('GSAP not available, falling back to instant updates');
  }
}

/**
 * Smoothly interpolates a number displayed in an element over ~400ms.
 * @param {HTMLElement} element
 * @param {number} value
 * @param {(v: number) => string} format
 */
export function animateNumberTo(element, value, format = v => v.toFixed(1)) {
  if (!element) return;
  const current = parseFloat(element.textContent) || 0;

  if (!gsap || prefersReducedMotion) {
    element.textContent = format(value);
    return;
  }

  const obj = { val: current };
  gsap.to(obj, {
    val: value,
    duration: 0.4,
    ease: 'power2.out',
    onUpdate: () => { element.textContent = format(obj.val); },
  });
}

/**
 * Animates a gauge ring to the given percent.
 * Uses elastic easing for small changes (<10% delta) and power2 for larger ones.
 * @param {SVGCircleElement} circleEl
 * @param {number} percent
 */
export function animateGaugeRing(circleEl, percent) {
  if (!circleEl) return;
  const circumference = 2 * Math.PI * 60; // r=60
  const offset = circumference * (1 - Math.min(percent, 100) / 100);

  if (!gsap || prefersReducedMotion) {
    circleEl.style.strokeDashoffset = offset;
    return;
  }

  const currentOffset = parseFloat(circleEl.style.strokeDashoffset) || circumference;
  const currentPercent = (1 - currentOffset / circumference) * 100;
  const delta = Math.abs(percent - currentPercent);
  const ease = delta < 10 ? 'elastic.out(1, 0.5)' : 'power2.out';

  gsap.to(circleEl, {
    strokeDashoffset: offset,
    duration: 0.8,
    ease,
  });
}

/**
 * Staggered grid entrance: fades + scales children of a container.
 * @param {string} selector  CSS selector for the parent whose children animate in.
 */
export function animateStaggeredGrid(selector) {
  const parent = document.querySelector(selector);
  if (!parent) return;
  const children = parent.children;
  if (children.length === 0) return;

  if (!gsap || prefersReducedMotion) {
    for (const child of children) { child.style.opacity = '1'; }
    return;
  }

  gsap.fromTo(children,
    { opacity: 0, y: 24, scale: 0.95 },
    { opacity: 1, y: 0, scale: 1, duration: 0.6, stagger: 0.08, ease: 'power2.out' }
  );
}

export function animateEntrance(elements) {
  if (!gsap || prefersReducedMotion) {
    elements.forEach(el => { el.style.opacity = '1'; });
    return;
  }

  gsap.fromTo(elements,
    { opacity: 0, y: 24 },
    { opacity: 1, y: 0, duration: 0.6, stagger: 0.08, ease: 'power3.out' }
  );
}

export function animateCardIn(element) {
  if (!gsap || prefersReducedMotion) {
    element.style.opacity = '1';
    return;
  }

  gsap.fromTo(element,
    { opacity: 0, y: 16, scale: 0.96 },
    { opacity: 1, y: 0, scale: 1, duration: 0.5, ease: 'back.out(1.2)' }
  );
}

export function animateCardOut(element) {
  return new Promise(resolve => {
    if (!gsap || prefersReducedMotion) {
      element.remove();
      resolve();
      return;
    }

    gsap.to(element, {
      opacity: 0, scale: 0.95, duration: 0.3, ease: 'power2.in',
      onComplete: () => { element.remove(); resolve(); },
    });
  });
}

/**
 * Briefly pulses an element — scales to 1.05 with a glow, then back.
 * Used to draw attention when a threshold is crossed (e.g. CPU > 80%).
 * @param {HTMLElement} element
 */
export function animatePulse(element) {
  if (!element) return;

  if (!gsap || prefersReducedMotion) return;

  const tl = gsap.timeline();
  tl.to(element, {
    scale: 1.05,
    boxShadow: '0 0 24px oklch(75% 0.18 250 / 0.35)',
    duration: 0.15,
    ease: 'power2.out',
  });
  tl.to(element, {
    scale: 1,
    boxShadow: '0 0 0px oklch(75% 0.18 250 / 0)',
    duration: 0.3,
    ease: 'power2.inOut',
  });
}
