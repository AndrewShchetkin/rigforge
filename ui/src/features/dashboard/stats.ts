import type { SensorReadingDto, SensorRole } from '../../api/contracts.ts';
import { EMPTY_VALUE, formatCelsius, formatPercent, formatRpm, formatWatts } from '../../lib/format.ts';
import { findByRole } from '../../lib/sensors.ts';
import { blockBar, meterFraction, meterTone, type MeterTone } from './meters.ts';

export interface Stat {
  role: SensorRole;
  label: string;
  value: string;
  /** Name of the sensor that provides the value, if any. */
  source: string | null;
  tone: MeterTone;
  filled: string;
  empty: string;
}

const STATS: readonly { role: SensorRole; label: string; format: (value: number) => string }[] = [
  { role: 'CpuTemperature', label: 'CPU temperature', format: formatCelsius },
  { role: 'GpuTemperature', label: 'GPU temperature', format: formatCelsius },
  { role: 'LiquidTemperature', label: 'Liquid temperature', format: formatCelsius },
  { role: 'CpuLoad', label: 'CPU load', format: formatPercent },
  { role: 'GpuLoad', label: 'GPU load', format: formatPercent },
  { role: 'GpuPower', label: 'GPU power', format: formatWatts },
  { role: 'PumpSpeed', label: 'Pump speed', format: formatRpm },
];

export function buildStats(readings: readonly SensorReadingDto[]): Stat[] {
  return STATS.map(({ role, label, format }) => {
    const reading = findByRole(readings, role);
    const numeric = reading && Number.isFinite(reading.value) ? reading.value : null;
    const bar = blockBar(meterFraction(role, numeric));
    return {
      role,
      label,
      value: reading ? format(reading.value) : EMPTY_VALUE,
      source: reading?.name ?? null,
      tone: meterTone(role, numeric),
      filled: bar.filled,
      empty: bar.empty,
    };
  });
}
