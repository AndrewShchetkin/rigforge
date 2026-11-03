import { Fragment, memo, useState, type ReactNode } from 'react';
import type { DeviceCapability, DeviceColorCorrection, DeviceDto } from '../../api/contracts.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge, type BadgeTone } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { ColorField } from '../../components/ui/ColorField.tsx';
import { Slider } from '../../components/ui/Slider.tsx';
import { EMPTY_VALUE } from '../../lib/format.ts';
import { useDaemonApi } from '../../state/hooks.ts';

const CAPABILITIES: Record<DeviceCapability, { label: string; tone: BadgeTone }> = {
  Rgb: { label: 'RGB', tone: 'accent' },
  FanControl: { label: 'Fan control', tone: 'info' },
  Sensors: { label: 'Sensors', tone: 'success' },
  Lcd: { label: 'LCD', tone: 'warning' },
};

function capabilityBadge(capability: DeviceCapability): { label: string; tone: BadgeTone } {
  // The daemon serializes capabilities as plain strings; tolerate values this UI does not know yet.
  return CAPABILITIES[capability] ?? { label: capability, tone: 'neutral' };
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div>
      <h3 className="mb-2 text-xs font-semibold tracking-wide text-zinc-400 uppercase">{title}</h3>
      {children}
    </div>
  );
}

const cell = 'px-2 py-1.5 first:pl-0 last:pr-0';

export const DeviceCard = memo(function DeviceCard({
  device,
  colorCorrection,
}: {
  device: DeviceDto;
  colorCorrection?: DeviceColorCorrection;
}) {
  const properties = Object.entries(device.properties);

  return (
    <Card
      title={device.name}
      description={`${device.vendor} · ${device.id}`}
      actions={device.capabilities.map((capability) => {
        const { label, tone } = capabilityBadge(capability);
        return (
          <Badge key={capability} tone={tone}>
            {label}
          </Badge>
        );
      })}
    >
      <div className="space-y-5 text-sm">
        {properties.length > 0 && (
          <Section title="Properties">
            <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1">
              {properties.map(([key, value]) => (
                <Fragment key={key}>
                  <dt className="text-zinc-500">{key}</dt>
                  <dd className="break-words text-zinc-200">{value}</dd>
                </Fragment>
              ))}
            </dl>
          </Section>
        )}

        {device.zones.length > 0 && (
          <Section title="RGB zones">
            <div className="overflow-x-auto">
              <table className="w-full text-left">
                <thead className="text-xs text-zinc-500">
                  <tr>
                    <th className={`${cell} font-medium`}>Zone</th>
                    <th className={`${cell} text-right font-medium`}>LEDs</th>
                    <th className={`${cell} font-medium`}>Topology</th>
                    <th className={`${cell} font-medium`}>Resizable</th>
                  </tr>
                </thead>
                <tbody>
                  {device.zones.map((zone) => (
                    <tr key={zone.id} className="border-t border-zinc-800/60">
                      <td className={`${cell} text-zinc-200`}>{zone.name}</td>
                      <td className={`${cell} text-right tabular-nums`}>{zone.ledCount}</td>
                      <td className={`${cell} text-zinc-400`}>{zone.topology}</td>
                      <td className={`${cell} text-zinc-400`}>
                        {zone.isResizable ? `Yes, up to ${zone.maxLedCount}` : 'No'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Section>
        )}

        {device.capabilities.includes('Rgb') && (
          <ColorCorrectionEditor
            key={`${device.id}:${colorCorrection?.gamma ?? 1}:${colorCorrection?.whiteBalance ?? '#FFFFFF'}`}
            deviceId={device.id}
            saved={colorCorrection}
          />
        )}

        {device.fanChannels.length > 0 && (
          <Section title="Fan channels">
            <ul className="divide-y divide-zinc-800/60">
              {device.fanChannels.map((channel) => (
                <li key={channel.id} className="flex flex-wrap items-center justify-between gap-2 py-1.5">
                  <span className="flex items-center gap-2 text-zinc-200">
                    {channel.name}
                    <Badge tone={channel.kind === 'Pump' ? 'info' : 'neutral'}>{channel.kind}</Badge>
                  </span>
                  <span className="text-xs text-zinc-400">
                    Duty {channel.minDutyPercent}–{channel.maxDutyPercent} % · speed sensor{' '}
                    <span className="font-mono">{channel.speedSensorId ?? EMPTY_VALUE}</span>
                  </span>
                </li>
              ))}
            </ul>
          </Section>
        )}

        {device.lcd && (
          <Section title="LCD">
            <p className="text-zinc-300">
              {device.lcd.width} × {device.lcd.height} px · {device.lcd.isCircular ? 'circular' : 'rectangular'} · up to{' '}
              {device.lcd.maxFramesPerSecond} FPS
            </p>
          </Section>
        )}
      </div>
    </Card>
  );
});

function ColorCorrectionEditor({ deviceId, saved }: { deviceId: string; saved?: DeviceColorCorrection }) {
  const api = useDaemonApi();
  const toast = useToast();
  const [gamma, setGamma] = useState(saved?.gamma ?? 1);
  const [whiteBalance, setWhiteBalance] = useState(saved?.whiteBalance ?? '#FFFFFF');
  const [saving, setSaving] = useState(false);
  const savedGamma = saved?.gamma ?? 1;
  const savedBalance = (saved?.whiteBalance ?? '#FFFFFF').toUpperCase();
  const dirty = gamma !== savedGamma || whiteBalance.toUpperCase() !== savedBalance;
  const neutral = !saved && gamma === 1 && whiteBalance.toUpperCase() === '#FFFFFF';

  const save = async () => {
    setSaving(true);
    const result = await api.upsertColorCorrection({ deviceId, gamma, whiteBalance });
    setSaving(false);
    if (result) {
      toast.success('Color correction saved.');
    }
  };

  const reset = async () => {
    if (!saved) {
      setGamma(1);
      setWhiteBalance('#FFFFFF');
      return;
    }
    setSaving(true);
    const result = await api.removeColorCorrection(deviceId);
    setSaving(false);
    if (result) {
      toast.success('Color correction reset.');
    }
  };

  return (
    <Section title="Color correction">
      <div className="space-y-4">
        <Slider
          label="Gamma"
          min={0.5}
          max={3}
          step={0.05}
          value={gamma}
          format={(value) => value.toFixed(2)}
          onChange={setGamma}
        />
        <ColorField label="White balance" value={whiteBalance} onChange={setWhiteBalance} />
        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="primary" onClick={() => void save()} disabled={saving || !dirty}>
            Save
          </Button>
          <Button size="sm" onClick={() => void reset()} disabled={saving || neutral}>
            Reset
          </Button>
        </div>
      </div>
    </Section>
  );
}
