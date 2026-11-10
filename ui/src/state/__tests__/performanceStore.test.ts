import { describe, expect, it, vi } from 'vitest';
import type { PerformanceMetricsDto } from '../../api/contracts.ts';
import { createPerformanceStore } from '../performanceStore.ts';

function metrics(cpuPercent: number): PerformanceMetricsDto {
  return {
    timestamp: `2026-01-01T00:00:${cpuPercent.toString().padStart(2, '0')}Z`,
    cpuPercent,
    privateBytes: 45_000_000,
    workingSetBytes: 60_000_000,
    managedHeapBytes: 7_100_000,
    effectFps: 60,
    frameTimeMicroseconds: 14,
    renderMode: 'DirectSampling',
    activeRgbDevices: 2,
    totalControlledLeds: 120,
    gen0Collections: 1,
    gen1Collections: 0,
    gen2Collections: 0,
  };
}

describe('createPerformanceStore', () => {
  it('starts empty, publishes the latest sample, and stops after unsubscribe', () => {
    const store = createPerformanceStore();
    expect(store.getSnapshot()).toBeNull();

    const first = vi.fn();
    const second = vi.fn();
    const unsubscribeFirst = store.subscribe(first);
    store.subscribe(second);

    const sample = metrics(0.2);
    store.publish(sample);
    expect(store.getSnapshot()).toBe(sample);
    expect(first).toHaveBeenCalledTimes(1);
    expect(second).toHaveBeenCalledTimes(1);

    const next = metrics(1.4);
    store.publish(next);
    expect(store.getSnapshot()).toBe(next);
    expect(store.getSnapshot()?.cpuPercent).toBe(1.4);
    expect(first).toHaveBeenCalledTimes(2);

    unsubscribeFirst();
    store.publish(metrics(2));
    expect(first).toHaveBeenCalledTimes(2);
    expect(second).toHaveBeenCalledTimes(3);
    expect(store.getSnapshot()?.cpuPercent).toBe(2);
  });
});
