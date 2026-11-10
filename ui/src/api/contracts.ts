// Wire contract of the RigForge daemon (SignalR hub at /hubs/control, JSON protocol, camelCase, enums as strings).
// Mirrors src/Host.Daemon/Contracts/Dtos.cs and src/Core/Models/*.cs — keep both sides in sync.
// Authoritative value ranges live in src/Application/Validation/RigProfileValidator.cs; ui/src/api/schemas.ts
// applies the same or stricter limits. Fields documented as integers are `int` on the C# side.

// ---------- Enums ----------
export type DeviceCapability = 'Rgb' | 'FanControl' | 'Sensors' | 'Lcd';
export type LedTopology = 'Point' | 'Linear' | 'Ring' | 'Composite' | 'Matrix2D';
export type FanChannelKind = 'Fan' | 'Pump';
export type SensorKind = 'Temperature' | 'Load' | 'FanSpeed' | 'Duty' | 'Power' | 'Clock' | 'Data' | 'Voltage';
export type SensorRole =
  | 'None'
  | 'CpuTemperature'
  | 'GpuTemperature'
  | 'LiquidTemperature'
  | 'CpuLoad'
  | 'GpuLoad'
  | 'GpuPower'
  | 'PumpSpeed';
export type FanControlMode = 'Curve' | 'Fixed';
export type LcdMode = 'Dashboard' | 'SingleSensor' | 'Graph' | 'BuiltInLiquid';
export type PreviewChannel = 'Leds' | 'Lcd';
export type DeviceSupportStatus = 'Supported' | 'TelemetryOnly' | 'Unmanaged';

/** Sensor selector: either an exact sensor id or `role:<SensorRole>` (e.g. `role:CpuTemperature`). */
export type SensorSelector = string;

// ---------- Read models ----------
export interface DaemonInfoDto {
  version: string;
  platform: string;
  startedAt: string; // ISO-8601
  privateBytes: number;
  workingSetBytes: number;
  managedHeapBytes: number;
}

/** What the lighting loop is doing. `Sleeping` and `Blackout` wait without burning CPU. */
export type EffectRenderMode = 'Sleeping' | 'Blackout' | 'DirectSampling' | 'CanvasRaster';

/** Process and lighting-loop sample, pushed about once a second. */
export interface PerformanceMetricsDto {
  timestamp: string;
  cpuPercent: number;
  privateBytes: number;
  workingSetBytes: number;
  managedHeapBytes: number;
  effectFps: number;
  frameTimeMicroseconds: number;
  renderMode: EffectRenderMode;
  activeRgbDevices: number;
  totalControlledLeds: number;
  gen0Collections: number;
  gen1Collections: number;
  gen2Collections: number;
}

export interface LedPositionDto {
  /** Normalized 0..1 inside the zone's bounding box. */
  x: number;
  y: number;
}

export interface RgbZoneDto {
  id: string;
  name: string;
  topology: LedTopology;
  ledCount: number;
  /** ARGB headers: LED count is user-configurable via ZonePlacement.ledCount. */
  isResizable: boolean;
  maxLedCount: number;
  leds: LedPositionDto[];
}

export interface FanChannelDto {
  id: string;
  name: string;
  kind: FanChannelKind;
  minDutyPercent: number;
  maxDutyPercent: number;
  speedSensorId: string | null;
}

export interface LcdInfoDto {
  width: number;
  height: number;
  isCircular: boolean;
  maxFramesPerSecond: number;
}

export interface DeviceDto {
  id: string;
  name: string;
  vendor: string;
  capabilities: DeviceCapability[];
  properties: Record<string, string>;
  zones: RgbZoneDto[];
  fanChannels: FanChannelDto[];
  lcd: LcdInfoDto | null;
  supportStatus: DeviceSupportStatus;
  /** Why the device is telemetry-only or unmanaged, when the daemon has a reason. */
  statusReason: string | null;
}

export interface SensorReadingDto {
  id: string;
  name: string;
  kind: SensorKind;
  role: SensorRole;
  value: number;
  unit: string;
  timestamp: string;
}

