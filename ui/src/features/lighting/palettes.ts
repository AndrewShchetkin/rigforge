export interface ColorPalette {
  id: string;
  name: string;
  colors: readonly string[];
}

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
