import type { ReactNode } from 'react';

export type BadgeTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'info';

const TONES: Record<BadgeTone, string> = {
  neutral: 'border-line text-dim',
  accent: 'border-accent/50 text-accent',
  success: 'border-ok/50 text-ok',
  warning: 'border-warn/50 text-warn',
  danger: 'border-bad/50 text-bad',
  info: 'border-line text-ink',
};

interface BadgeProps {
  tone?: BadgeTone;
  children: ReactNode;
}

export function Badge({ tone = 'neutral', children }: BadgeProps) {
  return (
    <span className={`inline-flex items-center gap-1 border px-1.5 py-0.5 text-xs whitespace-nowrap ${TONES[tone]}`}>
      {children}
    </span>
  );
}
