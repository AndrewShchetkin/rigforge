import { memo } from 'react';
import type { Stat } from './stats.ts';

export const StatCards = memo(function StatCards({ stats }: { stats: readonly Stat[] }) {
  return (
    <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-7">
      {stats.map((stat) => (
        <li key={stat.role} className="rounded-xl border border-zinc-800 bg-zinc-900/60 px-4 py-3">
          <p className="text-xs text-zinc-400">{stat.label}</p>
          <p className="mt-1 text-2xl font-semibold text-zinc-50 tabular-nums">{stat.value}</p>
          <p className="mt-0.5 truncate text-xs text-zinc-500" title={stat.source ?? undefined}>
            {stat.source ?? 'No sensor with this role'}
          </p>
        </li>
      ))}
    </ul>
  );
});
