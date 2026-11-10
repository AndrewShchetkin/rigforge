import type { LedPositionDto, VirtualCanvas, ZonePlacement } from '../../api/contracts.ts';
import { clamp, roundTo } from '../../lib/math.ts';
import { placementKey, zoneKey } from '../../lib/zones.ts';

/** Smallest zone edge in canvas units. */
export const MIN_ZONE_SIZE = 2;
/** Background grid spacing in canvas units. */
export const GRID_STEP = 10;
export const ROTATION_SNAPS = Array.from({ length: 24 }, (_, index) => index * 15);

/** `0` is free placement. `5` and `10` snap drags, resizes, and nudges to that grid. */
export type SnapStep = 0 | 5 | 10;

export const SNAP_OPTIONS: readonly { readonly value: SnapStep; readonly label: string }[] = [
  { value: 0, label: 'Free' },
  { value: 5, label: '5 px' },
  { value: 10, label: '10 px' },
];

export interface CanvasPreset {
  id: string;
  label: string;
  width: number;
  height: number;
}

export const CANVAS_PRESETS: readonly CanvasPreset[] = [
  { id: '16:9', label: '320 × 180 (16:9)', width: 320, height: 180 },
  { id: 'case', label: '240 × 240 (1:1 Case)', width: 240, height: 240 },
  { id: 'xl', label: '400 × 225 (XL)', width: 400, height: 225 },
  { id: 'hd', label: '480 × 270 (HD)', width: 480, height: 270 },
];

export const MIN_ZOOM = 0.5;
export const MAX_ZOOM = 3;
export const ZOOM_STEP = 0.25;

export interface CanvasView {
  zoom: number;
  panX: number;
  panY: number;
}

export const FIT_VIEW: CanvasView = { zoom: 1, panX: 0, panY: 0 };

export interface StageMetrics {
  baseScale: number;
  width: number;
  height: number;
}

export type PlacementPatch = Partial<Omit<ZonePlacement, 'deviceId' | 'zoneId'>>;

export interface SnappedBox {
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
}

/** Degrees in [0, 360), rounded to 0.01. */
export function normalizeRotation(degrees: number): number {
  const normalized = roundTo(((degrees % 360) + 360) % 360, 2);
  return normalized >= 360 ? 0 : normalized;
}

/** Free grid (`0`) keeps hundredths. A positive grid snaps to the nearest multiple. */
export function snapValue(value: number, step: SnapStep): number {
  if (!Number.isFinite(value)) {
    return 0;
  }
  if (step <= 0) {
    return roundTo(value, 2);
  }
  return roundTo(Math.round(value / step) * step, 2);
}

/** Snap a zone edge without dropping below {@link MIN_ZONE_SIZE} or off a positive grid. */
export function snapSize(value: number, step: SnapStep): number {
  const snapped = snapValue(Math.max(value, MIN_ZONE_SIZE), step);
  if (snapped >= MIN_ZONE_SIZE) {
    return snapped;
  }
  return step >= MIN_ZONE_SIZE ? step : MIN_ZONE_SIZE;
}

export function nudgeStep(step: SnapStep, shiftKey: boolean): number {
  if (step <= 0) {
    return shiftKey ? 10 : 1;
  }
  return shiftKey ? step * 2 : step;
}

const ROTATED_EDGE = 4;

function clampToRange(value: number, min: number, max: number): number {
  const low = Math.min(min, max);
  const high = Math.max(min, max);
  const next = clamp(value, low, high);
  return next === value ? value : roundTo(next, 2);
}

/**
 * Keeps a zone origin on the canvas. Unrotated boxes stay fully inside.
 * Rotated boxes may overhang by a quarter of their size: rotation is around the top-left corner.
 */
export function clampOrigin(
  x: number,
  y: number,
  width: number,
  height: number,
  rotation: number,
  canvasWidth: number,
  canvasHeight: number,
): { x: number; y: number } {
  const safeWidth = Number.isFinite(width) ? width : 0;
  const safeHeight = Number.isFinite(height) ? height : 0;
  const safeX = Number.isFinite(x) ? x : 0;
  const safeY = Number.isFinite(y) ? y : 0;
  const upright = !Number.isFinite(rotation) || normalizeRotation(rotation) === 0;
  const minX = upright ? 0 : -safeWidth * 0.25;
  const maxX = upright ? Math.max(0, canvasWidth - safeWidth) : canvasWidth - ROTATED_EDGE;
  const minY = upright ? 0 : -safeHeight * 0.25;
  const maxY = upright ? Math.max(0, canvasHeight - safeHeight) : canvasHeight - ROTATED_EDGE;
  return {
    x: clampToRange(safeX, minX, maxX),
    y: clampToRange(safeY, minY, maxY),
  };
}

export function clampPlacement<T extends ZonePlacement>(placement: T, canvasWidth: number, canvasHeight: number): T {
  const origin = clampOrigin(
    placement.x,
    placement.y,
    placement.width,
    placement.height,
    placement.rotation,
    canvasWidth,
    canvasHeight,
  );
  if (origin.x === placement.x && origin.y === placement.y) {
    return placement;
  }
  return { ...placement, x: origin.x, y: origin.y };
}

export function matchingCanvasPreset(width: number, height: number): CanvasPreset | undefined {
  return CANVAS_PRESETS.find((preset) => preset.width === width && preset.height === height);
}

/** Changes the canvas size and pulls any zone that would sit outside back into range. */
export function resizeCanvas(canvas: VirtualCanvas, width: number, height: number): VirtualCanvas {
  const nextWidth = Math.max(1, Math.round(width));
  const nextHeight = Math.max(1, Math.round(height));
  return {
    width: nextWidth,
    height: nextHeight,
    placements: canvas.placements.map((placement) => clampPlacement(placement, nextWidth, nextHeight)),
  };
}

