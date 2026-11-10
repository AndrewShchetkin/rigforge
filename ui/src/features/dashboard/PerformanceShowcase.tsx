import type { ReactNode } from 'react';
import type { EffectRenderMode, PerformanceMetricsDto } from '../../api/contracts.ts';
import { Badge, type BadgeTone } from '../../components/ui/Badge.tsx';
import {
  EMPTY_VALUE,
  WEBVIEW_SUITE_RAM_MB,
  formatCpuPercent,
  formatFrameTime,
  formatMegabytes,
  formatMegabytesPrecise,
  formatRamMultiplier,
} from '../../lib/format.ts';
import { usePerformance } from '../../state/hooks.ts';

function modeBadge(mode: EffectRenderMode): { label: string; tone: BadgeTone } {
  switch (mode) {
    case 'Sleeping':
      return { label: 'Sleeping · 0% CPU', tone: 'success' };
    case 'Blackout':
      return { label: 'Blackout · 0% CPU', tone: 'warning' };
    case 'DirectSampling':
      return { label: 'Direct LED Sampling', tone: 'accent' };
    case 'CanvasRaster':
      return { label: 'Canvas raster', tone: 'info' };
  }
}

function formatFps(fps: number): string {
  if (!Number.isFinite(fps)) {
    return EMPTY_VALUE;
  }
  const shown = fps >= 10 ? Math.round(fps) : Math.round(fps * 10) / 10;
  return `${shown} FPS`;
}

/** Live engine footprint, frame time, and a labelled comparison against typical WebView RGB suites. */
export function PerformanceShowcase() {
  const metrics = usePerformance();
  const sampling = metrics?.renderMode === 'DirectSampling' || metrics?.renderMode === 'CanvasRaster';
  const badge = metrics ? modeBadge(metrics.renderMode) : null;

  return (
    <section
      aria-labelledby="engine-efficiency-heading"
      className="relative overflow-hidden rounded-2xl border border-violet-500/25 bg-zinc-900/80 shadow-lg shadow-black/20"
    >
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_at_top_left,rgba(139,92,246,0.18),transparent_52%),radial-gradient(ellipse_at_bottom_right,rgba(16,185,129,0.12),transparent_48%)]"
      />
      <div className="relative space-y-5 p-4 sm:p-5">
        <header className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <p className="text-[11px] font-semibold tracking-[0.16em] text-violet-300 uppercase">Engine efficiency</p>
            <h2 id="engine-efficiency-heading" className="mt-1 text-base font-semibold text-zinc-50">
              Zero-alloc lighting telemetry
            </h2>
            <p className="mt-1 max-w-2xl text-xs text-zinc-400">
              Direct LED sampling on a dedicated thread. Static scenes and blackout sleep at 0% CPU, with no per-frame
              allocations.
            </p>
          </div>
          <span className="inline-flex items-center gap-1.5 text-xs text-zinc-400">
            <span
              aria-hidden="true"
              className={`size-1.5 rounded-full ${sampling ? 'bg-violet-400 shadow-[0_0_8px] shadow-violet-400 motion-safe:animate-pulse' : 'bg-emerald-400'}`}
            />
            {metrics ? metrics.renderMode : 'Waiting for a sample'}
          </span>
        </header>
        <dl className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Readout
            label="Daemon private RAM"
            value={metrics ? formatMegabytes(metrics.privateBytes) : EMPTY_VALUE}
            detail={metrics ? `${formatMegabytesPrecise(metrics.managedHeapBytes)} C# heap` : 'Managed heap'}
          />
          <Readout
            label="Daemon CPU usage"
            value={metrics ? formatCpuPercent(metrics.cpuPercent) : EMPTY_VALUE}
            detail={badge ? <Badge tone={badge.tone}>{badge.label}</Badge> : 'Render mode'}
          />
          <Readout
            label="Frame compute time"
            value={metrics ? `${formatFrameTime(metrics.frameTimeMicroseconds)} / frame` : EMPTY_VALUE}
            detail={metrics ? formatFps(metrics.effectFps) : 'Effect FPS'}
          />
          <Readout
            label="Active lighting scale"
            value={metrics ? `${metrics.totalControlledLeds} LEDs` : EMPTY_VALUE}
            detail={
              metrics
                ? `${metrics.activeRgbDevices} RGB ${metrics.activeRgbDevices === 1 ? 'controller' : 'controllers'} · GC ${metrics.gen0Collections}/${metrics.gen1Collections}/${metrics.gen2Collections}${metrics.gen2Collections === 0 ? ' · Stable' : ''}`
                : 'Devices and GC'
            }
          />
        </dl>
        <FootprintComparison metrics={metrics} />
      </div>
    </section>
  );
}

