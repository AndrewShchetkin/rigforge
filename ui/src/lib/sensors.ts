import type { SensorReadingDto, SensorRole, SensorSelector } from '../api/contracts.ts';

const ROLE_PREFIX = 'role:';

export interface SensorOption {
  value: SensorSelector;
  label: string;
}

export function roleSelector(role: SensorRole): SensorSelector {
  return `${ROLE_PREFIX}${role}`;
}

const ROLE_SENSOR_OPTIONS: readonly SensorOption[] = [
  { value: roleSelector('CpuTemperature'), label: 'CPU (auto)' },
  { value: roleSelector('GpuTemperature'), label: 'GPU (auto)' },
  { value: roleSelector('LiquidTemperature'), label: 'Liquid (auto)' },
];

/** Role shortcuts followed by every temperature sensor the daemon currently reports. */
export function temperatureSensorOptions(readings: readonly SensorReadingDto[]): SensorOption[] {
  return [
    ...ROLE_SENSOR_OPTIONS,
    ...readings
      .filter((reading) => reading.kind === 'Temperature')
      .map((reading) => ({ value: reading.id, label: `${reading.name} (${reading.id})` })),
  ];
}

/** Keeps configured selectors visible (and removable) even when the sensor is not reported right now. */
export function withSelectedOptions(
  options: readonly SensorOption[],
  selected: readonly SensorSelector[],
): readonly SensorOption[] {
  const known = new Set(options.map((option) => option.value));
  const missing = selected
    .filter((selector) => !known.has(selector))
    .map((selector) => ({ value: selector, label: `${selector} (not reported)` }));
  return missing.length > 0 ? [...options, ...missing] : options;
}

export function sameSensorOptions(a: readonly SensorOption[], b: readonly SensorOption[]): boolean {
  return a.length === b.length && a.every((option, index) => option.value === b[index]?.value && option.label === b[index]?.label);
}

/** Same rule as the daemon: when several sensors claim a role, the lowest id (ordinal) wins. */
export function findByRole(readings: readonly SensorReadingDto[], role: SensorRole): SensorReadingDto | undefined {
  let match: SensorReadingDto | undefined;
  for (const reading of readings) {
    if (reading.role === role && (match === undefined || reading.id < match.id)) {
      match = reading;
    }
  }
  return match;
}

/** Sensor ids are conventionally `{deviceId}/{sensorKey}`. */
export function sensorDeviceId(sensorId: string): string {
  const slash = sensorId.indexOf('/');
  return slash < 0 ? sensorId : sensorId.slice(0, slash);
}
