import type { LedPositionDto, LedTopology, RgbZoneDto } from '../api/contracts.ts';

const RING_RADIUS = 0.45;

/** Mirrors `LedLayouts.For` in the daemon (Core/Models/LedLayouts.cs). */
export function ledLayout(topology: LedTopology, count: number): LedPositionDto[] {
  if (count <= 0) {
    return [];
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

/** The daemon's LED layout, or a local approximation while an LED-count edit is not saved yet. */
export function zoneLeds(zone: RgbZoneDto, ledCount: number | null): readonly LedPositionDto[] {
  if (ledCount === null || ledCount === zone.leds.length) {
    return zone.leds;
  }
  return ledLayout(zone.topology, ledCount);
}
