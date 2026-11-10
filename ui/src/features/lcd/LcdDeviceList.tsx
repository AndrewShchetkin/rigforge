import { memo } from 'react';
import type { DeviceDto } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';

export const LcdDeviceList = memo(function LcdDeviceList({
  devices,
  selectedId,
  overrides,
  onSelect,
}: {
  devices: readonly DeviceDto[];
  selectedId: string | null;
  overrides: ReadonlySet<string>;
  onSelect: (deviceId: string) => void;
}) {
  if (devices.length === 0) {
    return (
      <EmptyState title="No LCD screen detected">
        The settings are still saved and apply as soon as a supported AIO screen is connected.
      </EmptyState>
    );
  }
  return (
    <ul className="space-y-1">
      {devices.map((device) =>
        device.lcd ? (
          <li key={device.id}>
            <button
              type="button"
              aria-pressed={selectedId === device.id}
              onClick={() => onSelect(device.id)}
              className={`flex w-full flex-wrap items-center justify-between gap-2 rounded-md px-2 py-2 text-left transition-colors focus-visible:outline-2 focus-visible:outline-violet-400 ${
                selectedId === device.id ? 'bg-violet-500/10 ring-1 ring-violet-500/40' : 'hover:bg-zinc-800/60'
              }`}
            >
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-zinc-100">{device.name}</p>
                <p className="text-xs text-zinc-500">{device.vendor}</p>
              </div>
              <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-400">
                <Badge tone={overrides.has(device.id) ? 'accent' : 'neutral'}>
                  {overrides.has(device.id) ? 'Custom override' : 'Using global default'}
                </Badge>
                <Badge>
                  {device.lcd.width} × {device.lcd.height}
                </Badge>
                <Badge>{device.lcd.isCircular ? 'Circular' : 'Rectangular'}</Badge>
                <Badge>Up to {device.lcd.maxFramesPerSecond} FPS</Badge>
              </div>
            </button>
          </li>
        ) : null,
      )}
    </ul>
  );
});
