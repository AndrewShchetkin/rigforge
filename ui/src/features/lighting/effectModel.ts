import type {
  EffectDescriptorDto,
  EffectParameterDefinitionDto,
  EffectParameterValue,
  EffectSettings,
} from '../../api/contracts.ts';

const PALETTE = ['#FF0080', '#00C8FF', '#8B5CF6', '#22C55E', '#F59E0B', '#EF4444'] as const;

/** Color suggested for the `index`-th slot when the user adds one. */
export function suggestedColor(index: number): string {
  return PALETTE[index % PALETTE.length] ?? '#FFFFFF';
}

function fitsKind(value: EffectParameterValue | undefined, parameter: EffectParameterDefinitionDto): boolean {
  if (value === undefined) {
    return false;
  }
  switch (parameter.kind) {
    case 'ColorList':
      return Array.isArray(value);
    case 'FloatRange':
      return typeof value === 'number';
    case 'SensorSelector':
      return typeof value === 'string';
  }
}

/** Patch for switching to another effect: keeps shared parameter values and fills the rest from the schema. */
export function switchEffectPatch(current: EffectSettings, descriptor: EffectDescriptorDto): Partial<EffectSettings> {
  const parameters: Record<string, EffectParameterValue> = {};
  for (const parameter of descriptor.parameters) {
    const existing = current.parameters[parameter.key];
    if (!fitsKind(existing, parameter)) {
      parameters[parameter.key] = Array.isArray(parameter.defaultValue) ? [...parameter.defaultValue] : parameter.defaultValue;
      continue;
    }

    if (parameter.kind === 'ColorList' && Array.isArray(existing)) {
      const colors = existing.slice(0, Math.max(parameter.maxColors, parameter.minColors));
      while (colors.length < parameter.minColors) {
        colors.push(suggestedColor(colors.length));
      }
      parameters[parameter.key] = colors;
    } else {
      parameters[parameter.key] = existing as EffectParameterValue;
    }
  }
  return { effectId: descriptor.id, parameters };
}
