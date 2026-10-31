import type { FanStatusDto } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { DutyMeter } from '../../components/ui/DutyMeter.tsx';
import { formatCelsius, formatPercent, formatRpm } from '../../lib/format.ts';

interface ChannelStatusProps {
  rpm: number | null;
  status: FanStatusDto | null;
}

/** Live readout of one channel: speed, applied/target duty, driving temperature and failsafe state. */
export function ChannelStatus({ rpm, status }: ChannelStatusProps) {
  return (
    <div className="space-y-2 rounded-lg border border-zinc-800 bg-zinc-950/40 px-4 py-3">
      <div className="flex flex-wrap items-center gap-x-6 gap-y-1">
        <p className="text-xl font-semibold tabular-nums">{formatRpm(rpm)}</p>
        {status ? (
          <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-zinc-400 tabular-nums">
            <div className="flex gap-1">
              <dt>Applied</dt>
              <dd className="font-medium text-zinc-100">{formatPercent(status.appliedDuty)}</dd>
            </div>
            <div className="flex gap-1">
              <dt>Target</dt>
              <dd className="font-medium text-zinc-100">{formatPercent(status.targetDuty)}</dd>
            </div>
            <div className="flex gap-1">
              <dt>Temperature</dt>
              <dd className="font-medium text-zinc-100">{formatCelsius(status.temperature)}</dd>
            </div>
          </dl>
        ) : (
          <p className="text-xs text-zinc-500">No live status (firmware/BIOS control)</p>
        )}
        {status?.failsafe && <Badge tone="danger">Failsafe</Badge>}
      </div>
      {status && <DutyMeter applied={status.appliedDuty} target={status.targetDuty} />}
      {status?.message && <p className="text-xs text-amber-300">{status.message}</p>}
    </div>
  );
}
