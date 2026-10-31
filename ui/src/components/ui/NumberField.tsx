import { useId, useState, type ReactNode } from 'react';
import { clamp, roundTo } from '../../lib/math.ts';
import { Field } from './Field.tsx';
import { controlClass, helpId } from './styles.ts';

interface NumberFieldProps {
  label: ReactNode;
  value: number;
  onChange: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  /** Decimals shown while the field is not being edited. */
  decimals?: number;
  suffix?: string;
  hint?: ReactNode;
  error?: string;
  disabled?: boolean;
}

/** Lets the user type freely; in-range values apply immediately, anything else is clamped on blur/Enter. */
export function NumberField({
  label,
  value,
  onChange,
  min = -Infinity,
  max = Infinity,
  step = 1,
  decimals = 0,
  suffix,
  hint,
  error,
  disabled,
}: NumberFieldProps) {
  const id = useId();
  const [text, setText] = useState<string | null>(null);

  const commit = (raw: string) => {
    setText(null);
    const parsed = Number.parseFloat(raw);
    if (!Number.isFinite(parsed)) {
      return;
    }
    const next = clamp(parsed, min, max);
    if (next !== value) {
      onChange(next);
    }
  };

  return (
    <Field label={label} htmlFor={id} hint={hint} error={error}>
      <div className="relative">
        <input
          id={id}
          type="number"
          inputMode="decimal"
          value={text ?? String(roundTo(value, decimals))}
          min={Number.isFinite(min) ? min : undefined}
          max={Number.isFinite(max) ? max : undefined}
          step={step}
          disabled={disabled}
          onChange={(event) => {
            const { value: raw, valueAsNumber } = event.currentTarget;
            setText(raw);
            if (Number.isFinite(valueAsNumber) && valueAsNumber >= min && valueAsNumber <= max) {
              onChange(valueAsNumber);
            }
          }}
          onBlur={(event) => commit(event.currentTarget.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') {
              commit(event.currentTarget.value);
            }
          }}
          aria-invalid={error !== undefined || undefined}
          aria-describedby={error !== undefined || hint !== undefined ? helpId(id) : undefined}
          className={`${controlClass} ${suffix ? 'pr-12' : ''}`}
        />
        {suffix && (
          <span
            aria-hidden="true"
            className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-xs text-zinc-500"
          >
            {suffix}
          </span>
        )}
      </div>
    </Field>
  );
}
