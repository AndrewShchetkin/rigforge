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
  DEVICE_SUPPORT_STATUSES,
  EFFECT_CATEGORIES,
  EFFECT_PARAMETER_KINDS,
  EFFECT_RENDER_MODES,
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
  segmentId: z.string().max(64).nullable().optional(),
  name: z.string().max(64).nullable().optional(),
  topology: z.enum(LED_TOPOLOGIES).nullable().optional(),
  matrixColumns: z.int().min(1).max(256).nullable().optional(),
  serpentine: z.boolean().optional(),
});

/** Whitespace-only ids are missing, matching the daemon's IsNullOrWhiteSpace check. */
function isMissingSegmentId(segmentId: string | null | undefined): boolean {
  return segmentId === undefined || segmentId === null || segmentId.trim() === '';
}

/**
 * Validates a canvas before UpdateCanvas. `maxLedCounts` is keyed by `deviceId/zoneId`.
 * When that zone is in the map, the sum of its LED counts cannot exceed the header.
 * Splits still cannot exceed the daemon's 1000 cap.
 */
export function virtualCanvasSchema(maxLedCounts?: ReadonlyMap<string, number>) {
  return z
    .object({
      width: z.int().positive(),
      height: z.int().positive(),
      placements: z.array(zonePlacementSchema),
    })
    .superRefine((canvas, ctx) => {
      const seen = new Set<string>();
      const groups = new Map<string, number[]>();
      canvas.placements.forEach((placement, index) => {
        const key = placement.segmentId
          ? `${placement.deviceId}/${placement.zoneId}#${placement.segmentId}`
          : `${placement.deviceId}/${placement.zoneId}`;
        if (seen.has(key)) {
          ctx.addIssue({ code: 'custom', path: ['placements', index], message: `Zone ${key} is placed twice` });
        }
        seen.add(key);
        const zone = `${placement.deviceId}/${placement.zoneId}`;
        const indexes = groups.get(zone) ?? [];
        indexes.push(index);
        groups.set(zone, indexes);
      });

      for (const [zone, indexes] of groups) {
        const split = indexes.length > 1;
        let sum = 0;
        for (const index of indexes) {
          const placement = canvas.placements[index];
          if (!placement) {
            continue;
          }
          if (split && isMissingSegmentId(placement.segmentId)) {
            ctx.addIssue({ code: 'custom', path: ['placements', index, 'segmentId'], message: 'Each part of a split zone needs a segment id' });
          }
          if (split && placement.ledCount === null) {
            ctx.addIssue({ code: 'custom', path: ['placements', index, 'ledCount'], message: 'Each segment needs a LED count' });
          }
          sum += placement.ledCount ?? 0;
        }
        const first = indexes[0];
        if (first === undefined) {
          continue;
        }
        const maxLedCount = maxLedCounts?.get(zone);
        if (maxLedCount !== undefined && sum > maxLedCount) {
          ctx.addIssue({ code: 'custom', path: ['placements', first, 'ledCount'], message: `A zone can have at most ${maxLedCount} LEDs` });
        }
        if (split && sum > 1000) {
          ctx.addIssue({ code: 'custom', path: ['placements', first, 'ledCount'], message: 'A zone can have at most 1000 LEDs' });
        }
      }
    }) satisfies z.ZodType<VirtualCanvas>;
}

const effectParameterValueSchema = z.union([z.array(z.string()), z.number(), z.string(), z.boolean()]);

export const effectSettingsSchema = z.object({
  effectId: z.string().min(1, 'Pick an effect'),
  /** Omitted on older profiles; the daemon treats that as lighting on. */
  enabled: z.boolean().default(true),
  brightness: z.number().min(0).max(1),
  parameters: z.record(z.string(), effectParameterValueSchema),
  targetFps: z.int().min(1).max(60),
}) satisfies z.ZodType<EffectSettings>;

