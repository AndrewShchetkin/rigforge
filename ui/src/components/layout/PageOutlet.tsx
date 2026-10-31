import { lazy, memo, Suspense } from 'react';
import { CoolingPage } from '../../features/cooling/CoolingPage.tsx';
import { DashboardPage } from '../../features/dashboard/DashboardPage.tsx';
import { DevicesPage } from '../../features/devices/DevicesPage.tsx';
import { LcdPage } from '../../features/lcd/LcdPage.tsx';
import { ErrorBoundary } from '../feedback/ErrorBoundary.tsx';
import { Spinner } from '../ui/Spinner.tsx';
import type { PageId } from './navigation.ts';

// Konva is by far the largest dependency; download it only when the Lighting page is opened.
const LightingPage = lazy(() =>
  import('../../features/lighting/LightingPage.tsx').then((module) => ({ default: module.LightingPage })),
);

function PageContent({ page }: { page: PageId }) {
  switch (page) {
    case 'dashboard':
      return <DashboardPage />;
    case 'lighting':
      return <LightingPage />;
    case 'cooling':
      return <CoolingPage />;
    case 'lcd':
      return <LcdPage />;
    case 'devices':
      return <DevicesPage />;
  }
}

/** The active page, isolated by its own error boundary (reset when switching pages). */
export const PageOutlet = memo(function PageOutlet({ page }: { page: PageId }) {
  return (
    <ErrorBoundary key={page}>
      <Suspense
        fallback={
          <div className="flex justify-center py-16">
            <Spinner className="size-6 text-violet-400" />
          </div>
        }
      >
        <PageContent page={page} />
      </Suspense>
    </ErrorBoundary>
  );
});
