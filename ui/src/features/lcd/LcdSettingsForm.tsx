import { memo } from 'react';
import type { LcdMode, LcdSettings, SensorSelector } from '../../api/contracts.ts';
import { LCD_ROTATIONS } from '../../api/enums.ts';
import { ColorField } from '../../components/ui/ColorField.tsx';
import { Segmented } from '../../components/ui/Segmented.tsx';
import { SelectField, type SelectOption } from '../../components/ui/SelectField.tsx';
import { Slider } from '../../components/ui/Slider.tsx';
import { formatPercent } from '../../lib/format.ts';
import { roundTo } from '../../lib/math.ts';
import { withSelectedOptions, type SensorOption } from '../../lib/sensors.ts';
import type { FieldErrors } from '../../lib/validation.ts';

const MODE_OPTIONS: readonly SelectOption<LcdMode>[] = [
  { value: 'Dashboard', label: 'Dashboard: gauge plus two values' },
  { value: 'SingleSensor', label: 'Single sensor with an arc gauge' },
  { value: 'Graph', label: 'Graph of the primary sensor' },
  { value: 'BuiltInLiquid', label: 'Built-in liquid screen (firmware)' },
];

const ROTATION_OPTIONS = LCD_ROTATIONS.map((rotation) => ({ value: rotation, label: `${rotation}°` }));

const SLOTS = ['Primary sensor', 'Second sensor', 'Third sensor'] as const;

const MODE_HINTS: Record<LcdMode, string> = {
  Dashboard: 'The primary sensor drives the gauge; the other two are shown below it.',
  SingleSensor: 'Only the primary sensor is shown in this mode.',
  Graph: 'Only the primary sensor is graphed in this mode.',
  BuiltInLiquid: 'The firmware screen shows the liquid temperature by itself.',
};

interface LcdSettingsFormProps {
  value: LcdSettings;
  errors: FieldErrors;
  sensorOptions: readonly SensorOption[];
  /** Lowest frame-rate limit of the connected screens, if any. */
  deviceMaxFps: number | null;
  onChange: (patch: Partial<LcdSettings>) => void;
}

export const LcdSettingsForm = memo(function LcdSettingsForm({
  value,
  errors,
  sensorOptions,
  deviceMaxFps,
  onChange,
}: LcdSettingsFormProps) {
  const firmware = value.mode === 'BuiltInLiquid';
  const slotOptions: readonly SelectOption<SensorSelector>[] = [
    { value: '', label: 'None' },
    ...withSelectedOptions(sensorOptions, value.sensorSelectors),
  ];

  const setSlot = (slot: number, selector: SensorSelector) => {
    const slots = SLOTS.map((_, index) => value.sensorSelectors[index] ?? '');
    slots[slot] = selector;
    onChange({ sensorSelectors: slots.filter((entry) => entry !== '') });
  };

  return (
    <div className="grid gap-x-6 gap-y-5 sm:grid-cols-2">
      <SelectField label="Mode" value={value.mode} options={MODE_OPTIONS} onChange={(mode) => onChange({ mode })} />
      <Segmented
        label="Rotation"
        value={value.rotation}
        options={ROTATION_OPTIONS}
        onChange={(rotation) => onChange({ rotation })}
      />
      <Slider
        label="Brightness"
        value={value.brightness}
        min={0}
        max={100}
        format={formatPercent}
        error={errors.brightness}
        onChange={(brightness) => onChange({ brightness: Math.round(brightness) })}
      />
      <Slider
        label="Frame rate"
        value={value.framesPerSecond}
        min={0.2}
        max={10}
        step={0.1}
        format={(fps) => `${fps.toFixed(1)} FPS`}
        hint={deviceMaxFps !== null && deviceMaxFps < 10 ? `The connected screen accepts up to ${deviceMaxFps} FPS.` : undefined}
        error={errors.framesPerSecond}
        onChange={(fps) => onChange({ framesPerSecond: roundTo(fps, 1) })}
      />
      <ColorField
        label="Accent color"
        value={value.accentColor}
        error={errors.accentColor}
        onChange={(accentColor) => onChange({ accentColor })}
      />

      <fieldset disabled={firmware} className="space-y-3 sm:col-span-2 disabled:opacity-60">
        <legend className="text-sm font-medium text-zinc-200">Sensors</legend>
        <p className="text-xs text-zinc-500">{MODE_HINTS[value.mode]}</p>
        <div className="grid gap-3 sm:grid-cols-3">
          {SLOTS.map((slotLabel, slot) => (
            <SelectField
              key={slotLabel}
              label={slotLabel}
              value={value.sensorSelectors[slot] ?? ''}
              options={slotOptions}
              disabled={slot > value.sensorSelectors.length}
              onChange={(selector) => setSlot(slot, selector)}
            />
          ))}
        </div>
        {errors.sensorSelectors !== undefined && <p className="text-xs text-red-400">{errors.sensorSelectors}</p>}
      </fieldset>
    </div>
  );
});
