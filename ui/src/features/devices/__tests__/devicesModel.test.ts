import { describe, expect, it } from 'vitest';
import type { DeviceSupportSource } from '../devicesModel.ts';
import {
  MAX_PROFILE_IMPORT_BYTES,
  coerceDeviceFilter,
  countDevicesBySupport,
  filterDevicesBySupport,
  formatProfileJson,
  groupDevicesBySupport,
  isLinuxPlatform,
  isProfileImportWithinLimit,
  isWindowsPlatform,
  parseProfileJson,
  profileFileName,
  resolveSupportStatus,
} from '../devicesModel.ts';

interface SampleDevice extends DeviceSupportSource {
  id: string;
}

function device(
  id: string,
  supportStatus?: string | null,
  capabilities?: readonly string[] | null,
): SampleDevice {
  return { id, supportStatus, capabilities };
}

function ids(devices: readonly SampleDevice[]): string[] {
  return devices.map((item) => item.id);
}

function expectProfile(text: string) {
  const result = parseProfileJson(text);
  if (!result.ok) {
    throw new Error(result.message);
  }
  return result.profile;
}

const sampleDevices: readonly SampleDevice[] = [
  device('unmanaged', 'Unmanaged', ['Rgb']),
  device('rgb', undefined, ['Rgb']),
  device('cpu', null, ['Sensors']),
  device('gpu', undefined, ['Sensors']),
  device('lcd', 'Supported', ['Lcd']),
  device('fans', undefined, ['FanControl']),
  device('mystery', 'nope', []),
];

describe('resolveSupportStatus', () => {
  const cases: ReadonlyArray<readonly [DeviceSupportSource, string]> = [
    [{ supportStatus: 'Supported', capabilities: ['Sensors'] }, 'Supported'],
    [{ supportStatus: 'TelemetryOnly', capabilities: ['Rgb', 'Lcd'] }, 'TelemetryOnly'],
    [{ supportStatus: 'Unmanaged', capabilities: ['FanControl'] }, 'Unmanaged'],
    [{ capabilities: ['Rgb'] }, 'Supported'],
    [{ capabilities: ['FanControl'] }, 'Supported'],
    [{ capabilities: ['Lcd'] }, 'Supported'],
    [{ capabilities: ['Sensors', 'Rgb'] }, 'Supported'],
    [{ supportStatus: null, capabilities: ['Sensors'] }, 'TelemetryOnly'],
    [{ supportStatus: 'not-a-status', capabilities: ['Sensors'] }, 'TelemetryOnly'],
    [{ supportStatus: '', capabilities: ['Lcd'] }, 'Supported'],
    [{ supportStatus: 'supported', capabilities: [] }, 'Unmanaged'],
    [{ capabilities: [] }, 'Unmanaged'],
    [{}, 'Unmanaged'],
    [{ capabilities: null }, 'Unmanaged'],
    [{ supportStatus: null, capabilities: null }, 'Unmanaged'],
  ];

  it('trusts an explicit status and infers Supported, TelemetryOnly, or Unmanaged when it is missing', () => {
    for (const [input, expected] of cases) {
      expect(resolveSupportStatus(input), JSON.stringify(input)).toBe(expected);
    }
  });
});

describe('groupDevicesBySupport', () => {
  it('buckets devices in encounter order, including inferred statuses', () => {
    const groups = groupDevicesBySupport(sampleDevices);
    expect(ids(groups.supported)).toEqual(['rgb', 'lcd', 'fans']);
    expect(ids(groups.telemetryOnly)).toEqual(['cpu', 'gpu']);
    expect(ids(groups.unmanaged)).toEqual(['unmanaged', 'mystery']);
  });

  it('counts and filters every group, and All keeps the original array', () => {
    const counts = countDevicesBySupport(sampleDevices);
    expect(counts).toEqual({ All: 7, Supported: 3, TelemetryOnly: 2, Unmanaged: 2 });
    expect(filterDevicesBySupport(sampleDevices, 'All')).toBe(sampleDevices);
    expect(ids(filterDevicesBySupport(sampleDevices, 'Supported'))).toEqual(['rgb', 'lcd', 'fans']);
    expect(ids(filterDevicesBySupport(sampleDevices, 'TelemetryOnly'))).toEqual(['cpu', 'gpu']);
    expect(ids(filterDevicesBySupport(sampleDevices, 'Unmanaged'))).toEqual(['unmanaged', 'mystery']);
    expect(countDevicesBySupport([])).toEqual({ All: 0, Supported: 0, TelemetryOnly: 0, Unmanaged: 0 });
  });

  it('drops the unmanaged filter only when that group is empty', () => {
    const empty = countDevicesBySupport([]);
    expect(coerceDeviceFilter('Unmanaged', empty)).toBe('All');
    expect(coerceDeviceFilter('TelemetryOnly', empty)).toBe('TelemetryOnly');
    expect(coerceDeviceFilter('Supported', countDevicesBySupport(sampleDevices))).toBe('Supported');
    expect(coerceDeviceFilter('Unmanaged', countDevicesBySupport(sampleDevices))).toBe('Unmanaged');
  });
});

