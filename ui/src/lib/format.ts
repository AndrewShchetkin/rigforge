import type { SensorReadingDto } from '../api/contracts.ts';

export const EMPTY_VALUE = '—';

type MaybeNumber = number | null | undefined;

function isFiniteNumber(value: MaybeNumber): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

export function formatCelsius(value: MaybeNumber): string {
  return isFiniteNumber(value) ? `${value.toFixed(1)} °C` : EMPTY_VALUE;
}

export function formatRpm(value: MaybeNumber): string {
  return isFiniteNumber(value) ? `${Math.round(value)} RPM` : EMPTY_VALUE;
}

export function formatPercent(value: MaybeNumber): string {
  return isFiniteNumber(value) ? `${Math.round(value)} %` : EMPTY_VALUE;
}

export function formatWatts(value: MaybeNumber): string {
  return isFiniteNumber(value) ? `${value.toFixed(1)} W` : EMPTY_VALUE;
}

export function formatMegabytes(bytes: MaybeNumber): string {
  return isFiniteNumber(bytes) ? `${Math.round(bytes / (1024 * 1024))} MB` : EMPTY_VALUE;
}

export function formatReading({ kind, value, unit }: SensorReadingDto): string {
  if (!isFiniteNumber(value)) {
    return EMPTY_VALUE;
  }
  switch (kind) {
    case 'Temperature':
      return formatCelsius(value);
    case 'Load':
    case 'Duty':
      return formatPercent(value);
    case 'FanSpeed':
      return formatRpm(value);
    case 'Power':
      return formatWatts(value);
    case 'Voltage':
      return `${value.toFixed(3)} V`;
    default:
      return `${Math.round(value)} ${unit}`.trim();
  }
}
