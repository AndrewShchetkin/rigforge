import type {
  DeviceColorCorrection,
  EffectSettings,
  FanCurve,
  LcdSettings,
  PreviewChannel,
  SystemStateDto,
  VirtualCanvas,
} from '../api/contracts.ts';
import type { DaemonClient } from '../api/daemonClient.ts';
import { describeHubError } from '../api/errors.ts';
import type { ToastApi } from '../components/feedback/toastContext.ts';

/**
 * Commands of the configurator. Mutations resolve to the new snapshot (already dispatched), or to
 * `null` after showing an error toast with the daemon's message.
 */
export interface DaemonApi {
  updateCanvas: (canvas: VirtualCanvas) => Promise<SystemStateDto | null>;
  updateEffect: (effect: EffectSettings) => Promise<SystemStateDto | null>;
  upsertFanCurve: (curve: FanCurve) => Promise<SystemStateDto | null>;
  removeFanCurve: (curveId: string) => Promise<SystemStateDto | null>;
  updateLcd: (lcd: LcdSettings) => Promise<SystemStateDto | null>;
  upsertColorCorrection: (correction: DeviceColorCorrection) => Promise<SystemStateDto | null>;
  removeColorCorrection: (deviceId: string) => Promise<SystemStateDto | null>;
  rescanDevices: () => Promise<SystemStateDto | null>;
  setPreview: (channel: PreviewChannel, enabled: boolean) => Promise<void>;
}

export interface DaemonApiSink {
  receiveSystem: (system: SystemStateDto) => void;
  clearPreview: (channel: PreviewChannel) => void;
}

export function createDaemonApi(client: DaemonClient, sink: DaemonApiSink, toast: ToastApi): DaemonApi {
  const mutate = async (run: () => Promise<SystemStateDto>): Promise<SystemStateDto | null> => {
    try {
      const system = await run();
      sink.receiveSystem(system);
      return system;
    } catch (error) {
      toast.error(describeHubError(error));
      return null;
    }
  };

  return {
    updateCanvas: (canvas) => mutate(() => client.invoke('UpdateCanvas', canvas)),
    updateEffect: (effect) => mutate(() => client.invoke('UpdateEffect', effect)),
    upsertFanCurve: (curve) => mutate(() => client.invoke('UpsertFanCurve', curve)),
    removeFanCurve: (curveId) => mutate(() => client.invoke('RemoveFanCurve', curveId)),
    updateLcd: (lcd) => mutate(() => client.invoke('UpdateLcd', lcd)),
    upsertColorCorrection: (correction) => mutate(() => client.invoke('UpsertColorCorrection', correction)),
    removeColorCorrection: (deviceId) => mutate(() => client.invoke('RemoveColorCorrection', deviceId)),
    rescanDevices: () => mutate(() => client.invoke('RescanDevices')),
    setPreview: async (channel, enabled) => {
      if (!enabled) {
        sink.clearPreview(channel);
      }
      try {
        await client.setPreview(channel, enabled);
      } catch (error) {
        toast.error(describeHubError(error));
      }
    },
  };
}
