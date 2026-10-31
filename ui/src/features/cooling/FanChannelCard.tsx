import { useState } from 'react';
import type { DeviceDto, FanChannelDto } from '../../api/contracts.ts';
import { fanCurveSchemaFor } from '../../api/schemas.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { PlusIcon } from '../../components/ui/icons.tsx';
import { firstError, toFieldErrors } from '../../lib/validation.ts';
import { useDaemonApi, useDaemonState, useReadings, useSystem } from '../../state/hooks.ts';
import { ChannelStatus } from './ChannelStatus.tsx';
import { curveIdFor, presetCurve } from './curveModel.ts';
import { FanCurvePanel } from './FanCurvePanel.tsx';

interface FanChannelCardProps {
  device: DeviceDto;
  channel: FanChannelDto;
}

export function FanChannelCard({ device, channel }: FanChannelCardProps) {
  const system = useSystem();
  const { fanStatus } = useDaemonState();
  const readings = useReadings();
  const api = useDaemonApi();
  const toast = useToast();
  const [enabling, setEnabling] = useState(false);

  const curveId = curveIdFor(device.id, channel.id);
  const curve = system.profile.fanCurves.find((candidate) => candidate.id === curveId) ?? null;
  const status = fanStatus.find((candidate) => candidate.curveId === curveId) ?? null;
  const rpm =
    channel.speedSensorId === null
      ? null
      : (readings.find((reading) => reading.id === channel.speedSensorId)?.value ?? null);
  const kind = channel.kind === 'Pump' ? 'pump' : 'fan';

  const enableControl = async () => {
    const parsed = fanCurveSchemaFor(channel).safeParse(presetCurve(device.id, channel));
    if (!parsed.success) {
      toast.error(`The default curve does not fit this ${kind}: ${firstError(toFieldErrors(parsed.error)) ?? 'invalid'}.`);
      return;
    }
    setEnabling(true);
    const result = await api.upsertFanCurve(parsed.data);
    setEnabling(false);
    if (result) {
      toast.success(`RigForge now controls ${channel.name}.`);
    }
  };

  return (
    <Card
      title={
        <span className="flex items-center gap-2">
          {channel.name}
          <Badge tone={channel.kind === 'Pump' ? 'info' : 'neutral'}>{channel.kind}</Badge>
        </span>
      }
      description={device.name}
    >
      <div className="space-y-5">
        <ChannelStatus rpm={rpm} status={status} />
        {curve ? (
          <FanCurvePanel curve={curve} channel={channel} status={status} />
        ) : (
          <div className="space-y-3">
            <p className="text-sm text-zinc-400">
              This {kind} runs under firmware/BIOS control. Enabling control applies a{' '}
              {channel.kind === 'Pump' ? 'liquid-temperature' : 'CPU-temperature'} preset that you can fine-tune
              afterwards.
            </p>
            <Button variant="primary" onClick={() => void enableControl()} disabled={enabling}>
              <PlusIcon className="size-4" />
              Enable control
            </Button>
          </div>
        )}
      </div>
    </Card>
  );
}
