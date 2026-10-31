import type { ZodError } from 'zod';

/** First validation message per field, keyed by the dotted path (e.g. `points.2.temperature`). */
export type FieldErrors = Readonly<Record<string, string>>;

export const NO_ERRORS: FieldErrors = {};

export function toFieldErrors(error: ZodError): FieldErrors {
  const errors: Record<string, string> = {};
  for (const issue of error.issues) {
    const key = issue.path.map(String).join('.');
    errors[key] ??= issue.message;
  }
  return errors;
}

export function firstError(errors: FieldErrors): string | undefined {
  return Object.values(errors)[0];
}
