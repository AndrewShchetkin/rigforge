import { useConnectionStatus, useSystemSnapshot } from '../../state/hooks.ts';
import { AsciiFrame } from './AsciiFrame.tsx';
import { ConnectionLostBanner, WaitingForDaemon, WarningsBanner } from './DaemonStatus.tsx';
import { Header } from './Header.tsx';
import { pageLabel, useHashPage } from './navigation.ts';
import { PageOutlet } from './PageOutlet.tsx';
import { Sidebar } from './Sidebar.tsx';

export function AppShell() {
  const page = useHashPage();
  const connection = useConnectionStatus();
  const system = useSystemSnapshot();

  return (
    <AsciiFrame>
      <div className="flex min-h-dvh flex-col md:flex-row">
        <Sidebar current={page} />
        <div className="flex min-w-0 flex-1 flex-col">
          <Header title={pageLabel(page)} connection={connection} info={system?.info ?? null} />
          <main className="flex flex-1 flex-col gap-2 px-3 py-3 md:px-4">
            {system && <WarningsBanner warnings={system.warnings} />}
            {system && connection !== 'connected' && <ConnectionLostBanner />}
            {system ? <PageOutlet page={page} /> : <WaitingForDaemon connection={connection} />}
          </main>
        </div>
      </div>
    </AsciiFrame>
  );
}
