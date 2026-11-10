import type { DeviceSupportStatus, RigProfile } from '../../api/contracts.ts';

/** 1 MiB. A real profile is a few kilobytes; the cap keeps a huge file from freezing the tab. */
export const MAX_PROFILE_IMPORT_BYTES = 1024 * 1024;

export const DEVICE_FILTERS = ['All', 'Supported', 'TelemetryOnly', 'Unmanaged'] as const;
export type DeviceFilter = (typeof DEVICE_FILTERS)[number];

export interface DeviceSupportSource {
  supportStatus?: string | null;
  capabilities?: readonly string[] | null;
}

export interface DeviceSupportGroups<T> {
  supported: T[];
  telemetryOnly: T[];
  unmanaged: T[];
}

export interface DeviceSupportCounts {
  All: number;
  Supported: number;
  TelemetryOnly: number;
  Unmanaged: number;
}

export type ProfileParseResult = { ok: true; profile: RigProfile } | { ok: false; message: string };

const CONTROL_CAPABILITIES = new Set(['Rgb', 'FanControl', 'Lcd']);

function isSupportStatus(value: string | null | undefined): value is DeviceSupportStatus {
  return value === 'Supported' || value === 'TelemetryOnly' || value === 'Unmanaged';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * Explicit daemon status wins. Older snapshots that omit `supportStatus` are inferred the same way as
 * `IHardwareDevice.SupportStatus`: any lighting, fan, or LCD capability is active control; sensors alone
 * are telemetry; anything else stays unmanaged.
 */
export function resolveSupportStatus(device: DeviceSupportSource): DeviceSupportStatus {
  if (isSupportStatus(device.supportStatus)) {
    return device.supportStatus;
  }
  const capabilities = device.capabilities ?? [];
  if (capabilities.some((capability) => CONTROL_CAPABILITIES.has(capability))) {
    return 'Supported';
  }
  if (capabilities.includes('Sensors')) {
    return 'TelemetryOnly';
  }
  return 'Unmanaged';
}

export function groupDevicesBySupport<T extends DeviceSupportSource>(devices: readonly T[]): DeviceSupportGroups<T> {
  const groups: DeviceSupportGroups<T> = { supported: [], telemetryOnly: [], unmanaged: [] };
  for (const device of devices) {
    switch (resolveSupportStatus(device)) {
      case 'Supported':
        groups.supported.push(device);
        break;
      case 'TelemetryOnly':
        groups.telemetryOnly.push(device);
        break;
      case 'Unmanaged':
        groups.unmanaged.push(device);
        break;
    }
  }
  return groups;
}

export function countDevicesBySupport<T extends DeviceSupportSource>(devices: readonly T[]): DeviceSupportCounts {
  const groups = groupDevicesBySupport(devices);
  return {
    All: devices.length,
    Supported: groups.supported.length,
    TelemetryOnly: groups.telemetryOnly.length,
    Unmanaged: groups.unmanaged.length,
  };
}

/** `All` returns the same array. Other filters keep the original encounter order. */
export function filterDevicesBySupport<T extends DeviceSupportSource>(
  devices: readonly T[],
  filter: DeviceFilter,
): readonly T[] {
  switch (filter) {
    case 'All':
      return devices;
    case 'Supported':
      return groupDevicesBySupport(devices).supported;
    case 'TelemetryOnly':
      return groupDevicesBySupport(devices).telemetryOnly;
    case 'Unmanaged':
      return groupDevicesBySupport(devices).unmanaged;
  }
}

/** Hides the unmanaged filter once that group is empty so the tab does not linger at zero. */
export function coerceDeviceFilter(filter: DeviceFilter, counts: DeviceSupportCounts): DeviceFilter {
  if (filter === 'Unmanaged' && counts.Unmanaged === 0) {
    return 'All';
  }
  return filter;
}

export function isLinuxPlatform(platform: string | null | undefined): boolean {
  return platform?.toLowerCase().includes('linux') ?? false;
}

export function isWindowsPlatform(platform: string | null | undefined): boolean {
  return platform?.toLowerCase().includes('windows') ?? false;
}

export function isProfileImportWithinLimit(byteLength: number): boolean {
  return Number.isFinite(byteLength) && byteLength >= 0 && byteLength <= MAX_PROFILE_IMPORT_BYTES;
}

/** Local calendar date, `rigforge-profile-YYYY-MM-DD.json`. */
export function profileFileName(now: Date): string {
  const year = now.getFullYear().toString();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `rigforge-profile-${year}-${month}-${day}.json`;
}

/** Pretty-printed profile with a trailing newline so the download is a normal text file. */
export function formatProfileJson(profile: object): string {
  return `${JSON.stringify(profile, null, 2)}\n`;
}

/**
 * Client sanity check before `ImportProfile`. The daemon still validates ranges and required fields;
 * this only rejects files that are not profile-shaped. Extra keys are preserved.
 */
export function parseProfileJson(text: string): ProfileParseResult {
  const source = text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
  if (source.trim() === '') {
    return { ok: false, message: 'The profile file is empty.' };
  }

  let value: unknown;
  try {
    value = JSON.parse(source) as unknown;
  } catch (error) {
    const detail = error instanceof Error ? error.message : 'Invalid JSON.';
    return { ok: false, message: `Profile JSON is malformed: ${detail}` };
  }

  if (!isRecord(value)) {
    return { ok: false, message: 'A profile must be a JSON object with "canvas" and "effect".' };
  }

  const missing: string[] = [];
  if (!isRecord(value.canvas)) {
    missing.push('canvas');
  }
  if (!isRecord(value.effect)) {
    missing.push('effect');
  }
  if (missing.length > 0) {
    const quoted = missing.map((key) => `"${key}"`).join(' and ');
    const noun = missing.length === 1 ? 'property' : 'properties';
    return { ok: false, message: `A profile must include object ${noun} ${quoted}.` };
  }

  return { ok: true, profile: value as unknown as RigProfile };
}