describe('parseProfileJson', () => {
  const valid = {
    schemaVersion: 2,
    canvas: { width: 320, height: 180, placements: [] },
    effect: { effectId: 'solid', brightness: 1 },
    fanCurves: [{ id: 'pump' }],
    lcd: { mode: 'Dashboard' },
  };

  it('accepts a profile object and keeps keys besides canvas and effect', () => {
    const profile = expectProfile(JSON.stringify(valid));
    expect(profile).toMatchObject({
      schemaVersion: 2,
      canvas: { width: 320, height: 180 },
      effect: { effectId: 'solid' },
      fanCurves: [{ id: 'pump' }],
      lcd: { mode: 'Dashboard' },
    });
  });

  it('accepts a leading BOM and pretty-printed JSON', () => {
    const pretty = formatProfileJson(valid);
    expect(pretty).toBe(`${JSON.stringify(valid, null, 2)}\n`);
    expect(expectProfile(`\uFEFF${pretty}`)).toMatchObject({ effect: { effectId: 'solid' } });
  });

  it('rejects malformed JSON without treating it as a missing-key error', () => {
    const result = parseProfileJson('{');
    expect(result.ok).toBe(false);
    if (result.ok) {
      return;
    }
    expect(result.message).toMatch(/malformed/i);
    expect(result.message).not.toMatch(/canvas/);
  });

  it('rejects empty input', () => {
    expect(parseProfileJson('')).toEqual({ ok: false, message: 'The profile file is empty.' });
    expect(parseProfileJson(' \n\t ')).toEqual({ ok: false, message: 'The profile file is empty.' });
  });

  it('rejects values that are not JSON objects', () => {
    for (const text of ['null', '[]', '"profile"', '1', 'true']) {
      expect(parseProfileJson(text), text).toEqual({
        ok: false,
        message: 'A profile must be a JSON object with "canvas" and "effect".',
      });
    }
  });

  it('rejects a missing or non-object canvas or effect', () => {
    expect(parseProfileJson(JSON.stringify({}))).toEqual({
      ok: false,
      message: 'A profile must include object properties "canvas" and "effect".',
    });
    expect(parseProfileJson(JSON.stringify({ effect: { effectId: 'solid' } }))).toEqual({
      ok: false,
      message: 'A profile must include object property "canvas".',
    });
    expect(parseProfileJson(JSON.stringify({ canvas: { width: 1 } }))).toEqual({
      ok: false,
      message: 'A profile must include object property "effect".',
    });
    expect(parseProfileJson(JSON.stringify({ canvas: [], effect: { effectId: 'solid' } }))).toEqual({
      ok: false,
      message: 'A profile must include object property "canvas".',
    });
    expect(parseProfileJson(JSON.stringify({ canvas: { width: 1 }, effect: 'solid' }))).toEqual({
      ok: false,
      message: 'A profile must include object property "effect".',
    });
    expect(parseProfileJson(JSON.stringify({ canvas: null, effect: null }))).toEqual({
      ok: false,
      message: 'A profile must include object properties "canvas" and "effect".',
    });
  });
});

describe('profileFileName', () => {
  it('formats the local calendar date as rigforge-profile-YYYY-MM-DD.json', () => {
    expect(profileFileName(new Date(2026, 10, 10, 23, 59))).toBe('rigforge-profile-2026-11-10.json');
    expect(profileFileName(new Date(2026, 0, 5, 0, 30))).toBe('rigforge-profile-2026-01-05.json');
  });
});

describe('formatProfileJson', () => {
  it('indents with two spaces and ends with a newline', () => {
    expect(formatProfileJson({ canvas: {}, effect: { effectId: 'solid' } })).toBe(
      '{\n  "canvas": {},\n  "effect": {\n    "effectId": "solid"\n  }\n}\n',
    );
  });
});

describe('isLinuxPlatform', () => {
  it('matches platform strings that contain linux, independent of case', () => {
    expect(isLinuxPlatform('Linux')).toBe(true);
    expect(isLinuxPlatform('linux')).toBe(true);
    expect(isLinuxPlatform('Ubuntu 24.04 Linux')).toBe(true);
    expect(isLinuxPlatform('Linux 6.8.0-45-generic')).toBe(true);
    expect(isLinuxPlatform('Microsoft Windows 11')).toBe(false);
    expect(isLinuxPlatform('Darwin 24.0.0')).toBe(false);
    expect(isLinuxPlatform('')).toBe(false);
    expect(isLinuxPlatform(null)).toBe(false);
    expect(isLinuxPlatform(undefined)).toBe(false);
  });

  it('recognizes Windows the same way', () => {
    expect(isWindowsPlatform('Microsoft Windows 11')).toBe(true);
    expect(isWindowsPlatform('windows')).toBe(true);
    expect(isWindowsPlatform('Linux 6.8.0')).toBe(false);
    expect(isWindowsPlatform(null)).toBe(false);
  });
});

describe('isProfileImportWithinLimit', () => {
  it('accepts files up to 1 MB and rejects everything else', () => {
    expect(MAX_PROFILE_IMPORT_BYTES).toBe(1024 * 1024);
    expect(isProfileImportWithinLimit(0)).toBe(true);
    expect(isProfileImportWithinLimit(MAX_PROFILE_IMPORT_BYTES)).toBe(true);
    expect(isProfileImportWithinLimit(MAX_PROFILE_IMPORT_BYTES + 1)).toBe(false);
    expect(isProfileImportWithinLimit(-1)).toBe(false);
    expect(isProfileImportWithinLimit(Number.NaN)).toBe(false);
    expect(isProfileImportWithinLimit(Number.POSITIVE_INFINITY)).toBe(false);
  });
});
