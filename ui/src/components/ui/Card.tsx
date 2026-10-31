import { useId, type ReactNode } from 'react';

interface CardProps {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
}

export function Card({ title, description, actions, children, className = '' }: CardProps) {
  const headingId = useId();
  const hasHeader = title !== undefined || actions !== undefined;

  return (
    <section
      aria-labelledby={title === undefined ? undefined : headingId}
      className={`rounded-xl border border-zinc-800 bg-zinc-900/60 shadow-sm shadow-black/20 ${className}`}
    >
      {hasHeader && (
        <header className="flex flex-wrap items-start justify-between gap-3 border-b border-zinc-800 px-4 py-3 sm:px-5">
          <div className="min-w-0">
            {title !== undefined && (
              <h2 id={headingId} className="text-sm font-semibold text-zinc-100">
                {title}
              </h2>
            )}
            {description !== undefined && <p className="mt-0.5 text-xs text-zinc-400">{description}</p>}
          </div>
          {actions !== undefined && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
        </header>
      )}
      <div className="p-4 sm:p-5">{children}</div>
    </section>
  );
}
