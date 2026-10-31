import { clamp } from '../../lib/math.ts';

/** Applied duty as a bar, with a tick at the target duty. */
export function DutyMeter({ applied, target }: { applied: number; target: number }) {
  return (
    <div
      role="meter"
      aria-label="Applied duty"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(applied)}
      className="relative h-1.5 rounded-full bg-zinc-800"
    >
      <div
        className="h-full rounded-full bg-violet-500 transition-[width] duration-500"
        style={{ width: `${clamp(applied, 0, 100)}%` }}
      />
      <div
        aria-hidden="true"
        className="absolute top-1/2 h-3 w-0.5 -translate-x-1/2 -translate-y-1/2 rounded bg-zinc-200"
        style={{ left: `${clamp(target, 0, 100)}%` }}
      />
    </div>
  );
}
