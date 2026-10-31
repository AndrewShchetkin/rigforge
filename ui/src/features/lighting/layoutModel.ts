import type { LedPositionDto, VirtualCanvas, ZonePlacement } from '../../api/contracts.ts';
import { clamp, roundTo } from '../../lib/math.ts';
import { placementKey } from '../../lib/zones.ts';

/** Smallest zone edge in canvas units. */
export const MIN_ZONE_SIZE = 2;
/** Background grid spacing in canvas units. */
export const GRID_STEP = 10;
export const ROTATION_SNAPS = Array.from({ length: 24 }, (_, index) => index * 15);

export type PlacementPatch = Partial<Omit<ZonePlacement, 'deviceId' | 'zoneId'>>;

export function patchPlacement(canvas: VirtualCanvas, key: string, patch: PlacementPatch): VirtualCanvas {
  return {
    ...canvas,
    placements: canvas.placements.map((placement) =>
      placementKey(placement) === key ? { ...placement, ...patch } : placement,
    ),
  };
}

/** Degrees in [0, 360), rounded to 0.01. */
export function normalizeRotation(degrees: number): number {
  const normalized = roundTo(((degrees % 360) + 360) % 360, 2);
  return normalized >= 360 ? 0 : normalized;
}

/** Client-side grid layout: one cell per zone, zones shrunk to fit their cell, rotation reset. */
export function autoArrange(canvas: VirtualCanvas): VirtualCanvas {
  const count = canvas.placements.length;
  if (count === 0) {
    return canvas;
  }
  const columns = clamp(Math.round(Math.sqrt((count * canvas.width) / canvas.height)), 1, count);
  const rows = Math.ceil(count / columns);
  const cellWidth = canvas.width / columns;
  const cellHeight = canvas.height / rows;

  const placements = canvas.placements.map((placement, index) => {
    const fit = Math.min(1, (cellWidth * 0.85) / placement.width, (cellHeight * 0.85) / placement.height);
    const width = roundTo(placement.width * fit, 2);
    const height = roundTo(placement.height * fit, 2);
    const column = index % columns;
    const row = Math.floor(index / columns);
    return {
      ...placement,
      x: roundTo(column * cellWidth + (cellWidth - width) / 2, 2),
      y: roundTo(row * cellHeight + (cellHeight - height) / 2, 2),
      width,
      height,
      rotation: 0,
    };
  });
  return { ...canvas, placements };
}

function samePlacement(a: ZonePlacement, b: ZonePlacement): boolean {
  return (
    a.x === b.x &&
    a.y === b.y &&
    a.width === b.width &&
    a.height === b.height &&
    a.rotation === b.rotation &&
    a.enabled === b.enabled &&
    a.ledCount === b.ledCount
  );
}

/** Structural equality, independent of placement order. */
export function canvasEquals(a: VirtualCanvas, b: VirtualCanvas): boolean {
  if (a.width !== b.width || a.height !== b.height || a.placements.length !== b.placements.length) {
    return false;
  }
  const byKey = new Map(b.placements.map((placement) => [placementKey(placement), placement]));
  return a.placements.every((placement) => {
    const other = byKey.get(placementKey(placement));
    return other !== undefined && samePlacement(placement, other);
  });
}

/** LED dot radius in canvas units: ~4.5 px on screen, smaller when neighbouring LEDs are close together. */
export function ledRadius(leds: readonly LedPositionDto[], width: number, height: number, scale: number): number {
  let minGap = Infinity;
  for (let index = 1; index < leds.length; index++) {
    const a = leds[index - 1];
    const b = leds[index];
    if (a && b) {
      const gap = Math.hypot((b.x - a.x) * width, (b.y - a.y) * height);
      if (gap > 0) {
        minGap = Math.min(minGap, gap);
      }
    }
  }
  return clamp(minGap * 0.42, 1.5 / scale, 4.5 / scale);
}