export interface TelemetryDto {
  timestamp: string;
  readings: SensorReadingDto[];
}

export interface FanStatusDto {
  curveId: string;
  deviceId: string;
  channelId: string;
  temperature: number | null;
  targetDuty: number;
  appliedDuty: number;
  failsafe: boolean;
  message: string | null;
}

export type EffectCategory = 'Flowing' | 'Spatial' | 'Reactive' | 'Static' | 'Custom';
export type EffectParameterKind = 'ColorList' | 'FloatRange' | 'SensorSelector' | 'Boolean' | 'IntRange' | 'Select';

/** Raw value of one effect parameter, as stored in `EffectSettings.parameters`. */
export type EffectParameterValue = string[] | number | string | boolean;

export interface EffectSelectOptionDto {
  value: string;
  label: string;
}

export interface EffectParameterDefinitionDto {
  key: string;
  label: string;
  kind: EffectParameterKind;
  defaultValue: EffectParameterValue;
  min: number | null;
  max: number | null;
  step: number | null;
  unit: string | null;
  minColors: number;
  maxColors: number;
  /** Choices for a `Select` parameter. Absent for every other kind. */
  options?: EffectSelectOptionDto[] | null;
}

export interface EffectDescriptorDto {
  id: string;
  name: string;
  description: string;
  isAnimated: boolean;
  parameters: EffectParameterDefinitionDto[];
  category: EffectCategory;
  usesAudio: boolean;
}

export interface ZonePreviewDto {
  deviceId: string;
  zoneId: string;
  /** base64-encoded RGB triplets, one per LED in zone order. */
  colors: string;
}

export interface LedPreviewDto {
  zones: ZonePreviewDto[];
}

// ---------- Profile (settings documents, sent back as-is) ----------
export interface ZonePlacement {
  deviceId: string;
  zoneId: string;
  /** Top-left corner of the unrotated box in canvas units; rotation (degrees, clockwise) is around that corner (Konva convention). */
  x: number;
  y: number;
  width: number;
  height: number;
  rotation: number;
  enabled: boolean;
  /**
   * Integer, only for resizable zones (ARGB headers), 1..zone.maxLedCount. After UpdateCanvas the daemon resizes the
   * zone to the sum of its segments, regenerates the physical LED buffer and broadcasts StateChanged.
   */
  ledCount: number | null;
  /** Set when this box is one part of a physical ARGB chain. Several parts share `deviceId` and `zoneId`. */
  segmentId?: string | null;
  /** User-facing segment name, for example "Fan 1". */
  name?: string | null;
  /** Layout override. Null keeps the hardware topology. */
  topology?: LedTopology | null;
  /** Column count for a Matrix2D override. Defaults to 8. */
  matrixColumns?: number | null;
  /** Reverses odd matrix rows. */
  serpentine?: boolean;
}

export interface VirtualCanvas {
  width: number;
  height: number;
  placements: ZonePlacement[];
}

export interface EffectSettings {
  effectId: string;
  /**
   * Master lighting power. False blacks out every LED and keeps `brightness` for when power returns.
   * Missing values from older profiles mean on.
   */
  enabled: boolean;
  /** 0..1 */
  brightness: number;
  /** Effect-specific values by parameter key, described by `EffectDescriptorDto.parameters`. */
  parameters: Record<string, EffectParameterValue>;
  /** 1..60 */
  targetFps: number;
}

export interface FanCurvePoint {
  temperature: number;
  duty: number;
}

export interface FanCurve {
  /** Convention: `${deviceId}:${channelId}` */
  id: string;
  deviceId: string;
  channelId: string;
  enabled: boolean;
  mode: FanControlMode;
  fixedDuty: number;
  sensorSelectors: SensorSelector[];
  points: FanCurvePoint[];
  hysteresisCelsius: number;
  rampUpPercentPerSecond: number;
  rampDownPercentPerSecond: number;
  minDuty: number;
  maxDuty: number;
  temperatureSmoothingSeconds: number;
}

