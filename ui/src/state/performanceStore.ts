import type { PerformanceMetricsDto } from '../api/contracts.ts';

/**
 * Tiny pub/sub for the ~1 Hz performance sample. Kept out of the reducer so a tick only re-renders
 * components that subscribed (see `usePerformance`), not the lighting canvas or device lists.
 */
export interface PerformanceStore {
  publish: (metrics: PerformanceMetricsDto) => void;
  subscribe: (listener: () => void) => () => void;
  /** Latest sample, or null until the daemon has sent one. Stable until the next publish. */
  getSnapshot: () => PerformanceMetricsDto | null;
}

export function createPerformanceStore(): PerformanceStore {
  let snapshot: PerformanceMetricsDto | null = null;
  const listeners = new Set<() => void>();

  const notify = () => {
    for (const listener of listeners) {
      listener();
    }
  };

  return {
    publish: (metrics) => {
      snapshot = metrics;
      notify();
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    getSnapshot: () => snapshot,
  };
}
