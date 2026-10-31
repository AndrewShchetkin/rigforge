import type { ConnectionStatus } from '../../api/daemonClient.ts';
import { Badge, type BadgeTone } from '../ui/Badge.tsx';

const STATUS: Record<ConnectionStatus, { label: string; tone: BadgeTone; dot: string }> = {
  connected: { label: 'Connected', tone: 'success', dot: 'bg-emerald-400' },
  connecting: { label: 'Connecting…', tone: 'info', dot: 'animate-pulse bg-sky-400' },
  reconnecting: { label: 'Reconnecting…', tone: 'warning', dot: 'animate-pulse bg-amber-400' },
  disconnected: { label: 'Disconnected', tone: 'danger', dot: 'bg-red-400' },
};

export function ConnectionBadge({ status }: { status: ConnectionStatus }) {
  const { label, tone, dot } = STATUS[status];
  return (
    <span role="status">
      <Badge tone={tone}>
        <span aria-hidden="true" className={`size-1.5 rounded-full ${dot}`} />
        {label}
      </Badge>
    </span>
  );
}
