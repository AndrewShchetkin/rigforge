import type { LedPositionDto, VirtualCanvas, ZonePlacement } from '../../api/contracts.ts';
import { clamp, roundTo } from '../../lib/math.ts';
import { placementKey, zoneKey } from '../../lib/zones.ts';

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

/** LED index of each placement inside its physical zone, in chain order. */
export function segmentLedOffsets(placements: readonly ZonePlacement[]): Map<string, number> {
  const next = new Map<string, number>();
  const offsets = new Map<string, number>();
  for (const placement of placements) {
    const zone = zoneKey(placement.deviceId, placement.zoneId);
    const offset = next.get(zone) ?? 0;
    offsets.set(placementKey(placement), offset);
    next.set(zone, offset + (placement.ledCount ?? 0));
  }
  return offsets;
}

/**
 * Appends a segment after the selected part of a resizable zone. The first split gives the existing
 * box an id. When the header is already full, LEDs are taken from the selected segment.
 */
export function addSegment(
  canvas: VirtualCanvas,
  key: string,
  zoneLedCount: number,
  maxLedCount: number,
): { canvas: VirtualCanvas; selectKey: string } | null {
  const selectedIndex = canvas.placements.findIndex((placement) => placementKey(placement) === key);
  if (selectedIndex < 0) {
    return null;
  }
  const selected = canvas.placements[selectedIndex];
  if (!selected) {
    return null;
  }

  const placements = canvas.placements.map((placement) => ({ ...placement }));
  const isSibling = (placement: ZonePlacement) =>
    placement.deviceId === selected.deviceId && placement.zoneId === selected.zoneId;
  if (placements.filter(isSibling).length === 1 && !placements[selectedIndex]?.segmentId) {
    const current = placements[selectedIndex];
    if (current) {
      placements[selectedIndex] = {
        ...current,
        segmentId: 'seg1',
        name: current.name ?? 'Segment 1',
        ledCount: current.ledCount ?? zoneLedCount,
      };
    }
  }

  const donor = placements[selectedIndex];
  if (!donor) {
    return null;
  }
  const siblings = placements.filter(isSibling);
  const used = siblings.reduce((sum, placement) => sum + (placement.ledCount ?? 0), 0);
  let newCount = Math.min(8, Math.max(0, maxLedCount - used));
  if (newCount < 1) {
    const donorCount = donor.ledCount ?? zoneLedCount;
    if (donorCount <= 1) {
      return null;
    }
    newCount = Math.max(1, Math.floor(donorCount / 2));
    placements[selectedIndex] = { ...donor, ledCount: donorCount - newCount };
  }

  const taken = new Set(siblings.map((placement) => placement.segmentId).filter((id): id is string => Boolean(id)));
  let segmentNumber = siblings.length + 1;
  let segmentId = `seg${segmentNumber}`;
  while (taken.has(segmentId)) {
    segmentNumber += 1;
    segmentId = `seg${segmentNumber}`;
  }

  const created: ZonePlacement = {
    deviceId: selected.deviceId,
    zoneId: selected.zoneId,
    segmentId,
    name: `Segment ${siblings.length + 1}`,
    x: roundTo(selected.x + selected.width + 8, 2),
    y: selected.y,
    width: selected.width,
    height: selected.height,
    rotation: 0,
    enabled: true,
    ledCount: newCount,
    topology: donor.topology ?? null,
    matrixColumns: null,
    serpentine: false,
  };
  placements.splice(selectedIndex + 1, 0, created);
  return { canvas: { ...canvas, placements }, selectKey: placementKey(created) };
}

/** Drops one segment. The last remaining part of a zone is left in place. */
export function removeSegment(
  canvas: VirtualCanvas,
  key: string,
): { canvas: VirtualCanvas; selectKey: string | null } | null {
  const selected = canvas.placements.find((placement) => placementKey(placement) === key);
  if (!selected?.segmentId) {
    return null;
  }
  const isSibling = (placement: ZonePlacement) =>
    placement.deviceId === selected.deviceId && placement.zoneId === selected.zoneId;
  const siblings = canvas.placements.filter(isSibling);
  if (siblings.length <= 1) {
    return null;
  }
  const ordinal = siblings.findIndex((placement) => placementKey(placement) === key);
  const placements = canvas.placements.filter((placement) => placementKey(placement) !== key);
  const remaining = placements.filter(isSibling);
  const nextIndex = ordinal >= remaining.length ? remaining.length - 1 : ordinal;
  const next = remaining[nextIndex];
  return { canvas: { ...canvas, placements }, selectKey: next ? placementKey(next) : null };
}

function samePlacement(a: ZonePlacement, b: ZonePlacement): boolean {
  return (
    a.x === b.x &&
    a.y === b.y &&
    a.width === b.width &&
    a.height === b.height &&
    a.rotation === b.rotation &&
    a.enabled === b.enabled &&
    a.ledCount === b.ledCount &&
    (a.name ?? null) === (b.name ?? null) &&
    (a.topology ?? null) === (b.topology ?? null) &&
    (a.matrixColumns ?? null) === (b.matrixColumns ?? null) &&
    (a.serpentine ?? false) === (b.serpentine ?? false)
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
