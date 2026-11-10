import { useId, useState } from 'react';
import type { LedTopology, ZonePlacement } from '../../api/contracts.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Button } from '../../components/ui/Button.tsx';
import { Field } from '../../components/ui/Field.tsx';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { SelectField } from '../../components/ui/SelectField.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { controlClass } from '../../components/ui/styles.ts';
import { clamp } from '../../lib/math.ts';
import { placementKey, zoneKey, type ZoneRef } from '../../lib/zones.ts';
import { useDaemonApi } from '../../state/hooks.ts';
import { MIN_ZONE_SIZE, normalizeRotation, type PlacementPatch, type SnapStep } from './layoutModel.ts';

const TOPOLOGY_VALUES: readonly LedTopology[] = ['Point', 'Linear', 'Ring', 'Matrix2D', 'Composite'];

interface ZoneInspectorProps {
  placements: readonly ZonePlacement[];
  zones: ReadonlyMap<string, ZoneRef>;
  selected: ZonePlacement | null;
  snap: SnapStep;
  onSelect: (key: string | null) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
  onAddSegment: () => void;
  onRemoveSegment: () => void;
}

/** Side panel: zone picker (keyboard alternative to clicking the canvas) and numeric placement fields. */
export function ZoneInspector({
  placements,
  zones,
  selected,
  snap,
  onSelect,
  onChange,
  onAddSegment,
  onRemoveSegment,
}: ZoneInspectorProps) {
  const api = useDaemonApi();
  const toast = useToast();
  const [identifying, setIdentifying] = useState(false);
  const selectedKey = selected ? placementKey(selected) : '';
  const ref = selected ? zones.get(zoneKey(selected.deviceId, selected.zoneId)) : undefined;
  const selectedZone = selected ? zoneKey(selected.deviceId, selected.zoneId) : null;
  const siblings = selectedZone
    ? placements.filter((item) => zoneKey(item.deviceId, item.zoneId) === selectedZone)
    : [];
  const zoneLedCount = ref?.zone.ledCount ?? 0;
  const headerMax = ref?.zone.maxLedCount ?? 1;
  const usedByOthers = siblings.reduce((sum, item) => {
    if (placementKey(item) === selectedKey) {
      return sum;
    }
    return sum + claimedLedCount(item.ledCount, siblings.length, zoneLedCount);
  }, 0);
  const remaining = headerMax - usedByOthers;
  const ledFieldMax = Math.max(1, remaining);
  const options = [
    { value: '', label: 'No zone selected' },
    ...placements.map((placement) => {
      const key = placementKey(placement);
      const zone = zones.get(zoneKey(placement.deviceId, placement.zoneId));
      const label = placement.name || zone?.zone.name || placement.zoneId;
      return { value: key, label: zone ? `${zone.device.name} · ${label}` : `${key} (not detected)` };
    }),
  ];

  const blink = async () => {
    if (!selected || identifying) {
      return;
    }
    const deviceId = selected.deviceId;
    const zoneId = selected.zoneId;
    const label = selected.name || ref?.zone.name || zoneId;
    setIdentifying(true);
    try {
      const flashed = await api.identifyDevice(deviceId, zoneId);
      if (flashed) {
        toast.success(`Flashing ${label} cyan for 2 seconds.`);
      }
    } finally {
      setIdentifying(false);
    }
  };

  return (
    <aside aria-label="Selected zone" className="space-y-4">
      <SelectField
        label="Zone"
        value={selectedKey}
        options={options}
        onChange={(key) => onSelect(key === '' ? null : key)}
      />
      {selected === null ? (
        <p className="text-sm text-zinc-500">
          Click a zone on the canvas or pick it above. Drag to move it; use the handles to resize and rotate (rotation
          snaps to 15°). {canvasHelp(snap)}
        </p>
      ) : (
        <div key={selectedKey} className="space-y-4">
          <div>
            <p className="text-sm font-medium text-zinc-100">{selected.name || ref?.zone.name || selected.zoneId}</p>
            <p className="text-xs text-zinc-500">
              {ref
                ? `${ref.device.name} · ${topologyTitle(selected.topology ?? ref.zone.topology)} · ${ref.zone.ledCount} LEDs`
                : selected.deviceId}
            </p>
          </div>
          <div className="space-y-1.5">
            <Button size="sm" className="w-full" onClick={() => void blink()} disabled={identifying}>
              {identifying ? 'Blinking…' : 'Blink / Identify'}
            </Button>
            <p className="text-xs text-zinc-500">Flashes this zone cyan on the hardware for 2 seconds.</p>
          </div>
          {selected.segmentId && (
            <SegmentNameField value={selected.name ?? ''} onChange={(name) => onChange(selectedKey, { name })} />
          )}
          <Toggle
            label="Enabled"
            description="Disabled zones are not driven by the effect."
            checked={selected.enabled}
            onChange={(enabled) => onChange(selectedKey, { enabled })}
          />
          <div className="grid grid-cols-2 gap-3">
            <NumberField label="X" value={selected.x} decimals={1} onChange={(x) => onChange(selectedKey, { x })} />
            <NumberField label="Y" value={selected.y} decimals={1} onChange={(y) => onChange(selectedKey, { y })} />
            <NumberField
              label="Width"
              value={selected.width}
              min={MIN_ZONE_SIZE}
              decimals={1}
              onChange={(width) => onChange(selectedKey, { width })}
            />
            <NumberField
              label="Height"
              value={selected.height}
              min={MIN_ZONE_SIZE}
              decimals={1}
              onChange={(height) => onChange(selectedKey, { height })}
            />
            <NumberField
              label="Rotation"
              value={selected.rotation}
              min={0}
              max={359}
              decimals={1}
              suffix="°"
              onChange={(rotation) => onChange(selectedKey, { rotation: normalizeRotation(rotation) })}
            />
            {ref?.zone.isResizable && (
              <NumberField
                label="LED count"
                value={claimedLedCount(selected.ledCount, siblings.length, ref.zone.ledCount)}
                min={1}
                max={ledFieldMax}
                hint={
                  usedByOthers > 0
                    ? `1–${ledFieldMax} · ${Math.max(0, remaining)} left of ${headerMax}`
                    : `1–${headerMax}`
                }
                onChange={(count) => onChange(selectedKey, { ledCount: clamp(Math.round(count), 1, ledFieldMax) })}
              />
            )}
          </div>
          <TopologyFields
            placement={selected}
            hardwareTopology={ref?.zone.topology}
            onChange={(patch) => onChange(selectedKey, patch)}
          />
          {ref?.zone.isResizable && (
            <div className="space-y-2">
              <div className="flex flex-wrap gap-2">
                <Button size="sm" onClick={onAddSegment}>
                  Add segment
                </Button>
                <Button size="sm" onClick={onRemoveSegment} disabled={siblings.length <= 1}>
                  Remove segment
                </Button>
              </div>
              <p className="text-xs text-zinc-500">
                Segments of this header are sent in canvas order, up to {ref.zone.maxLedCount} LEDs.
              </p>
            </div>
          )}
          <p className="text-xs text-zinc-500">{canvasHelp(snap)}</p>
        </div>
      )}
    </aside>
  );
}

