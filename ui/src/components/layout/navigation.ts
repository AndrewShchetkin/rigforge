import { useSyncExternalStore } from 'react';

export const PAGES = [
  { id: 'dashboard', label: 'Dashboard' },
  { id: 'lighting', label: 'Lighting' },
  { id: 'cooling', label: 'Cooling' },
  { id: 'lcd', label: 'LCD' },
  { id: 'devices', label: 'Devices' },
] as const;

export type PageId = (typeof PAGES)[number]['id'];

const DEFAULT_PAGE: PageId = 'dashboard';

export function pageHref(page: PageId): string {
  return `#/${page}`;
}

export function pageLabel(page: PageId): string {
  return PAGES.find((entry) => entry.id === page)?.label ?? page;
}

function readPage(): PageId {
  const id = window.location.hash.replace(/^#\/?/, '');
  return PAGES.find((entry) => entry.id === id)?.id ?? DEFAULT_PAGE;
}

function subscribe(onChange: () => void): () => void {
  window.addEventListener('hashchange', onChange);
  return () => window.removeEventListener('hashchange', onChange);
}

/** Current page from `location.hash` (e.g. `#/lighting`); navigation is plain links. */
export function useHashPage(): PageId {
  return useSyncExternalStore(subscribe, readPage);
}
