export function createSparkline(canvas, options = {}) {
  const { color = '#60a5fa', maxPoints = 300 } = options;
  const ctx = canvas.getContext('2d');
  const data = [];
  let animFrame = null;

  const observer = new ResizeObserver(() => draw());
  observer.observe(canvas);

  function draw() {
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const w = rect.width;
    const h = rect.height;

    ctx.clearRect(0, 0, w, h);
    if (data.length < 2) return;

    const max = Math.max(...data, 1);
    const step = w / (maxPoints - 1);

    // Gradient fill area below the line
    ctx.beginPath();
    for (let i = 0; i < data.length; i++) {
      const x = (maxPoints - data.length + i) * step;
      const y = h - (data[i] / max) * (h - 4) - 2;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    const lastDataX = (maxPoints - 1) * step;
    const firstDataX = (maxPoints - data.length) * step;
    ctx.lineTo(lastDataX, h);
    ctx.lineTo(firstDataX, h);
    ctx.closePath();

    const gradient = ctx.createLinearGradient(0, 0, 0, h);
    gradient.addColorStop(0, colorWithAlpha(color, 0.15));
    gradient.addColorStop(1, colorWithAlpha(color, 0));
    ctx.fillStyle = gradient;
    ctx.fill();

    // Stroke line
    ctx.beginPath();
    ctx.strokeStyle = color;
    ctx.lineWidth = 1.5;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';

    for (let i = 0; i < data.length; i++) {
      const x = (maxPoints - data.length + i) * step;
      const y = h - (data[i] / max) * (h - 4) - 2;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    }
    ctx.stroke();

    // Glow dot on last point
    if (data.length > 0) {
      const lastX = w;
      const lastY = h - (data[data.length - 1] / max) * (h - 4) - 2;
      ctx.beginPath();
      ctx.arc(lastX, lastY, 3, 0, Math.PI * 2);
      ctx.fillStyle = color;
      ctx.fill();
      ctx.beginPath();
      ctx.arc(lastX, lastY, 6, 0, Math.PI * 2);
      ctx.fillStyle = colorWithAlpha(color, 0.3);
      ctx.fill();
    }
  }

  return {
    update(value) {
      data.push(value);
      if (data.length > maxPoints) data.shift();
      if (animFrame) cancelAnimationFrame(animFrame);
      animFrame = requestAnimationFrame(draw);
    },
    destroy() {
      observer.disconnect();
      if (animFrame) cancelAnimationFrame(animFrame);
    },
  };
}

export function createAreaChart(canvas, options = {}) {
  const {
    colorTx = '#4ade80',
    colorRx = '#60a5fa',
    maxPoints = 300,
  } = options;

  const ctx = canvas.getContext('2d');
  const txData = [];
  const rxData = [];
  let animFrame = null;

  const observer = new ResizeObserver(() => draw());
  observer.observe(canvas);

  function draw() {
    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const w = rect.width;
    const h = rect.height;

    ctx.clearRect(0, 0, w, h);
    if (txData.length < 2) return;

    const allValues = [...txData, ...rxData];
    const max = Math.max(...allValues, 1024); // min 1KB/s scale
    const step = w / (maxPoints - 1);

    drawArea(rxData, colorRx, 0.2);
    drawArea(txData, colorTx, 0.2);
    drawLine(rxData, colorRx);
    drawLine(txData, colorTx);

    function drawLine(data, color) {
      ctx.beginPath();
      ctx.strokeStyle = color;
      ctx.lineWidth = 1.5;
      ctx.lineJoin = 'round';

      for (let i = 0; i < data.length; i++) {
        const x = (maxPoints - data.length + i) * step;
        const y = h - (data[i] / max) * (h - 8) - 4;
        if (i === 0) ctx.moveTo(x, y);
        else ctx.lineTo(x, y);
      }
      ctx.stroke();
    }

    function drawArea(data, color, opacity) {
      if (data.length < 2) return;
      const startX = (maxPoints - data.length) * step;

      ctx.beginPath();
      ctx.moveTo(startX, h);
      for (let i = 0; i < data.length; i++) {
        const x = (maxPoints - data.length + i) * step;
        const y = h - (data[i] / max) * (h - 8) - 4;
        ctx.lineTo(x, y);
      }
      ctx.lineTo((maxPoints - data.length + data.length - 1) * step, h);
      ctx.closePath();

      const gradient = ctx.createLinearGradient(0, 0, 0, h);
      gradient.addColorStop(0, colorWithAlpha(color, opacity));
      gradient.addColorStop(1, 'transparent');
      ctx.fillStyle = gradient;
      ctx.fill();
    }
  }

  return {
    update(tx, rx) {
      txData.push(tx);
      rxData.push(rx);
      if (txData.length > maxPoints) { txData.shift(); rxData.shift(); }
      if (animFrame) cancelAnimationFrame(animFrame);
      animFrame = requestAnimationFrame(draw);
    },
    destroy() {
      observer.disconnect();
      if (animFrame) cancelAnimationFrame(animFrame);
    },
  };
}

/**
 * Convert a hex or CSS color string to one with the given alpha.
 * Handles hex (#rrggbb, #rgb) and returns an rgba string.
 * @param {string} color
 * @param {number} alpha
 * @returns {string}
 */
function colorWithAlpha(color, alpha) {
  if (color.startsWith('#')) {
    let hex = color.slice(1);
    if (hex.length === 3) {
      hex = hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
    }
    const r = parseInt(hex.slice(0, 2), 16);
    const g = parseInt(hex.slice(2, 4), 16);
    const b = parseInt(hex.slice(4, 6), 16);
    return `rgba(${r}, ${g}, ${b}, ${alpha})`;
  }
  // Fallback: try to append alpha via string replacement
  if (color.startsWith('rgba')) {
    return color.replace(/[\d.]+\)$/, `${alpha})`);
  }
  if (color.startsWith('rgb(')) {
    return color.replace('rgb(', 'rgba(').replace(')', `, ${alpha})`);
  }
  return color;
}
