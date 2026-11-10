import { describe, expect, it } from 'vitest';
import type { VirtualCanvas, ZonePlacement } from '../../../api/contracts.ts';
import { placementKey } from '../../../lib/zones.ts';
import {
  CANVAS_PRESETS,
  FIT_VIEW,
  addSegment,
  autoArrange,
  clampZoom,
  nudgeStep,
  removeSegment,
  resizeCanvas,
  segmentLedOffsets,
  snapPlacement,
  snapValue,
  zoomFromWheel,
  zoomViewAt,
  type SnapStep,
} from '../layoutModel.ts';

function zone(partial: Partial<ZonePlacement> = {}): ZonePlacement {
  return {
    deviceId: 'device',
    zoneId: 'zone',
    x: 0,
    y: 0,
    width: 40,
    height: 16,
    rotation: 0,
    enabled: true,
    ledCount: null,
    ...partial,
  };
}

function board(placements: ZonePlacement[], width = 320, height = 180): VirtualCanvas {
  return { width, height, placements };
}

function at<T>(items: readonly T[], index: number): T {
  const item = items[index];
  if (item === undefined) {
    throw new Error(`Expected an item at index ${index}.`);
  }
  return item;
}

function overlaps(a: ZonePlacement, b: ZonePlacement): boolean {
  return a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
}

function ledSum(placements: readonly ZonePlacement[], deviceId = 'device', zoneId = 'zone'): number {
  return placements
    .filter((placement) => placement.deviceId === deviceId && placement.zoneId === zoneId)
    .reduce((sum, placement) => sum + (placement.ledCount ?? 0), 0);
}

describe('snapValue', () => {
  const cases: ReadonlyArray<readonly [number, SnapStep, number]> = [
    [10.126, 0, 10.13],
    [10.124, 0, 10.12],
    [7, 0, 7],
    [0, 5, 0],
    [2, 5, 0],
    [3, 5, 5],
    [12, 5, 10],
    [13, 5, 15],
    [15, 5, 15],
    [4, 10, 0],
    [5, 10, 10],
    [14, 10, 10],
    [15, 10, 20],
    [20, 10, 20],
  ];

  it('snaps free placement to hundredths and 5 px / 10 px grids to the nearest line', () => {
    for (const [value, step, expected] of cases) {
      expect(snapValue(value, step), `snapValue(${value}, ${step})`).toBe(expected);
    }
    expect(snapValue(Number.NaN, 5)).toBe(0);
  });

  it('uses the grid as the arrow-key step and keeps free nudges at 1 / 10', () => {
    expect(nudgeStep(0, false)).toBe(1);
    expect(nudgeStep(0, true)).toBe(10);
    expect(nudgeStep(5, false)).toBe(5);
    expect(nudgeStep(5, true)).toBe(10);
    expect(nudgeStep(10, true)).toBe(20);
  });

  it('snaps a drag without resizing it, and clamps the snapped origin inside the canvas', () => {
    const box = zone({ x: 12, y: 7, width: 33, height: 19 });
    expect(snapPlacement(box, { x: 12, y: 7 }, 320, 180, 5)).toMatchObject({
      x: 10,
      y: 5,
      width: 33,
      height: 19,
      rotation: 0,
    });

    const edge = snapPlacement(zone({ x: 300, y: 0, width: 40, height: 20 }), { x: 308, y: 4 }, 320, 180, 5);
    expect(edge.x).toBe(280);
    expect(edge.y).toBe(5);
    expect(edge.width).toBe(40);

    const turned = snapPlacement(zone({ width: 80, height: 40, rotation: 30 }), { x: -100, y: -100 }, 200, 100, 0);
    expect(turned).toMatchObject({ x: -20, y: -10, rotation: 30 });
  });

  it('snaps resized edges onto the grid and never below the minimum size', () => {
    expect(snapPlacement(zone({ width: 33, height: 19 }), { x: 12, y: 7, width: 33, height: 19 }, 320, 180, 10)).toMatchObject({
      x: 10,
      y: 10,
      width: 30,
      height: 20,
    });
    expect(snapPlacement(zone(), { width: 3, height: 4 }, 320, 180, 5)).toMatchObject({ width: 5, height: 5 });
    expect(snapPlacement(zone(), { width: 10.5, height: 8.25 }, 100, 100, 0)).toMatchObject({ width: 10.5, height: 8.25 });
  });
});

