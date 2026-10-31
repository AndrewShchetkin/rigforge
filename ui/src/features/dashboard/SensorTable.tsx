import { memo, useMemo } from 'react';
import type { DeviceDto, SensorReadingDto } from '../../api/contracts.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { EMPTY_VALUE, formatReading } from '../../lib/format.ts';
import { sensorDeviceId } from '../../lib/sensors.ts';

interface SensorGroup {
  deviceId: string;
  label: string;
  readings: SensorReadingDto[];
}

function groupByDevice(readings: readonly SensorReadingDto[], devices: readonly DeviceDto[]): SensorGroup[] {
  const names = new Map(devices.map((device) => [device.id, device.name]));
  const groups = new Map<string, SensorGroup>();
  for (const reading of readings) {
    const deviceId = sensorDeviceId(reading.id);
    let group = groups.get(deviceId);
    if (!group) {
      group = { deviceId, label: names.get(deviceId) ?? deviceId, readings: [] };
      groups.set(deviceId, group);
    }
    group.readings.push(reading);
  }
  return [...groups.values()].sort((a, b) => a.label.localeCompare(b.label));
}

interface SensorTableProps {
  readings: readonly SensorReadingDto[];
  devices: readonly DeviceDto[];
}

export const SensorTable = memo(function SensorTable({ readings, devices }: SensorTableProps) {
  const groups = useMemo(() => groupByDevice(readings, devices), [readings, devices]);

  if (readings.length === 0) {
    return <EmptyState title="No sensor readings yet" />;
  }

  return (
    <div className="-mx-4 overflow-x-auto sm:-mx-5">
      <table className="w-full min-w-[32rem] text-sm">
        <thead className="text-left text-xs text-zinc-500">
          <tr>
            <th scope="col" className="px-4 py-2 font-medium sm:px-5">
              Sensor
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Kind
            </th>
            <th scope="col" className="px-3 py-2 font-medium">
              Role
            </th>
            <th scope="col" className="px-4 py-2 text-right font-medium sm:px-5">
              Value
            </th>
          </tr>
        </thead>
        {groups.map((group) => (
          <tbody key={group.deviceId} className="border-t border-zinc-800">
            <tr>
              <th
                scope="rowgroup"
                colSpan={4}
                className="bg-zinc-900/80 px-4 py-1.5 text-left text-xs font-semibold text-zinc-300 sm:px-5"
              >
                {group.label}
              </th>
            </tr>
            {group.readings.map((reading) => (
              <tr key={reading.id} className="border-t border-zinc-800/60">
                <td className="px-4 py-1.5 sm:px-5">
                  <span className="text-zinc-200">{reading.name}</span>
                  <span className="block font-mono text-[11px] text-zinc-500">{reading.id}</span>
                </td>
                <td className="px-3 py-1.5 text-zinc-400">{reading.kind}</td>
                <td className="px-3 py-1.5">
                  {reading.role === 'None' ? (
                    <span className="text-zinc-600">{EMPTY_VALUE}</span>
                  ) : (
                    <Badge tone="accent">{reading.role}</Badge>
                  )}
                </td>
                <td className="px-4 py-1.5 text-right font-medium text-zinc-100 tabular-nums sm:px-5">
                  {formatReading(reading)}
                </td>
              </tr>
            ))}
          </tbody>
        ))}
      </table>
    </div>
  );
});
