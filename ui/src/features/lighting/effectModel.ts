import type { EffectDescriptorDto, EffectSettings } from '../../api/contracts.ts';
import { roleSelector } from '../../lib/sensors.ts';

const PALETTE = ['#FF0080', '#00C8FF', '#8B5CF6', '#22C55E', '#F59E0B', '#EF4444'] as const;

/** Color suggested for the `index`-th slot when the user adds one. */
export function suggestedColor(index: number): string {
  return PALETTE[index % PALETTE.length] ?? '#FFFFFF';
}

/** Patch for switching to another effect: fits the colors into its limits and picks a sensor if it needs one. */
export function switchEffectPatch(current: EffectSettings, descriptor: EffectDescriptorDto): Partial<EffectSettings> {
  const patch: Partial<EffectSettings> = { effectId: descriptor.id };
  if (descriptor.usesColors) {
    const colors = current.colors.slice(0, Math.max(descriptor.maxColors, descriptor.minColors));
    while (colors.length < descriptor.minColors) {
      colors.push(suggestedColor(colors.length));
    }
    patch.colors = colors;
  }
  if (descriptor.usesSensor && current.sensorSelector === null) {
    patch.sensorSelector = roleSelector('CpuTemperature');
  }
  return patch;
}
