import { useEffect, useMemo, useReducer, useState, type ReactNode } from 'react';
import { DaemonClient } from '../api/daemonClient.ts';
import { describeHubError } from '../api/errors.ts';
import { checkSystemState } from '../api/schemas.ts';
import { useToast } from '../components/feedback/useToast.ts';
import {
  CanvasPreviewContext,
  ConnectionContext,
  DaemonApiContext,
  DaemonStateContext,
  LedPreviewContext,
  PerformanceContext,
  SystemContext,
} from './context.ts';
import { createCanvasPreviewStore } from './canvasPreviewStore.ts';
import { createDaemonApi, type DaemonApiSink } from './daemonApi.ts';
import { createLedPreviewStore } from './ledPreviewStore.ts';
import { createPerformanceStore } from './performanceStore.ts';
import { daemonReducer, initialDaemonState } from './reducer.ts';

/** Owns the hub connection and exposes daemon state, the API facade and the LED preview store. */
export function DaemonProvider({ children }: { children: ReactNode }) {
  const toast = useToast();
  const [client] = useState(() => new DaemonClient());
  const [ledPreview] = useState(createLedPreviewStore);
  const [canvasPreview] = useState(createCanvasPreviewStore);
  const [performance] = useState(createPerformanceStore);
  const [state, dispatch] = useReducer(daemonReducer, initialDaemonState);

  const sink = useMemo<DaemonApiSink>(
    () => ({
      receiveSystem: (system) => {
        checkSystemState(system);
        if (system.performance) {
          performance.publish(system.performance);
        }
        dispatch({ type: 'systemReceived', system });
      },
      clearPreview: (channel) => {
        if (channel === 'Leds') {
          ledPreview.clear();
          canvasPreview.clear();
        } else {
          dispatch({ type: 'lcdPreviewReceived', png: null });
        }
      },
    }),
    [canvasPreview, ledPreview, performance],
  );

  const api = useMemo(() => createDaemonApi(client, sink, toast), [client, sink, toast]);

  useEffect(() => {
    const loadState = () => {
      client.invoke('GetState').then(sink.receiveSystem, (error: unknown) => {
        toast.error(`Could not load the daemon state: ${describeHubError(error)}`);
      });
    };
    const unsubscribers = [
      client.onStatusChange((status) => dispatch({ type: 'connectionChanged', status })),
      client.onConnected(loadState),
      client.on('StateChanged', sink.receiveSystem),
      client.on('Telemetry', (telemetry) => dispatch({ type: 'telemetryReceived', telemetry })),
      client.on('Performance', performance.publish),
      client.on('FanStatus', (fanStatus) => dispatch({ type: 'fanStatusReceived', fanStatus })),
      client.on('LcdPreview', (png) => dispatch({ type: 'lcdPreviewReceived', png })),
      client.on('LedPreview', ledPreview.publish),
      client.on('CanvasPreview', canvasPreview.publish),
    ];
    client.start();
    return () => {
      for (const unsubscribe of unsubscribers) {
        unsubscribe();
      }
      void client.stop();
    };
  }, [canvasPreview, client, ledPreview, performance, sink, toast]);

  return (
    <DaemonApiContext value={api}>
      <PerformanceContext value={performance}>
        <LedPreviewContext value={ledPreview}>
          <CanvasPreviewContext value={canvasPreview}>
            <ConnectionContext value={state.connection}>
              <SystemContext value={state.system}>
                <DaemonStateContext value={state}>{children}</DaemonStateContext>
              </SystemContext>
            </ConnectionContext>
          </CanvasPreviewContext>
        </LedPreviewContext>
      </PerformanceContext>
    </DaemonApiContext>
  );
}