/** Effect settings plus the rules that depend on the selected effect's parameter schema. */
export function effectSettingsSchemaFor(descriptor: EffectDescriptorDto | undefined) {
  return effectSettingsSchema.superRefine((effect, ctx) => {
    if (!descriptor) {
      ctx.addIssue({ code: 'custom', path: ['effectId'], message: 'Unknown effect' });
      return;
    }

    const knownKeys = new Set(descriptor.parameters.map((parameter) => parameter.key));
    for (const key of Object.keys(effect.parameters)) {
      if (!knownKeys.has(key)) {
        ctx.addIssue({ code: 'custom', path: ['parameters', key], message: `Unknown parameter '${key}'` });
      }
    }

    for (const parameter of descriptor.parameters) {
      // Missing keys fall back to the schema default, mirroring the daemon.
      const value = effect.parameters[parameter.key] ?? parameter.defaultValue;
      const path = ['parameters', parameter.key];
      switch (parameter.kind) {
        case 'ColorList':
          if (!Array.isArray(value) || value.length < parameter.minColors || value.length > parameter.maxColors) {
            ctx.addIssue({ code: 'custom', path, message: `Use ${parameter.minColors}–${parameter.maxColors} colors` });
          } else if (value.some((color) => !/^#[0-9a-f]{6}$/i.test(color))) {
            ctx.addIssue({ code: 'custom', path, message: 'Colors must be #RRGGBB' });
          }
          break;
        case 'FloatRange':
          if (typeof value !== 'number' || !Number.isFinite(value)) {
            ctx.addIssue({ code: 'custom', path, message: 'Must be a number' });
          } else if ((parameter.min !== null && value < parameter.min) || (parameter.max !== null && value > parameter.max)) {
            ctx.addIssue({ code: 'custom', path, message: `Must be between ${parameter.min} and ${parameter.max}` });
          }
          break;
        case 'IntRange':
          if (typeof value !== 'number' || !Number.isInteger(value)) {
            ctx.addIssue({ code: 'custom', path, message: 'Must be a whole number' });
          } else if ((parameter.min !== null && value < parameter.min) || (parameter.max !== null && value > parameter.max)) {
            ctx.addIssue({ code: 'custom', path, message: `Must be between ${parameter.min} and ${parameter.max}` });
          }
          break;
        case 'Boolean':
          if (typeof value !== 'boolean') {
            ctx.addIssue({ code: 'custom', path, message: 'Must be on or off' });
          }
          break;
        case 'Select': {
          const options = parameter.options ?? [];
          const allowed = typeof value === 'string' && options.some((option) => option.value === value);
          if (!allowed) {
            ctx.addIssue({ code: 'custom', path, message: 'Pick one of the options' });
          }
          break;
        }
        case 'SensorSelector':
          if (typeof value !== 'string' || value.length === 0) {
            ctx.addIssue({ code: 'custom', path, message: 'Pick a sensor' });
          }
          break;
        default: {
          const unhandled: never = parameter.kind;
          void unhandled;
        }
      }
    }

    const low = effect.parameters.temperatureLow ?? descriptor.parameters.find((p) => p.key === 'temperatureLow')?.defaultValue;
    const high = effect.parameters.temperatureHigh ?? descriptor.parameters.find((p) => p.key === 'temperatureHigh')?.defaultValue;
    if (typeof low === 'number' && typeof high === 'number' && low >= high) {
      ctx.addIssue({ code: 'custom', path: ['parameters', 'temperatureHigh'], message: 'Must be above the low temperature' });
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

const performanceShape = z.looseObject({
  timestamp: str,
  cpuPercent: num,
  privateBytes: num,
  workingSetBytes: num,
  managedHeapBytes: num,
  effectFps: num,
  frameTimeMicroseconds: num,
  renderMode: z.enum(EFFECT_RENDER_MODES),
  activeRgbDevices: num,
  totalControlledLeds: num,
  gen0Collections: num,
  gen1Collections: num,
  gen2Collections: num,
});

export const systemStateSchema = z.looseObject({
  info: z.looseObject({
    version: str,
    platform: str,
    startedAt: str,
    workingSetBytes: num,
    managedHeapBytes: num,
    privateBytes: num.optional(),
    product: str.optional(),
    sessionId: num.optional(),
    autostartSupported: bool.optional(),
    autostartEnabled: bool.optional(),
  }),
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
      supportStatus: z.enum(DEVICE_SUPPORT_STATUSES).optional(),
      statusReason: str.nullable().optional(),
    }),
  ),
  profile: z.looseObject({
    canvas: canvasShape,
    effect: z.looseObject({ effectId: str, parameters: z.record(str, z.unknown()), enabled: bool.optional() }),
    fanCurves: z.array(z.looseObject({ id: str, deviceId: str, channelId: str, points: z.array(z.looseObject({})) })),
    lcd: z.looseObject({ mode: z.enum(LCD_MODES), sensorSelectors: z.array(str) }),
    displays: z.record(str, z.looseObject({ mode: z.enum(LCD_MODES), sensorSelectors: z.array(str) })).optional(),
    colorCorrections: z.array(z.looseObject({ deviceId: str, gamma: num, whiteBalance: str })).optional(),
  }),
  effectiveCanvas: canvasShape,
  effects: z.array(
    z.looseObject({
      id: str,
      name: str,
      category: z.enum(EFFECT_CATEGORIES).optional(),
      usesAudio: bool.optional(),
      parameters: z.array(
        z.looseObject({
          key: str,
          kind: z.enum(EFFECT_PARAMETER_KINDS),
          minColors: num,
          maxColors: num,
          options: z.array(z.looseObject({ value: str, label: str })).nullable().optional(),
        }),
      ),
    }),
  ),
  telemetry: z.looseObject({
    timestamp: str,
    readings: z.array(
      z.looseObject({ id: str, name: str, kind: z.enum(SENSOR_KINDS), role: z.enum(SENSOR_ROLES), value: num, unit: str }),
    ),
  }),
  fanStatus: z.array(z.looseObject({ curveId: str, appliedDuty: num, targetDuty: num, failsafe: bool })),
  warnings: z.array(str),
  performance: performanceShape.optional(),
});

/** Logs a warning when a snapshot does not look like the contract; never blocks rendering. */
export function checkSystemState(value: unknown): void {
  const result = systemStateSchema.safeParse(value);
  if (!result.success) {
    console.warn(`Daemon state does not match the UI contract:\n${z.prettifyError(result.error)}`);
  }
}
