import type { ReactNode } from 'react';
import { helpId } from './styles.ts';

interface FieldProps {
  label: ReactNode;
  htmlFor: string;
  hint?: ReactNode;
  error?: string;
  children: ReactNode;
}

/** Label, control and a hint or error line linked through `helpId(htmlFor)`. */
export function Field({ label, htmlFor, hint, error, children }: FieldProps) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="block text-sm text-zinc-300">
        {label}
      </label>
      {children}
      {error !== undefined ? (
        <p id={helpId(htmlFor)} className="text-xs text-red-400">
          {error}
        </p>
      ) : (
        hint !== undefined && (
          <p id={helpId(htmlFor)} className="text-xs text-zinc-500">
            {hint}
          </p>
        )
      )}
    </div>
  );
}
