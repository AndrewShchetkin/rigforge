import { memo } from 'react';
import type { DeviceDto } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';

export const LcdDeviceList = memo(function LcdDeviceList({ devices }: { devices: readonly DeviceDto[] }) {
  if (devices.length === 0) {
    return (
      <EmptyState title="No LCD screen detected">
        The settings are still saved and apply as soon as a supported AIO screen is connected.
      </EmptyState>
    );
  }
  return (
    <ul className="divide-y divide-zinc-800">
      {devices.map((device) =>
        device.lcd ? (
          <li key={device.id} className="flex flex-wrap items-center justify-between gap-2 py-2 first:pt-0 last:pb-0">
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-zinc-100">{device.name}</p>
              <p className="text-xs text-zinc-500">{device.vendor}</p>
            </div>
            <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-400">
              <Badge>
                {device.lcd.width} × {device.lcd.height}
              </Badge>
              <Badge>{device.lcd.isCircular ? 'Circular' : 'Rectangular'}</Badge>
              <Badge>Up to {device.lcd.maxFramesPerSecond} FPS</Badge>
            </div>
          </li>
        ) : null,
      )}
    </ul>
  );
});