describe('autoArrange', () => {
  it('returns an empty canvas unchanged', () => {
    const empty = board([]);
    expect(autoArrange(empty)).toBe(empty);
  });

  it('centers a single zone and clears its rotation', () => {
    const next = autoArrange(board([zone({ x: 4, y: 9, width: 20, height: 10, rotation: 45 })], 200, 100));
    expect(at(next.placements, 0)).toMatchObject({ x: 90, y: 45, width: 20, height: 10, rotation: 0 });
  });

  it('packs zones into a non-overlapping grid that stays inside the canvas', () => {
    const placements = Array.from({ length: 7 }, (_, index) =>
      zone({
        zoneId: `z${index}`,
        x: index * 5,
        y: index * 3,
        width: 80 + index * 10,
        height: 30 + index * 4,
        rotation: index * 15,
        enabled: index % 2 === 0,
      }),
    );
    const next = autoArrange(board(placements));
    expect(next.placements).toHaveLength(7);
    expect(next.placements.map((placement) => placement.zoneId)).toEqual(placements.map((placement) => placement.zoneId));

    for (const placement of next.placements) {
      expect(placement.rotation).toBe(0);
      expect(placement.x).toBeGreaterThanOrEqual(0);
      expect(placement.y).toBeGreaterThanOrEqual(0);
      expect(placement.x + placement.width).toBeLessThanOrEqual(next.width + 0.001);
      expect(placement.y + placement.height).toBeLessThanOrEqual(next.height + 0.001);
    }

    for (let left = 0; left < next.placements.length; left += 1) {
      for (let right = left + 1; right < next.placements.length; right += 1) {
        expect(overlaps(at(next.placements, left), at(next.placements, right))).toBe(false);
      }
    }
  });
});

