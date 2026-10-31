import { memo, useMemo } from 'react';
import { Circle } from 'react-konva';
import type { LedPositionDto } from '../../api/contracts.ts';
import { rgbTripletAt } from '../../lib/base64.ts';
import { useLedPreview } from '../../state/hooks.ts';
import { ledRadius } from './layoutModel.ts';

const NO_PREVIEW_COLOR = '#52525b';

interface ZoneLedsProps {
  zoneKey: string;
  leds: readonly LedPositionDto[];
  width: number;
  height: number;
  scale: number;
  dimmed: boolean;
}

/** LED dots of one zone. The only part of the stage that re-renders for each ~20 Hz preview frame. */
export const ZoneLeds = memo(function ZoneLeds({ zoneKey, leds, width, height, scale, dimmed }: ZoneLedsProps) {
  const colors = useLedPreview(zoneKey);
  const radius = useMemo(() => ledRadius(leds, width, height, scale), [leds, width, height, scale]);

  return leds.map((led, index) => (
    <Circle
      key={index}
      x={led.x * width}
      y={led.y * height}
      radius={radius}
      fill={(colors ? rgbTripletAt(colors, index) : null) ?? NO_PREVIEW_COLOR}
      opacity={dimmed ? 0.35 : 1}
      listening={false}
      perfectDrawEnabled={false}
    />
  ));
});