function Readout({ label, value, detail }: { label: string; value: string; detail: ReactNode }) {
  return (
    <div className="rounded-xl border border-white/5 bg-zinc-950/50 px-3.5 py-3">
      <dt className="text-[11px] tracking-wide text-zinc-500 uppercase">{label}</dt>
      <dd className="mt-1 text-xl font-semibold text-zinc-50 tabular-nums">{value}</dd>
      <div className="mt-1 truncate text-xs text-zinc-400">{detail}</div>
    </div>
  );
}

function FootprintComparison({ metrics }: { metrics: PerformanceMetricsDto | null }) {
  const megabytes = metrics ? metrics.privateBytes / (1024 * 1024) : 0;
  const rigPercent = Math.min(100, Math.max(megabytes > 0 ? 3 : 0, (megabytes / WEBVIEW_SUITE_RAM_MB.high) * 100));
  const bandStart = (WEBVIEW_SUITE_RAM_MB.low / WEBVIEW_SUITE_RAM_MB.high) * 100;
  const advantage = metrics ? formatRamMultiplier(metrics.privateBytes) : 'Waiting for private bytes';

  return (
    <div className="rounded-xl border border-white/5 bg-zinc-950/40 px-3.5 py-3">
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-medium text-zinc-100">Footprint</h3>
        <p className="text-xs font-medium text-emerald-200 tabular-nums">{advantage}</p>
      </div>
      <div className="space-y-3">
        <BarRow
          label="RigForge native engine"
          value={metrics ? `~${formatMegabytes(metrics.privateBytes)}` : EMPTY_VALUE}
          start={0}
          width={rigPercent}
          className="bg-emerald-400"
        />
        <BarRow
          label="Typical WebView RGB suites"
          value={`~${WEBVIEW_SUITE_RAM_MB.low}–${WEBVIEW_SUITE_RAM_MB.high} MB`}
          start={bandStart}
          width={100 - bandStart}
          className="bg-zinc-500"
        />
      </div>
      <p className="mt-3 text-[11px] leading-relaxed text-zinc-500">
        The green bar is this daemon&apos;s private bytes. The grey band is the usual {WEBVIEW_SUITE_RAM_MB.low}–
        {WEBVIEW_SUITE_RAM_MB.high} MB working set of a Chromium-based RGB suite. The multiplier compares live private RAM
        with a {WEBVIEW_SUITE_RAM_MB.typical} MB point inside that band, and RigForge does not start a Chromium process.
      </p>
    </div>
  );
}

function BarRow({
  label,
  value,
  start,
  width,
  className,
}: {
  label: string;
  value: string;
  start: number;
  width: number;
  className: string;
}) {
  return (
    <div>
      <div className="mb-1 flex items-baseline justify-between gap-3 text-xs">
        <span className="text-zinc-300">{label}</span>
        <span className="text-zinc-400 tabular-nums">{value}</span>
      </div>
      <div className="relative h-2 overflow-hidden rounded-full bg-zinc-800" aria-hidden="true">
        <div className={`absolute inset-y-0 rounded-full ${className}`} style={{ left: `${start}%`, width: `${width}%` }} />
      </div>
    </div>
  );
}
