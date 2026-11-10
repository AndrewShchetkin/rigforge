import { describe, expect, it } from 'vitest';
import type { EffectDescriptorDto, EffectParameterDefinitionDto, EffectSettings } from '../../../api/contracts.ts';
import { applyCuratedPalette, isLightingEnabled, suggestedColor, switchEffectPatch } from '../effectModel.ts';
import { COLOR_PALETTES, type ColorPalette } from '../palettes.ts';

function parameter(
  overrides: Pick<EffectParameterDefinitionDto, 'key' | 'kind' | 'defaultValue'> & Partial<EffectParameterDefinitionDto>,
): EffectParameterDefinitionDto {
  return {
    label: overrides.key,
    min: null,
    max: null,
    step: null,
    unit: null,
    minColors: 0,
    maxColors: 0,
    ...overrides,
  };
}

function descriptor(
  id: string,
  parameters: EffectParameterDefinitionDto[],
  category: EffectDescriptorDto['category'] = 'Flowing',
): EffectDescriptorDto {
  return {
    id,
    name: id,
    description: '',
    isAnimated: true,
    category,
    usesAudio: false,
    parameters,
  };
}

function settings(effectId: string, parameters: EffectSettings['parameters']): EffectSettings {
  return { effectId, enabled: true, brightness: 0.8, targetFps: 60, parameters };
}

function palette(id: string): ColorPalette {
  const match = COLOR_PALETTES.find((item) => item.id === id);
  if (!match) {
    throw new Error(`Missing palette ${id}`);
  }
  return match;
}

describe('switchEffectPatch', () => {
  const colors = ['#112233', '#445566', '#778899'];

  it('keeps compatible colors and speed, and fills new parameter defaults', () => {
    const current = settings('gradient', { colors: [...colors], speed: 1.5, unknown: true });
    const next = descriptor('pulse', [
      parameter({ key: 'colors', kind: 'ColorList', defaultValue: ['#FFFFFF'], minColors: 2, maxColors: 2 }),
      parameter({ key: 'speed', kind: 'FloatRange', defaultValue: 1, min: 0.05, max: 10 }),
      parameter({ key: 'mirror', kind: 'Boolean', defaultValue: false }),
      parameter({ key: 'count', kind: 'IntRange', defaultValue: 4, min: 1, max: 16 }),
      parameter({
        key: 'style',
        kind: 'Select',
        defaultValue: 'soft',
        options: [
          { value: 'soft', label: 'Soft' },
          { value: 'hard', label: 'Hard' },
        ],
      }),
    ]);

    const patch = switchEffectPatch(current, next);
    expect(patch.effectId).toBe('pulse');
    expect(patch).not.toHaveProperty('brightness');
    expect(patch).not.toHaveProperty('enabled');
    expect(patch.parameters).toEqual({
      colors: ['#112233', '#445566'],
      speed: 1.5,
      mirror: false,
      count: 4,
      style: 'soft',
    });
  });

  it('pads a short color list and replaces values whose kind does not fit', () => {
    const current = settings('solid', {
      colors: ['#112233'],
      count: 1.5,
      mirror: 1,
      style: 'nope',
      speed: Number.NaN,
    });
    const next = descriptor('rich', [
      parameter({ key: 'colors', kind: 'ColorList', defaultValue: ['#FFFFFF'], minColors: 2, maxColors: 4 }),
      parameter({ key: 'count', kind: 'IntRange', defaultValue: 8, min: 1, max: 16 }),
      parameter({ key: 'mirror', kind: 'Boolean', defaultValue: true }),
      parameter({
        key: 'style',
        kind: 'Select',
        defaultValue: 'wide',
        options: [
          { value: 'wide', label: 'Wide' },
          { value: 'narrow', label: 'Narrow' },
        ],
      }),
      parameter({ key: 'speed', kind: 'FloatRange', defaultValue: 0.5 }),
    ]);

    const patch = switchEffectPatch(current, next);
    expect(patch.parameters).toEqual({
      colors: ['#112233', suggestedColor(1)],
      count: 8,
      mirror: true,
      style: 'wide',
      speed: 0.5,
    });
  });

  it('does not mutate the previous color list', () => {
    const original = ['#112233', '#445566'];
    const patch = switchEffectPatch(
      settings('gradient', { colors: original }),
      descriptor('solid', [parameter({ key: 'colors', kind: 'ColorList', defaultValue: ['#FFFFFF'], minColors: 1, maxColors: 1 })]),
    );
    const nextColors = patch.parameters?.colors;
    expect(Array.isArray(nextColors)).toBe(true);
    if (Array.isArray(nextColors)) {
      nextColors.push('#000000');
    }
    expect(original).toEqual(['#112233', '#445566']);
  });

  it('keeps a select value that is still one of the options', () => {
    const patch = switchEffectPatch(
      settings('old', { style: 'hard' }),
      descriptor('new', [
        parameter({
          key: 'style',
          kind: 'Select',
          defaultValue: 'soft',
          options: [
            { value: 'soft', label: 'Soft' },
            { value: 'hard', label: 'Hard' },
          ],
        }),
      ]),
    );
    expect(patch.parameters?.style).toBe('hard');
  });
});

describe('applyCuratedPalette', () => {
  const current = settings('gradient', { colors: ['#000000'], speed: 1 });
  const colors = parameter({ key: 'colors', kind: 'ColorList', defaultValue: ['#FFFFFF'], minColors: 2, maxColors: 8 });

  it('keeps a palette that already fits the color range', () => {
    expect(applyCuratedPalette(current, colors, palette('cyberpunk')).parameters?.colors).toEqual([
      '#00F5FF',
      '#FF0080',
      '#FFE600',
    ]);
  });

  it('slices palettes that are longer than maxColors', () => {
    expect(
      applyCuratedPalette(current, { ...colors, maxColors: 3 }, palette('synthwave')).parameters?.colors,
    ).toEqual(['#FF00A0', '#7928CA', '#2B094D']);
    expect(applyCuratedPalette(current, { ...colors, minColors: 2, maxColors: 2 }, palette('matrix')).parameters?.colors).toEqual([
      '#00FF41',
      '#008F11',
    ]);
  });

  it('repeats colors until minColors is satisfied', () => {
    expect(
      applyCuratedPalette(current, { ...colors, minColors: 4, maxColors: 8 }, palette('arctic-ice')).parameters?.colors,
    ).toEqual(['#E0F7FF', '#60D5FF', '#0077FF', '#E0F7FF']);
  });
});

describe('isLightingEnabled', () => {
  it('treats a missing flag as on and false as blackout', () => {
    expect(isLightingEnabled({ enabled: true })).toBe(true);
    expect(isLightingEnabled({ enabled: false })).toBe(false);
    expect(isLightingEnabled({ enabled: undefined as unknown as boolean })).toBe(true);
  });
});
