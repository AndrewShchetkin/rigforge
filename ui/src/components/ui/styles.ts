/** Shared look of text-like form controls (inputs and selects). */
export const controlClass =
  'w-full rounded-md border border-zinc-700 bg-zinc-950/80 px-2.5 py-1.5 text-sm text-zinc-100 transition-colors ' +
  'hover:border-zinc-600 focus:border-violet-500 disabled:cursor-not-allowed disabled:opacity-50 ' +
  'aria-invalid:border-red-500/80';

/** Id of the hint/error paragraph rendered under a field, for `aria-describedby`. */
export function helpId(fieldId: string): string {
  return `${fieldId}-help`;
}
