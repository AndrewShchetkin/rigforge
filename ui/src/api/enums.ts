import type {
  DeviceCapability,
  DeviceSupportStatus,
  EffectCategory,
  EffectParameterKind,
  EffectRenderMode,
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

export const EFFECT_RENDER_MODES = ['Sleeping', 'Blackout', 'DirectSampling', 'CanvasRaster'] as const satisfies readonly EffectRenderMode[];

export const DEVICE_SUPPORT_STATUSES = ['Supported', 'TelemetryOnly', 'Unmanaged'] as const satisfies readonly DeviceSupportStatus[];

export const EFFECT_CATEGORIES = ['Flowing', 'Spatial', 'Reactive', 'Static', 'Custom'] as const satisfies readonly EffectCategory[];

export const EFFECT_PARAMETER_KINDS = [
  'ColorList',
  'FloatRange',
  'SensorSelector',
  'Boolean',
  'IntRange',
  'Select',
] as const satisfies readonly EffectParameterKind[];
