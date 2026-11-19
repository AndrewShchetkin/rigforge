import { useEffect, useRef, type ReactNode } from 'react';
import { usePrefersReducedMotion } from '../ui/motion.ts';

const TRAIL = ['·', '+', '×', '░'];
const BURST = ['*', '+', '×', '·'];

/** Cursor trail, click burst, and a glyph that runs along the panel under the pointer. */
export function AsciiFrame({ children }: { children: ReactNode }) {
  const stageRef = useRef<HTMLDivElement>(null);
  const layerRef = useRef<HTMLDivElement>(null);
  const reduced = usePrefersReducedMotion();

  useEffect(() => {
    const stage = stageRef.current;
    const layer = layerRef.current;
    if (!stage || !layer || reduced) {
      return;
    }

    let lastAt = 0;
    let lastX = 0;
    let lastY = 0;
    let glyphIndex = 0;

    const spawn = (className: string, x: number, y: number, glyph: string, dx?: string, dy?: string) => {
      if (layer.childElementCount > 48) {
        return;
      }
      const el = document.createElement('span');
      el.className = `ascii-glyph ${className}`;
      el.textContent = glyph;
      el.style.left = `${x}px`;
      el.style.top = `${y}px`;
      if (dx !== undefined) {
        el.style.setProperty('--dx', dx);
      }
      if (dy !== undefined) {
        el.style.setProperty('--dy', dy);
      }
      layer.appendChild(el);
      el.addEventListener('animationend', () => el.remove());
    };

    const onMove = (event: PointerEvent) => {
      const now = performance.now();
      if (now - lastAt < 28) {
        return;
      }
      const rect = stage.getBoundingClientRect();
      const x = event.clientX - rect.left;
      const y = event.clientY - rect.top;
      if (Math.abs(x - lastX) + Math.abs(y - lastY) < 12) {
        return;
      }
      lastAt = now;
      lastX = x;
      lastY = y;
      spawn('ascii-trail', x, y, TRAIL[glyphIndex % TRAIL.length] ?? '·');
      glyphIndex += 1;
    };

    const onDown = (event: PointerEvent) => {
      const rect = stage.getBoundingClientRect();
      const x = event.clientX - rect.left;
      const y = event.clientY - rect.top;
      for (let i = 0; i < 8; i += 1) {
        const angle = (Math.PI * 2 * i) / 8;
        spawn(
          'ascii-burst',
          x,
          y,
          BURST[i % BURST.length] ?? '*',
          `${Math.cos(angle) * 22}px`,
          `${Math.sin(angle) * 16}px`,
        );
      }
      const target = event.target;
      if (!(target instanceof Element)) {
        return;
      }
      const panel = target.closest('[data-panel]');
      if (!(panel instanceof HTMLElement)) {
        return;
      }
      panel.querySelector(':scope > .ascii-scan')?.remove();
      const scan = document.createElement('span');
      scan.className = 'ascii-scan';
      scan.setAttribute('aria-hidden', 'true');
      scan.textContent = '▓';
      panel.appendChild(scan);
      scan.addEventListener('animationend', () => scan.remove());
    };

    stage.addEventListener('pointermove', onMove);
    stage.addEventListener('pointerdown', onDown);
    return () => {
      stage.removeEventListener('pointermove', onMove);
      stage.removeEventListener('pointerdown', onDown);
    };
  }, [reduced]);

  return (
    <div ref={stageRef} className="relative min-h-dvh">
      {children}
      <div ref={layerRef} className="pointer-events-none absolute inset-0 z-40 overflow-hidden" />
    </div>
  );
}
