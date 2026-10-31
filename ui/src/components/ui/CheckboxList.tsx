import { useId, type ReactNode } from 'react';
import { helpId } from './styles.ts';

interface CheckboxListProps {
  legend: ReactNode;
  options: readonly { value: string; label: string }[];
  selected: readonly string[];
  onChange: (selected: string[]) => void;
  hint?: ReactNode;
  error?: string;
}

/** Multi-select as a scrollable list of checkboxes. */
export function CheckboxList({ legend, options, selected, onChange, hint, error }: CheckboxListProps) {
  const id = useId();
  const toggle = (value: string) =>
    onChange(selected.includes(value) ? selected.filter((item) => item !== value) : [...selected, value]);

  return (
    <fieldset aria-describedby={error !== undefined || hint !== undefined ? helpId(id) : undefined}>
      <legend className="mb-1.5 text-sm text-zinc-300">{legend}</legend>
      <div
        className={`max-h-44 space-y-0.5 overflow-y-auto rounded-md border bg-zinc-950/80 p-1.5 ${error === undefined ? 'border-zinc-700' : 'border-red-500/80'}`}
      >
        {options.map((option) => (
          <label
            key={option.value}
            className="flex cursor-pointer items-center gap-2 rounded px-1.5 py-1 text-sm text-zinc-200 hover:bg-zinc-800/60"
          >
            <input
              type="checkbox"
              checked={selected.includes(option.value)}
              onChange={() => toggle(option.value)}
              className="size-4 shrink-0 accent-violet-500"
            />
            <span className="min-w-0 truncate" title={option.label}>
              {option.label}
            </span>
          </label>
        ))}
      </div>
      {error !== undefined ? (
        <p id={helpId(id)} className="mt-1.5 text-xs text-red-400">
          {error}
        </p>
      ) : (
        hint !== undefined && (
          <p id={helpId(id)} className="mt-1.5 text-xs text-zinc-500">
            {hint}
          </p>
        )
      )}
    </fieldset>
  );
}
