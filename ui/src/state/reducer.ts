import type { FanStatusDto, SystemStateDto, TelemetryDto } from '../api/contracts.ts';
import type { ConnectionStatus } from '../api/daemonClient.ts';

export interface DaemonState {
  connection: ConnectionStatus;
  system: SystemStateDto | null;
  telemetry: TelemetryDto | null;
  fanStatus: FanStatusDto[];
  /** Latest LCD frame as base64 PNG (only while the LCD preview is enabled). */
  lcdPreview: string | null;
}

export type DaemonAction =
  | { type: 'connectionChanged'; status: ConnectionStatus }
  | { type: 'systemReceived'; system: SystemStateDto }
  | { type: 'telemetryReceived'; telemetry: TelemetryDto }
  | { type: 'fanStatusReceived'; fanStatus: FanStatusDto[] }
  | { type: 'lcdPreviewReceived'; png: string | null };

export const initialDaemonState: DaemonState = {
  connection: 'connecting',
  system: null,
  telemetry: null,
  fanStatus: [],
  lcdPreview: null,
};

export function daemonReducer(state: DaemonState, action: DaemonAction): DaemonState {
  switch (action.type) {
    case 'connectionChanged':
      return state.connection === action.status ? state : { ...state, connection: action.status };
    case 'systemReceived':
      // A snapshot carries telemetry and fan status of the same moment.
      return {
        ...state,
        system: action.system,
        telemetry: action.system.telemetry,
        fanStatus: action.system.fanStatus,
      };
    case 'telemetryReceived':
      return { ...state, telemetry: action.telemetry };
    case 'fanStatusReceived':
      return { ...state, fanStatus: action.fanStatus };
    case 'lcdPreviewReceived':
      return state.lcdPreview === action.png ? state : { ...state, lcdPreview: action.png };
  }
}
