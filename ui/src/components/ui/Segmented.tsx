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
      <div className="inline-flex flex-wrap border border-line bg-base p-0.5">
        {options.map((option) => {
          const selected = option.value === value;
          return (
            <label
              key={String(option.value)}
              className={`cursor-pointer px-3 py-1 text-sm transition-colors ${selected ? 'bg-accent/15 text-accent' : 'text-dim hover:text-ink'}`}
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
