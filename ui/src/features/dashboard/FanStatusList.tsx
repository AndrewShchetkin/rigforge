import { memo } from 'react';
import type { DeviceDto, FanStatusDto } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { DutyMeter } from '../../components/ui/DutyMeter.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { formatCelsius, formatPercent } from '../../lib/format.ts';

interface FanStatusListProps {
  statuses: readonly FanStatusDto[];
  devices: readonly DeviceDto[];
}

export const FanStatusList = memo(function FanStatusList({ statuses, devices }: FanStatusListProps) {
  if (statuses.length === 0) {
    return (
      <EmptyState title="No controlled channels">Enable control of a fan or pump on the Cooling page.</EmptyState>
    );
  }

  return (
    <ul className="space-y-2">
      {statuses.map((status) => {
        const device = devices.find((candidate) => candidate.id === status.deviceId);
        const channel = device?.fanChannels.find((candidate) => candidate.id === status.channelId);
        return (
          <li key={status.curveId} className="space-y-2 border-b border-line py-2 last:border-b-0">
            <div className="flex items-center justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-zinc-100">{channel?.name ?? status.channelId}</p>
                <p className="truncate text-xs text-zinc-500">{device?.name ?? status.deviceId}</p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                {status.failsafe && <Badge tone="danger">Failsafe</Badge>}
                <span className="text-sm font-medium tabular-nums">{formatPercent(status.appliedDuty)}</span>
              </div>
            </div>
            <DutyMeter applied={status.appliedDuty} target={status.targetDuty} />
            <p className="flex flex-wrap gap-x-4 text-xs text-zinc-400 tabular-nums">
              <span>Applied {formatPercent(status.appliedDuty)}</span>
              <span>Target {formatPercent(status.targetDuty)}</span>
              <span>Temperature {formatCelsius(status.temperature)}</span>
            </p>
            {status.message && <p className="text-xs text-amber-300">{status.message}</p>}
          </li>
        );
      })}
    </ul>
  );
});
