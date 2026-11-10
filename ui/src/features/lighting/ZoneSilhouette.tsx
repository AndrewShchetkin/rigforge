import type Konva from 'konva';
import { memo } from 'react';
import { Group, Shape } from 'react-konva';
import type { LedPositionDto, LedTopology } from '../../api/contracts.ts';

const BODY = 'rgba(161, 161, 170, 0.45)';
const ACCENT = 'rgba(167, 139, 250, 0.5)';

interface ZoneSilhouetteProps {
  topology: LedTopology;
  /** Zone, segment, and device names, used to pick a linear body. */
  name: string;
  width: number;
  height: number;
  leds: readonly LedPositionDto[];
  matrixColumns?: number | null;
  ledCount: number;
  dimmed: boolean;
}

interface Point {
  x: number;
  y: number;
}

interface SilhouetteModel {
  topology: LedTopology;
  name: string;
  width: number;
  height: number;
  leds: readonly LedPositionDto[];
  matrixColumns: number | null | undefined;
  ledCount: number;
}

/** Vector cue drawn under the live LED dots. Topology picks the shape; the name refines linear bodies. */
export const ZoneSilhouette = memo(function ZoneSilhouette({
  topology,
  name,
  width,
  height,
  leds,
  matrixColumns,
  ledCount,
  dimmed,
}: ZoneSilhouetteProps) {
  if (!(width > 0) || !(height > 0)) {
    return null;
  }
  const model: SilhouetteModel = { topology, name, width, height, leds, matrixColumns, ledCount };
  return (
    <Group listening={false} opacity={dimmed ? 0.35 : 1}>
      <Hairline stroke={BODY} draw={(context) => traceSilhouette(context, 'body', model)} />
      <Hairline stroke={ACCENT} draw={(context) => traceSilhouette(context, 'accent', model)} />
    </Group>
  );
});

function Hairline({ stroke, draw }: { stroke: string; draw: (context: Konva.Context) => void }) {
  return (
    <Shape
      listening={false}
      perfectDrawEnabled={false}
      shadowForStrokeEnabled={false}
      strokeScaleEnabled={false}
      stroke={stroke}
      strokeWidth={1}
      lineJoin="round"
      lineCap="round"
      hitStrokeWidth={0}
      sceneFunc={(context, shape) => {
        context.beginPath();
        draw(context);
        context.strokeShape(shape);
      }}
    />
  );
}

function traceSilhouette(context: Konva.Context, layer: 'body' | 'accent', model: SilhouetteModel) {
  switch (model.topology) {
    case 'Ring':
      traceRing(context, layer, model.width, model.height);
      return;
    case 'Matrix2D':
      if (layer === 'body') {
        const grid = matrixGrid(model.matrixColumns, model.ledCount, model.leds);
        traceMatrix(context, model.width, model.height, grid.columns, grid.rows);
      }
      return;
    case 'Point':
      tracePoint(context, layer, model.width, model.height, pointsOf(model.leds, model.width, model.height));
      return;
    case 'Linear':
      traceLinear(context, layer, model);
      return;
    case 'Composite':
      traceComposite(context, layer, model);
      return;
    default: {
      const exhaustive: never = model.topology;
      void exhaustive;
    }
  }
}

function traceRing(context: Konva.Context, layer: 'body' | 'accent', width: number, height: number) {
  const cx = width / 2;
  const cy = height / 2;
  const outerX = width * 0.45;
  const outerY = height * 0.45;
  if (!(outerX > 1) || !(outerY > 1)) {
    return;
  }
  if (layer === 'accent') {
    traceEllipse(context, cx, cy, width * 0.12, height * 0.12);
    return;
  }
  traceEllipse(context, cx, cy, outerX, outerY);
  const blades = 5;
  const sweep = 0.7;
  for (let index = 0; index < blades; index += 1) {
    const mid = -Math.PI / 2 + (index * 2 * Math.PI) / blades;
    const start = mid - sweep / 2;
    const end = mid + sweep / 2;
    context.moveTo(cx + Math.cos(start) * width * 0.3, cy + Math.sin(start) * height * 0.3);
    context.ellipse(cx, cy, width * 0.3, height * 0.3, 0, start, end, false);
  }
}

