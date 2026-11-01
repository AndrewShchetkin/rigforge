import type { LedPreviewDto } from '../api/contracts.ts';
import { decodeBase64 } from '../lib/base64.ts';
import { zoneKey } from '../lib/zones.ts';

/**
 * Tiny pub/sub for the ~20 Hz LED preview. Kept out of the reducer so a frame only re-renders the
 * components that subscribed to it (see `useLedPreview`).
 */
export interface LedPreviewStore {
  publish: (frame: LedPreviewDto) => void;
  clear: () => void;
  subscribe: (listener: () => void) => () => void;
  /** Decoded RGB triplets of one zone, keyed by `${deviceId}/${zoneId}`. Stable until the next frame. */
  getZoneColors: (key: string) => Uint8Array | null;
}

export function createLedPreviewStore(): LedPreviewStore {
  let zones = new Map<string, Uint8Array>();
  let rawByZone = new Map<string, string>();
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) {
      listener();
    }
  };

  return {
    publish: (frame) => {
      const next = new Map<string, Uint8Array>();
      const nextRaw = new Map<string, string>();
      let changed = false;

      for (const zone of frame.zones) {
        const key = zoneKey(zone.deviceId, zone.zoneId);
        const existing = zones.get(key);
        if (existing && rawByZone.get(key) === zone.colors) {
          next.set(key, existing);
          nextRaw.set(key, zone.colors);
          continue;
        }

        const colors = decodeBase64(zone.colors);
        if (colors) {
          next.set(key, colors);
          nextRaw.set(key, zone.colors);
          changed = true;
        }
      }

      if (!changed && next.size === zones.size) {
        for (const key of zones.keys()) {
          if (!next.has(key)) {
            changed = true;
            break;
          }
        }
      } else if (next.size !== zones.size) {
        changed = true;
      }

      zones = next;
      rawByZone = nextRaw;
      if (changed) {
        notify();
      }
    },
    clear: () => {
      if (zones.size > 0 || rawByZone.size > 0) {
        zones = new Map();
        rawByZone = new Map();
        notify();
      }
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getZoneColors: (key) => zones.get(key) ?? null,
  };
}
