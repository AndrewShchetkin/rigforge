import { useId, type ReactNode } from 'react';

interface SegmentedOption<T extends string | number> {
  value: T;
  label: string;
}

interface SegmentedProps<T extends string | number> {
  label: ReactNode;
  value: T;
  options: readonly SegmentedOption<T>[];
  onChange: (value: T) => void;
  disabled?: boolean;
}

/** Radio group styled as a segmented control (arrow keys move between options natively). */
export function Segmented<T extends string | number>({ label, value, options, onChange, disabled }: SegmentedProps<T>) {
  const name = useId();
  return (
    <fieldset disabled={disabled} className="min-w-0 disabled:opacity-50">
      <legend className="mb-1.5 text-sm text-zinc-300">{label}</legend>
      <div className="inline-flex flex-wrap rounded-md border border-zinc-700 bg-zinc-950/80 p-0.5">
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <label
              key={String(option.value)}
              className={`cursor-pointer rounded px-3 py-1 text-sm transition-colors has-focus-visible:outline-2 has-focus-visible:outline-violet-400 ${selected ? 'bg-violet-600 text-white' : 'text-zinc-300 hover:bg-zinc-800'}`}
            >
              <input
                type="radio"
                name={name}
                className="sr-only"
                checked={selected}
                onChange={() => onChange(option.value)}
              />
              {option.label}
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
