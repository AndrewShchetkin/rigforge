import { createContext } from 'react';
import type { SystemStateDto } from '../api/contracts.ts';
import type { ConnectionStatus } from '../api/daemonClient.ts';
import type { DaemonApi } from './daemonApi.ts';
import type { CanvasPreviewStore } from './canvasPreviewStore.ts';
import type { LedPreviewStore } from './ledPreviewStore.ts';
import type { PerformanceStore } from './performanceStore.ts';
import type { DaemonState } from './reducer.ts';

// Several narrow contexts so that components which only need the snapshot, the connection status,
// the (stable) API or the LED store do not re-render on every telemetry tick.
// `undefined` means "rendered outside <DaemonProvider>".
export const DaemonStateContext = createContext<DaemonState | undefined>(undefined);
export const SystemContext = createContext<SystemStateDto | null | undefined>(undefined);
export const ConnectionContext = createContext<ConnectionStatus | undefined>(undefined);
export const DaemonApiContext = createContext<DaemonApi | undefined>(undefined);
export const LedPreviewContext = createContext<LedPreviewStore | undefined>(undefined);
export const CanvasPreviewContext = createContext<CanvasPreviewStore | undefined>(undefined);
export const PerformanceContext = createContext<PerformanceStore | undefined>(undefined);
