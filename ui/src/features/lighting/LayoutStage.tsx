import type Konva from 'konva';
import { memo, useCallback, useEffect, useMemo, useRef } from 'react';
import { Image as CanvasImage, Layer, Rect, Shape, Stage, Transformer } from 'react-konva';
import type { VirtualCanvas } from '../../api/contracts.ts';
import { clamp } from '../../lib/math.ts';
import { useElementWidth } from '../../lib/useElementWidth.ts';
import { placementKey, zoneKey, type ZoneRef } from '../../lib/zones.ts';
import { useCanvasPreviewStore } from '../../state/hooks.ts';
import { GRID_STEP, ROTATION_SNAPS, segmentLedOffsets, type PlacementPatch } from './layoutModel.ts';
import { ZoneNode } from './ZoneNode.tsx';

const MIN_BOX_PX = 8;
const ACCENT = '#a78bfa';

const limitBox: NonNullable<Konva.TransformerConfig['boundBoxFunc']> = (oldBox, newBox) =>
  Math.abs(newBox.width) < MIN_BOX_PX || Math.abs(newBox.height) < MIN_BOX_PX ? oldBox : newBox;

const CanvasEffectImage = memo(function CanvasEffectImage({ width, height }: { width: number; height: number }) {
  const store = useCanvasPreviewStore();
  const imageRef = useRef<Konva.Image>(null);

  useEffect(() => {
    let generation = 0;
    let active = true;
    const apply = () => {
      const node = imageRef.current;
      if (!node || !active) {
        return;
      }

      const url = store.getUrl();
      const ticket = ++generation;
      if (!url) {
        node.image(undefined);
        node.getLayer()?.batchDraw();
        return;
      }

      const image = new window.Image();
      image.onload = () => {
        if (!active || ticket !== generation) {
          return;
        }

        node.image(image);
        node.getLayer()?.batchDraw();
      };
      image.src = url;
    };

    apply();
    const unsubscribe = store.subscribe(apply);
    return () => {
      active = false;
      unsubscribe();
    };
  }, [store]);

  return <CanvasImage ref={imageRef} image={undefined} width={width} height={height} listening={false} />;
});

const CanvasBackground = memo(function CanvasBackground({ width, height }: { width: number; height: number }) {
  return (
    <>
      <Rect width={width} height={height} fill="#0b0b0f" />
      <CanvasEffectImage width={width} height={height} />
      <Shape
        sceneFunc={(context, shape) => {
          context.beginPath();
          for (let x = GRID_STEP; x < width; x += GRID_STEP) {
            context.moveTo(x, 0);
            context.lineTo(x, height);
          }
          for (let y = GRID_STEP; y < height; y += GRID_STEP) {
            context.moveTo(0, y);
            context.lineTo(width, y);
          }
          context.strokeShape(shape);
        }}
        stroke="rgba(161, 161, 170, 0.1)"
        strokeWidth={1}
        strokeScaleEnabled={false}
      />
    </>
  );
});

interface LayoutStageProps {
  canvas: VirtualCanvas;
  zones: ReadonlyMap<string, ZoneRef>;
  selectedKey: string | null;
  onSelect: (key: string | null) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
}

/** Konva stage that fits the container width and keeps the canvas aspect ratio. */
export const LayoutStage = memo(function LayoutStage({ canvas, zones, selectedKey, onSelect, onChange }: LayoutStageProps) {
  const [containerRef, containerWidth] = useElementWidth<HTMLDivElement>();
  const transformerRef = useRef<Konva.Transformer>(null);
  const nodesRef = useRef(new Map<string, Konva.Group>());
  const ready = containerWidth > 0 && canvas.width > 0;
  const scale = ready ? containerWidth / canvas.width : 1;
  const stageHeight = Math.round(canvas.height * scale);
  const selectedPresent = canvas.placements.some((placement) => placementKey(placement) === selectedKey);
  const ledOffsets = useMemo(() => segmentLedOffsets(canvas.placements), [canvas.placements]);

  const registerNode = useCallback((key: string, node: Konva.Group | null) => {
    if (node) {
      nodesRef.current.set(key, node);
    } else {
      nodesRef.current.delete(key);
    }
  }, []);

  // Keeps the zone origin on the visible stage while dragging (positions are absolute stage pixels).
  const dragBound = useCallback(
    (position: Konva.Vector2d) => ({ x: clamp(position.x, 0, containerWidth), y: clamp(position.y, 0, stageHeight) }),
    [containerWidth, stageHeight],
  );

  useEffect(() => {
    const transformer = transformerRef.current;
    if (!transformer) {
      return;
    }
    const node = selectedKey === null || !selectedPresent ? undefined : nodesRef.current.get(selectedKey);
    transformer.nodes(node ? [node] : []);
    transformer.getLayer()?.batchDraw();
  }, [selectedKey, selectedPresent, ready]);

  const deselectOnEmpty = (event: Konva.KonvaEventObject<MouseEvent | TouchEvent>) => {
    if (event.target === event.target.getStage()) {
      onSelect(null);
    }
  };

  return (
    <div ref={containerRef} className="w-full overflow-hidden rounded-lg border border-zinc-800 bg-zinc-950">
      {ready && (
        <Stage
          width={containerWidth}
          height={stageHeight}
          scaleX={scale}
          scaleY={scale}
          onMouseDown={deselectOnEmpty}
          onTouchStart={deselectOnEmpty}
        >
          <Layer listening={false}>
            <CanvasBackground width={canvas.width} height={canvas.height} />
          </Layer>
          <Layer>
            {canvas.placements.map((placement) => {
              const key = placementKey(placement);
              const previewKey = zoneKey(placement.deviceId, placement.zoneId);
              return (
                <ZoneNode
                  key={key}
                  zoneKey={key}
                  previewKey={previewKey}
                  placement={placement}
                  zone={zones.get(previewKey)?.zone}
                  selected={key === selectedKey}
                  scale={scale}
                  dragBound={dragBound}
                  ledOffset={ledOffsets.get(key) ?? 0}
                  onSelect={onSelect}
                  onChange={onChange}
                  onRegister={registerNode}
                />
              );
            })}
            <Transformer
              ref={transformerRef}
              keepRatio={false}
              flipEnabled={false}
              rotationSnaps={ROTATION_SNAPS}
              rotationSnapTolerance={7.5}
              ignoreStroke
              anchorSize={9}
              anchorCornerRadius={2}
              anchorStroke={ACCENT}
              anchorFill="#18181b"
              borderStroke={ACCENT}
              boundBoxFunc={limitBox}
            />
          </Layer>
        </Stage>
      )}
    </div>
  );
});