function traceMatrix(context: Konva.Context, width: number, height: number, columns: number, rows: number) {
  for (let column = 0; column <= columns; column += 1) {
    const x = (width * column) / columns;
    context.moveTo(x, 0);
    context.lineTo(x, height);
  }
  for (let row = 0; row <= rows; row += 1) {
    const y = (height * row) / rows;
    context.moveTo(0, y);
    context.lineTo(width, y);
  }
}

function tracePoint(context: Konva.Context, layer: 'body' | 'accent', width: number, height: number, points: readonly Point[]) {
  const center = points[0] ?? { x: width / 2, y: height / 2 };
  const arm = Math.min(width, height) * 0.2;
  if (!(arm > 0)) {
    return;
  }
  if (layer === 'body') {
    context.moveTo(center.x, center.y - arm);
    context.lineTo(center.x + arm * 0.72, center.y);
    context.lineTo(center.x, center.y + arm);
    context.lineTo(center.x - arm * 0.72, center.y);
    context.closePath();
    return;
  }
  const gap = arm * 0.28;
  const reach = arm * 1.35;
  context.moveTo(center.x - reach, center.y);
  context.lineTo(center.x - gap, center.y);
  context.moveTo(center.x + gap, center.y);
  context.lineTo(center.x + reach, center.y);
  context.moveTo(center.x, center.y - reach);
  context.lineTo(center.x, center.y - gap);
  context.moveTo(center.x, center.y + gap);
  context.lineTo(center.x, center.y + reach);
}

function traceLinear(context: Konva.Context, layer: 'body' | 'accent', model: SilhouetteModel) {
  const chain = chainPoints(model);
  if (layer === 'accent') {
    traceChevron(context, chain, model.width, model.height);
    return;
  }
  const motif = linearMotif(model.name);
  if (motif === 'ram') {
    traceRam(context, model.width, model.height);
  } else if (motif === 'bar') {
    traceBar(context, model.width, model.height);
  }
  traceChain(context, chain);
}

function traceComposite(context: Konva.Context, layer: 'body' | 'accent', model: SilhouetteModel) {
  const chain = pointsOf(model.leds, model.width, model.height);
  if (layer === 'accent') {
    traceChevron(context, chain.length > 0 ? chain : chainPoints(model), model.width, model.height);
    return;
  }
  const pad = Math.min(model.width, model.height) * 0.08;
  traceRoundRect(context, pad, pad, model.width - pad * 2, model.height - pad * 2, Math.min(8, pad));
  traceChain(context, chain);
}

function linearMotif(name: string): 'ram' | 'bar' | 'strip' {
  const text = name.toLowerCase();
  if (/\b(ram|dimm|dram|memory)\b/.test(text)) {
    return 'ram';
  }
  if (/(light\s*bar|lightbar|diffuser|underglow|\bbar\b)/.test(text)) {
    return 'bar';
  }
  return 'strip';
}

function matrixGrid(
  columns: number | null | undefined,
  ledCount: number,
  leds: readonly LedPositionDto[],
): { columns: number; rows: number } {
  const count = Math.max(1, ledCount, leds.length);
  if (columns !== null && columns !== undefined && columns > 0) {
    const cols = Math.min(Math.max(Math.round(columns), 1), count);
    return { columns: cols, rows: Math.ceil(count / cols) };
  }
  if (leds.length > 1) {
    const xs = new Set<number>();
    const ys = new Set<number>();
    for (const led of leds) {
      xs.add(Math.round(led.x * 1000));
      ys.add(Math.round(led.y * 1000));
    }
    if (xs.size > 1 && ys.size > 1) {
      return { columns: xs.size, rows: ys.size };
    }
  }
  const cols = Math.min(8, count);
  return { columns: cols, rows: Math.ceil(count / cols) };
}

