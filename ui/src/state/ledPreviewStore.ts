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
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) {
      listener();
    }
  };

  return {
    publish: (frame) => {
      const next = new Map<string, Uint8Array>();
      for (const zone of frame.zones) {
        const colors = decodeBase64(zone.colors);
        if (colors) {
          next.set(zoneKey(zone.deviceId, zone.zoneId), colors);
        }
      }
      zones = next;
      notify();
    },
    clear: () => {
      if (zones.size > 0) {
        zones = new Map();
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