function canvasHelp(snap: SnapStep): string {
  const nudge = snap === 0 ? 'Arrow keys move 1 unit (Shift: 10).' : `Arrow keys move ${snap} px (Shift: ${snap * 2}).`;
  return `${nudge} Ctrl or ⌘ and the wheel zooms from 50% to 300%. Middle-drag, or Alt-drag on empty canvas, pans. Fit resets the view.`;
}

function topologyTitle(topology: LedTopology): string {
  switch (topology) {
    case 'Matrix2D':
      return 'Matrix';
    case 'Composite':
      return 'Composite';
    case 'Point':
      return 'Point';
    case 'Linear':
      return 'Linear';
    case 'Ring':
      return 'Ring';
    default: {
      const exhaustive: never = topology;
      return exhaustive;
    }
  }
}

/** Null on the only box is the live zone length. Beside other segments, null is not the whole header. */
function claimedLedCount(ledCount: number | null, siblingCount: number, zoneLedCount: number): number {
  if (ledCount !== null) {
    return ledCount;
  }
  return siblingCount === 1 ? zoneLedCount : 0;
}

function SegmentNameField({ value, onChange }: { value: string; onChange: (name: string) => void }) {
  const id = useId();
  return (
    <Field label="Name" htmlFor={id}>
      <input
        id={id}
        value={value}
        maxLength={64}
        onChange={(event) => onChange(event.currentTarget.value)}
        className={controlClass}
      />
    </Field>
  );
}

function TopologyFields({
  placement,
  hardwareTopology,
  onChange,
}: {
  placement: ZonePlacement;
  hardwareTopology: LedTopology | undefined;
  onChange: (patch: PlacementPatch) => void;
}) {
  const topology = placement.topology ?? 'default';
  const options = [
    {
      value: 'default' as const,
      label: hardwareTopology ? `Hardware default (${topologyTitle(hardwareTopology)})` : 'Hardware default',
    },
    ...TOPOLOGY_VALUES.map((value) => ({ value, label: topologyTitle(value) })),
  ];
  return (
    <div className="space-y-4">
      <SelectField
        label="Topology"
        value={topology}
        options={options}
        onChange={(value) => {
          if (value === 'default') {
            onChange({ topology: null });
            return;
          }
          onChange({
            topology: value,
            ...(value === 'Matrix2D'
              ? { matrixColumns: placement.matrixColumns ?? 8 }
              : { serpentine: false }),
          });
        }}
      />
      {topology === 'default' && (
        <p className="text-xs text-zinc-500">
          Hardware default keeps the device&apos;s {hardwareTopology ? topologyTitle(hardwareTopology) : 'physical'} LED
          positions.
        </p>
      )}
      {topology === 'Matrix2D' && (
        <>
          <NumberField
            label="Columns"
            value={placement.matrixColumns ?? 8}
            min={1}
            max={256}
            onChange={(columns) => onChange({ matrixColumns: Math.round(columns) })}
          />
          <Toggle
            label="Serpentine"
            description="Odd rows run in the opposite direction."
            checked={placement.serpentine ?? false}
            onChange={(serpentine) => onChange({ serpentine })}
          />
        </>
      )}
    </div>
  );
}
