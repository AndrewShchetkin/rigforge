import { describe, expect, it } from 'vitest';
import type { EffectDescriptorDto, EffectParameterDefinitionDto, EffectSettings } from '../contracts.ts';
import { effectSettingsSchema, effectSettingsSchemaFor, systemStateSchema } from '../schemas.ts';

function parameter(
  overrides: Pick<EffectParameterDefinitionDto, 'key' | 'kind' | 'defaultValue'> & Partial<EffectParameterDefinitionDto>,
): EffectParameterDefinitionDto {
  return {
    label: overrides.key,
    min: null,
    max: null,
    step: null,
    unit: null,
    minColors: 0,
    maxColors: 0,
    ...overrides,
  };
}

function descriptor(parameters: EffectParameterDefinitionDto[]): EffectDescriptorDto {
  return {
    id: 'demo',
    name: 'Demo',
    description: 'Demo effect',
    isAnimated: true,
    category: 'Custom',
    usesAudio: false,
    parameters,
  };
}

function settings(parameters: EffectSettings['parameters'], enabled = true): EffectSettings {
  return { effectId: 'demo', enabled, brightness: 1, targetFps: 30, parameters };
}

const allParameters = [
  parameter({ key: 'colors', kind: 'ColorList', defaultValue: ['#FF0080'], minColors: 1, maxColors: 4 }),
  parameter({ key: 'speed', kind: 'FloatRange', defaultValue: 1, min: 0, max: 1, step: 0.1 }),
  parameter({ key: 'count', kind: 'IntRange', defaultValue: 2, min: 0, max: 10, step: 1 }),
  parameter({ key: 'mirror', kind: 'Boolean', defaultValue: false }),
  parameter({
    key: 'mode',
    kind: 'Select',
    defaultValue: 'a',
    options: [
      { value: 'a', label: 'A' },
      { value: 'b', label: 'B' },
    ],
  }),
  parameter({ key: 'sensor', kind: 'SensorSelector', defaultValue: 'role:CpuTemperature' }),
];

const validParameters = {
  colors: ['#112233', '#AABBCC'],
  speed: 0.5,
  count: 4,
  mirror: false,
  mode: 'a',
  sensor: 'role:CpuTemperature',
};

describe('effectSettingsSchemaFor', () => {
  const schema = effectSettingsSchemaFor(descriptor(allParameters));

  it('defaults enabled to on and keeps an explicit blackout', () => {
    const omitted = schema.safeParse({ effectId: 'demo', brightness: 0.4, targetFps: 30, parameters: validParameters });
    expect(omitted.success).toBe(true);
    if (omitted.success) {
      expect(omitted.data.enabled).toBe(true);
    }

    const blackout = schema.safeParse(settings(validParameters, false));
    expect(blackout.success).toBe(true);
    if (blackout.success) {
      expect(blackout.data.enabled).toBe(false);
    }

    expect(schema.safeParse({ ...settings(validParameters), enabled: 'off' }).success).toBe(false);
  });

  it('accepts each parameter kind and rejects the wrong shape', () => {
    expect(schema.safeParse(settings(validParameters)).success).toBe(true);

    expect(schema.safeParse(settings({ ...validParameters, colors: ['red'] })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, colors: [] })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, speed: 2 })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, speed: true })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, count: 1.5 })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, count: 11 })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, count: false })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, mirror: 'false' })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, mode: 'z' })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, mode: 1 })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, sensor: '' })).success).toBe(false);
    expect(schema.safeParse(settings({ ...validParameters, sensor: 4 })).success).toBe(false);
  });

  it('rejects unknown parameters and an unknown effect', () => {
    expect(schema.safeParse(settings({ ...validParameters, extra: 1 })).success).toBe(false);
    expect(effectSettingsSchemaFor(undefined).safeParse(settings({})).success).toBe(false);
  });

  it('still requires the hot threshold to sit above the cold one', () => {
    const thermal = effectSettingsSchemaFor(
      descriptor([
        parameter({ key: 'temperatureLow', kind: 'FloatRange', defaultValue: 35, min: 0, max: 120 }),
        parameter({ key: 'temperatureHigh', kind: 'FloatRange', defaultValue: 80, min: 0, max: 120 }),
      ]),
    );
    expect(thermal.safeParse(settings({ temperatureLow: 40, temperatureHigh: 70 })).success).toBe(true);
    expect(thermal.safeParse(settings({ temperatureLow: 70, temperatureHigh: 40 })).success).toBe(false);
  });
});

describe('effectSettingsSchema', () => {
  it('fills enabled when the outgoing document omits it', () => {
    const parsed = effectSettingsSchema.parse({
      effectId: 'solid',
      brightness: 1,
      targetFps: 60,
      parameters: {},
    });
    expect(parsed.enabled).toBe(true);
  });
});

