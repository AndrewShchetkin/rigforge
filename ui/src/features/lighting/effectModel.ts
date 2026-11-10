import type {
  EffectDescriptorDto,
  EffectParameterDefinitionDto,
  EffectParameterValue,
  EffectSettings,
} from '../../api/contracts.ts';
import { fitPaletteColors, type ColorPalette } from './palettes.ts';

const PALETTE = ['#FF0080', '#00C8FF', '#8B5CF6', '#22C55E', '#F59E0B', '#EF4444'] as const;

/** Color suggested for the `index`-th slot when the user adds one. */
export function suggestedColor(index: number): string {
  return PALETTE[index % PALETTE.length] ?? '#FFFFFF';
}

/** Older profiles omit `enabled`; the daemon and the form both treat that as lighting on. */
export function isLightingEnabled(effect: Pick<EffectSettings, 'enabled'>): boolean {
  return effect.enabled !== false;
}

function fitsKind(value: EffectParameterValue | undefined, parameter: EffectParameterDefinitionDto): boolean {
  if (value === undefined) {
    return false;
  }
  switch (parameter.kind) {
    case 'ColorList':
      return Array.isArray(value);
    case 'FloatRange':
      return typeof value === 'number' && Number.isFinite(value);
    case 'IntRange':
      return typeof value === 'number' && Number.isInteger(value);
    case 'Boolean':
      return typeof value === 'boolean';
    case 'Select':
      return typeof value === 'string' && (parameter.options ?? []).some((option) => option.value === value);
    case 'SensorSelector':
      return typeof value === 'string';
    default: {
      const unhandled: never = parameter.kind;
      void unhandled;
      return false;
    }
  }
}

function copyDefault(value: EffectParameterValue): EffectParameterValue {
  return Array.isArray(value) ? [...value] : value;
}

/** Keeps as many current colors as the new schema allows, then pads up to `minColors`. */
function fitExistingColors(existing: readonly string[], minColors: number, maxColors: number): string[] {
  const cap = Math.max(0, maxColors);
  const floor = Math.min(Math.max(0, minColors), cap);
  const colors = existing.slice(0, cap);
  while (colors.length < floor) {
    colors.push(suggestedColor(colors.length));
  }
  return colors;
}

/** Patch for switching to another effect: keeps shared parameter values and fills the rest from the schema. */
export function switchEffectPatch(current: EffectSettings, descriptor: EffectDescriptorDto): Partial<EffectSettings> {
  const parameters: Record<string, EffectParameterValue> = {};
  for (const parameter of descriptor.parameters) {
    const existing = current.parameters[parameter.key];
    if (existing === undefined || !fitsKind(existing, parameter)) {
      parameters[parameter.key] = copyDefault(parameter.defaultValue);
      continue;
    }

    if (parameter.kind === 'ColorList' && Array.isArray(existing)) {
      parameters[parameter.key] = fitExistingColors(existing, parameter.minColors, parameter.maxColors);
    } else {
      parameters[parameter.key] = existing;
    }
  }
  return { effectId: descriptor.id, parameters };
}

/** Applies a curated palette to one color-list parameter, fitted to that parameter's color count. */
export function applyCuratedPalette(
  current: EffectSettings,
  parameter: Pick<EffectParameterDefinitionDto, 'key' | 'minColors' | 'maxColors'>,
  palette: ColorPalette,
): Partial<EffectSettings> {
  return {
    parameters: {
      ...current.parameters,
      [parameter.key]: fitPaletteColors(palette.colors, parameter.minColors, parameter.maxColors),
    },
  };
}
