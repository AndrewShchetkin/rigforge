import type { FanChannelDto, FanCurve, FanCurvePoint } from '../../api/contracts.ts';
import { clamp } from '../../lib/math.ts';
import { roleSelector } from '../../lib/sensors.ts';

export const CURVE_LIMITS = {
  minTemperature: 20,
  maxTemperature: 100,
  /** Temperatures must stay strictly increasing by at least this much. */
  minGap: 1,
  minPoints: 2,
  maxPoints: 16,
} as const;

/** Curve id convention shared with the daemon (also used as `FanStatusDto.curveId`). */
export function curveIdFor(deviceId: string, channelId: string): string {
  return `${deviceId}:${channelId}`;
}

function toPoints(pairs: readonly (readonly [number, number])[]): FanCurvePoint[] {
  return pairs.map(([temperature, duty]) => ({ temperature, duty }));
}

/** Starting curve when the user enables control of a channel. */
export function presetCurve(deviceId: string, channel: FanChannelDto): FanCurve {
  const common = {
    id: curveIdFor(deviceId, channel.id),
    deviceId,
    channelId: channel.id,
    enabled: true,
    mode: 'Curve',
    fixedDuty: 50,
    maxDuty: 100,
    temperatureSmoothingSeconds: 0,
  } as const;

  if (channel.kind === 'Pump') {
    return {
      ...common,
      sensorSelectors: [roleSelector('LiquidTemperature')],
      points: toPoints([
        [25, 40],
        [30, 50],
        [35, 70],
        [40, 90],
        [45, 100],
      ]),
      hysteresisCelsius: 1,
      rampUpPercentPerSecond: 10,
      rampDownPercentPerSecond: 5,
      minDuty: channel.minDutyPercent,
    };
  }
  return {
    ...common,
    sensorSelectors: [roleSelector('CpuTemperature')],
    points: toPoints([
      [30, 25],
      [50, 40],
      [65, 60],
      [75, 80],
      [85, 100],
    ]),
    hysteresisCelsius: 2,
    rampUpPercentPerSecond: 10,
    rampDownPercentPerSecond: 3,
    minDuty: 0,
  };
}

/** Moves a point to the target, staying between its neighbours (≥ 1 °C apart) and inside the axes. */
export function movePoint(points: readonly FanCurvePoint[], index: number, target: FanCurvePoint): FanCurvePoint[] {
  const previous = points[index - 1];
  const next = points[index + 1];
  const low = previous ? previous.temperature + CURVE_LIMITS.minGap : CURVE_LIMITS.minTemperature;
  const high = next ? next.temperature - CURVE_LIMITS.minGap : CURVE_LIMITS.maxTemperature;
  const moved = {
    temperature: clamp(Math.round(target.temperature), low, high),
    duty: clamp(Math.round(target.duty), 0, 100),
  };
  return points.map((point, position) => (position === index ? moved : point));
}

/** Adds a point in temperature order; null when the curve is full or the spot is taken. */
export function insertPoint(points: readonly FanCurvePoint[], target: FanCurvePoint): FanCurvePoint[] | null {
  if (points.length >= CURVE_LIMITS.maxPoints) {
    return null;
  }
  const temperature = Math.round(clamp(target.temperature, CURVE_LIMITS.minTemperature, CURVE_LIMITS.maxTemperature));
  const duty = Math.round(clamp(target.duty, 0, 100));
  if (points.some((point) => Math.abs(point.temperature - temperature) < CURVE_LIMITS.minGap)) {
    return null;
  }
  return [...points, { temperature, duty }].sort((a, b) => a.temperature - b.temperature);
}

/** Adds a point halfway to the next one (or 5 °C after the last). */
export function insertAfter(points: readonly FanCurvePoint[], index: number): FanCurvePoint[] | null {
  const point = points[index];
  if (!point) {
    return null;
  }
  const next = points[index + 1];
  const target = next
    ? { temperature: (point.temperature + next.temperature) / 2, duty: (point.duty + next.duty) / 2 }
    : { temperature: point.temperature + 5, duty: point.duty };
  return insertPoint(points, target);
}

/** Removes a point; null when the curve would drop below the minimum number of points. */
export function removePoint(points: readonly FanCurvePoint[], index: number): FanCurvePoint[] | null {
  return points.length <= CURVE_LIMITS.minPoints ? null : points.filter((_, position) => position !== index);
}
