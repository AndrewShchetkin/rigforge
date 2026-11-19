import type { ConnectionStatus } from '../../api/daemonClient.ts';
import { usePrefersReducedMotion, useSpinnerFrame } from '../ui/motion.ts';

const LABEL: Record<ConnectionStatus, string> = {
  connected: 'connected',
  connecting: 'connecting',
  reconnecting: 'reconnecting',
  disconnected: 'disconnected',
};

const TONE: Record<ConnectionStatus, string> = {
  connected: 'text-ok',
  connecting: 'text-warn',
  reconnecting: 'text-warn',
  disconnected: 'text-bad',
};

export function ConnectionBadge({ status }: { status: ConnectionStatus }) {
  const reduced = usePrefersReducedMotion();
  const frame = useSpinnerFrame();
  const glyph = status === 'disconnected' ? '×' : reduced && status === 'connected' ? '●' : frame;

  return (
    <span role="status" className={TONE[status]}>
      <span aria-hidden="true">{glyph} </span>
      {LABEL[status]}
    </span>
  );
}
