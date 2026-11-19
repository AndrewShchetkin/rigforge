/** Shared look of text-like form controls (inputs and selects). */
export const controlClass =
  'w-full border border-line bg-base px-2.5 py-1.5 text-sm text-ink transition-colors ' +
  'hover:border-dim focus:border-accent disabled:cursor-not-allowed disabled:opacity-50 ' +
  'aria-invalid:border-bad';

/** Id of the hint/error paragraph rendered under a field, for `aria-describedby`. */
export function helpId(fieldId: string): string {
  return `${fieldId}-help`;
}
