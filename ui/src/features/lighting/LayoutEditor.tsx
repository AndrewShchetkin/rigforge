import { useCallback, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import type { VirtualCanvas } from '../../api/contracts.ts';
import { virtualCanvasSchema } from '../../api/schemas.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { GridIcon, UndoIcon } from '../../components/ui/icons.tsx';
import { firstError, toFieldErrors } from '../../lib/validation.ts';
import { indexZones, placementKey, zoneKey } from '../../lib/zones.ts';
import { useDaemonApi, useSystem } from '../../state/hooks.ts';
import {
  CANVAS_PRESETS,
  FIT_VIEW,
  MAX_ZOOM,
  MIN_ZOOM,
  SNAP_OPTIONS,
  ZOOM_STEP,
  addSegment,
  autoArrange,
  canvasEquals,
  matchingCanvasPreset,
  nudgeStep,
  patchPlacement,
  removeSegment,
  resizeCanvas,
  snapPlacement,
  snapValue,
  zoomViewAt,
  type CanvasPreset,
  type CanvasView,
  type PlacementPatch,
  type SnapStep,
  type StageMetrics,
} from './layoutModel.ts';
import { LayoutStage } from './LayoutStage.tsx';
import { ZoneInspector } from './ZoneInspector.tsx';

const NUDGES: Readonly<Record<string, readonly [number, number]>> = {
  ArrowLeft: [-1, 0],
  ArrowRight: [1, 0],
  ArrowUp: [0, -1],
  ArrowDown: [0, 1],
};

/** Owns the local layout draft (dirty until saved), the selection and the save/reset/auto-arrange actions. */
export function LayoutEditor() {
  const { devices, effectiveCanvas, profile } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const [draft, setDraft] = useState<VirtualCanvas | null>(null);
  const [selectedKey, setSelectedKey] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [snap, setSnap] = useState<SnapStep>(5);
  const [view, setView] = useState<CanvasView>(FIT_VIEW);
  const metricsRef = useRef<StageMetrics>({ baseScale: 1, width: 0, height: 0 });

  const canvas = draft ?? effectiveCanvas;
  const zones = useMemo(() => indexZones(devices), [devices]);
  const dirty = draft !== null && !canvasEquals(draft, effectiveCanvas);
  const hasUnsavedAutoPlacements = !canvasEquals(effectiveCanvas, profile.canvas);
  const selected = canvas.placements.find((placement) => placementKey(placement) === selectedKey) ?? null;

  const changePlacement = useCallback(
    (key: string, patch: PlacementPatch) => setDraft((current) => patchPlacement(current ?? effectiveCanvas, key, patch)),
    [effectiveCanvas],
  );

  const save = async () => {
    const maxLedCounts = new Map<string, number>();
    for (const [key, zoneRef] of zones) {
      maxLedCounts.set(key, zoneRef.zone.maxLedCount);
    }
    const parsed = virtualCanvasSchema(maxLedCounts).safeParse(canvas);
    if (!parsed.success) {
      toast.error(`Cannot save the layout: ${firstError(toFieldErrors(parsed.error)) ?? 'it is invalid'}.`);
      return;
    }
    setSaving(true);
    const result = await api.updateCanvas(parsed.data);
    setSaving(false);
    if (result) {
      setDraft(null);
      toast.success('Layout saved.');
    }
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') {
      setSelectedKey(null);
      return;
    }
    const nudge = NUDGES[event.key];
    if (!selected || !nudge) {
      return;
    }
    event.preventDefault();
    const step = nudgeStep(snap, event.shiftKey);
    const dx = nudge[0] ?? 0;
    const dy = nudge[1] ?? 0;
    const placed = snapPlacement(
      selected,
      {
        x: snapValue(selected.x, snap) + dx * step,
        y: snapValue(selected.y, snap) + dy * step,
      },
      canvas.width,
      canvas.height,
      snap,
    );
    changePlacement(placementKey(selected), { x: placed.x, y: placed.y });
  };

  const addSelectedSegment = () => {
    if (!selected) {
      return;
    }
    const zone = zones.get(zoneKey(selected.deviceId, selected.zoneId))?.zone;
    if (!zone?.isResizable) {
      return;
    }
    const result = addSegment(canvas, placementKey(selected), zone.ledCount, zone.maxLedCount);
    if (!result) {
      toast.error('This header has no free LEDs to split off.');
      return;
    }
    setDraft(result.canvas);
    setSelectedKey(result.selectKey);
  };

  const removeSelectedSegment = () => {
    if (!selected) {
      return;
    }
    const result = removeSegment(canvas, placementKey(selected));
    if (!result) {
      return;
    }
    setDraft(result.canvas);
    setSelectedKey(result.selectKey);
  };

  const zoomTo = (zoom: number) => {
    setView((current) => {
      const metrics = metricsRef.current;
      return zoomViewAt(
        current,
        metrics.baseScale > 0 ? metrics.baseScale : 1,
        metrics.width / 2,
        metrics.height / 2,
        zoom,
      );
    });
  };

  const applyPreset = (preset: CanvasPreset) => {
    setDraft(resizeCanvas(canvas, preset.width, preset.height));
    setView(FIT_VIEW);
  };

  return (
    <Card
      title="Spatial layout"
      description={`Canvas ${canvas.width} × ${canvas.height} · ${canvas.placements.length} zones`}
      actions={
        <>
          {dirty && <Badge tone="warning">Unsaved changes</Badge>}
          {!dirty && hasUnsavedAutoPlacements && <Badge tone="info">Auto-placed zones not saved</Badge>}
          <Button size="sm" onClick={() => setDraft(autoArrange(canvas))} disabled={canvas.placements.length === 0}>
            <GridIcon className="size-3.5" />
            Auto arrange
          </Button>
          <Button size="sm" onClick={() => setDraft(null)} disabled={!dirty || saving}>
            <UndoIcon className="size-3.5" />
            Reset
          </Button>
          <Button
            size="sm"
            variant="primary"
            onClick={() => void save()}
            disabled={saving || !(dirty || hasUnsavedAutoPlacements)}
          >
            Save layout
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <CanvasToolbar snap={snap} onSnap={setSnap} view={view} onZoom={zoomTo} onFit={() => setView(FIT_VIEW)} canvas={canvas} onResize={applyPreset} />
        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_17rem]">
          <div
            role="group"
            tabIndex={0}
            aria-label="Layout canvas. Arrow keys move the selected zone. Shift uses a larger step. Escape clears the selection. Ctrl or Command and the wheel zooms toward the pointer. Middle-drag, or Alt-drag on the background, pans."
            onKeyDown={handleKeyDown}
            className="rounded-lg"
          >
            <LayoutStage
              canvas={canvas}
              zones={zones}
              selectedKey={selectedKey}
              view={view}
              snap={snap}
              metricsRef={metricsRef}
              onSelect={setSelectedKey}
              onChange={changePlacement}
              onViewChange={setView}
            />
          </div>
          <ZoneInspector
            placements={canvas.placements}
            zones={zones}
            selected={selected}
            snap={snap}
            onSelect={setSelectedKey}
            onChange={changePlacement}
            onAddSegment={addSelectedSegment}
            onRemoveSegment={removeSelectedSegment}
          />
        </div>
      </div>
    </Card>
  );
}