describe('segments', () => {
  it('splits within the header budget, then steals LEDs instead of exceeding it', () => {
    const start = board([zone()]);
    const key = placementKey(at(start.placements, 0));
    const added = addSegment(start, key, 12, 20);
    if (!added) {
      throw new Error('Expected the first split.');
    }
    expect(start.placements).toHaveLength(1);
    expect(at(added.canvas.placements, 0)).toMatchObject({ segmentId: 'seg1', ledCount: 12 });
    expect(at(added.canvas.placements, 1)).toMatchObject({ segmentId: 'seg2', ledCount: 8 });
    expect(ledSum(added.canvas.placements)).toBe(20);
    expect(added.selectKey).toBe(placementKey(at(added.canvas.placements, 1)));

    const again = addSegment(added.canvas, added.selectKey, 12, 20);
    if (!again) {
      throw new Error('Expected a split of the full header.');
    }
    expect(again.canvas.placements.map((placement) => placement.ledCount)).toEqual([12, 4, 4]);
    expect(ledSum(again.canvas.placements)).toBeLessThanOrEqual(20);

    const offsets = segmentLedOffsets(again.canvas.placements);
    expect(offsets.get(placementKey(at(again.canvas.placements, 0)))).toBe(0);
    expect(offsets.get(placementKey(at(again.canvas.placements, 1)))).toBe(12);
    expect(offsets.get(placementKey(at(again.canvas.placements, 2)))).toBe(16);

    const merged = removeSegment(again.canvas, again.selectKey);
    if (!merged) {
      throw new Error('Expected the new segment to merge back.');
    }
    expect(merged.canvas.placements.map((placement) => placement.ledCount)).toEqual([12, 8]);
    expect(ledSum(merged.canvas.placements)).toBe(20);

    const rest = removeSegment(merged.canvas, merged.selectKey ?? '');
    if (!rest) {
      throw new Error('Expected the remaining pair to merge.');
    }
    expect(at(rest.canvas.placements, 0).ledCount).toBe(20);
    expect(removeSegment(rest.canvas, rest.selectKey ?? '')).toBeNull();
  });

  it('refuses to split a one-LED header or an unknown zone, and leaves the source canvas alone', () => {
    const single = zone({ segmentId: 'seg1', ledCount: 1 });
    const source = board([single]);
    expect(addSegment(source, placementKey(single), 1, 1)).toBeNull();
    expect(at(source.placements, 0).ledCount).toBe(1);
    expect(addSegment(source, 'missing', 10, 20)).toBeNull();
    expect(removeSegment(board([zone()]), placementKey(zone()))).toBeNull();
  });

  it('gives a removed segment’s LEDs to the following part and keeps chain order', () => {
    const first = zone({ segmentId: 'seg1', ledCount: 10, name: 'Segment 1' });
    const middle = zone({ segmentId: 'seg2', ledCount: 6, name: 'Segment 2', x: 48 });
    const last = zone({ segmentId: 'seg3', ledCount: 4, name: 'Segment 3', x: 96 });
    const removed = removeSegment(board([first, middle, last]), placementKey(middle));
    if (!removed) {
      throw new Error('Expected the middle segment to be removed.');
    }
    expect(removed.canvas.placements.map((placement) => placement.ledCount)).toEqual([10, 10]);
    expect(removed.selectKey).toBe(placementKey(last));
    expect(ledSum(removed.canvas.placements)).toBe(20);
  });

  it('counts LED offsets in placement order, separately for each physical zone', () => {
    const stripA = zone({ deviceId: 'a', zoneId: 'strip', segmentId: 'seg1', ledCount: 4 });
    const fan = zone({ deviceId: 'b', zoneId: 'fan', segmentId: 'seg1', ledCount: 6 });
    const stripB = zone({ deviceId: 'a', zoneId: 'strip', segmentId: 'seg2', ledCount: 3 });
    const offsets = segmentLedOffsets([stripA, fan, stripB]);
    expect(offsets.get(placementKey(stripA))).toBe(0);
    expect(offsets.get(placementKey(fan))).toBe(0);
    expect(offsets.get(placementKey(stripB))).toBe(4);
  });

  it('clamps a new segment that would be created past the canvas edge', () => {
    const source = board([zone({ x: 80, y: 10, width: 30, height: 16 })], 100, 80);
    const added = addSegment(source, placementKey(at(source.placements, 0)), 10, 40);
    if (!added) {
      throw new Error('Expected a segment.');
    }
    expect(at(added.canvas.placements, 1)).toMatchObject({ x: 70, y: 10, ledCount: 8 });
    expect(at(source.placements, 0).x).toBe(80);
  });
});

