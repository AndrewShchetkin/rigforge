import type { ZonePlacement } from '../../api/contracts.ts';
import { NumberField } from '../../components/ui/NumberField.tsx';
import { SelectField } from '../../components/ui/SelectField.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { placementKey, type ZoneRef } from '../../lib/zones.ts';
import { MIN_ZONE_SIZE, normalizeRotation, type PlacementPatch } from './layoutModel.ts';

interface ZoneInspectorProps {
  placements: readonly ZonePlacement[];
  zones: ReadonlyMap<string, ZoneRef>;
  selected: ZonePlacement | null;
  onSelect: (key: string | null) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
}

/** Side panel: zone picker (keyboard alternative to clicking the canvas) and numeric placement fields. */
export function ZoneInspector({ placements, zones, selected, onSelect, onChange }: ZoneInspectorProps) {
  const selectedKey = selected ? placementKey(selected) : '';
  const ref = zones.get(selectedKey);
  const options = [
    { value: '', label: 'No zone selected' },
    ...placements.map((placement) => {
      const key = placementKey(placement);
      const zone = zones.get(key);
      return { value: key, label: zone ? `${zone.device.name} · ${zone.zone.name}` : `${key} (not detected)` };
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
            <p className="text-sm font-medium text-zinc-100">{ref?.zone.name ?? selected.zoneId}</p>
            <p className="text-xs text-zinc-500">
              {ref ? `${ref.device.name} · ${ref.zone.topology} · ${ref.zone.ledCount} LEDs` : selected.deviceId}
            </p>
          </div>
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
                value={selected.ledCount ?? ref.zone.ledCount}
                min={1}
                max={ref.zone.maxLedCount}
                hint={`1–${ref.zone.maxLedCount}`}
                onChange={(count) => onChange(selectedKey, { ledCount: Math.round(count) })}
              />
            )}
          </div>
        </div>
      )}
    </aside>
  );
}
