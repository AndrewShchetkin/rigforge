import { memo, type ReactNode } from 'react';
import type {
  ColorPalette,
  EffectDescriptorDto,
  EffectParameterDefinitionDto,
  EffectParameterValue,
  EffectSettings,
  SensorSelector,
} from '../../api/contracts.ts';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { Segmented } from '../../components/ui/Segmented.tsx';
import { SelectField, type SelectOption } from '../../components/ui/SelectField.tsx';
import { Slider } from '../../components/ui/Slider.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { formatPercent } from '../../lib/format.ts';
import { roundTo } from '../../lib/math.ts';
import { withSelectedOptions, type SensorOption } from '../../lib/sensors.ts';
import type { FieldErrors } from '../../lib/validation.ts';
import { ColorListEditor } from './ColorListEditor.tsx';
import { EffectGallery } from './EffectGallery.tsx';
import { switchEffectPatch } from './effectModel.ts';

interface EffectFormProps {
  effects: readonly EffectDescriptorDto[];
  value: EffectSettings;
  errors: FieldErrors;
  sensorOptions: readonly SensorOption[];
  palettes: readonly ColorPalette[];
  onChange: (patch: Partial<EffectSettings>) => void;
  onSavePalette: (name: string, colors: readonly string[]) => Promise<boolean>;
  onRenamePalette: (palette: ColorPalette, name: string) => Promise<boolean>;
  onDeletePalette: (palette: ColorPalette) => Promise<boolean>;
}

function formatNumeric(value: number, parameter: EffectParameterDefinitionDto, integer = false): string {
  if (integer || parameter.unit === '°') {
    const shown = Math.round(value);
    if (parameter.unit === '°') {
      return `${shown}°`;
    }
    return parameter.unit ? `${shown} ${parameter.unit}` : String(shown);
  }
  if (parameter.unit === '×') {
    return `${value.toFixed(1)}×`;
  }
  return parameter.unit ? `${value} ${parameter.unit}` : String(value);
}

function numericValue(current: EffectParameterValue, parameter: EffectParameterDefinitionDto): number {
  if (typeof current === 'number' && Number.isFinite(current)) {
    return current;
  }
  if (typeof parameter.defaultValue === 'number' && Number.isFinite(parameter.defaultValue)) {
    return parameter.defaultValue;
  }
  return parameter.min ?? 0;
}

/** Effect parameters; which fields appear is driven by the effect descriptor's parameter schema. */
export const EffectForm = memo(function EffectForm({
  effects,
  value,
  errors,
  sensorOptions,
  palettes,
  onChange,
  onSavePalette,
  onRenamePalette,
  onDeletePalette,
}: EffectFormProps) {
  const descriptor = effects.find((effect) => effect.id === value.effectId);

  const setParameter = (key: string, parameterValue: EffectParameterValue) =>
    onChange({ parameters: { ...value.parameters, [key]: parameterValue } });

  const renderParameter = (parameter: EffectParameterDefinitionDto): ReactNode => {
    const current = value.parameters[parameter.key] ?? parameter.defaultValue;
    const error = errors[`parameters.${parameter.key}`];

    switch (parameter.kind) {
      case 'ColorList': {
        const colors = Array.isArray(current) ? current : [];
        return (
          <ColorListEditor
            key={parameter.key}
            colors={colors}
            min={parameter.minColors}
            max={parameter.maxColors}
            error={error}
            palettes={palettes}
            onChange={(next) => setParameter(parameter.key, next)}
            onSavePalette={onSavePalette}
            onRenamePalette={onRenamePalette}
            onDeletePalette={onDeletePalette}
          />
        );
      }
      case 'SensorSelector': {
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
      case 'Boolean':
        return (
          <div key={parameter.key} className="space-y-1.5">
            <Toggle label={parameter.label} checked={current === true} onChange={(checked) => setParameter(parameter.key, checked)} />
            {error !== undefined && <p className="text-xs text-red-400">{error}</p>}
          </div>
        );
      case 'Select':
        return (
          <SelectControl
            key={parameter.key}
            parameter={parameter}
            value={typeof current === 'string' ? current : ''}
            error={error}
            onChange={(next) => setParameter(parameter.key, next)}
          />
        );
      case 'IntRange':
      case 'FloatRange': {
        const integer = parameter.kind === 'IntRange';
        const numeric = numericValue(current, parameter);
        const min = parameter.min ?? 0;
        const max = parameter.max ?? 100;
        const step = parameter.step != null && parameter.step > 0 ? parameter.step : 1;
        if (!integer && parameter.unit === '°C') {
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
            format={(next) => formatNumeric(next, parameter, integer)}
            error={error}
            onChange={(next) => {
              if (integer) {
                const snapped = Math.round((next - min) / step) * step + min;
                const clamped = Math.min(max, Math.max(min, snapped));
                setParameter(parameter.key, Math.round(clamped));
                return;
              }
              setParameter(parameter.key, roundTo(next, 2));
            }}
          />
        );
      }
      default: {
        const unhandled: never = parameter.kind;
        return unhandled;
      }
    }
  };

  return (
    <div className="space-y-5">
      <EffectGallery
        effects={effects}
        value={value}
        error={errors.effectId}
        onSelect={(effectId) => {
          if (effectId === value.effectId) {
            return;
          }
          const next = effects.find((effect) => effect.id === effectId);
          if (next) {
            onChange(switchEffectPatch(value, next));
          }
        }}
      />
      {descriptor && <p className="text-xs text-zinc-500">{descriptor.description}</p>}
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

function SelectControl({
  parameter,
  value,
  error,
  onChange,
}: {
  parameter: EffectParameterDefinitionDto;
  value: string;
  error?: string;
  onChange: (value: string) => void;
}) {
  const options = parameter.options ?? [];
  const choices = options.map((option) => ({ value: option.value, label: option.label }));
  if (options.length > 0 && options.length <= 3) {
    return (
      <div className="space-y-1.5">
        <Segmented label={parameter.label} value={value} options={choices} onChange={onChange} />
        {error !== undefined && <p className="text-xs text-red-400">{error}</p>}
      </div>
    );
  }

  const known = choices.some((option) => option.value === value);
  const selectOptions = known ? choices : [{ value, label: value === '' ? 'Select…' : value }, ...choices];
  return (
    <SelectField
      label={parameter.label}
      value={value}
      options={selectOptions.length > 0 ? selectOptions : [{ value: '', label: 'No choices' }]}
      error={error}
      onChange={onChange}
    />
  );
}
