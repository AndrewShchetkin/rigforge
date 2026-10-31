import { useState } from 'react';

/** Returns the previous value while `isEqual` says nothing changed, so memoized children can skip renders. */
export function useStableValue<T>(value: T, isEqual: (previous: T, next: T) => boolean): T {
  const [stable, setStable] = useState(value);
  const changed = stable !== value && !isEqual(stable, value);
  if (changed) {
    setStable(value);
  }
  return changed ? value : stable;
}
