import type Konva from 'konva';
import { memo, useCallback, useMemo } from 'react';
import { Group, Rect, Text } from 'react-konva';
import type { RgbZoneDto, ZonePlacement } from '../../api/contracts.ts';
import { zoneLeds } from '../../lib/ledLayout.ts';
import { clampOrigin, snapPlacement, type PlacementPatch, type SnapStep } from './layoutModel.ts';
import { ZoneLeds } from './ZoneLeds.tsx';
import { ZoneSilhouette } from './ZoneSilhouette.tsx';

const LABEL_PX = 11;
const PADDING_PX = 3;
const ZONE_DASH = [6, 4];

function setCursor(event: Konva.KonvaEventObject<MouseEvent>, cursor: string) {
  const container = event.target.getStage()?.container();
  if (container) {
    container.style.cursor = cursor;
  }
}

function joinedLabel(...parts: Array<string | null | undefined>): string {
  const names: string[] = [];
  for (const part of parts) {
    if (part && part.trim() !== '') {
      names.push(part.trim());
    }
  }
  return names.join(' ');
}

interface ZoneNodeProps {
  /** Placement key, including the segment id when the zone is split. */
  zoneKey: string;
  /** Physical zone key used by the LED preview stream. */
  previewKey: string;
  placement: ZonePlacement;
  /** Undefined when the placement refers to a zone the daemon does not currently report. */
  zone: RgbZoneDto | undefined;
  deviceName?: string;
  selected: boolean;
  /** Fit-to-width stage pixels per canvas unit, before the user zoom. */
  scale: number;
  zoom: number;
  panX: number;
  panY: number;
  canvasWidth: number;
  canvasHeight: number;
  snap: SnapStep;
  ledOffset: number;
  onSelect: (key: string) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
  onRegister: (key: string, node: Konva.Group | null) => void;
}

/** One placed zone: dashed box at (x, y) rotated around its top-left corner, its name and its LEDs. */
export const ZoneNode = memo(function ZoneNode({
  zoneKey,
  previewKey,
  placement,
  zone,
  deviceName,
  selected,
  scale,
  zoom,
  panX,
  panY,
  canvasWidth,
  canvasHeight,
  snap,
  ledOffset,
  onSelect,
  onChange,
  onRegister,
}: ZoneNodeProps) {
  const { x, y, width, height, rotation, enabled, ledCount, name, topology, matrixColumns, serpentine } = placement;
  const register = useCallback((node: Konva.Group | null) => onRegister(zoneKey, node), [zoneKey, onRegister]);
  const leds = useMemo(
    () => (zone ? zoneLeds(zone, { ledCount, topology, matrixColumns, serpentine }) : []),
    [zone, ledCount, topology, matrixColumns, serpentine],
  );
  const padding = PADDING_PX / scale;
  const effectiveTopology = topology ?? zone?.topology ?? 'Linear';

  // dragBoundFunc speaks absolute stage pixels. Convert through pan and zoom, then clamp in canvas units.
  const dragBound = useCallback(
    (position: Konva.Vector2d) => {
      const viewScale = scale * zoom;
      if (!(viewScale > 0)) {
        return position;
      }
      const origin = clampOrigin(
        (position.x - panX) / viewScale,
        (position.y - panY) / viewScale,
        width,
        height,
        rotation,
        canvasWidth,
        canvasHeight,
      );
      return { x: panX + origin.x * viewScale, y: panY + origin.y * viewScale };
    },
    [scale, zoom, panX, panY, width, height, rotation, canvasWidth, canvasHeight],
  );

  const handleDragEnd = (event: Konva.KonvaEventObject<DragEvent>) => {
    const node = event.currentTarget;
    const placed = snapPlacement(placement, { x: node.x(), y: node.y() }, canvasWidth, canvasHeight, snap);
    onChange(zoneKey, { x: placed.x, y: placed.y });
  };

  // The Transformer resizes by scaling the group; fold the scale back into width/height.
  const handleTransformEnd = (event: Konva.KonvaEventObject<Event>) => {
    const node = event.currentTarget;
    const scaleX = node.scaleX();
    const scaleY = node.scaleY();
    node.scale({ x: 1, y: 1 });
    const placed = snapPlacement(
      placement,
      {
        x: node.x(),
        y: node.y(),
        width: width * Math.abs(scaleX),
        height: height * Math.abs(scaleY),
        rotation: node.rotation(),
      },
      canvasWidth,
      canvasHeight,
      snap,
    );
    onChange(zoneKey, {
      x: placed.x,
      y: placed.y,
      width: placed.width,
      height: placed.height,
      rotation: placed.rotation,
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
      onMouseDown={(event) => {
        if (event.evt.button !== 0) {
          return;
        }
        onSelect(zoneKey);
      }}
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
      <ZoneSilhouette
        topology={effectiveTopology}
        name={joinedLabel(name, zone?.name, deviceName)}
        width={width}
        height={height}
        leds={leds}
        matrixColumns={matrixColumns}
        ledCount={leds.length > 0 ? leds.length : (ledCount ?? zone?.ledCount ?? 0)}
        dimmed={!enabled}
      />
      <Text
        x={padding}
        y={padding}
        width={Math.max(0, width - 2 * padding)}
        text={name || zone?.name || `${placement.zoneId} (not detected)`}
        fontSize={LABEL_PX / scale}
        fill={enabled ? '#e4e4e7' : '#71717a'}
        shadowColor="#000"
        shadowBlur={3 / scale}
        shadowOffsetY={1 / scale}
        shadowOpacity={0.9}
        wrap="none"
        ellipsis
        listening={false}
      />
      {zone && (
        <ZoneLeds
          zoneKey={previewKey}
          leds={leds}
          ledOffset={ledOffset}
          width={width}
          height={height}
          scale={scale}
          dimmed={!enabled}
        />
      )}
    </Group>
  );
});
