import { use, useEffect, useMemo, useSyncExternalStore, type Context } from 'react';
import type { PreviewChannel, SensorReadingDto, SystemStateDto } from '../api/contracts.ts';
import type { ConnectionStatus } from '../api/daemonClient.ts';
import { sameSensorOptions, temperatureSensorOptions, type SensorOption } from '../lib/sensors.ts';
import { useStableValue } from '../lib/useStableValue.ts';
import {
  ConnectionContext,
  DaemonApiContext,
  DaemonStateContext,
  LedPreviewContext,
  SystemContext,
} from './context.ts';
import type { DaemonApi } from './daemonApi.ts';
import type { DaemonState } from './reducer.ts';

const NO_READINGS: readonly SensorReadingDto[] = [];

function useRequiredContext<T>(context: Context<T | undefined>, hookName: string): T {
  const value = use(context);
  if (value === undefined) {
    throw new Error(`${hookName}() must be used inside <DaemonProvider>.`);
  }
  return value;
}

/** Everything, including 1 Hz telemetry: prefer the narrower hooks below when possible. */
export function useDaemonState(): DaemonState {
  return useRequiredContext(DaemonStateContext, 'useDaemonState');
}

export function useDaemonApi(): DaemonApi {
  return useRequiredContext(DaemonApiContext, 'useDaemonApi');
}

export function useConnectionStatus(): ConnectionStatus {
  return useRequiredContext(ConnectionContext, 'useConnectionStatus');
}

/** The latest snapshot, or null until the daemon has answered `GetState`. */
export function useSystemSnapshot(): SystemStateDto | null {
  return useRequiredContext(SystemContext, 'useSystemSnapshot');
}

/** The latest snapshot. Pages only render once the first snapshot has arrived. */
export function useSystem(): SystemStateDto {
  const system = useSystemSnapshot();
  if (!system) {
    throw new Error('useSystem() requires a daemon snapshot.');
  }
  return system;
}

/** Live sensor readings from Telemetry events, falling back to the snapshot. */
export function useReadings(): readonly SensorReadingDto[] {
  const { telemetry, system } = useDaemonState();
  return telemetry?.readings ?? system?.telemetry.readings ?? NO_READINGS;
}

/** Role shortcuts plus temperature sensors; keeps its identity while the sensor list is unchanged. */
export function useSensorOptions(): readonly SensorOption[] {
  const readings = useReadings();
  const options = useMemo(() => temperatureSensorOptions(readings), [readings]);
  return useStableValue<readonly SensorOption[]>(options, sameSensorOptions);
}

/** RGB triplets of one zone from the ~20 Hz LED preview; re-renders only the calling component. */
export function useLedPreview(zoneKey: string): Uint8Array | null {
  const store = useRequiredContext(LedPreviewContext, 'useLedPreview');
  return useSyncExternalStore(store.subscribe, () => store.getZoneColors(zoneKey));
}

/** Enables a preview stream while the calling component is mounted. */
export function usePreviewStream(channel: PreviewChannel): void {
  const api = useDaemonApi();
  useEffect(() => {
    void api.setPreview(channel, true);
    return () => {
      void api.setPreview(channel, false);
    };
  }, [api, channel]);
}