describe('canvas presets', () => {
  it('offers the studio sizes', () => {
    expect(CANVAS_PRESETS.map((preset) => preset.label)).toEqual([
      '320 × 180 (16:9)',
      '240 × 240 (1:1 Case)',
      '400 × 225 (XL)',
      '480 × 270 (HD)',
    ]);
    expect(CANVAS_PRESETS.map((preset) => [preset.width, preset.height])).toEqual([
      [320, 180],
      [240, 240],
      [400, 225],
      [480, 270],
    ]);
  });

  it('pulls an upright zone fully inside and lets a rotated zone overhang by a quarter', () => {
    const source = board(
      [
        zone({ x: 400, y: 200, width: 100, height: 50, rotation: 0, ledCount: 6, segmentId: 'seg1' }),
        zone({ deviceId: 'fan', zoneId: 'ring', x: 400, y: 200, width: 80, height: 40, rotation: 90 }),
        zone({ deviceId: 'logo', zoneId: 'point', x: -40, y: -30, width: 80, height: 40, rotation: 15 }),
      ],
      480,
      270,
    );
    const next = resizeCanvas(source, 240, 240);
    expect(next).toMatchObject({ width: 240, height: 240 });
    expect(at(next.placements, 0)).toMatchObject({ x: 140, y: 190, width: 100, height: 50, ledCount: 6, segmentId: 'seg1' });
    expect(at(next.placements, 1)).toMatchObject({ x: 236, y: 200, rotation: 90 });
    expect(at(next.placements, 2)).toMatchObject({ x: -20, y: -10, rotation: 15 });
    expect(at(source.placements, 0).x).toBe(400);
  });

  it('treats a full turn as upright, keeps in-range decimals, and parks an oversized zone at the origin', () => {
    expect(at(resizeCanvas(board([zone({ x: -10, y: 5, rotation: 360 })]), 200, 100).placements, 0)).toMatchObject({
      x: 0,
      y: 5,
      rotation: 360,
    });
    expect(at(resizeCanvas(board([zone({ x: 10.25, y: 4.5, width: 20, height: 10 })]), 320, 180).placements, 0)).toMatchObject({
      x: 10.25,
      y: 4.5,
    });
    expect(at(resizeCanvas(board([zone({ x: 30, width: 300, height: 20 })]), 240, 180).placements, 0)).toMatchObject({
      x: 0,
      width: 300,
    });
  });

  it('clamps every preset without dropping LED counts', () => {
    const source = board(
      [
        zone({ x: 460, y: 240, width: 80, height: 40, rotation: 0, ledCount: 6, segmentId: 'seg1' }),
        zone({ deviceId: 'other', zoneId: 'fan', x: -80, y: 300, width: 40, height: 40, rotation: 20, ledCount: 12 }),
      ],
      480,
      270,
    );
    for (const preset of CANVAS_PRESETS) {
      const next = resizeCanvas(source, preset.width, preset.height);
      expect(next.width).toBe(preset.width);
      expect(next.height).toBe(preset.height);
      const straight = at(next.placements, 0);
      const angled = at(next.placements, 1);
      expect(straight.ledCount).toBe(6);
      expect(straight.segmentId).toBe('seg1');
      expect(straight.x).toBeGreaterThanOrEqual(0);
      expect(straight.y).toBeGreaterThanOrEqual(0);
      expect(straight.x).toBeLessThanOrEqual(Math.max(0, preset.width - straight.width));
      expect(straight.y).toBeLessThanOrEqual(Math.max(0, preset.height - straight.height));
      expect(angled.ledCount).toBe(12);
      expect(angled.rotation).toBe(20);
      expect(angled.x).toBeGreaterThanOrEqual(-angled.width * 0.25);
      expect(angled.x).toBeLessThanOrEqual(preset.width - 4);
      expect(angled.y).toBeGreaterThanOrEqual(-angled.height * 0.25);
      expect(angled.y).toBeLessThanOrEqual(preset.height - 4);
    }
  });
});

describe('zoomViewAt', () => {
  it('stays inside 0.5×–3× and keeps the pointer pinned to the same canvas point', () => {
    expect(clampZoom(9)).toBe(3);
    expect(clampZoom(0)).toBe(0.5);
    expect(FIT_VIEW).toEqual({ zoom: 1, panX: 0, panY: 0 });
    const next = zoomViewAt({ zoom: 1, panX: 0, panY: 0 }, 2, 100, 40, 2);
    expect(next.zoom).toBe(2);
    expect(next.panX + 50 * 4).toBeCloseTo(100);
    expect(next.panY + 20 * 4).toBeCloseTo(40);
    expect(zoomFromWheel(1, 100, 0)).toBeLessThan(1);
    expect(zoomFromWheel(1, -100, 0)).toBeGreaterThan(1);
    expect(zoomFromWheel(3, -500, 0)).toBe(3);
    expect(zoomFromWheel(0.5, 500, 0)).toBe(0.5);
  });
});
