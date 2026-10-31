import { memo } from 'react';
import { formatCelsius, formatPercent } from '../../lib/format.ts';
import { DUTY_TICKS, PLOT, TEMPERATURE_TICKS, VIEW, toX, toY } from './curveGeometry.ts';

/** Static grid, ticks and axis titles of the curve editor. */
export const CurveAxes = memo(function CurveAxes() {
  const bottom = PLOT.y + PLOT.height;
  const right = PLOT.x + PLOT.width;
  return (
    <g aria-hidden="true" className="text-[11px]">
      <rect x={PLOT.x} y={PLOT.y} width={PLOT.width} height={PLOT.height} className="fill-zinc-950/60 stroke-zinc-800" />
      {TEMPERATURE_TICKS.map((temperature) => (
        <g key={`t${temperature}`}>
          <line x1={toX(temperature)} x2={toX(temperature)} y1={PLOT.y} y2={bottom} className="stroke-zinc-800/80" />
          <text x={toX(temperature)} y={bottom + 16} textAnchor="middle" className="fill-zinc-500">
            {temperature}°
          </text>
        </g>
      ))}
      {DUTY_TICKS.map((duty) => (
        <g key={`d${duty}`}>
          <line x1={PLOT.x} x2={right} y1={toY(duty)} y2={toY(duty)} className="stroke-zinc-800/80" />
          <text x={PLOT.x - 8} y={toY(duty) + 4} textAnchor="end" className="fill-zinc-500">
            {duty}%
          </text>
        </g>
      ))}
      <text x={PLOT.x + PLOT.width / 2} y={VIEW.height - 4} textAnchor="middle" className="fill-zinc-500">
        Temperature (°C)
      </text>
    </g>
  );
});

interface DutyLineProps {
  duty: number;
  label: string;
  variant: 'floor' | 'fixed';
}

/** Horizontal marker: the minimum duty floor (dashed) or the fixed duty in Fixed mode. */
export function DutyLine({ duty, label, variant }: DutyLineProps) {
  const y = toY(duty);
  const fixed = variant === 'fixed';
  return (
    <g aria-hidden="true" pointerEvents="none" className="text-[11px]">
      <line
        x1={PLOT.x}
        x2={PLOT.x + PLOT.width}
        y1={y}
        y2={y}
        className={fixed ? 'stroke-amber-300' : 'stroke-zinc-500'}
        strokeWidth={fixed ? 2 : 1}
        strokeDasharray={fixed ? undefined : '4 4'}
      />
      <text x={PLOT.x + PLOT.width - 4} y={y - 5} textAnchor="end" className={fixed ? 'fill-amber-300' : 'fill-zinc-500'}>
        {label}
      </text>
    </g>
  );
}

/** Current operating point reported by the daemon (temperature, applied duty). */
export function LiveMarker({ temperature, duty }: { temperature: number; duty: number }) {
  const x = toX(temperature);
  const y = toY(duty);
  const labelOnLeft = x > PLOT.x + PLOT.width * 0.7;
  return (
    <g aria-hidden="true" pointerEvents="none">
      <line x1={x} x2={x} y1={PLOT.y} y2={PLOT.y + PLOT.height} className="stroke-emerald-400/40" strokeDasharray="3 3" />
      <circle cx={x} cy={y} r={10} className="fill-emerald-400/15" />
      <circle cx={x} cy={y} r={4.5} strokeWidth={1.5} className="fill-emerald-400 stroke-zinc-950" />
      <text
        x={labelOnLeft ? x - 10 : x + 10}
        y={y - 10}
        textAnchor={labelOnLeft ? 'end' : 'start'}
        className="fill-emerald-300 text-[11px] font-medium"
      >
        {`${formatCelsius(temperature)} · ${formatPercent(duty)}`}
      </text>
    </g>
  );
}