function pointsOf(leds: readonly LedPositionDto[], width: number, height: number): Point[] {
  return leds.map((led) => ({ x: led.x * width, y: led.y * height }));
}

function chainPoints(model: SilhouetteModel): Point[] {
  const points = pointsOf(model.leds, model.width, model.height);
  if (points.length > 0) {
    return points;
  }
  const pad = Math.min(model.width, model.height) * 0.12;
  if (model.width >= model.height) {
    return [
      { x: pad, y: model.height / 2 },
      { x: model.width - pad, y: model.height / 2 },
    ];
  }
  return [
    { x: model.width / 2, y: pad },
    { x: model.width / 2, y: model.height - pad },
  ];
}

function traceChain(context: Konva.Context, points: readonly Point[]) {
  const first = points[0];
  if (!first) {
    return;
  }
  context.moveTo(first.x, first.y);
  for (let index = 1; index < points.length; index += 1) {
    const point = points[index];
    if (point) {
      context.lineTo(point.x, point.y);
    }
  }
}

function traceChevron(context: Konva.Context, points: readonly Point[], width: number, height: number) {
  const first = points[0];
  if (!first) {
    return;
  }
  const next = points[1] ?? { x: first.x + Math.max(width * 0.08, 4), y: first.y };
  const angle = Math.atan2(next.y - first.y, next.x - first.x);
  const size = Math.min(Math.min(width, height) * 0.16, 12);
  if (!(size > 0)) {
    return;
  }
  const left = angle + Math.PI - 0.55;
  const right = angle + Math.PI + 0.55;
  context.moveTo(first.x + Math.cos(left) * size, first.y + Math.sin(left) * size);
  context.lineTo(first.x, first.y);
  context.lineTo(first.x + Math.cos(right) * size, first.y + Math.sin(right) * size);
}

function traceEllipse(context: Konva.Context, cx: number, cy: number, rx: number, ry: number) {
  if (!(rx > 0) || !(ry > 0)) {
    return;
  }
  context.moveTo(cx + rx, cy);
  context.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2, false);
}

function traceRoundRect(context: Konva.Context, x: number, y: number, width: number, height: number, radius: number) {
  if (!(width > 0) || !(height > 0)) {
    return;
  }
  const r = Math.max(0, Math.min(radius, width / 2, height / 2));
  context.moveTo(x + r, y);
  context.arcTo(x + width, y, x + width, y + height, r);
  context.arcTo(x + width, y + height, x, y + height, r);
  context.arcTo(x, y + height, x, y, r);
  context.arcTo(x, y, x + width, y, r);
  context.closePath();
}

function traceRam(context: Konva.Context, width: number, height: number) {
  if (width >= height) {
    const x = width * 0.05;
    const y = height * 0.22;
    const w = width * 0.9;
    const h = height * 0.56;
    traceRoundRect(context, x, y, w, h, 2);
    const notch = x + w * 0.72;
    context.moveTo(notch, y);
    context.lineTo(notch, y + h * 0.28);
    context.lineTo(notch + w * 0.08, y + h * 0.28);
    context.lineTo(notch + w * 0.08, y);
    return;
  }
  const x = width * 0.22;
  const y = height * 0.05;
  const w = width * 0.56;
  const h = height * 0.9;
  traceRoundRect(context, x, y, w, h, 2);
  const notch = y + h * 0.72;
  context.moveTo(x, notch);
  context.lineTo(x + w * 0.28, notch);
  context.lineTo(x + w * 0.28, notch + h * 0.08);
  context.lineTo(x, notch + h * 0.08);
}

function traceBar(context: Konva.Context, width: number, height: number) {
  if (width >= height) {
    const bar = Math.max(height * 0.34, 2);
    traceRoundRect(context, width * 0.04, (height - bar) / 2, width * 0.92, bar, bar / 2);
    return;
  }
  const bar = Math.max(width * 0.34, 2);
  traceRoundRect(context, (width - bar) / 2, height * 0.04, bar, height * 0.92, bar / 2);
}
