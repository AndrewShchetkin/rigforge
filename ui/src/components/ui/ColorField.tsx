import { useId, type ReactNode } from 'react';
import { Field } from './Field.tsx';
import { helpId } from './styles.ts';

interface ColorFieldProps {
  label: ReactNode;
  /** "#RRGGBB" */
  value: string;
  onChange: (value: string) => void;
  hint?: ReactNode;
  error?: string;
}

export function ColorField({ label, value, onChange, hint, error }: ColorFieldProps) {
  const id = useId();
  return (
    <Field label={label} htmlFor={id} hint={hint} error={error}>
      <div className="flex items-center gap-3">
        <input
          id={id}
          type="color"
          value={value.toLowerCase()}
          onChange={(event) => onChange(event.currentTarget.value.toUpperCase())}
          aria-invalid={error !== undefined || undefined}
          aria-describedby={error !== undefined || hint !== undefined ? helpId(id) : undefined}
          className="h-9 w-14 cursor-pointer rounded-md border border-zinc-700 bg-zinc-950 p-1"
        />
        <span className="font-mono text-sm text-zinc-300">{value.toUpperCase()}</span>
      </div>
    </Field>
  );
}
