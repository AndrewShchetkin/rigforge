import { memo } from 'react';
import type { MeterTone } from './meters.ts';
import { PanelCorners, panelClass, panelTitleClass } from '../../components/ui/PanelFrame.tsx';
import type { Stat } from './stats.ts';

const TONE: Record<MeterTone, string> = {
  ok: 'text-ok',
  warn: 'text-warn',
  bad: 'text-bad',
  neutral: 'text-ink',
};

export const StatCards = memo(function StatCards({ stats }: { stats: readonly Stat[] }) {
  return (
    <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-7">
      {stats.map((stat) => (
        <li key={stat.role} data-panel className={`${panelClass} min-w-0 px-2.5 pt-4 pb-2`}>
          <PanelCorners />
          <h3 className={`${panelTitleClass} max-w-[calc(100%-1rem)] truncate`}>{stat.label}</h3>
          <p className={`text-base font-medium tabular-nums ${TONE[stat.tone]}`}>{stat.value}</p>
          <p className="mt-1 tracking-wider" aria-hidden="true">
            <span className={TONE[stat.tone]}>{stat.filled}</span>
            <span className="text-dim">{stat.empty}</span>
          </p>
          <p className="mt-1 truncate text-[10px] text-dim" title={stat.source ?? undefined}>
            {stat.source ?? 'No sensor with this role'}
          </p>
        </li>
      ))}
    </ul>
  );
});
