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

/** One decimal under 10 MB, otherwise a whole number. Useful for heap and small private sets. */
export function formatMegabytesPrecise(bytes: MaybeNumber, digits = 1): string {
  return isFiniteNumber(bytes) ? `${(bytes / (1024 * 1024)).toFixed(digits)} MB` : EMPTY_VALUE;
}

export function formatMegabytesAdaptive(bytes: MaybeNumber): string {
  if (!isFiniteNumber(bytes)) {
    return EMPTY_VALUE;
  }
  const megabytes = bytes / (1024 * 1024);
  return megabytes >= 10 ? `${Math.round(megabytes)} MB` : `${megabytes.toFixed(1)} MB`;
}

export function formatCpuPercent(value: MaybeNumber): string {
  return isFiniteNumber(value) ? `${value.toFixed(1)}%` : EMPTY_VALUE;
}

/** Frame compute time. Sub-10 µs keeps one decimal; 10 µs and up round to a whole microsecond. */
export function formatFrameTime(microseconds: MaybeNumber): string {
  if (!isFiniteNumber(microseconds)) {
    return EMPTY_VALUE;
  }
  const clamped = Math.max(0, microseconds);
  if (clamped >= 1_000_000) {
    return `${(clamped / 1_000_000).toFixed(2)} s`;
  }
  if (clamped >= 1000) {
    const milliseconds = clamped / 1000;
    return `${milliseconds >= 10 ? milliseconds.toFixed(1) : milliseconds.toFixed(2)} ms`;
  }
  if (clamped >= 10) {
    return `${Math.round(clamped)} µs`;
  }
  return `${clamped.toFixed(1)} µs`;
}

/**
 * Typical resident size of a Chromium-based RGB suite, used only as a labelled comparison band.
 * `typical` sits inside that band (675 MB); 45 MB of private RAM is 15× under it.
 */
export const WEBVIEW_SUITE_RAM_MB = { low: 650, high: 900, typical: 675 } as const;

export function formatRamMultiplier(privateBytes: number): string {
  const megabytes = privateBytes / (1024 * 1024);
  if (!(megabytes > 0)) {
    return '0 Chromium processes';
  }
  const rounded = Math.round(WEBVIEW_SUITE_RAM_MB.typical / megabytes);
  if (rounded <= 1) {
    return 'Comparable RAM · 0 Chromium processes';
  }
  return `~${rounded}× less RAM · 0 Chromium processes`;
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
