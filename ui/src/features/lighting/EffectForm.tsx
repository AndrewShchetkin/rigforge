import { memo, type ReactNode } from 'react';
import type {
  EffectDescriptorDto,
  EffectParameterDefinitionDto,
  EffectParameterValue,
  EffectSettings,
  SensorSelector,
} from '../../api/contracts.ts';
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

function formatFloat(value: number, parameter: EffectParameterDefinitionDto): string {
  if (parameter.unit === '°') {
    return `${Math.round(value)}°`;
  }
  if (parameter.unit === '×') {
    return `${value.toFixed(1)}×`;
  }
  return parameter.unit ? `${value} ${parameter.unit}` : String(value);
}

/** Effect parameters; which fields appear is driven by the effect descriptor's parameter schema. */
export const EffectForm = memo(function EffectForm({ effects, value, errors, sensorOptions, onChange }: EffectFormProps) {
  const descriptor = effects.find((effect) => effect.id === value.effectId);
  const effectOptions: SelectOption<string>[] = effects.map((effect) => ({ value: effect.id, label: effect.name }));
  if (!descriptor) {
    effectOptions.unshift({ value: value.effectId, label: `${value.effectId} (not available)` });
  }

  const setParameter = (key: string, parameterValue: EffectParameterValue) =>
    onChange({ parameters: { ...value.parameters, [key]: parameterValue } });

  const renderParameter = (parameter: EffectParameterDefinitionDto): ReactNode => {
    const current = value.parameters[parameter.key] ?? parameter.defaultValue;
    const error = errors[`parameters.${parameter.key}`];

    if (parameter.kind === 'ColorList') {
      const colors = Array.isArray(current) ? current : [];
      return (
        <ColorListEditor
          key={parameter.key}
          colors={colors}
          min={parameter.minColors}
          max={parameter.maxColors}
          error={error}
          onChange={(colors) => setParameter(parameter.key, colors)}
        />
      );
    }

    if (parameter.kind === 'SensorSelector') {
      const selected = typeof current === 'string' ? current : '';
      const options: readonly SelectOption<SensorSelector>[] = [
        { value: '', label: 'Select a sensor…' },
        ...withSelectedOptions(sensorOptions, selected === '' ? [] : [selected]),
      ];
      return (
        <SelectField
          key={parameter.key}
          label={parameter.label}
          value={selected}
          options={options}
          error={error}
          onChange={(selector) => setParameter(parameter.key, selector)}
        />
      );
    }

    // FloatRange
    const numeric = typeof current === 'number' ? current : 0;
    const min = parameter.min ?? 0;
    const max = parameter.max ?? 100;
    const step = parameter.step ?? 1;
    if (parameter.unit === '°C') {
      return (
        <NumberField
          key={parameter.key}
          label={parameter.label}
          value={numeric}
          min={min}
          max={max}
          step={step}
          suffix="°C"
          error={error}
          onChange={(next) => setParameter(parameter.key, next)}
        />
      );
    }

    return (
      <Slider
        key={parameter.key}
        label={parameter.label}
        value={numeric}
        min={min}
        max={max}
        step={step}
        format={(next) => formatFloat(next, parameter)}
        error={error}
        onChange={(next) => setParameter(parameter.key, roundTo(next, 2))}
      />
    );
  };

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
      {descriptor?.parameters.map(renderParameter)}
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
