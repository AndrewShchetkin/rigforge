import { useMemo, useState } from 'react';
import type { DeviceColorCorrection } from '../../api/contracts.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Button } from '../../components/ui/Button.tsx';
import { Callout } from '../../components/ui/Callout.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { RefreshIcon, ShieldIcon } from '../../components/ui/icons.tsx';
import { Spinner } from '../../components/ui/Spinner.tsx';
import { useDaemonApi, useSystem } from '../../state/hooks.ts';
import { DeviceCard } from './DeviceCard.tsx';
import { PlatformSetupModal } from './PlatformSetupModal.tsx';
import { ProfileTransferBar } from './ProfileTransferBar.tsx';
import type { DeviceFilter, DeviceSupportCounts } from './devicesModel.ts';
import {
  DEVICE_FILTERS,
  coerceDeviceFilter,
  countDevicesBySupport,
  filterDevicesBySupport,
  isLinuxPlatform,
} from './devicesModel.ts';

const FILTER_META: Record<DeviceFilter, { label: string; hint: string }> = {
  All: { label: 'All', hint: 'Every detected device' },
  Supported: { label: 'Active Control', hint: 'RGB, fan control, or LCD' },
  TelemetryOnly: { label: 'Sensors Only', hint: 'CPU, GPU, and other sensors' },
  Unmanaged: { label: 'Detected (Unmanaged)', hint: 'Seen on the bus, not driven' },
};

function formatDeviceCount(count: number): string {
  return `${count} ${count === 1 ? 'device' : 'devices'}`;
}

function describeSelection(filter: DeviceFilter, counts: DeviceSupportCounts): string {
  if (filter === 'All') {
    return `${formatDeviceCount(counts.All)} detected`;
  }
  return `${formatDeviceCount(counts[filter])} in ${FILTER_META[filter].label} · ${formatDeviceCount(counts.All)} detected`;
}

export function DevicesPage() {
  const { devices, profile, info } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const [scanning, setScanning] = useState(false);
  const [filter, setFilter] = useState<DeviceFilter>('All');
  const [setupOpen, setSetupOpen] = useState(false);
  const linux = isLinuxPlatform(info.platform);
  const counts = useMemo(() => countDevicesBySupport(devices), [devices]);
  const activeFilter = coerceDeviceFilter(filter, counts);
  const visible = useMemo(() => filterDevicesBySupport(devices, activeFilter), [devices, activeFilter]);
  const corrections = useMemo(() => {
    const byDevice = new Map<string, DeviceColorCorrection>();
    for (const correction of profile.colorCorrections ?? []) {
      byDevice.set(correction.deviceId, correction);
    }
    return byDevice;
  }, [profile.colorCorrections]);

  const rescan = async () => {
    setScanning(true);
    const result = await api.rescanDevices();
    setScanning(false);
    if (result) {
      toast.success(`Rescan complete: ${formatDeviceCount(result.devices.length)} found.`);
    }
  };

  const nothingDetected = devices.length === 0;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-zinc-400">{describeSelection(activeFilter, counts)}</p>
        <div className="flex flex-wrap items-center gap-2">
          <ProfileTransferBar />
          <Button
            variant={linux ? 'primary' : 'secondary'}
            onClick={() => setSetupOpen(true)}
            aria-haspopup="dialog"
            aria-expanded={setupOpen}
          >
            <ShieldIcon className="size-4" />
            OS Permissions / udev
            {linux && (
              <span className="rounded bg-white/15 px-1.5 py-0.5 text-[10px] font-semibold tracking-wide uppercase">
                Linux
              </span>
            )}
          </Button>
          <Button variant={linux ? 'secondary' : 'primary'} onClick={() => void rescan()} disabled={scanning}>
            {scanning ? <Spinner /> : <RefreshIcon className="size-4" />}
            Rescan devices
          </Button>
        </div>
      </div>

      <DeviceGroupStrip counts={counts} value={activeFilter} onChange={setFilter} />

      {activeFilter === 'Unmanaged' && (
        <Callout title="Detected, not guessed">
          RigForge lists every controller it can see on the bus, and it only drives devices whose registers belong to a
          known protocol. A custom <code className="font-mono text-[0.92em]">devices/*.json</code> manifest binds a new
          USB product id to one of those families. Rescan after adding one and the device moves into Active Control.
        </Callout>
      )}

      {visible.length === 0 ? (
        <EmptyState title={nothingDetected ? 'No devices detected' : 'Nothing in this group'}>
          {nothingDetected
            ? 'Connect a supported device (or close conflicting vendor software) and rescan.'
            : 'Choose another group, or rescan after connecting hardware.'}
        </EmptyState>
      ) : (
        <div className="grid gap-6 xl:grid-cols-2">
          {visible.map((device) => (
            <DeviceCard key={device.id} device={device} colorCorrection={corrections.get(device.id)} />
          ))}
        </div>
      )}

      <PlatformSetupModal open={setupOpen} info={info} counts={counts} onClose={() => setSetupOpen(false)} />
    </div>
  );
}

function DeviceGroupStrip({
  counts,
  value,
  onChange,
}: {
  counts: DeviceSupportCounts;
  value: DeviceFilter;
  onChange: (filter: DeviceFilter) => void;
}) {
  const filters = DEVICE_FILTERS.filter((item) => item !== 'Unmanaged' || counts.Unmanaged > 0);

  return (
    <div role="group" aria-label="Filter devices by support" className="flex flex-wrap gap-2">
      {filters.map((item) => {
        const selected = item === value;
        const meta = FILTER_META[item];
        const count = counts[item];
        return (
          <button
            key={item}
            type="button"
            aria-pressed={selected}
            onClick={() => onChange(item)}
            className={`flex min-w-[13.5rem] flex-1 items-center justify-between gap-3 rounded-lg border px-3 py-2.5 text-left transition-colors ${
              selected
                ? 'border-violet-500 bg-violet-500/15'
                : 'border-zinc-800 bg-zinc-900/50 hover:border-zinc-700 hover:bg-zinc-900'
            }`}
          >
            <span>
              <span className={`block text-sm font-medium ${selected ? 'text-violet-50' : 'text-zinc-200'}`}>
                {meta.label}
              </span>
              <span className={`block text-xs ${selected ? 'text-violet-200/80' : 'text-zinc-500'}`}>{meta.hint}</span>
            </span>
            <span className={`text-xl font-semibold tabular-nums ${selected ? 'text-white' : 'text-zinc-100'}`}>
              {count}
              <span className="sr-only"> {count === 1 ? 'device' : 'devices'}</span>
            </span>
          </button>
        );
      })}
    </div>
  );
}
