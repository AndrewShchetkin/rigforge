import type {
  DeviceCapability,
  FanChannelKind,
  FanControlMode,
  LcdMode,
  LcdSettings,
  LedTopology,
  SensorKind,
  SensorRole,
} from './contracts.ts';

// Runtime lists of the string unions in contracts.ts (for zod schemas and form options).

export const DEVICE_CAPABILITIES = ['Rgb', 'FanControl', 'Sensors', 'Lcd'] as const satisfies readonly DeviceCapability[];

export const LED_TOPOLOGIES = ['Point', 'Linear', 'Ring', 'Composite', 'Matrix2D'] as const satisfies readonly LedTopology[];

export const FAN_CHANNEL_KINDS = ['Fan', 'Pump'] as const satisfies readonly FanChannelKind[];

export const SENSOR_KINDS = [
  'Temperature',
  'Load',
  'FanSpeed',
  'Duty',
  'Power',
  'Clock',
  'Data',
  'Voltage',
] as const satisfies readonly SensorKind[];

export const SENSOR_ROLES = [
  'None',
  'CpuTemperature',
  'GpuTemperature',
  'LiquidTemperature',
  'CpuLoad',
  'GpuLoad',
  'GpuPower',
  'PumpSpeed',
] as const satisfies readonly SensorRole[];

export const FAN_CONTROL_MODES = ['Curve', 'Fixed'] as const satisfies readonly FanControlMode[];

export const LCD_MODES = ['Dashboard', 'SingleSensor', 'Graph', 'BuiltInLiquid'] as const satisfies readonly LcdMode[];

export const LCD_ROTATIONS = [0, 90, 180, 270] as const satisfies readonly LcdSettings['rotation'][];
