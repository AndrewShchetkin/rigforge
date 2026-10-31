import { z } from 'zod';
import type {
  EffectDescriptorDto,
  EffectSettings,
  FanChannelDto,
  FanCurve,
  LcdSettings,
  VirtualCanvas,
} from './contracts.ts';
import {
  DEVICE_CAPABILITIES,
  FAN_CHANNEL_KINDS,
  FAN_CONTROL_MODES,
  LCD_MODES,
  LCD_ROTATIONS,
  LED_TOPOLOGIES,
  SENSOR_KINDS,
  SENSOR_ROLES,
} from './enums.ts';

const hexColor = z.string().regex(/^#[0-9a-f]{6}$/i, 'Use a #RRGGBB color');
const sensorSelector = z.string().min(1, 'Pick a sensor');

// ---------- Settings documents (validated before invoking the hub) ----------

const zonePlacementSchema = z.object({
  deviceId: z.string().min(1),
  zoneId: z.string().min(1),
  x: z.number(),
  y: z.number(),
  width: z.number().positive('Width must be positive'),
  height: z.number().positive('Height must be positive'),
  rotation: z.number().min(-360).max(360),
  enabled: z.boolean(),
  ledCount: z.int().min(1, 'At least 1 LED').nullable(),
});

export const virtualCanvasSchema = z
  .object({
    width: z.int().positive(),
    height: z.int().positive(),
    placements: z.array(zonePlacementSchema),
  })
  .superRefine((canvas, ctx) => {
    const seen = new Set<string>();
    canvas.placements.forEach((placement, index) => {
      const key = `${placement.deviceId}/${placement.zoneId}`;
      if (seen.has(key)) {
        ctx.addIssue({ code: 'custom', path: ['placements', index], message: `Zone ${key} is placed twice` });
      }
      seen.add(key);
    });
  }) satisfies z.ZodType<VirtualCanvas>;

const effectSettingsSchema = z.object({
  effectId: z.string().min(1, 'Pick an effect'),
  brightness: z.number().min(0).max(1),
  speed: z.number().min(0.05, 'At least 0.05×').max(10, 'At most 10×'),
  colors: z.array(hexColor),
  angle: z.number().min(0).lt(360),
  sensorSelector: sensorSelector.nullable(),
  temperatureLow: z.number().min(0).max(120),
  temperatureHigh: z.number().min(0).max(120),
  targetFps: z.int().min(1).max(60),
}) satisfies z.ZodType<EffectSettings>;

/** Effect settings plus the rules that depend on the selected effect's descriptor. */
export function effectSettingsSchemaFor(descriptor: EffectDescriptorDto | undefined) {
  return effectSettingsSchema.superRefine((effect, ctx) => {
    if (!descriptor) {
      ctx.addIssue({ code: 'custom', path: ['effectId'], message: 'Unknown effect' });
      return;
    }
    const { usesColors, usesSensor, minColors, maxColors } = descriptor;
    if (usesColors && (effect.colors.length < minColors || effect.colors.length > maxColors)) {
      ctx.addIssue({ code: 'custom', path: ['colors'], message: `Use ${minColors}–${maxColors} colors` });
    }
    if (usesSensor && effect.sensorSelector === null) {
      ctx.addIssue({ code: 'custom', path: ['sensorSelector'], message: 'Pick a sensor' });
    }
    if (usesSensor && effect.temperatureLow >= effect.temperatureHigh) {
      ctx.addIssue({ code: 'custom', path: ['temperatureHigh'], message: 'Must be above the low temperature' });
    }
  });
}

const fanCurveSchema = z
  .object({
    id: z.string().min(1),
    deviceId: z.string().min(1),
    channelId: z.string().min(1),
    enabled: z.boolean(),
    mode: z.enum(FAN_CONTROL_MODES),
    fixedDuty: z.int().min(0).max(100),
    sensorSelectors: z.array(sensorSelector).min(1, 'Select at least one temperature source'),
    points: z
      .array(z.object({ temperature: z.number().min(0).max(120), duty: z.number().min(0).max(100) }))
      .min(2, 'A curve needs at least 2 points')
      .max(16, 'A curve can have at most 16 points'),
    hysteresisCelsius: z.number().min(0).max(20),
    rampUpPercentPerSecond: z.number().min(0).max(100),
    rampDownPercentPerSecond: z.number().min(0).max(100),
    minDuty: z.int().min(0).max(100),
    maxDuty: z.int().min(0).max(100),
    temperatureSmoothingSeconds: z.number().min(0).max(30),
  })
  .superRefine((curve, ctx) => {
    curve.points.forEach((point, index) => {
      const previous = curve.points[index - 1];
      if (previous && point.temperature - previous.temperature < 1) {
        ctx.addIssue({
          code: 'custom',
          path: ['points', index, 'temperature'],
          message: 'Temperatures must increase by at least 1 °C',
        });
      }
    });
    if (curve.minDuty > curve.maxDuty) {
      ctx.addIssue({ code: 'custom', path: ['maxDuty'], message: 'Max duty must not be below min duty' });
    }
    if (new Set(curve.sensorSelectors).size !== curve.sensorSelectors.length) {
      ctx.addIssue({ code: 'custom', path: ['sensorSelectors'], message: 'Each sensor can only be selected once' });
    }
    if (curve.id !== `${curve.deviceId}:${curve.channelId}`) {
      ctx.addIssue({ code: 'custom', path: ['id'], message: 'Curve id must be "<deviceId>:<channelId>"' });
    }
  }) satisfies z.ZodType<FanCurve>;

/** Fan curve plus the hardware limits of the channel it drives. */
export function fanCurveSchemaFor(channel: FanChannelDto) {
  return fanCurveSchema.superRefine((curve, ctx) => {
    if (channel.kind !== 'Pump') {
      return;
    }
    const message = `This pump needs at least ${channel.minDutyPercent} %`;
    if (curve.minDuty < channel.minDutyPercent) {
      ctx.addIssue({ code: 'custom', path: ['minDuty'], message });
    }
    if (curve.mode === 'Fixed' && curve.fixedDuty < channel.minDutyPercent) {
      ctx.addIssue({ code: 'custom', path: ['fixedDuty'], message });
    }
  });
}

export const lcdSettingsSchema = z
  .object({
    mode: z.enum(LCD_MODES),
    brightness: z.int().min(0).max(100),
    rotation: z.literal(LCD_ROTATIONS),
    sensorSelectors: z.array(sensorSelector).max(3, 'Pick at most 3 sensors'),
    accentColor: hexColor,
    framesPerSecond: z.number().min(0.2, 'At least 0.2 FPS').max(10, 'At most 10 FPS'),
  })
  .superRefine((lcd, ctx) => {
    if (lcd.mode !== 'BuiltInLiquid' && lcd.sensorSelectors.length === 0) {
      ctx.addIssue({ code: 'custom', path: ['sensorSelectors'], message: 'Pick a primary sensor' });
    }
    if (new Set(lcd.sensorSelectors).size !== lcd.sensorSelectors.length) {
      ctx.addIssue({ code: 'custom', path: ['sensorSelectors'], message: 'Each sensor can only be shown once' });
    }
  }) satisfies z.ZodType<LcdSettings>;

// ---------- Lenient structural check of daemon snapshots ----------

const str = z.string();
const num = z.number();
const bool = z.boolean();

const canvasShape = z.looseObject({
  width: num,
  height: num,
  placements: z.array(
    z.looseObject({ deviceId: str, zoneId: str, x: num, y: num, width: num, height: num, rotation: num, enabled: bool }),
  ),
});

const systemStateSchema = z.looseObject({
  info: z.looseObject({ version: str, platform: str, startedAt: str, workingSetBytes: num, managedHeapBytes: num }),
  devices: z.array(
    z.looseObject({
      id: str,
      name: str,
      vendor: str,
      capabilities: z.array(z.enum(DEVICE_CAPABILITIES)),
      properties: z.record(str, str),
      zones: z.array(
        z.looseObject({
          id: str,
          name: str,
          topology: z.enum(LED_TOPOLOGIES),
          ledCount: num,
          isResizable: bool,
          maxLedCount: num,
          leds: z.array(z.looseObject({ x: num, y: num })),
        }),
      ),
      fanChannels: z.array(
        z.looseObject({
          id: str,
          name: str,
          kind: z.enum(FAN_CHANNEL_KINDS),
          minDutyPercent: num,
          maxDutyPercent: num,
          speedSensorId: str.nullable(),
        }),
      ),
      lcd: z.looseObject({ width: num, height: num, isCircular: bool, maxFramesPerSecond: num }).nullable(),
    }),
  ),
  profile: z.looseObject({
    canvas: canvasShape,
    effect: z.looseObject({ effectId: str, colors: z.array(str) }),
    fanCurves: z.array(z.looseObject({ id: str, deviceId: str, channelId: str, points: z.array(z.looseObject({})) })),
    lcd: z.looseObject({ mode: z.enum(LCD_MODES), sensorSelectors: z.array(str) }),
  }),
  effectiveCanvas: canvasShape,
  effects: z.array(z.looseObject({ id: str, name: str, minColors: num, maxColors: num })),
  telemetry: z.looseObject({
    timestamp: str,
    readings: z.array(
      z.looseObject({ id: str, name: str, kind: z.enum(SENSOR_KINDS), role: z.enum(SENSOR_ROLES), value: num, unit: str }),
    ),
  }),
  fanStatus: z.array(z.looseObject({ curveId: str, appliedDuty: num, targetDuty: num, failsafe: bool })),
  warnings: z.array(str),
});

/** Logs a warning when a snapshot does not look like the contract; never blocks rendering. */
export function checkSystemState(value: unknown): void {
  const result = systemStateSchema.safeParse(value);
  if (!result.success) {
    console.warn(`Daemon state does not match the UI contract:\n${z.prettifyError(result.error)}`);
  }
}
