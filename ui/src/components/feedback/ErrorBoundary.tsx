import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button } from '../ui/Button.tsx';
import { ErrorIcon } from '../ui/icons.tsx';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/** Catches render errors of a page and shows a fallback card instead of a blank screen. */
export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  override state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: unknown): ErrorBoundaryState {
    return { error: error instanceof Error ? error : new Error(String(error)) };
  }

  override componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error('Page crashed:', error, info.componentStack);
  }

  override render(): ReactNode {
    const { error } = this.state;
    if (error === null) {
      return this.props.children;
    }
    return (
      <div role="alert" className="mx-auto max-w-lg rounded-xl border border-red-500/30 bg-red-950/30 p-6 text-center">
        <ErrorIcon className="mx-auto size-8 text-red-400" />
        <h2 className="mt-3 text-base font-semibold text-zinc-100">This page crashed</h2>
        <p className="mt-1 text-sm break-words text-zinc-400">{error.message}</p>
        <Button variant="primary" className="mt-5" onClick={() => window.location.reload()}>
          Reload
        </Button>
      </div>
    );
  }
}
