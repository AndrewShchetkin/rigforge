import { useId, type ReactNode } from 'react';
import { helpId } from './styles.ts';

interface ToggleProps {
  label: ReactNode;
  checked: boolean;
  onChange: (checked: boolean) => void;
  description?: ReactNode;
  disabled?: boolean;
}

/** Accessible switch (`role="switch"`), labelled by its visible label. */
export function Toggle({ label, checked, onChange, description, disabled }: ToggleProps) {
  const id = useId();
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0">
        <label htmlFor={id} className="text-sm text-zinc-200">
          {label}
        </label>
        {description !== undefined && (
          <p id={helpId(id)} className="text-xs text-zinc-500">
            {description}
          </p>
        )}
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-describedby={description !== undefined ? helpId(id) : undefined}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative inline-flex h-4 w-8 shrink-0 items-center border border-line transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${checked ? 'bg-accent/20' : 'bg-base'}`}
      >
        <span
          aria-hidden="true"
          className={`inline-block size-3 transition-transform ${checked ? 'translate-x-4 bg-accent' : 'translate-x-0.5 bg-dim'}`}
        />
      </button>
    </div>
  );
}
