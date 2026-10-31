import { useCallback, useMemo, useState, type KeyboardEvent } from 'react';
import type { VirtualCanvas } from '../../api/contracts.ts';
import { virtualCanvasSchema } from '../../api/schemas.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { GridIcon, UndoIcon } from '../../components/ui/icons.tsx';
import { roundTo } from '../../lib/math.ts';
import { firstError, toFieldErrors } from '../../lib/validation.ts';
import { indexZones, placementKey } from '../../lib/zones.ts';
import { useDaemonApi, useSystem } from '../../state/hooks.ts';
import { autoArrange, canvasEquals, patchPlacement, type PlacementPatch } from './layoutModel.ts';
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
    const parsed = virtualCanvasSchema.safeParse(canvas);
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
    const step = event.shiftKey ? 10 : 1;
    changePlacement(placementKey(selected), {
      x: roundTo(selected.x + nudge[0] * step, 2),
      y: roundTo(selected.y + nudge[1] * step, 2),
    });
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
      <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_17rem]">
        <div
          role="group"
          tabIndex={0}
          aria-label="Layout canvas. Arrow keys move the selected zone, Shift moves 10 units, Escape clears the selection."
          onKeyDown={handleKeyDown}
          className="rounded-lg"
        >
          <LayoutStage
            canvas={canvas}
            zones={zones}
            selectedKey={selectedKey}
            onSelect={setSelectedKey}
            onChange={changePlacement}
          />
        </div>
        <ZoneInspector
          placements={canvas.placements}
          zones={zones}
          selected={selected}
          onSelect={setSelectedKey}
          onChange={changePlacement}
        />
      </div>
    </Card>
  );
}
