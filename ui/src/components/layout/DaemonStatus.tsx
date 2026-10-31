import { memo } from 'react';
import type { ConnectionStatus } from '../../api/daemonClient.ts';
import { Callout } from '../ui/Callout.tsx';
import { Spinner } from '../ui/Spinner.tsx';

// In production the daemon serves this UI itself, so its address is our own origin.
const daemonAddress = import.meta.env.DEV ? __DAEMON_ADDRESS__ : window.location.host;

/** Full-page state shown until the first snapshot has arrived. */
export function WaitingForDaemon({ connection }: { connection: ConnectionStatus }) {
  const loading = connection === 'connected';
  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <div role="status" className="max-w-md rounded-xl border border-zinc-800 bg-zinc-900/60 px-8 py-10 text-center">
        <Spinner className="mx-auto size-8 text-violet-400" />
        <h2 className="mt-5 text-base font-semibold">
          {loading ? 'Loading daemon state…' : `Waiting for daemon at ${daemonAddress}…`}
        </h2>
        <p className="mt-2 text-sm text-zinc-400">
          {loading
            ? 'Connected. Fetching devices and settings.'
            : 'Make sure the RigForge daemon is running. This page connects automatically as soon as it answers.'}
        </p>
      </div>
    </div>
  );
}

/** Banner over the last known state while the connection is down. */
export const ConnectionLostBanner = memo(function ConnectionLostBanner() {
  return (
    <div className="px-4 pt-4 md:px-6">
      <Callout tone="warning" title="Connection to the daemon lost">
        Showing the last known state while reconnecting to {daemonAddress}… Changes cannot be saved until the
        connection is back.
      </Callout>
    </div>
  );
});

export const WarningsBanner = memo(function WarningsBanner({ warnings }: { warnings: readonly string[] }) {
  if (warnings.length === 0) {
    return null;
  }
  return (
    <div className="px-4 pt-4 md:px-6">
      <Callout tone="warning" title={warnings.length === 1 ? 'Warning' : `${warnings.length} warnings`}>
        <ul className="list-disc space-y-0.5 pl-4">
          {warnings.map((warning, index) => (
            <li key={`${index}:${warning}`}>{warning}</li>
          ))}
        </ul>
      </Callout>
    </div>
  );
});
