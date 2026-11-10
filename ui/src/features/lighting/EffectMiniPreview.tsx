import { memo, useEffect, useRef } from 'react';
import type { EffectCategory } from '../../api/contracts.ts';

interface EffectMiniPreviewProps {
  effectId: string;
  category: EffectCategory;
  /** Comma-separated `#RRGGBB` stops. Equal strings skip a re-render. */
  palette: string;
  animated: boolean;
}

function colorAt(colors: readonly string[], index: number): string {
  if (colors.length === 0) {
    return '#FFFFFF';
  }
  const wrapped = ((index % colors.length) + colors.length) % colors.length;
  return colors[wrapped] ?? '#FFFFFF';
}

function paintFlow(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  colors: readonly string[],
  seconds: number,
) {
  const count = Math.max(colors.length, 1);
  const band = width / count;
  const offset = band === 0 ? 0 : (seconds * 36) % band;
  for (let index = -1; index <= count; index += 1) {
    ctx.fillStyle = colorAt(colors, index);
    ctx.fillRect(index * band + offset, 0, band + 1, height);
  }
}

function paintRainbow(ctx: CanvasRenderingContext2D, width: number, height: number, seconds: number) {
  const gradient = ctx.createLinearGradient(0, 0, width, 0);
  const steps = 8;
  for (let index = 0; index <= steps; index += 1) {
    const hue = (index / steps) * 360 + seconds * 48;
    gradient.addColorStop(index / steps, `hsl(${hue} 100% 58%)`);
  }
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);
}

function paintBreathing(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  colors: readonly string[],
  seconds: number,
) {
  const pulse = 0.35 + 0.65 * (0.5 + 0.5 * Math.sin(seconds * Math.PI));
  ctx.fillStyle = '#09090b';
  ctx.fillRect(0, 0, width, height);
  ctx.globalAlpha = pulse;
  ctx.fillStyle = colorAt(colors, Math.floor(seconds / 2));
  ctx.fillRect(0, 0, width, height);
  ctx.globalAlpha = 1;
}

function paintSpectrum(ctx: CanvasRenderingContext2D, width: number, height: number, seconds: number) {
  ctx.fillStyle = `hsl(${seconds * 48} 100% 56%)`;
  ctx.fillRect(0, 0, width, height);
}

function paintTemperature(ctx: CanvasRenderingContext2D, width: number, height: number, colors: readonly string[]) {
  if (colors.length <= 1) {
    ctx.fillStyle = colors[0] ?? '#FFFFFF';
    ctx.fillRect(0, 0, width, height);
    return;
  }
  const gradient = ctx.createLinearGradient(0, 0, width, 0);
  colors.forEach((color, index) => {
    gradient.addColorStop(index / (colors.length - 1), color);
  });
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, width, height);
  ctx.fillStyle = '#ffffff';
  ctx.globalAlpha = 0.85;
  ctx.beginPath();
  ctx.arc(width * 0.62, height / 2, Math.max(2, height * 0.12), 0, Math.PI * 2);
  ctx.fill();
  ctx.globalAlpha = 1;
}

function paintAmbilight(ctx: CanvasRenderingContext2D, width: number, height: number, seconds: number) {
  const columns = 8;
  const rows = 3;
  const cellWidth = width / columns;
  const cellHeight = height / rows;
  for (let y = 0; y < rows; y += 1) {
    for (let x = 0; x < columns; x += 1) {
      const hue = (x * 28 + y * 46 + seconds * 40) % 360;
      ctx.fillStyle = `hsl(${hue} 75% ${42 + ((x + y) % 3) * 8}%)`;
      ctx.fillRect(x * cellWidth, y * cellHeight, cellWidth + 0.5, cellHeight + 0.5);
    }
  }
}