export interface LcdSettings {
  mode: LcdMode;
  brightness: number;
  rotation: 0 | 90 | 180 | 270;
  sensorSelectors: SensorSelector[];
  accentColor: string;
  framesPerSecond: number;
}

export interface DeviceColorCorrection {
  deviceId: string;
  /** 0.5..3. 1 is linear. */
  gamma: number;
  /** Per-channel gain, #RRGGBB. #FFFFFF is neutral. */
  whiteBalance: string;
}

export interface RigProfile {
  schemaVersion: number;
  canvas: VirtualCanvas;
  effect: EffectSettings;
  fanCurves: FanCurve[];
  lcd: LcdSettings;
  /**
   * Per-device LCD settings keyed by device id. `lcd` remains the fallback for devices without an entry.
   * Older daemons omit this.
   */
  displays?: Record<string, LcdSettings>;
  /** Older daemons omit this. Treat a missing array as no corrections. */
  colorCorrections?: DeviceColorCorrection[];
}

export interface SystemStateDto {
  info: DaemonInfoDto;
  devices: DeviceDto[];
  profile: RigProfile;
  /** Saved placements plus auto-placed zones that the user has not positioned yet. */
  effectiveCanvas: VirtualCanvas;
  effects: EffectDescriptorDto[];
  telemetry: TelemetryDto;
  fanStatus: FanStatusDto[];
  /** Human-readable problems, e.g. conflicting vendor software running. */
  warnings: string[];
  performance: PerformanceMetricsDto;
}

// ---------- Hub API ----------
/** Client -> server methods (connection.invoke). Validation failures reject with the HubException message. */
export interface DaemonHubMethods {
  GetState(): Promise<SystemStateDto>;
  UpdateCanvas(canvas: VirtualCanvas): Promise<SystemStateDto>;
  UpdateEffect(effect: EffectSettings): Promise<SystemStateDto>;
  UpsertFanCurve(curve: FanCurve): Promise<SystemStateDto>;
  RemoveFanCurve(curveId: string): Promise<SystemStateDto>;
  UpdateLcd(lcd: LcdSettings): Promise<SystemStateDto>;
  UpsertColorCorrection(correction: DeviceColorCorrection): Promise<SystemStateDto>;
  RemoveColorCorrection(deviceId: string): Promise<SystemStateDto>;
  RescanDevices(): Promise<SystemStateDto>;
  /** Opt-in to high-rate preview streams; only enable while the corresponding page is visible. */
  SetPreview(channel: PreviewChannel, enabled: boolean): Promise<void>;
  /** Flash one device, or one of its zones, so the user can see which hardware it is. */
  IdentifyDevice(deviceId: string, zoneId?: string | null): Promise<void>;
  ExportProfile(): Promise<RigProfile>;
  ImportProfile(profile: RigProfile): Promise<SystemStateDto>;
  UpsertDisplay(deviceId: string, lcd: LcdSettings): Promise<SystemStateDto>;
  RemoveDisplay(deviceId: string): Promise<SystemStateDto>;
}

/** Server -> client events (connection.on). */
export interface DaemonHubEvents {
  /** ~1 Hz */
  Telemetry: (telemetry: TelemetryDto) => void;
  /** ~1 Hz. Kept off the reducer so heavy pages do not re-render on every sample. */
  Performance: (metrics: PerformanceMetricsDto) => void;
  /** ~1 Hz */
  FanStatus: (status: FanStatusDto[]) => void;
  /** ~20 Hz, only after SetPreview('Leds', true) */
  LedPreview: (frame: LedPreviewDto) => void;
  /** ~10 Hz JPEG of the whole effect field, only after SetPreview('Leds', true) */
  CanvasPreview: (jpegBase64: string) => void;
  /** LCD frame rate, only after SetPreview('Lcd', true); base64 PNG */
  LcdPreview: (pngBase64: string) => void;
  /** Devices or profile changed (possibly by another client). */
  StateChanged: (state: SystemStateDto) => void;
}

export const HUB_PATH = '/hubs/control';
