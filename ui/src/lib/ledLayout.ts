import type { LedPositionDto, LedTopology, RgbZoneDto } from '../api/contracts.ts';

const RING_RADIUS = 0.45;

/** Mirrors `LedLayouts.For` in the daemon (Core/Models/LedLayouts.cs). */
export function ledLayout(
  topology: LedTopology,
  count: number,
  matrixColumns = 8,
  serpentine = false,
): LedPositionDto[] {
  if (count <= 0) {
    return [];
  }
  if (topology === 'Matrix2D') {
    const columns = Math.min(Math.max(matrixColumns, 1), count);
    const rows = Math.ceil(count / columns);
    return Array.from({ length: count }, (_, index) => {
      const row = Math.floor(index / columns);
      let column = index % columns;
      if (serpentine && (row & 1) === 1) {
        column = columns - 1 - column;
      }
      return { x: (column + 0.5) / columns, y: (row + 0.5) / rows };
    });
  }
  if (topology === 'Ring') {
    return Array.from({ length: count }, (_, index) => {
      const radians = ((-90 + (360 * index) / count) * Math.PI) / 180;
      return { x: 0.5 + RING_RADIUS * Math.cos(radians), y: 0.5 + RING_RADIUS * Math.sin(radians) };
    });
  }
  if (count === 1) {
    return [{ x: 0.5, y: 0.5 }];
  }
  return Array.from({ length: count }, (_, index) => ({ x: index / (count - 1), y: 0.5 }));
}

export interface ZoneLedOptions {
  ledCount: number | null;
  topology?: LedTopology | null;
  matrixColumns?: number | null;
  serpentine?: boolean;
}

/**
 * Positions for one placement. Hardware LEDs win when the daemon's UsesHardwareLayout is true
 * (no topology override, and the count is unset or already the zone length). Serpentine is
 * ignored unless the effective topology — placement, then zone — is Matrix2D.
 */
export function zoneLeds(zone: RgbZoneDto, options: ZoneLedOptions): readonly LedPositionDto[] {
  const topology = options.topology ?? zone.topology;
  const count = options.ledCount ?? zone.leds.length;
  const usesHardwareLayout =
    (options.topology === undefined || options.topology === null) &&
    (options.ledCount === null || options.ledCount === zone.leds.length);
  if (usesHardwareLayout) {
    return zone.leds;
  }
  const serpentine = topology === 'Matrix2D' && (options.serpentine ?? false);
  return ledLayout(topology, count, options.matrixColumns ?? 8, serpentine);
}
