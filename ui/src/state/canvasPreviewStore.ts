/**
 * Pub/sub for the ~10 Hz canvas JPEG. Kept out of React state so a frame only redraws the Konva
 * image, not the zone nodes sitting on top of it.
 */
export interface CanvasPreviewStore {
  publish: (jpegBase64: string) => void;
  clear: () => void;
  subscribe: (listener: () => void) => () => void;
  /** `data:image/jpeg` URL, or null after the lighting page unsubscribes. */
  getUrl: () => string | null;
}

export function createCanvasPreviewStore(): CanvasPreviewStore {
  let url: string | null = null;
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) {
      listener();
    }
  };

  return {
    publish: (jpegBase64) => {
      if (jpegBase64.length === 0) {
        return;
      }

      const next = `data:image/jpeg;base64,${jpegBase64}`;
      if (next === url) {
        return;
      }

      url = next;
      notify();
    },
    clear: () => {
      if (url === null) {
        return;
      }

      url = null;
      notify();
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getUrl: () => url,
  };
}
