import { useMemo, useState } from 'react';
import type { DeviceDto } from '../../api/contracts.ts';
import { Card } from '../../components/ui/Card.tsx';
import { useDaemonState, usePreviewStream, useSystem } from '../../state/hooks.ts';
import { LcdDeviceList } from './LcdDeviceList.tsx';
import { LcdPreview } from './LcdPreview.tsx';
import { LcdSettingsPanel } from './LcdSettingsPanel.tsx';

export function LcdPage() {
  usePreviewStream('Lcd');
  const { devices, profile } = useSystem();
  const { lcdPreview } = useDaemonState();
  const screens = devices.filter((device) => device.lcd !== null);
  const [deviceId, setDeviceId] = useState<string | null>(null);
  const selected = deviceId ? screens.find((screen) => screen.id === deviceId) : undefined;
  const resolvedId = selected ? selected.id : null;
  const previewDevice = resolvedId ? selected : screens[0];
  const overrideIds = useMemo(() => new Set(Object.keys(profile.displays ?? {})), [profile.displays]);

  return (
    <div className="space-y-2">
      <div className="grid gap-2 lg:grid-cols-[minmax(0,20rem)_minmax(0,1fr)]">
        <Card title="Live preview" description={describePreview(resolvedId !== null, previewDevice)}>
          <LcdPreview png={lcdPreview} lcd={previewDevice?.lcd ?? null} />
        </Card>
        <LcdSettingsPanel screens={screens} deviceId={resolvedId} onDeviceId={setDeviceId} />
      </div>
      <Card title="Screens">
        <LcdDeviceList devices={screens} selectedId={resolvedId} overrides={overrideIds} onSelect={setDeviceId} />
      </Card>
    </div>
  );
}

function describePreview(specific: boolean, device: DeviceDto | undefined): string {
  if (!device?.lcd) {
    return specific ? 'Screen unavailable' : 'No screen connected';
  }
  const shape = device.lcd.isCircular ? 'circular' : 'rectangular';
  const summary = `${device.name} · ${device.lcd.width} × ${device.lcd.height} · ${shape}`;
  return specific ? summary : `Global default · ${summary}`;
}
