import type { ReactNode } from 'react';
import { PanelCorners, panelClass, panelTitleClass } from './PanelFrame.tsx';

interface CalloutProps {
  tone?: 'info' | 'warning';
  title?: ReactNode;
  children: ReactNode;
}

export function Callout({ tone = 'info', title, children }: CalloutProps) {
  const warning = tone === 'warning';
  return (
    <div data-panel className={`${panelClass} px-4 py-3 text-sm`}>
      <PanelCorners />
      {title !== undefined && <p className={`${panelTitleClass} ${warning ? 'text-warn' : 'text-dim'}`}>{title}</p>}
      <div className="pt-1 text-ink">{children}</div>
    </div>
  );
}
