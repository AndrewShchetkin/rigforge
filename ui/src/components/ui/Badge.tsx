import type { ReactNode } from 'react';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const TONES: Record<BadgeTone, string> = {
  neutral: 'border-zinc-700 bg-zinc-800/80 text-zinc-300',
  accent: 'border-violet-500/40 bg-violet-500/15 text-violet-200',
  success: 'border-emerald-500/40 bg-emerald-500/15 text-emerald-200',
  warning: 'border-amber-500/40 bg-amber-500/15 text-amber-200',
  danger: 'border-red-500/40 bg-red-500/15 text-red-200',
  info: 'border-sky-500/40 bg-sky-500/15 text-sky-200',
};

interface BadgeProps {
  tone?: BadgeTone;
  children: ReactNode;
}

export function Badge({ tone = 'neutral', children }: BadgeProps) {
  return (
    <span
      className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs font-medium whitespace-nowrap ${TONES[tone]}`}
    >
      {children}
    </span>
  );
}
