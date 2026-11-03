import { useState } from 'react';
import { useToast } from '../../components/feedback/useToast.ts';
import { Button } from '../../components/ui/Button.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { RefreshIcon } from '../../components/ui/icons.tsx';
import { Spinner } from '../../components/ui/Spinner.tsx';
import { useDaemonApi, useSystem } from '../../state/hooks.ts';
import { DeviceCard } from './DeviceCard.tsx';

function countDevices(count: number): string {
  return `${count} ${count === 1 ? 'device' : 'devices'}`;
}

export function DevicesPage() {
  const { devices, profile } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const [scanning, setScanning] = useState(false);

  const rescan = async () => {
    setScanning(true);
    const result = await api.rescanDevices();
    setScanning(false);
    if (result) {
      toast.success(`Rescan complete: ${countDevices(result.devices.length)} found.`);
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-zinc-400">{countDevices(devices.length)} detected</p>
        <Button variant="primary" onClick={() => void rescan()} disabled={scanning}>
          {scanning ? <Spinner /> : <RefreshIcon className="size-4" />}
          Rescan devices
        </Button>
      </div>
      {devices.length === 0 ? (
        <EmptyState title="No devices detected">
          Connect a supported device (or close conflicting vendor software) and rescan.
        </EmptyState>
      ) : (
        <div className="grid gap-6 xl:grid-cols-2">
          {devices.map((device) => (
            <DeviceCard
              key={device.id}
              device={device}
              colorCorrection={(profile.colorCorrections ?? []).find((correction) => correction.deviceId === device.id)}
            />
          ))}
        </div>
      )}
    </div>
  );
}
