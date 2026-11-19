import { memo, useEffect, useId, useRef, useState } from 'react';
import type { DaemonInfoDto, EffectSettings, PerformanceMetricsDto } from '../../api/contracts.ts';
import type { ConnectionStatus } from '../../api/daemonClient.ts';
import { formatCpuPercent, formatFrameTime, formatMegabytes } from '../../lib/format.ts';
import { isLightingEnabled } from '../../features/lighting/effectModel.ts';
import { useDaemonApi, usePerformance, useSystemSnapshot } from '../../state/hooks.ts';
import { ConnectionBadge } from './ConnectionBadge.tsx';
import { pageHref } from './navigation.ts';

interface HeaderProps {
  title: string;
  connection: ConnectionStatus;
  info: DaemonInfoDto | null;
}

const BRIGHTNESS_DELAY_MS = 200;

export const Header = memo(function Header({ title, connection, info }: HeaderProps) {
  const system = useSystemSnapshot();
  const metrics = usePerformance();
  const effect = system?.profile.effect;

  return (
    <header data-panel className="relative flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line px-3 py-2 text-xs md:px-4">
      <h1 className="text-dim">{title}</h1>
      <span aria-hidden="true" className="text-line">
        │
      </span>
      <div className="flex flex-1 flex-wrap items-center justify-end gap-2 text-xs text-zinc-400">
        {effect && (
          <div className="flex items-center gap-1 border border-line bg-panel p-0.5">
            <LightingPowerButton effect={effect} />
            <BrightnessControl effect={effect} />
          </div>
        )}
        <PerformancePill metrics={metrics} />
        {info && (
          <dl className="flex flex-wrap items-center gap-x-4 gap-y-1">
            <div className="flex gap-1.5">
              <dt>Daemon</dt>
              <dd className="font-medium text-zinc-200">v{info.version}</dd>
            </div>
            <div className="hidden gap-1.5 sm:flex">
              <dt>Platform</dt>
              <dd className="font-medium text-zinc-200">{info.platform}</dd>
            </div>
          </dl>
        )}
        <ConnectionBadge status={connection} />
      </div>
    </header>
  );
});

function LightingPowerButton({ effect }: { effect: EffectSettings }) {
  const api = useDaemonApi();
  const on = isLightingEnabled(effect);

  return (
    <button
      type="button"
      aria-pressed={on}
      aria-label={on ? 'Lighting on' : 'Lighting blackout'}
      onClick={() => {
        void api.updateEffect({ ...effect, enabled: !on });
      }}
      className={`inline-flex h-7 items-center gap-1.5 px-2 text-xs ${on ? 'text-accent hover:bg-accent/10' : 'text-warn hover:bg-warn/10'}`}
    >
      <span
        aria-hidden="true"
        className={`size-2 ${on ? 'bg-ok' : 'bg-warn'}`}
      />
      {on ? 'Lighting' : 'Blackout'}
    </button>
  );
}

function BrightnessControl({ effect }: { effect: EffectSettings }) {
  const api = useDaemonApi();
  const effectRef = useRef(effect);
  const rootRef = useRef<HTMLDivElement>(null);
  const timerRef = useRef<number | null>(null);
  const sliderId = useId();
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<number | null>(null);
  const percent = draft ?? Math.round(effect.brightness * 100);

  useEffect(() => {
    effectRef.current = effect;
  }, [effect]);

  useEffect(
    () => () => {
      if (timerRef.current !== null) {
        window.clearTimeout(timerRef.current);
      }
    },
    [],
  );

  useEffect(() => {
    if (!open) {
      return;
    }
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target;
      if (target instanceof Node && !rootRef.current?.contains(target)) {
        setOpen(false);
      }
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  const commit = (nextPercent: number) => {
    setDraft(nextPercent);
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
    }
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null;
      const current = effectRef.current;
      void api.updateEffect({
        ...current,
        enabled: isLightingEnabled(current),
        brightness: nextPercent / 100,
      });
    }, BRIGHTNESS_DELAY_MS);
  };

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        aria-expanded={open}
        aria-controls={sliderId}
        onClick={() => {
          setOpen((current) => {
            if (current) {
              setDraft(null);
            }
            return !current;
          });
        }}
        className="inline-flex h-7 items-center px-2 text-xs text-ink tabular-nums hover:bg-white/5"
      >
        {percent}%
      </button>
      {open && (
        <div
          id={sliderId}
          className="absolute top-full right-0 z-30 mt-2 w-56 border border-line bg-panel p-3"
        >
          <div className="mb-2 flex items-baseline justify-between gap-3 text-xs">
            <label htmlFor={`${sliderId}-range`} className="text-zinc-300">
              Master brightness
            </label>
            <output htmlFor={`${sliderId}-range`} className="text-zinc-400 tabular-nums">
              {percent}%
            </output>
          </div>
          <input
            id={`${sliderId}-range`}
            type="range"
            min={0}
            max={100}
            step={1}
            value={percent}
            aria-valuetext={`${percent}%`}
            onChange={(event) => commit(event.currentTarget.valueAsNumber)}
            className="h-2 w-full cursor-pointer"
          />
        </div>
      )}
    </div>
  );
}

function PerformancePill({ metrics }: { metrics: PerformanceMetricsDto | null }) {
  const idle = metrics?.renderMode === 'Sleeping' || metrics?.renderMode === 'Blackout';
  const frame = !metrics ? '—' : idle ? '0% Idle' : formatFrameTime(metrics.frameTimeMicroseconds);
  const cpu = metrics ? `${formatCpuPercent(metrics.cpuPercent)} CPU` : '— CPU';
  const ram = metrics ? formatMegabytes(metrics.privateBytes) : '—';
  const tone = idle ? 'text-warn' : 'text-ok';

  return (
    <a
      href={pageHref('dashboard')}
      title={metrics ? `${cpu}, ${frame}, ${ram} private, ${metrics.renderMode}` : 'Open the performance dashboard'}
      className={`inline-flex flex-wrap items-center gap-x-2 gap-y-0.5 tabular-nums ${metrics ? tone : 'text-dim'}`}
    >
      <span
        aria-hidden="true"
        className={`size-1.5 ${idle ? 'bg-warn' : 'bg-ok'}`}
      />
      <span>{cpu}</span>
      <span aria-hidden="true" className="opacity-40">
        ·
      </span>
      <span>{frame}</span>
      <span aria-hidden="true" className="opacity-40">
        ·
      </span>
      <span>{ram}</span>
    </a>
  );
}
