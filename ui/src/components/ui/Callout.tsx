import type { ReactNode } from 'react';
import { InfoIcon, WarningIcon } from './icons.tsx';

interface CalloutProps {
  tone?: 'info' | 'warning';
  title?: ReactNode;
  children: ReactNode;
}

export function Callout({ tone = 'info', title, children }: CalloutProps) {
  const warning = tone === 'warning';
  return (
    <div
      className={`flex gap-3 rounded-lg border px-4 py-3 text-sm ${warning ? 'border-amber-500/40 bg-amber-500/10 text-amber-100' : 'border-sky-500/30 bg-sky-500/10 text-sky-100'}`}
    >
      {warning ? (
        <WarningIcon className="mt-0.5 size-4 shrink-0 text-amber-300" />
      ) : (
        <InfoIcon className="mt-0.5 size-4 shrink-0 text-sky-300" />
      )}
      <div className="min-w-0 space-y-1">
        {title !== undefined && <p className="font-medium">{title}</p>}
        <div className="opacity-90">{children}</div>
      </div>
    </div>
  );
}
