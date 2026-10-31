import { memo } from 'react';
import type { EffectDescriptorDto, EffectSettings, SensorSelector } from '../../api/contracts.ts';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { SelectField, type SelectOption } from '../../components/ui/SelectField.tsx';
import { Slider } from '../../components/ui/Slider.tsx';
import { formatPercent } from '../../lib/format.ts';
import { roundTo } from '../../lib/math.ts';
import { withSelectedOptions, type SensorOption } from '../../lib/sensors.ts';
import type { FieldErrors } from '../../lib/validation.ts';
import { ColorListEditor } from './ColorListEditor.tsx';
import { switchEffectPatch } from './effectModel.ts';

interface EffectFormProps {
  effects: readonly EffectDescriptorDto[];
  value: EffectSettings;
  errors: FieldErrors;
  sensorOptions: readonly SensorOption[];
  onChange: (patch: Partial<EffectSettings>) => void;
}

/** Effect parameters; which fields appear is driven by the effect descriptor's flags. */
export const EffectForm = memo(function EffectForm({ effects, value, errors, sensorOptions, onChange }: EffectFormProps) {
  const descriptor = effects.find((effect) => effect.id === value.effectId);
  const effectOptions: SelectOption<string>[] = effects.map((effect) => ({ value: effect.id, label: effect.name }));
  if (!descriptor) {
    effectOptions.unshift({ value: value.effectId, label: `${value.effectId} (not available)` });
  }
  const sensorSelectOptions: readonly SelectOption<SensorSelector>[] = [
    { value: '', label: 'Select a sensor…' },
    ...withSelectedOptions(sensorOptions, value.sensorSelector === null ? [] : [value.sensorSelector]),
  ];

  return (
    <div className="space-y-5">
      <SelectField
        label="Effect"
        value={value.effectId}
        options={effectOptions}
        hint={descriptor?.description}
        error={errors.effectId}
        onChange={(effectId) => {
          const next = effects.find((effect) => effect.id === effectId);
          if (next) {
            onChange(switchEffectPatch(value, next));
          }
        }}
      />
      <Slider
        label="Brightness"
        value={Math.round(value.brightness * 100)}
        min={0}
        max={100}
        format={formatPercent}
        error={errors.brightness}
        onChange={(percent) => onChange({ brightness: percent / 100 })}
      />
      {descriptor?.usesSpeed && (
        <Slider
          label="Speed"
          value={value.speed}
          min={0.1}
          max={5}
          step={0.1}
          format={(speed) => `${speed.toFixed(1)}×`}
          error={errors.speed}
          onChange={(speed) => onChange({ speed: roundTo(speed, 2) })}
        />
      )}
      {descriptor?.usesColors && (
        <ColorListEditor
          colors={value.colors}
          min={descriptor.minColors}
          max={descriptor.maxColors}
          error={errors.colors}
          onChange={(colors) => onChange({ colors })}
        />
      )}
      {descriptor?.usesAngle && (
        <Slider
          label="Direction"
          value={value.angle}
          min={0}
          max={359}
          format={(angle) => `${Math.round(angle)}°`}
          error={errors.angle}
          onChange={(angle) => onChange({ angle })}
        />
      )}
      {descriptor?.usesSensor && (
        <div className="space-y-4 rounded-lg border border-zinc-800 p-3">
          <SelectField
            label="Sensor"
            value={value.sensorSelector ?? ''}
            options={sensorSelectOptions}
            error={errors.sensorSelector}
            onChange={(selector) => onChange({ sensorSelector: selector === '' ? null : selector })}
          />
          <div className="grid grid-cols-2 gap-3">
            <NumberField
              label="Cold at"
              value={value.temperatureLow}
              min={0}
              max={120}
              suffix="°C"
              error={errors.temperatureLow}
              onChange={(temperatureLow) => onChange({ temperatureLow })}
            />
            <NumberField
              label="Hot at"
              value={value.temperatureHigh}
              min={0}
              max={120}
              suffix="°C"
              error={errors.temperatureHigh}
              onChange={(temperatureHigh) => onChange({ temperatureHigh })}
            />
          </div>
        </div>
      )}
      {descriptor?.isAnimated && (
        <Slider
          label="Frame rate"
          value={value.targetFps}
          min={1}
          max={60}
          format={(fps) => `${Math.round(fps)} FPS`}
          error={errors.targetFps}
          onChange={(fps) => onChange({ targetFps: Math.round(fps) })}
        />
      )}
    </div>
  );
});