function paintSpatial(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  colors: readonly string[],
  seconds: number,
) {
  ctx.fillStyle = '#09090b';
  ctx.fillRect(0, 0, width, height);
  const centerX = width * (0.5 + 0.18 * Math.sin(seconds * 0.8));
  const centerY = height * (0.5 + 0.12 * Math.cos(seconds * 0.7));
  for (let ring = 4; ring >= 1; ring -= 1) {
    ctx.beginPath();
    ctx.fillStyle = colorAt(colors, ring);
    ctx.globalAlpha = 0.28 + ring * 0.12;
    ctx.arc(centerX, centerY, Math.min(width, height) * 0.2 * ring, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.globalAlpha = 1;
}

function paintReactive(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  colors: readonly string[],
  seconds: number,
) {
  ctx.fillStyle = '#09090b';
  ctx.fillRect(0, 0, width, height);
  const bars = 16;
  const gap = 2;
  const barWidth = (width - gap * (bars - 1)) / bars;
  for (let index = 0; index < bars; index += 1) {
    const wave = 0.22 + 0.78 * Math.abs(Math.sin(seconds * 3 + index * 0.55));
    const barHeight = height * wave;
    ctx.fillStyle = colorAt(colors, index);
    ctx.fillRect(index * (barWidth + gap), height - barHeight, barWidth, barHeight);
  }
}

function paintCustom(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  colors: readonly string[],
  seconds: number,
) {
  ctx.fillStyle = '#09090b';
  ctx.fillRect(0, 0, width, height);
  const stripe = 12;
  const offset = (seconds * 24) % stripe;
  for (let index = -4; index < width / stripe + 6; index += 1) {
    ctx.fillStyle = colorAt(colors, index);
    ctx.beginPath();
    const start = index * stripe + offset;
    ctx.moveTo(start, 0);
    ctx.lineTo(start + stripe * 0.55, 0);
    ctx.lineTo(start + stripe * 0.55 - height, height);
    ctx.lineTo(start - height, height);
    ctx.closePath();
    ctx.fill();
  }
}

function paintPreview(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  effectId: string,
  category: EffectCategory,
  colors: readonly string[],
  seconds: number,
) {
  const palette = colors.length > 0 ? colors : ['#7C3AED', '#22D3EE', '#F472B6'];
  switch (effectId) {
    case 'rainbow-wave':
      paintRainbow(ctx, width, height, seconds);
      return;
    case 'spectrum-cycle':
      paintSpectrum(ctx, width, height, seconds);
      return;
    case 'breathing':
      paintBreathing(ctx, width, height, palette, seconds);
      return;
    case 'solid':
      ctx.fillStyle = palette[0] ?? '#FF0080';
      ctx.fillRect(0, 0, width, height);
      return;
    case 'gradient':
      paintFlow(ctx, width, height, palette, seconds);
      return;
    case 'temperature':
      paintTemperature(ctx, width, height, palette);
      return;
    case 'ambilight':
      paintAmbilight(ctx, width, height, seconds);
      return;
    default:
      break;
  }

  switch (category) {
    case 'Flowing':
      paintFlow(ctx, width, height, palette, seconds);
      break;
    case 'Spatial':
      paintSpatial(ctx, width, height, palette, seconds);
      break;
    case 'Reactive':
      paintReactive(ctx, width, height, palette, seconds);
      break;
    case 'Static':
      paintTemperature(ctx, width, height, palette);
      break;
    case 'Custom':
      paintCustom(ctx, width, height, palette, seconds);
      break;
    default: {
      const unhandled: never = category;
      void unhandled;
    }
  }
}

/** Lightweight looping preview. Holds a single frame when the effect is static or motion is reduced. */
export const EffectMiniPreview = memo(function EffectMiniPreview({
  effectId,
  category,
  palette,
  animated,
}: EffectMiniPreviewProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) {
      return;
    }

    const media = window.matchMedia('(prefers-reduced-motion: reduce)');
    const colors = palette.length > 0 ? palette.split(',') : [];
    let frame = 0;

    const shouldLoop = () => animated && !media.matches;

    const draw = (time: number) => {
      const viewWidth = canvas.clientWidth;
      const viewHeight = canvas.clientHeight;
      if (viewWidth === 0 || viewHeight === 0) {
        frame = requestAnimationFrame(draw);
        return;
      }

      const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
      const pixelWidth = Math.round(viewWidth * pixelRatio);
      const pixelHeight = Math.round(viewHeight * pixelRatio);
      if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
        canvas.width = pixelWidth;
        canvas.height = pixelHeight;
      }

      const ctx = canvas.getContext('2d');
      if (!ctx) {
        return;
      }

      ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
      ctx.globalAlpha = 1;
      paintPreview(ctx, viewWidth, viewHeight, effectId, category, colors, shouldLoop() ? time / 1000 : 0);
      if (shouldLoop()) {
        frame = requestAnimationFrame(draw);
      }
    };

    const start = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(draw);
    };

    media.addEventListener('change', start);
    start();
    return () => {
      cancelAnimationFrame(frame);
      media.removeEventListener('change', start);
    };
  }, [animated, category, effectId, palette]);

  return <canvas ref={canvasRef} aria-hidden="true" className="pointer-events-none block h-12 w-full" />;
});
