import type { ColorPalette } from '../../api/contracts.ts';

export type { ColorPalette };

export const MAX_SAVED_PALETTES = 32;
export const MAX_PALETTE_NAME_LENGTH = 32;
export const MAX_PALETTE_COLORS = 8;

export const COLOR_PALETTES = [
  { id: 'cyberpunk', name: 'Cyberpunk', colors: ['#00F5FF', '#FF0080', '#FFE600'] },
  { id: 'synthwave', name: 'Synthwave', colors: ['#FF00A0', '#7928CA', '#2B094D', '#00DFD8'] },
  { id: 'northern-lights', name: 'Northern Lights', colors: ['#00FF9D', '#00C8FF', '#7B2FFF', '#062B45'] },
  { id: 'inferno', name: 'Inferno', colors: ['#1A0000', '#B81400', '#FF5E00', '#FFD000'] },
  { id: 'arctic-ice', name: 'Arctic Ice', colors: ['#E0F7FF', '#60D5FF', '#0077FF'] },
  { id: 'matrix', name: 'Matrix', colors: ['#00FF41', '#008F11', '#B8FFD0'] },
  { id: 'rog-crimson', name: 'ROG Crimson', colors: ['#FF0026', '#6E0010', '#FF5500'] },
  { id: 'vaporwave', name: 'Vaporwave', colors: ['#FF71CE', '#01CDFE', '#05FFA1', '#B967FF'] },
] as const satisfies readonly ColorPalette[];

/** Why `name` cannot be saved, or undefined when the trimmed name is free to use. */
export function paletteNameError(
  name: string,
  palettes: readonly Pick<ColorPalette, 'id' | 'name'>[],
  ignoreId?: string,
): string | undefined {
  const trimmed = name.trim();
  if (trimmed.length < 1 || trimmed.length > MAX_PALETTE_NAME_LENGTH) {
    return 'Use 1–32 characters.';
  }

  const duplicate = palettes.some(
    (palette) => palette.id !== ignoreId && palette.name.trim().toLocaleLowerCase() === trimmed.toLocaleLowerCase(),
  );
  return duplicate ? 'A palette with this name already exists.' : undefined;
}

/** Current effect colors can be stored when the list is non-empty and inside both the effect and palette caps. */
export function canSavePaletteColors(colors: readonly string[], min: number, max: number): boolean {
  return colors.length >= 1 && colors.length >= min && colors.length <= max && colors.length <= MAX_PALETTE_COLORS;
}

/**
 * Slices or repeats `colors` until the length sits in `[minColors, maxColors]`.
 * `maxColors` is a hard cap; when `minColors` is larger, the result still stops at the cap.
 */
export function fitPaletteColors(colors: readonly string[], minColors: number, maxColors: number): string[] {
  const source = colors.length > 0 ? colors : ['#FFFFFF'];
  const cap = Math.max(0, maxColors);
  const floor = Math.min(Math.max(0, minColors), cap);
  const fitted: string[] = [];
  for (const color of source) {
    if (fitted.length >= cap) {
      break;
    }
    fitted.push(color);
  }
  let index = 0;
  while (fitted.length < floor) {
    fitted.push(source[index % source.length] ?? '#FFFFFF');
    index += 1;
  }
  return fitted;
}
