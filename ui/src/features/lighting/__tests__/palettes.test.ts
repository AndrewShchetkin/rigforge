import { describe, expect, it } from 'vitest';
import { canSavePaletteColors, paletteNameError } from '../palettes.ts';

const saved = [
  { id: 'sunset', name: 'Sunset' },
  { id: 'dusk', name: 'Закат' },
];

describe('paletteNameError', () => {
  it('rejects a blank or oversized name', () => {
    expect(paletteNameError('   ', saved)).toBe('Use 1–32 characters.');
    expect(paletteNameError('n'.repeat(33), saved)).toBe('Use 1–32 characters.');
    expect(paletteNameError('n'.repeat(32), saved)).toBeUndefined();
  });

  it('rejects a duplicate name without regard to case or surrounding spaces', () => {
    expect(paletteNameError('  sunset ', saved)).toBe('A palette with this name already exists.');
    expect(paletteNameError('закат', saved)).toBe('A palette with this name already exists.');
  });

  it('allows the palette to keep its own name', () => {
    expect(paletteNameError('Sunset', saved, 'sunset')).toBeUndefined();
    expect(paletteNameError('Ocean', saved, 'sunset')).toBeUndefined();
  });
});

describe('canSavePaletteColors', () => {
  it('requires a non-empty list inside the effect range and the palette cap', () => {
    expect(canSavePaletteColors([], 0, 8)).toBe(false);
    expect(canSavePaletteColors(['#FF0000'], 2, 8)).toBe(false);
    expect(canSavePaletteColors(['#FF0000', '#00FF00'], 2, 4)).toBe(true);
    expect(canSavePaletteColors(['#FFFFFF'], 1, 1)).toBe(true);
    expect(canSavePaletteColors(Array.from({ length: 8 }, () => '#FFFFFF'), 1, 8)).toBe(true);
    expect(canSavePaletteColors(Array.from({ length: 9 }, () => '#FFFFFF'), 1, 9)).toBe(false);
  });
});
