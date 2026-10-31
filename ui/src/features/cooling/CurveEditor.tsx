import { memo, useRef, useState, type KeyboardEvent, type MouseEvent, type PointerEvent } from 'react';
import type { FanCurvePoint } from '../../api/contracts.ts';
import { formatCelsius, formatPercent } from '../../lib/format.ts';
import { CurveAxes, DutyLine, LiveMarker } from './CurveOverlays.tsx';
import { PLOT, VIEW, extendedCurve, fromSvg, hysteresisBand, toPolyline, toX, toY } from './curveGeometry.ts';
import { insertAfter, insertPoint, movePoint, removePoint } from './curveModel.ts';

interface CurveEditorProps {
  /** Accessible name of the channel being edited. */
  label: string;
  points: readonly FanCurvePoint[];
  onChange: (points: FanCurvePoint[]) => void;
  hysteresis: number;
  showHysteresis: boolean;
  minDuty: number;
  /** Set in Fixed mode: the curve is dimmed and the fixed duty drawn as a line. */
  fixedDuty: number | null;
  liveTemperature: number | null;
  liveDuty: number | null;
}

const KEY_STEP = 1;
const KEY_STEP_LARGE = 5;

/** SVG fan curve editor: drag points, double-click to add, Alt+click / right-click / Delete to remove. */
export const CurveEditor = memo(function CurveEditor({
  label,
  points,
  onChange,
  hysteresis,
  showHysteresis,
  minDuty,
  fixedDuty,
  liveTemperature,
  liveDuty,
}: CurveEditorProps) {
  const svgRef = useRef<SVGSVGElement>(null);
  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const dimmed = fixedDuty !== null;

  const apply = (next: FanCurvePoint[] | null) => {
    if (next) {
      onChange(next);
    }
  };

  const toCurvePoint = (event: { clientX: number; clientY: number }): FanCurvePoint | null => {
    const matrix = svgRef.current?.getScreenCTM();
    if (!matrix) {
      return null;
    }
    const point = new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse());
    return fromSvg(point.x, point.y);
  };

  const handlePointerDown = (event: PointerEvent<SVGCircleElement>, index: number) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    if (event.altKey) {
      apply(removePoint(points, index));
      return;
    }
    event.currentTarget.focus();
    event.currentTarget.setPointerCapture(event.pointerId);
    setDragIndex(index);
  };

  const handlePointerMove = (event: PointerEvent<SVGCircleElement>, index: number) => {
    if (dragIndex !== index) {
      return;
    }
    const target = toCurvePoint(event);
    if (target) {
      onChange(movePoint(points, index, target));
    }
  };

  const endDrag = () => setDragIndex(null);

  const handleKeyDown = (event: KeyboardEvent<SVGCircleElement>, index: number) => {
    const point = points[index];
    if (!point) {
      return;
    }
    const step = event.shiftKey ? KEY_STEP_LARGE : KEY_STEP;
    let next: FanCurvePoint[] | null;
    switch (event.key) {
      case 'ArrowLeft':
        next = movePoint(points, index, { ...point, temperature: point.temperature - step });
        break;
      case 'ArrowRight':
        next = movePoint(points, index, { ...point, temperature: point.temperature + step });
        break;
      case 'ArrowUp':
        next = movePoint(points, index, { ...point, duty: point.duty + step });
        break;
      case 'ArrowDown':
        next = movePoint(points, index, { ...point, duty: point.duty - step });
        break;
      case 'Delete':
      case 'Backspace':
        next = removePoint(points, index);
        break;
      case 'Insert':
      case '+':
        next = insertAfter(points, index);
        break;
      default:
        return;
    }
    event.preventDefault();
    apply(next);
  };

  const handleDoubleClick = (event: MouseEvent<SVGRectElement>) => {
    const target = toCurvePoint(event);
    if (target) {
      apply(insertPoint(points, target));
    }
  };

  return (
    <figure className="space-y-2">
      <svg
        ref={svgRef}
        viewBox={`0 0 ${VIEW.width} ${VIEW.height}`}
        role="group"
        aria-label={`${label}: fan curve`}
        className="w-full touch-none select-none"
      >
        <CurveAxes />
        {showHysteresis && hysteresis > 0 && (
          <polygon points={hysteresisBand(points, hysteresis)} className="fill-violet-500/10" pointerEvents="none" />
        )}
        {minDuty > 0 && <DutyLine duty={minDuty} label={`min ${minDuty}%`} variant="floor" />}
        <rect
          x={PLOT.x}
          y={PLOT.y}
          width={PLOT.width}
          height={PLOT.height}
          fill="transparent"
          className="cursor-crosshair"
          onDoubleClick={handleDoubleClick}
        />
        <polyline
          points={toPolyline(extendedCurve(points))}
          fill="none"
          strokeWidth={2.5}
          strokeLinejoin="round"
          pointerEvents="none"
          className={dimmed ? 'stroke-violet-400/30' : 'stroke-violet-400'}
        />
        {fixedDuty !== null && <DutyLine duty={fixedDuty} label={`fixed ${fixedDuty}%`} variant="fixed" />}
        {liveTemperature !== null && liveDuty !== null && <LiveMarker temperature={liveTemperature} duty={liveDuty} />}
        {points.map((point, index) => (
          <circle
            key={index}
            cx={toX(point.temperature)}
            cy={toY(point.duty)}
            r={dragIndex === index ? 8 : 6.5}
            tabIndex={0}
            role="button"
            aria-roledescription="curve point"
            aria-label={`Point ${index + 1} of ${points.length}: ${formatCelsius(point.temperature)}, ${formatPercent(point.duty)}`}
            strokeWidth={2}
            className={`stroke-zinc-950 outline-none focus-visible:stroke-white ${dimmed ? 'fill-violet-300/40' : 'fill-violet-300'} ${dragIndex === index ? 'cursor-grabbing' : 'cursor-grab'}`}
            onPointerDown={(event) => handlePointerDown(event, index)}
            onPointerMove={(event) => handlePointerMove(event, index)}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
            onLostPointerCapture={endDrag}
            onContextMenu={(event) => {
              event.preventDefault();
              apply(removePoint(points, index));
            }}
            onKeyDown={(event) => handleKeyDown(event, index)}
          />
        ))}
      </svg>
      <figcaption className="text-xs text-zinc-500">
        Drag points to edit, double-click the plot to add one, Alt+click or right-click a point to remove it. With a
        point focused: arrow keys move it (Shift ×5), Delete removes it, + adds one after it.
      </figcaption>
    </figure>
  );
});
