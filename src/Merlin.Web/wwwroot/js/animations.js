let gsap = null;
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export async function init() {
  if (reducedMotion) return;
  try {
    const mod = await import('https://cdn.jsdelivr.net/npm/gsap@3.12.5/+esm');
    gsap = mod.gsap || mod.default;
  } catch (e) {
    console.warn('GSAP not available, falling back to instant updates');
  }
}

export function animateNumberTo(element, value, format = v => v.toFixed(1)) {
  if (!element) return;
  const current = parseFloat(element.textContent) || 0;

  if (!gsap || reducedMotion) {
    element.textContent = format(value);
    return;
  }

  const obj = { val: current };
  gsap.to(obj, {
    val: value,
    duration: 0.6,
    ease: 'power2.out',
    onUpdate: () => { element.textContent = format(obj.val); },
  });
}

export function animateGaugeRing(circleEl, percent) {
  if (!circleEl) return;
  const circumference = 2 * Math.PI * 60; // r=60
  const offset = circumference * (1 - Math.min(percent, 100) / 100);

  if (!gsap || reducedMotion) {
    circleEl.style.strokeDashoffset = offset;
    return;
  }

  gsap.to(circleEl, {
    strokeDashoffset: offset,
    duration: 0.8,
    ease: 'power2.out',
  });
}

export function animateEntrance(elements) {
  if (!gsap || reducedMotion) {
    elements.forEach(el => { el.style.opacity = '1'; });
    return;
  }

  gsap.fromTo(elements,
    { opacity: 0, y: 24 },
    { opacity: 1, y: 0, duration: 0.6, stagger: 0.08, ease: 'power3.out' }
  );
}

export function animateCardIn(element) {
  if (!gsap || reducedMotion) {
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
    if (!gsap || reducedMotion) {
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
