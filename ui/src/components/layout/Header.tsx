import { memo } from 'react';
import type { DaemonInfoDto } from '../../api/contracts.ts';
import type { ConnectionStatus } from '../../api/daemonClient.ts';
import { formatMegabytes } from '../../lib/format.ts';
import { ConnectionBadge } from './ConnectionBadge.tsx';

interface HeaderProps {
  title: string;
  connection: ConnectionStatus;
  info: DaemonInfoDto | null;
}

export const Header = memo(function Header({ title, connection, info }: HeaderProps) {
  return (
    <header className="flex flex-wrap items-center justify-between gap-3 border-b border-zinc-800 px-4 py-3 md:px-6">
      <h1 className="text-lg font-semibold tracking-tight">{title}</h1>
      <div className="flex flex-wrap items-center gap-x-5 gap-y-2 text-xs text-zinc-400">
        {info && (
          <dl className="flex flex-wrap items-center gap-x-5 gap-y-1">
            <div className="flex gap-1.5">
              <dt>Daemon</dt>
              <dd className="font-medium text-zinc-200">v{info.version}</dd>
            </div>
            <div className="flex gap-1.5">
              <dt>Memory</dt>
              <dd className="font-medium text-zinc-200 tabular-nums">{formatMegabytes(info.workingSetBytes)}</dd>
            </div>
            <div className="hidden gap-1.5 sm:flex">
              <dt>Platform</dt>
              <dd className="font-medium text-zinc-200">{info.platform}</dd>
            </div>
          </dl>
        )}
        <ConnectionBadge status={connection} />
      </div>
    </header>
  );
});