function CanvasToolbar({
  snap,
  onSnap,
  view,
  onZoom,
  onFit,
  canvas,
  onResize,
}: {
  snap: SnapStep;
  onSnap: (snap: SnapStep) => void;
  view: CanvasView;
  onZoom: (zoom: number) => void;
  onFit: () => void;
  canvas: VirtualCanvas;
  onResize: (preset: CanvasPreset) => void;
}) {
  const sizeId = useId();
  const matched = matchingCanvasPreset(canvas.width, canvas.height);
  const fitted = view.zoom === 1 && view.panX === 0 && view.panY === 0;
  const zoomLabel = fitted ? '100%' : `${Math.round(view.zoom * 100)}% · Fit`;

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border border-zinc-800 bg-zinc-950/50 px-2 py-1.5">
      <div role="group" aria-label="Snap to grid" className="inline-flex items-center gap-1">
        <span className="px-1 text-xs text-zinc-400">Snap</span>
        <div className="inline-flex rounded-md border border-zinc-700 bg-zinc-950/80 p-0.5">
          {SNAP_OPTIONS.map((option) => {
            const selected = snap === option.value;
            return (
              <button
                key={option.value}
                type="button"
                aria-pressed={selected}
                onClick={() => onSnap(option.value)}
                className={`rounded px-2 py-1 text-xs font-medium transition-colors focus-visible:outline-2 focus-visible:outline-violet-400 ${selected ? 'bg-violet-600 text-white' : 'text-zinc-300 hover:bg-zinc-800'}`}
              >
                {option.label}
              </button>
            );
          })}
        </div>
      </div>
      <span aria-hidden className="hidden h-5 w-px bg-zinc-800 sm:inline-block" />
      <div role="group" aria-label="Zoom" className="inline-flex items-center overflow-hidden rounded-md border border-zinc-700 bg-zinc-950/80">
        <button
          type="button"
          aria-label="Zoom out"
          disabled={view.zoom <= MIN_ZOOM}
          onClick={() => onZoom(view.zoom - ZOOM_STEP)}
          className="h-7 min-w-7 px-2 text-xs font-medium text-zinc-200 hover:bg-zinc-800 focus-visible:outline-2 focus-visible:outline-violet-400 disabled:cursor-not-allowed disabled:opacity-40"
        >
          −
        </button>
        <button
          type="button"
          aria-label="Fit canvas at 100%"
          title="Fit canvas"
          onClick={onFit}
          className="h-7 min-w-16 border-x border-zinc-800 px-2 text-xs font-medium text-zinc-100 tabular-nums hover:bg-zinc-800 focus-visible:outline-2 focus-visible:outline-violet-400"
        >
          {zoomLabel}
        </button>
        <button
          type="button"
          aria-label="Zoom in"
          disabled={view.zoom >= MAX_ZOOM}
          onClick={() => onZoom(view.zoom + ZOOM_STEP)}
          className="h-7 min-w-7 px-2 text-xs font-medium text-zinc-200 hover:bg-zinc-800 focus-visible:outline-2 focus-visible:outline-violet-400 disabled:cursor-not-allowed disabled:opacity-40"
        >
          +
        </button>
      </div>
      <label htmlFor={sizeId} className="inline-flex items-center gap-2 text-xs text-zinc-400 sm:ml-auto">
        Canvas
        <select
          id={sizeId}
          aria-label="Canvas size"
          value={matched?.id ?? 'custom'}
          onChange={(event) => {
            const preset = CANVAS_PRESETS.find((item) => item.id === event.currentTarget.value);
            if (preset) {
              onResize(preset);
            }
          }}
          className="rounded-md border border-zinc-700 bg-zinc-950/80 px-2 py-1 text-xs text-zinc-100 hover:border-zinc-600 focus:border-violet-500"
        >
          {matched === undefined && (
            <option value="custom">
              {canvas.width} × {canvas.height} (Custom)
            </option>
          )}
          {CANVAS_PRESETS.map((preset) => (
            <option key={preset.id} value={preset.id}>
              {preset.label}
            </option>
          ))}
        </select>
      </label>
    </div>
  );
}
