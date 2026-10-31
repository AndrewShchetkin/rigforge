import type { ReactNode } from 'react';

export function EmptyState({ title, children }: { title: string; children?: ReactNode }) {
  return (
    <div className="rounded-lg border border-dashed border-zinc-700 px-4 py-8 text-center">
      <p className="text-sm font-medium text-zinc-200">{title}</p>
      {children !== undefined && <div className="mt-1 text-sm text-zinc-400">{children}</div>}
    </div>
  );
}
