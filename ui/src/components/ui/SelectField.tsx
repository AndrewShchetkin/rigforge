import { useId, type ReactNode } from 'react';
import { Field } from './Field.tsx';
import { controlClass, helpId } from './styles.ts';

export interface SelectOption<T extends string> {
  value: T;
  label: string;
}

interface SelectFieldProps<T extends string> {
  label: ReactNode;
  value: T;
  options: readonly SelectOption<T>[];
  onChange: (value: T) => void;
  hint?: ReactNode;
  error?: string;
  disabled?: boolean;
}

export function SelectField<T extends string>({
  label,
  value,
  options,
  onChange,
  hint,
  error,
  disabled,
}: SelectFieldProps<T>) {
  const id = useId();
  return (
    <Field label={label} htmlFor={id} hint={hint} error={error}>
      <select
        id={id}
        value={value}
        disabled={disabled}
        onChange={(event) => {
          const selected = options.find((option) => option.value === event.currentTarget.value);
          if (selected) {
            onChange(selected.value);
          }
        }}
        aria-invalid={error !== undefined || undefined}
        aria-describedby={error !== undefined || hint !== undefined ? helpId(id) : undefined}
        className={controlClass}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value} className="bg-zinc-900">
            {option.label}
          </option>
        ))}
      </select>
    </Field>
  );
}