const snapshot = {
  info: {
    version: '0.4.0',
    platform: 'win32',
    startedAt: '2026-01-01T00:00:00.000Z',
    privateBytes: 45_000_000,
    workingSetBytes: 60_000_000,
    managedHeapBytes: 7_100_000,
  },
  devices: [
    {
      id: 'header',
      name: 'ARGB header',
      vendor: 'RigForge',
      capabilities: ['Rgb'],
      properties: { bus: 'usb' },
      zones: [
        {
          id: 'strip',
          name: 'Strip',
          topology: 'Linear',
          ledCount: 12,
          isResizable: true,
          maxLedCount: 120,
          leds: [{ x: 0, y: 0 }],
        },
      ],
      fanChannels: [],
      lcd: null,
      supportStatus: 'Supported',
      statusReason: null,
    },
  ],
  profile: {
    schemaVersion: 2,
    canvas: { width: 1920, height: 1080, placements: [] },
    effect: { effectId: 'rainbow-wave', enabled: false, brightness: 0.8, parameters: { speed: 1 }, targetFps: 60 },
    fanCurves: [],
    lcd: { mode: 'Dashboard', sensorSelectors: ['role:CpuTemperature'] },
    displays: { panel: { mode: 'Graph', sensorSelectors: ['role:GpuTemperature'] } },
    colorCorrections: [],
  },
  effectiveCanvas: { width: 1920, height: 1080, placements: [] },
  effects: [
    {
      id: 'rainbow-wave',
      name: 'Rainbow wave',
      description: 'Spectrum',
      isAnimated: true,
      category: 'Flowing',
      usesAudio: false,
      parameters: [{ key: 'speed', kind: 'FloatRange', minColors: 0, maxColors: 0, options: null }],
    },
  ],
  telemetry: {
    timestamp: '2026-01-01T00:00:00.000Z',
    readings: [
      {
        id: 'cpu',
        name: 'CPU',
        kind: 'Temperature',
        role: 'CpuTemperature',
        value: 42,
        unit: '°C',
        timestamp: '2026-01-01T00:00:00.000Z',
      },
    ],
  },
  fanStatus: [{ curveId: 'header:cpu', appliedDuty: 30, targetDuty: 35, failsafe: false }],
  warnings: [],
  performance: {
    timestamp: '2026-01-01T00:00:01.000Z',
    cpuPercent: 0.2,
    privateBytes: 45_000_000,
    workingSetBytes: 60_000_000,
    managedHeapBytes: 7_100_000,
    effectFps: 0,
    frameTimeMicroseconds: 14,
    renderMode: 'DirectSampling',
    activeRgbDevices: 1,
    totalControlledLeds: 12,
    gen0Collections: 3,
    gen1Collections: 1,
    gen2Collections: 0,
  },
};

describe('systemStateSchema', () => {
  it('accepts an upgraded daemon snapshot', () => {
    const parsed = systemStateSchema.safeParse(snapshot);
    expect(parsed.success).toBe(true);
    if (!parsed.success) {
      return;
    }

    expect({
      privateBytes: parsed.data.info.privateBytes,
      supportStatus: parsed.data.devices[0]?.supportStatus,
      statusReason: parsed.data.devices[0]?.statusReason,
      enabled: parsed.data.profile.effect.enabled,
      category: parsed.data.effects[0]?.category,
      usesAudio: parsed.data.effects[0]?.usesAudio,
      renderMode: parsed.data.performance?.renderMode,
      cpuPercent: parsed.data.performance?.cpuPercent,
      activeRgbDevices: parsed.data.performance?.activeRgbDevices,
      displays: parsed.data.profile.displays ? Object.keys(parsed.data.profile.displays) : [],
    }).toEqual({
      privateBytes: 45_000_000,
      supportStatus: 'Supported',
      statusReason: null,
      enabled: false,
      category: 'Flowing',
      usesAudio: false,
      renderMode: 'DirectSampling',
      cpuPercent: 0.2,
      activeRgbDevices: 1,
      displays: ['panel'],
    });
  });

  it('still accepts a snapshot that omits the new fields', () => {
    const legacy = structuredClone(snapshot) as {
      performance?: unknown;
      info: { privateBytes?: number };
      devices: Array<{ supportStatus?: string; statusReason?: string | null }>;
      profile: { displays?: unknown; effect: { enabled?: boolean } };
      effects: Array<{ category?: string; usesAudio?: boolean; parameters: Array<{ options?: unknown }> }>;
    };
    delete legacy.performance;
    delete legacy.info.privateBytes;
    const device = legacy.devices[0];
    const described = legacy.effects[0];
    if (!device || !described) {
      throw new Error('Fixture lost its device or effect');
    }
    delete device.supportStatus;
    delete device.statusReason;
    delete legacy.profile.displays;
    delete legacy.profile.effect.enabled;
    delete described.category;
    delete described.usesAudio;
    const firstParameter = described.parameters[0];
    if (firstParameter) {
      delete firstParameter.options;
    }
    expect(systemStateSchema.safeParse(legacy).success).toBe(true);
  });

  it('accepts saved color palettes and a snapshot that omits them', () => {
    const withPalettes = structuredClone(snapshot) as typeof snapshot & {
      profile: (typeof snapshot)['profile'] & { colorPalettes?: { id: string; name: string; colors: string[] }[] };
    };
    withPalettes.profile.colorPalettes = [{ id: 'sunset', name: 'Закат', colors: ['#FF8800'] }];
    expect(systemStateSchema.safeParse(withPalettes).success).toBe(true);

    const omitted = structuredClone(snapshot) as { profile: { colorPalettes?: unknown } };
    delete omitted.profile.colorPalettes;
    expect(systemStateSchema.safeParse(omitted).success).toBe(true);
  });

  it('rejects a performance sample with an unknown render mode', () => {
    const broken = structuredClone(snapshot);
    (broken.performance as { renderMode: string }).renderMode = 'Turbo';
    expect(systemStateSchema.safeParse(broken).success).toBe(false);
  });
});
