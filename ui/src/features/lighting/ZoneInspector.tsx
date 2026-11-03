import { useId } from 'react';
import type { LedTopology, ZonePlacement } from '../../api/contracts.ts';
import { Button } from '../../components/ui/Button.tsx';
import { Field } from '../../components/ui/Field.tsx';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { SelectField } from '../../components/ui/SelectField.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { controlClass } from '../../components/ui/styles.ts';
import { clamp } from '../../lib/math.ts';
import { placementKey, zoneKey, type ZoneRef } from '../../lib/zones.ts';
import { MIN_ZONE_SIZE, normalizeRotation, type PlacementPatch } from './layoutModel.ts';

const TOPOLOGY_OPTIONS: { value: 'default' | LedTopology; label: string }[] = [
  { value: 'default', label: 'Hardware default' },
  { value: 'Linear', label: 'Linear' },
  { value: 'Ring', label: 'Ring' },
  { value: 'Matrix2D', label: 'Matrix' },
];

interface ZoneInspectorProps {
  placements: readonly ZonePlacement[];
  zones: ReadonlyMap<string, ZoneRef>;
  selected: ZonePlacement | null;
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
  onSelect,
  onChange,
  onAddSegment,
  onRemoveSegment,
}: ZoneInspectorProps) {
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
          snaps to 15°). With the canvas focused, arrow keys nudge the selected zone (Shift: 10 units).
        </p>
      ) : (
        <div key={selectedKey} className="space-y-4">
          <div>
            <p className="text-sm font-medium text-zinc-100">{selected.name || ref?.zone.name || selected.zoneId}</p>
            <p className="text-xs text-zinc-500">
              {ref ? `${ref.device.name} · ${selected.topology ?? ref.zone.topology} · ${ref.zone.ledCount} LEDs` : selected.deviceId}
            </p>
          </div>
          {selected.segmentId && (
            <SegmentNameField
              value={selected.name ?? ''}
              onChange={(name) => onChange(selectedKey, { name })}
            />
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
                onChange={(count) =>
                  onChange(selectedKey, { ledCount: clamp(Math.round(count), 1, ledFieldMax) })
                }
              />
            )}
          </div>
          {ref?.zone.isResizable && (
            <SegmentControls
              placement={selected}
              maxLedCount={ref.zone.maxLedCount}
              siblingCount={siblings.length}
              onChange={(patch) => onChange(selectedKey, patch)}
              onAddSegment={onAddSegment}
              onRemoveSegment={onRemoveSegment}
            />
          )}
        </div>
      )}
    </aside>
  );
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

function SegmentControls({
  placement,
  maxLedCount,
  siblingCount,
  onChange,
  onAddSegment,
  onRemoveSegment,
}: {
  placement: ZonePlacement;
  maxLedCount: number;
  siblingCount: number;
  onChange: (patch: PlacementPatch) => void;
  onAddSegment: () => void;
  onRemoveSegment: () => void;
}) {
  const topology = placement.topology ?? 'default';
  return (
    <div className="space-y-4">
      <SelectField
        label="Topology"
        value={topology}
        options={TOPOLOGY_OPTIONS}
        onChange={(value) =>
          onChange({
            topology: value === 'default' ? null : value,
            matrixColumns: value === 'Matrix2D' ? (placement.matrixColumns ?? 8) : placement.matrixColumns,
            ...(value === 'Matrix2D' ? {} : { serpentine: false }),
          })
        }
      />
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
      <div className="flex flex-wrap gap-2">
        <Button size="sm" onClick={onAddSegment}>
          Add segment
        </Button>
        <Button size="sm" onClick={onRemoveSegment} disabled={siblingCount <= 1}>
          Remove segment
        </Button>
      </div>
      <p className="text-xs text-zinc-500">
        Segments of this header are sent in canvas order, up to {maxLedCount} LEDs.
      </p>
    </div>
  );
}
