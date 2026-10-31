import { useId, type ReactNode } from 'react';
import { clamp } from '../../lib/math.ts';
import { helpId } from './styles.ts';

interface SliderProps {
  label: ReactNode;
  value: number;
  min: number;
  max: number;
  step?: number;
  format?: (value: number) => string;
  hint?: ReactNode;
  error?: string;
  disabled?: boolean;
  onChange: (value: number) => void;
}

export function Slider({
  label,
  value,
  min,
  max,
  step = 1,
  format = String,
  hint,
  error,
  disabled,
  onChange,
}: SliderProps) {
  const id = useId();
  const valueText = format(value);

  return (
    <div className="space-y-1.5">
      <div className="flex items-baseline justify-between gap-3 text-sm">
        <label htmlFor={id} className="text-zinc-300">
          {label}
        </label>
        <output htmlFor={id} className="text-zinc-400">
          {valueText}
        </output>
      </div>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={clamp(value, min, max)}
        disabled={disabled}
        onChange={(event) => onChange(event.currentTarget.valueAsNumber)}
        aria-valuetext={valueText}
        aria-invalid={error !== undefined || undefined}
        aria-describedby={error !== undefined || hint !== undefined ? helpId(id) : undefined}
        className="h-2 w-full cursor-pointer accent-violet-500 disabled:cursor-not-allowed disabled:opacity-50"
      />
      {error !== undefined ? (
        <p id={helpId(id)} className="text-xs text-red-400">
          {error}
        </p>
      ) : (
        hint !== undefined && (
          <p id={helpId(id)} className="text-xs text-zinc-500">
            {hint}
          </p>
        )
      )}
    </div>
  );
}
