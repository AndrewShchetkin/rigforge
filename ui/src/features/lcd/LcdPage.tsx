import { Card } from '../../components/ui/Card.tsx';
import { useDaemonState, usePreviewStream, useSystem } from '../../state/hooks.ts';
import { LcdDeviceList } from './LcdDeviceList.tsx';
import { LcdPreview } from './LcdPreview.tsx';
import { LcdSettingsPanel } from './LcdSettingsPanel.tsx';

export function LcdPage() {
  usePreviewStream('Lcd');
  const { devices } = useSystem();
  const { lcdPreview } = useDaemonState();
  const screens = devices.filter((device) => device.lcd !== null);
  const primaryScreen = screens[0]?.lcd ?? null;

  return (
    <div className="space-y-6">
      <div className="grid gap-6 lg:grid-cols-[minmax(0,20rem)_minmax(0,1fr)]">
        <Card title="Live preview" description={screens[0]?.name ?? 'No screen connected'}>
          <LcdPreview png={lcdPreview} lcd={primaryScreen} />
        </Card>
        <LcdSettingsPanel screens={screens} />
      </div>
      <Card title="Screens">
        <LcdDeviceList devices={screens} />
      </Card>
    </div>
  );
}
