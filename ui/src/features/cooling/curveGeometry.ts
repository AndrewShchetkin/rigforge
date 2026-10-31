import type { FanCurvePoint } from '../../api/contracts.ts';
import { clamp } from '../../lib/math.ts';
import { CURVE_LIMITS } from './curveModel.ts';

/** SVG viewBox of the curve editor and the plot area inside it. */
export const VIEW = { width: 560, height: 300 } as const;
export const PLOT = { x: 46, y: 16, width: 560 - 46 - 18, height: 300 - 16 - 40 } as const;

export const TEMPERATURE_TICKS = [20, 30, 40, 50, 60, 70, 80, 90, 100] as const;
export const DUTY_TICKS = [0, 20, 40, 60, 80, 100] as const;

const { minTemperature, maxTemperature } = CURVE_LIMITS;

export function toX(temperature: number): number {
  const ratio = (clamp(temperature, minTemperature, maxTemperature) - minTemperature) / (maxTemperature - minTemperature);
  return PLOT.x + ratio * PLOT.width;
}

export function toY(duty: number): number {
  return PLOT.y + (1 - clamp(duty, 0, 100) / 100) * PLOT.height;
}

export function fromSvg(x: number, y: number): FanCurvePoint {
  return {
    temperature: minTemperature + ((x - PLOT.x) / PLOT.width) * (maxTemperature - minTemperature),
    duty: (1 - (y - PLOT.y) / PLOT.height) * 100,
  };
}

/** The effective curve: flat before the first point and after the last one. */
export function extendedCurve(points: readonly FanCurvePoint[]): FanCurvePoint[] {
  const first = points[0];
  const last = points.at(-1);
  if (!first || !last) {
    return [];
  }
  return [{ temperature: minTemperature, duty: first.duty }, ...points, { temperature: maxTemperature, duty: last.duty }];
}

export function toPolyline(points: readonly FanCurvePoint[]): string {
  return points.map((point) => `${toX(point.temperature).toFixed(1)},${toY(point.duty).toFixed(1)}`).join(' ');
}

/**
 * Where the duty can sit while cooling down: the duty is only lowered once the temperature fell by the
 * hysteresis, so the band spans the curve and the curve shifted left by that amount.
 */
export function hysteresisBand(points: readonly FanCurvePoint[], hysteresis: number): string {
  const curve = extendedCurve(points);
  const shifted = curve.map((point) => ({ temperature: point.temperature - hysteresis, duty: point.duty })).reverse();
  return toPolyline([...curve, ...shifted]);
}
