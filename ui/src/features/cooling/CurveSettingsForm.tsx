import { memo } from 'react';
import type { FanChannelDto, FanControlMode, FanCurve } from '../../api/contracts.ts';
import { CheckboxList } from '../../components/ui/CheckboxList.tsx';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { Segmented } from '../../components/ui/Segmented.tsx';
import { Slider } from '../../components/ui/Slider.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { formatPercent } from '../../lib/format.ts';
import { withSelectedOptions, type SensorOption } from '../../lib/sensors.ts';
import type { FieldErrors } from '../../lib/validation.ts';

const MODE_OPTIONS: readonly { value: FanControlMode; label: string }[] = [
  { value: 'Curve', label: 'Curve' },
  { value: 'Fixed', label: 'Fixed' },
];

function formatRate(value: number): string {
  return value === 0 ? 'Immediate' : `${Math.round(value)} %/s`;
}

interface CurveSettingsFormProps {
  value: FanCurve;
  channel: FanChannelDto;
  errors: FieldErrors;
  sensorOptions: readonly SensorOption[];
  onChange: (patch: Partial<FanCurve>) => void;
}

export const CurveSettingsForm = memo(function CurveSettingsForm({
  value,
  channel,
  errors,
  sensorOptions,
  onChange,
}: CurveSettingsFormProps) {
  const floor = channel.kind === 'Pump' ? channel.minDutyPercent : 0;

  return (
    <div className="grid gap-x-6 gap-y-5 sm:grid-cols-2">
      <div className="sm:col-span-2">
        <Toggle
          label="Control enabled"
          description="When off, the curve is kept but RigForge does not drive this channel."
          checked={value.enabled}
          onChange={(enabled) => onChange({ enabled })}
        />
      </div>
      <Segmented label="Mode" value={value.mode} options={MODE_OPTIONS} onChange={(mode) => onChange({ mode })} />
      {value.mode === 'Fixed' ? (
        <Slider
          label="Fixed duty"
          value={value.fixedDuty}
          min={floor}
          max={100}
          format={formatPercent}
          error={errors.fixedDuty}
          onChange={(fixedDuty) => onChange({ fixedDuty: Math.round(fixedDuty) })}
        />
      ) : (
        <div aria-hidden="true" />
      )}
      <div className="sm:col-span-2">
        <CheckboxList
          legend="Temperature sources"
          hint="The hottest selected source drives the curve."
          options={withSelectedOptions(sensorOptions, value.sensorSelectors)}
          selected={value.sensorSelectors}
          error={errors.sensorSelectors}
          onChange={(sensorSelectors) => onChange({ sensorSelectors })}
        />
      </div>
      <Slider
        label="Hysteresis"
        value={value.hysteresisCelsius}
        min={0}
        max={20}
        step={0.5}
        format={(hysteresis) => `${hysteresis.toFixed(1)} °C`}
        hint="Cool down this much before the duty is lowered."
        error={errors.hysteresisCelsius}
        onChange={(hysteresisCelsius) => onChange({ hysteresisCelsius })}
      />
      <Slider
        label="Temperature smoothing"
        value={value.temperatureSmoothingSeconds}
        min={0}
        max={30}
        step={0.5}
        format={(seconds) => (seconds === 0 ? 'Off' : `${seconds.toFixed(1)} s`)}
        error={errors.temperatureSmoothingSeconds}
        onChange={(temperatureSmoothingSeconds) => onChange({ temperatureSmoothingSeconds })}
      />
      <Slider
        label="Ramp up"
        value={value.rampUpPercentPerSecond}
        min={0}
        max={100}
        format={formatRate}
        error={errors.rampUpPercentPerSecond}
        onChange={(rampUpPercentPerSecond) => onChange({ rampUpPercentPerSecond })}
      />
      <Slider
        label="Ramp down"
        value={value.rampDownPercentPerSecond}
        min={0}
        max={100}
        format={formatRate}
        error={errors.rampDownPercentPerSecond}
        onChange={(rampDownPercentPerSecond) => onChange({ rampDownPercentPerSecond })}
      />
      <NumberField
        label="Min duty"
        value={value.minDuty}
        min={0}
        max={100}
        suffix="%"
        hint={channel.kind === 'Pump' ? `This pump needs at least ${channel.minDutyPercent} %.` : undefined}
        error={errors.minDuty}
        onChange={(minDuty) => onChange({ minDuty: Math.round(minDuty) })}
      />
      <NumberField
        label="Max duty"
        value={value.maxDuty}
        min={0}
        max={100}
        suffix="%"
        error={errors.maxDuty}
        onChange={(maxDuty) => onChange({ maxDuty: Math.round(maxDuty) })}
      />
    </div>
  );
});
