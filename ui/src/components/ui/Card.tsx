import { useId, type ReactNode } from 'react';
import { PanelCorners, panelClass, panelTitleClass } from './PanelFrame.tsx';

interface CardProps {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}

export function Card({ title, description, actions, children, className = '' }: CardProps) {
  const headingId = useId();
  const hasMeta = description !== undefined || actions !== undefined;

  return (
    <section
      data-panel
      aria-labelledby={title === undefined ? undefined : headingId}
      className={`${panelClass} ${className}`}
    >
      <PanelCorners />
      {title !== undefined && (
        <h2 id={headingId} className={panelTitleClass}>
          {title}
        </h2>
      )}
      <div className="p-3 pt-4">
        {hasMeta && (
          <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
            {description !== undefined && <p className="min-w-0 text-xs text-dim">{description}</p>}
            {actions !== undefined && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
          </div>
        )}
        {children}
      </div>
    </section>
  );
}
