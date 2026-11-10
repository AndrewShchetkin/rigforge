import Konva from 'konva';
import { memo, useCallback, useEffect, useMemo, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import { Image as CanvasImage, Layer, Rect, Shape, Stage, Transformer } from 'react-konva';
import type { VirtualCanvas } from '../../api/contracts.ts';
import { useElementWidth } from '../../lib/useElementWidth.ts';
import { placementKey, zoneKey, type ZoneRef } from '../../lib/zones.ts';
import { useCanvasPreviewStore } from '../../state/hooks.ts';
import {
  GRID_STEP,
  ROTATION_SNAPS,
  segmentLedOffsets,
  zoomFromWheel,
  zoomViewAt,
  type CanvasView,
  type PlacementPatch,
  type SnapStep,
  type StageMetrics,
} from './layoutModel.ts';
import { ZoneNode } from './ZoneNode.tsx';

const MIN_BOX_PX = 8;
const ACCENT = '#a78bfa';

// Middle-click pans the canvas. Konva would otherwise treat button 1 as a zone drag.
Konva.dragButtons = [0];

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
  view: CanvasView;
  snap: SnapStep;
  metricsRef: { current: StageMetrics };
  onSelect: (key: string | null) => void;
  onChange: (key: string, patch: PlacementPatch) => void;
  onViewChange: Dispatch<SetStateAction<CanvasView>>;
}

/** Konva stage that fits the container width and keeps the canvas aspect ratio. */
export const LayoutStage = memo(function LayoutStage({
  canvas,
  zones,
  selectedKey,
  view,
  snap,
  metricsRef,
  onSelect,
  onChange,
  onViewChange,
}: LayoutStageProps) {
  const [setMeasured, containerWidth] = useElementWidth<HTMLDivElement>();
  const [container, setContainer] = useState<HTMLDivElement | null>(null);
  const transformerRef = useRef<Konva.Transformer>(null);
  const stageRef = useRef<Konva.Stage>(null);
  const nodesRef = useRef(new Map<string, Konva.Group>());
  const panSession = useRef<{ lastX: number; lastY: number } | null>(null);
  const ready = containerWidth > 0 && canvas.width > 0;
  const scale = ready ? containerWidth / canvas.width : 1;
  const stageHeight = Math.round(canvas.height * scale);
  const viewScale = scale * view.zoom;
  const selectedPresent = canvas.placements.some((placement) => placementKey(placement) === selectedKey);
  const ledOffsets = useMemo(() => segmentLedOffsets(canvas.placements), [canvas.placements]);

  const bindContainer = useCallback(
    (node: HTMLDivElement | null) => {
      setContainer(node);
      setMeasured(node);
    },
    [setMeasured],
  );

  const registerNode = useCallback((key: string, node: Konva.Group | null) => {
    if (node) {
      nodesRef.current.set(key, node);
    } else {
      nodesRef.current.delete(key);
    }
  }, []);

  useEffect(() => {
    if (!ready) {
      return;
    }
    metricsRef.current = { baseScale: scale, width: containerWidth, height: stageHeight };
  }, [metricsRef, ready, scale, containerWidth, stageHeight]);

  useEffect(() => {
    if (!container) {
      return;
    }
    const blockBrowserZoom = (event: WheelEvent) => {
      if (event.ctrlKey || event.metaKey) {
        event.preventDefault();
      }
    };
    container.addEventListener('wheel', blockBrowserZoom, { passive: false });
    return () => container.removeEventListener('wheel', blockBrowserZoom);
  }, [container]);

  useEffect(() => {
    const endPan = () => {
      if (panSession.current === null) {
        return;
      }
      panSession.current = null;
      const node = stageRef.current?.container();
      if (node) {
        node.style.cursor = '';
      }
    };
    const onMove = (event: MouseEvent) => {
      const session = panSession.current;
      if (session === null) {
        return;
      }
      const dx = event.clientX - session.lastX;
      const dy = event.clientY - session.lastY;
      session.lastX = event.clientX;
      session.lastY = event.clientY;
      if (dx === 0 && dy === 0) {
        return;
      }
      onViewChange((current) => ({ ...current, panX: current.panX + dx, panY: current.panY + dy }));
    };
    window.addEventListener('mousemove', onMove);
    window.addEventListener('mouseup', endPan);
    window.addEventListener('blur', endPan);
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('mouseup', endPan);
      window.removeEventListener('blur', endPan);
    };
  }, [onViewChange]);

  useEffect(() => {
    const transformer = transformerRef.current;
    if (!transformer) {
      return;
    }
    const node = selectedKey === null || !selectedPresent ? undefined : nodesRef.current.get(selectedKey);
    transformer.nodes(node ? [node] : []);
    transformer.getLayer()?.batchDraw();
  }, [selectedKey, selectedPresent, ready]);

  const handleWheel = (event: Konva.KonvaEventObject<WheelEvent>) => {
    const native = event.evt;
    if (!native.ctrlKey && !native.metaKey) {
      return;
    }
    native.preventDefault();
    const stage = event.target.getStage();
    const pointer = stage?.getPointerPosition();
    if (!pointer) {
      return;
    }
    const pointerX = pointer.x;
    const pointerY = pointer.y;
    const deltaY = native.deltaY;
    const deltaMode = native.deltaMode;
    onViewChange((current) => {
      const nextZoom = zoomFromWheel(current.zoom, deltaY, deltaMode);
      if (nextZoom === current.zoom) {
        return current;
      }
      return zoomViewAt(current, scale, pointerX, pointerY, nextZoom);
    });
  };

  const handleMouseDown = (event: Konva.KonvaEventObject<MouseEvent>) => {
    const native = event.evt;
    const onBackground = event.target === event.target.getStage();
    const middle = native.button === 1;
    const altBackground = native.button === 0 && native.altKey && onBackground;
    if (middle || altBackground) {
      native.preventDefault();
      panSession.current = { lastX: native.clientX, lastY: native.clientY };
      const node = event.target.getStage()?.container();
      if (node) {
        node.style.cursor = 'grabbing';
      }
      return;
    }
    if (onBackground) {
      onSelect(null);
    }
  };

  return (
    <div
      ref={bindContainer}
      className="w-full overflow-hidden rounded-lg border border-zinc-800 bg-zinc-950"
      onMouseDown={(event) => {
        if (event.button === 1) {
          event.preventDefault();
        }
      }}
      onAuxClick={(event) => {
        if (event.button === 1) {
          event.preventDefault();
        }
      }}
    >
      {ready && (
        <Stage
          ref={stageRef}
          width={containerWidth}
          height={stageHeight}
          x={view.panX}
          y={view.panY}
          scaleX={viewScale}
          scaleY={viewScale}
          onWheel={handleWheel}
          onMouseDown={handleMouseDown}
          onTouchStart={(event) => {
            if (event.target === event.target.getStage()) {
              onSelect(null);
            }
          }}
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
                  deviceName={zones.get(previewKey)?.device.name}
                  selected={key === selectedKey}
                  scale={scale}
                  zoom={view.zoom}
                  panX={view.panX}
                  panY={view.panY}
                  canvasWidth={canvas.width}
                  canvasHeight={canvas.height}
                  snap={snap}
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
              anchorSize={view.zoom > 0 ? 9 / view.zoom : 9}
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
