import type { DeviceDto, RgbZoneDto, ZonePlacement } from '../api/contracts.ts';

/** Key used by the LED preview stream and the layout editor: `${deviceId}/${zoneId}`. */
export function zoneKey(deviceId: string, zoneId: string): string {
  return `${deviceId}/${zoneId}`;
}

export function placementKey(placement: Pick<ZonePlacement, 'deviceId' | 'zoneId'>): string {
  return zoneKey(placement.deviceId, placement.zoneId);
}

export interface ZoneRef {
  device: DeviceDto;
  zone: RgbZoneDto;
}

export function indexZones(devices: readonly DeviceDto[]): ReadonlyMap<string, ZoneRef> {
  const index = new Map<string, ZoneRef>();
  for (const device of devices) {
    for (const zone of device.zones) {
      index.set(zoneKey(device.id, zone.id), { device, zone });
    }
  }
  return index;
}
