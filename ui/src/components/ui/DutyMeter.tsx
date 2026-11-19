import { healthTone, blockBar, type MeterTone } from '../../features/dashboard/meters.ts';

const TONE: Record<MeterTone, string> = {
  ok: 'text-ok',
  warn: 'text-warn',
  bad: 'text-bad',
  neutral: 'text-ink',
};

/** Applied duty as a block bar. The accessible value includes the target. */
export function DutyMeter({ applied, target }: { applied: number; target: number }) {
  const tone = healthTone(applied);
  const bar = blockBar(applied / 100);
  return (
    <div
      role="meter"
      aria-label="Applied duty"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(applied)}
      aria-valuetext={`${Math.round(applied)}%, target ${Math.round(target)}%`}
      className={`tracking-wider ${TONE[tone]}`}
    >
      <span>{bar.filled}</span>
      <span className="text-dim">{bar.empty}</span>
    </div>
  );
}