export function snapPlacement(
  placement: Pick<ZonePlacement, 'x' | 'y' | 'width' | 'height' | 'rotation'>,
  patch: Pick<PlacementPatch, 'x' | 'y' | 'width' | 'height' | 'rotation'>,
  canvasWidth: number,
  canvasHeight: number,
  step: SnapStep,
): SnappedBox {
  const width = patch.width === undefined ? placement.width : snapSize(patch.width, step);
  const height = patch.height === undefined ? placement.height : snapSize(patch.height, step);
  const rotation = patch.rotation === undefined ? placement.rotation : normalizeRotation(patch.rotation);
  const x = patch.x === undefined ? placement.x : snapValue(patch.x, step);
  const y = patch.y === undefined ? placement.y : snapValue(patch.y, step);
  const origin = clampOrigin(x, y, width, height, rotation, canvasWidth, canvasHeight);
  return { x: origin.x, y: origin.y, width, height, rotation };
}

export function clampZoom(zoom: number): number {
  if (!Number.isFinite(zoom)) {
    return 1;
  }
  return roundTo(clamp(zoom, MIN_ZOOM, MAX_ZOOM), 2);
}

/** Zooms around a pointer given in stage-container pixels. `baseScale` is fit-to-width, before zoom. */
export function zoomViewAt(
  view: CanvasView,
  baseScale: number,
  pointerX: number,
  pointerY: number,
  nextZoom: number,
): CanvasView {
  const zoom = clampZoom(nextZoom);
  const safeBase = baseScale > 0 ? baseScale : 1;
  const oldScale = safeBase * (view.zoom > 0 ? view.zoom : 1);
  const newScale = safeBase * zoom;
  const canvasX = (pointerX - view.panX) / oldScale;
  const canvasY = (pointerY - view.panY) / oldScale;
  return {
    zoom,
    panX: roundTo(pointerX - canvasX * newScale, 2),
    panY: roundTo(pointerY - canvasY * newScale, 2),
  };
}

/** Positive `deltaY` zooms out. `deltaMode` 1 is line units; otherwise pixels (100 px ≈ one notch). */
export function zoomFromWheel(zoom: number, deltaY: number, deltaMode = 0): number {
  if (!Number.isFinite(deltaY) || deltaY === 0) {
    return clampZoom(zoom);
  }
  const notches = deltaMode === 1 ? clamp(deltaY, -4, 4) : clamp(deltaY / 100, -4, 4);
  return clampZoom(zoom * Math.exp(-notches * Math.log(1.12)));
}

export function patchPlacement(canvas: VirtualCanvas, key: string, patch: PlacementPatch): VirtualCanvas {
  return {
    ...canvas,
    placements: canvas.placements.map((placement) => {
      if (placementKey(placement) !== key) {
        return placement;
      }
      return clampPlacement({ ...placement, ...patch }, canvas.width, canvas.height);
    }),
  };
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

  const origin = clampOrigin(
    roundTo(selected.x + selected.width + 8, 2),
    selected.y,
    selected.width,
    selected.height,
    0,
    canvas.width,
    canvas.height,
  );
  const created: ZonePlacement = {
    deviceId: selected.deviceId,
    zoneId: selected.zoneId,
    segmentId,
    name: `Segment ${segmentNumber}`,
    x: origin.x,
    y: origin.y,
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
  if (next && selected.ledCount !== null) {
    const keptIndex = placements.findIndex((placement) => placement === next);
    const kept = keptIndex >= 0 ? placements[keptIndex] : undefined;
    if (kept) {
      placements[keptIndex] = { ...kept, ledCount: (kept.ledCount ?? 0) + selected.ledCount };
    }
  }
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

/** Structural equality. Order matters only inside each (deviceId, zoneId) group. */
export function canvasEquals(a: VirtualCanvas, b: VirtualCanvas): boolean {
  if (a.width !== b.width || a.height !== b.height || a.placements.length !== b.placements.length) {
    return false;
  }
  const byKey = new Map(b.placements.map((placement) => [placementKey(placement), placement]));
  return (
    a.placements.every((placement) => {
      const other = byKey.get(placementKey(placement));
      return other !== undefined && samePlacement(placement, other);
    }) && sameZoneSegmentOrder(a.placements, b.placements)
  );
}

/** ARGB chain order: each zone must list the same placement keys in the same sequence. */
function sameZoneSegmentOrder(left: readonly ZonePlacement[], right: readonly ZonePlacement[]): boolean {
  const leftOrder = placementKeysByZone(left);
  const rightOrder = placementKeysByZone(right);
  if (leftOrder.size !== rightOrder.size) {
    return false;
  }
  for (const [zone, sequence] of leftOrder) {
    const other = rightOrder.get(zone);
    if (other === undefined || !sameKeySequence(sequence, other)) {
      return false;
    }
  }
  return true;
}

function placementKeysByZone(placements: readonly ZonePlacement[]): Map<string, string[]> {
  const order = new Map<string, string[]>();
  for (const placement of placements) {
    const zone = zoneKey(placement.deviceId, placement.zoneId);
    const sequence = order.get(zone) ?? [];
    sequence.push(placementKey(placement));
    order.set(zone, sequence);
  }
  return order;
}

function sameKeySequence(left: readonly string[], right: readonly string[]): boolean {
  return left.length === right.length && left.every((key, index) => key === right[index]);
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
