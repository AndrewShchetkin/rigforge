import type { SensorRole } from '../../api/contracts.ts';

export type MeterTone = 'ok' | 'warn' | 'bad' | 'neutral';

const CELLS = 10;
const FILLED = '█';
const EMPTY = '░';

const HEALTH_ROLES = new Set<SensorRole>([
  'CpuTemperature',
  'GpuTemperature',
  'LiquidTemperature',
  'CpuLoad',
  'GpuLoad',
]);

const SCALE: Record<SensorRole, number> = {
  None: 1,
  CpuTemperature: 100,
  GpuTemperature: 100,
  LiquidTemperature: 100,
  CpuLoad: 100,
  GpuLoad: 100,
  GpuPower: 400,
  PumpSpeed: 3000,
};

function finite(value: number | null): value is number {
  return value !== null && Number.isFinite(value);
}

/** Temperature, load, and duty bands. Missing numbers are not a status. */
export function healthTone(value: number | null): MeterTone {
  if (!finite(value)) {
    return 'neutral';
  }
  if (value < 60) {
    return 'ok';
  }
  if (value < 80) {
    return 'warn';
  }
  return 'bad';
}

/** Health color for sensor roles that represent temperature or load. Power and rpm stay neutral. */
export function meterTone(role: SensorRole, value: number | null): MeterTone {
  if (!HEALTH_ROLES.has(role)) {
    return 'neutral';
  }
  return healthTone(value);
}

/** Fraction of a meter's display ceiling, clamped to 0..1. */
export function meterFraction(role: SensorRole, value: number | null): number {
  if (!finite(value)) {
    return 0;
  }
  return Math.min(1, Math.max(0, value / SCALE[role]));
}

/** Ten-cell block bar. `fraction` is clamped to 0..1 before rounding. */
export function blockBar(fraction: number, cells = CELLS): { filled: string; empty: string } {
  const clamped = Math.min(1, Math.max(0, Number.isFinite(fraction) ? fraction : 0));
  const filledCount = Math.round(clamped * cells);
  return { filled: FILLED.repeat(filledCount), empty: EMPTY.repeat(cells - filledCount) };
}
