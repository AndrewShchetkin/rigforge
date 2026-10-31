import { memo } from 'react';
import { CoolingIcon, DashboardIcon, DevicesIcon, LcdIcon, LightingIcon } from '../ui/icons.tsx';
import { PAGES, pageHref, type PageId } from './navigation.ts';

function PageIcon({ page }: { page: PageId }) {
  const className = 'size-5 shrink-0';
  switch (page) {
    case 'dashboard':
      return <DashboardIcon className={className} />;
    case 'lighting':
      return <LightingIcon className={className} />;
    case 'cooling':
      return <CoolingIcon className={className} />;
    case 'lcd':
      return <LcdIcon className={className} />;
    case 'devices':
      return <DevicesIcon className={className} />;
  }
}

/** Vertical navigation on wide screens, a scrollable top bar on small ones. */
export const Sidebar = memo(function Sidebar({ current }: { current: PageId }) {
  return (
    <aside className="border-b border-zinc-800 bg-zinc-900/40 md:sticky md:top-0 md:h-dvh md:w-56 md:shrink-0 md:border-r md:border-b-0">
      <div className="flex items-center gap-2.5 px-4 pt-3 pb-2 md:px-5 md:py-5">
        <img src="/favicon.svg" alt="" className="size-7" />
        <span className="text-base font-semibold tracking-tight">RigForge</span>
      </div>
      <nav aria-label="Main" className="overflow-x-auto px-2 pb-2 md:px-3">
        <ul className="flex gap-1 md:flex-col">
          {PAGES.map((page) => {
            const active = page.id === current;
            return (
              <li key={page.id}>
                <a
                  href={pageHref(page.id)}
                  aria-current={active ? 'page' : undefined}
                  className={`flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium whitespace-nowrap transition-colors ${active ? 'bg-violet-500/15 text-violet-200' : 'text-zinc-400 hover:bg-zinc-800/70 hover:text-zinc-100'}`}
                >
                  <PageIcon page={page.id} />
                  {page.label}
                </a>
              </li>
            );
          })}
        </ul>
      </nav>
    </aside>
  );
});
