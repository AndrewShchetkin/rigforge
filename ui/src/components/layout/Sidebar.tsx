import { memo } from 'react';
import { CoolingIcon, DashboardIcon, DevicesIcon, LcdIcon, LightingIcon } from '../ui/icons.tsx';
import { PanelCorners, panelClass } from '../ui/PanelFrame.tsx';
import { PAGES, pageHref, type PageId } from './navigation.ts';

function PageIcon({ page }: { page: PageId }) {
  const className = 'size-4 shrink-0';
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
    <aside
      data-panel
      className={`${panelClass} border-b md:sticky md:top-0 md:flex md:h-dvh md:w-44 md:shrink-0 md:flex-col md:border-r md:border-b-0`}
    >
      <PanelCorners />
      <div className="px-3 pt-4 pb-2 text-[11px] tracking-[0.14em] text-dim uppercase">rigforge</div>
      <nav aria-label="Main" className="overflow-x-auto px-2 pb-2 md:px-2">
        <ul className="flex gap-1 md:flex-col">
          {PAGES.map((page) => {
            const active = page.id === current;
            return (
              <li key={page.id}>
                <a
                  href={pageHref(page.id)}
                  aria-current={active ? 'page' : undefined}
                  className={`flex items-center gap-2 px-2 py-1.5 text-sm whitespace-nowrap ${active ? 'bg-accent/10 text-accent shadow-[inset_2px_0_0_var(--color-accent)]' : 'text-dim hover:text-ink'}`}
                >
                  <PageIcon page={page.id} />
                  {page.label}
                </a>
              </li>
            );
          })}
        </ul>
      </nav>
      <p className="mt-auto hidden px-3 pt-4 pb-3 tracking-[0.18em] text-dim md:block" aria-hidden="true">
        ░▒▓█▓▒░
        <span className="mt-1 block tracking-normal"># hw live</span>
      </p>
    </aside>
  );
});
