import type Konva from 'konva';
import { memo, useCallback, useMemo } from 'react';
import { Group, Rect, Text } from 'react-konva';
import type { RgbZoneDto, ZonePlacement } from '../../api/contracts.ts';
import { zoneLeds } from '../../lib/ledLayout.ts';
import { roundTo } from '../../lib/math.ts';
import { MIN_ZONE_SIZE, normalizeRotation, type PlacementPatch } from './layoutModel.ts';
import { ZoneLeds } from './ZoneLeds.tsx';

const LABEL_PX = 11;
const PADDING_PX = 3;
const ZONE_DASH = [6, 4];

function setCursor(event: Konva.KonvaEventObject<MouseEvent>, cursor: string) {
  const container = event.target.getStage()?.container();
  if (container) {
    container.style.cursor = cursor;
  }
}

interface ZoneNodeProps {
  zoneKey: string;
  placement: ZonePlacement;
  /** Undefined when the placement refers to a zone the daemon does not currently report. */
  zone: RgbZoneDto | undefined;
  selected: boolean;
  /** Stage pixels per canvas unit. */
  scale: number;
  dragBound: (position: Konva.Vector2d) => Konva.Vector2d;
  onSelect: (key: string) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
  onRegister: (key: string, node: Konva.Group | null) => void;
}

/** One placed zone: dashed box at (x, y) rotated around its top-left corner, its name and its LEDs. */
export const ZoneNode = memo(function ZoneNode({
  zoneKey,
  placement,
  zone,
  selected,
  scale,
  dragBound,
  onSelect,
  onChange,
  onRegister,
}: ZoneNodeProps) {
  const { x, y, width, height, rotation, enabled, ledCount } = placement;
  const register = useCallback((node: Konva.Group | null) => onRegister(zoneKey, node), [zoneKey, onRegister]);
  const leds = useMemo(() => (zone ? zoneLeds(zone, ledCount) : []), [zone, ledCount]);
  const padding = PADDING_PX / scale;

  const handleDragEnd = (event: Konva.KonvaEventObject<DragEvent>) => {
    const node = event.currentTarget;
    onChange(zoneKey, { x: roundTo(node.x(), 2), y: roundTo(node.y(), 2) });
  };

  // The Transformer resizes by scaling the group; fold the scale back into width/height.
  const handleTransformEnd = (event: Konva.KonvaEventObject<Event>) => {
    const node = event.currentTarget;
    const scaleX = node.scaleX();
    const scaleY = node.scaleY();
    node.scale({ x: 1, y: 1 });
    onChange(zoneKey, {
      x: roundTo(node.x(), 2),
      y: roundTo(node.y(), 2),
      width: roundTo(Math.max(MIN_ZONE_SIZE, width * scaleX), 2),
      height: roundTo(Math.max(MIN_ZONE_SIZE, height * scaleY), 2),
      rotation: normalizeRotation(node.rotation()),
    });
  };

  return (
    <Group
      ref={register}
      x={x}
      y={y}
      rotation={rotation}
      draggable
      dragBoundFunc={dragBound}
      onMouseDown={() => onSelect(zoneKey)}
      onTouchStart={() => onSelect(zoneKey)}
      onDragEnd={handleDragEnd}
      onTransformEnd={handleTransformEnd}
      onMouseEnter={(event) => setCursor(event, 'move')}
      onMouseLeave={(event) => setCursor(event, '')}
    >
      <Rect
        width={width}
        height={height}
        fill={selected ? 'rgba(139, 92, 246, 0.16)' : 'rgba(255, 255, 255, 0.03)'}
        stroke={selected ? '#a78bfa' : '#71717a'}
        strokeWidth={1}
        strokeScaleEnabled={false}
        dash={ZONE_DASH}
        opacity={enabled ? 1 : 0.4}
      />
      <Text
        x={padding}
        y={padding}
        width={Math.max(0, width - 2 * padding)}
        text={zone?.name ?? `${placement.zoneId} (not detected)`}
        fontSize={LABEL_PX / scale}
        fill={enabled ? '#e4e4e7' : '#71717a'}
        wrap="none"
        ellipsis
        listening={false}
      />
      {zone && (
        <ZoneLeds zoneKey={zoneKey} leds={leds} width={width} height={height} scale={scale} dimmed={!enabled} />
      )}
    </Group>
  );
});
