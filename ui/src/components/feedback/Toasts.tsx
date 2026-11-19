import { memo, useEffect } from 'react';
import { CheckCircleIcon, CloseIcon, ErrorIcon, InfoIcon } from '../ui/icons.tsx';
import type { ToastKind } from './toastContext.ts';

export interface ToastMessage {
  id: number;
  kind: ToastKind;
  message: string;
}

const AUTO_DISMISS_MS = 4_000;

const KIND_STYLES: Record<ToastKind, string> = {
  error: 'border-bad/50 bg-panel text-bad',
  success: 'border-ok/50 bg-panel text-ok',
  info: 'border-line bg-panel text-ink',
};

const KIND_LABELS: Record<ToastKind, string> = { error: 'Error', success: 'Success', info: 'Info' };

function ToastIcon({ kind }: { kind: ToastKind }) {
  const className = 'mt-0.5 size-4 shrink-0';
  switch (kind) {
    case 'error':
      return <ErrorIcon className={className} />;
    case 'success':
      return <CheckCircleIcon className={className} />;
    case 'info':
      return <InfoIcon className={className} />;
  }
}

interface ToastViewportProps {
  toasts: readonly ToastMessage[];
  onDismiss: (id: number) => void;
}

/** Bottom-right stack of notifications; each one dismisses itself after 4 s. */
export function ToastViewport({ toasts, onDismiss }: ToastViewportProps) {
  return (
    <div
      role="region"
      aria-label="Notifications"
      aria-live="polite"
      className="pointer-events-none fixed right-4 bottom-4 z-50 flex w-[min(24rem,calc(100vw-2rem))] flex-col gap-2"
    >
      {toasts.map((toast) => (
        <ToastCard key={toast.id} toast={toast} onDismiss={onDismiss} />
      ))}
    </div>
  );
}

interface ToastCardProps {
  toast: ToastMessage;
  onDismiss: (id: number) => void;
}

const ToastCard = memo(function ToastCard({ toast, onDismiss }: ToastCardProps) {
  const { id, kind, message } = toast;

  useEffect(() => {
    const timer = window.setTimeout(() => onDismiss(id), AUTO_DISMISS_MS);
    return () => window.clearTimeout(timer);
  }, [id, onDismiss]);

  return (
    <div
      role={kind === 'error' ? 'alert' : 'status'}
      className={`pointer-events-auto flex items-start gap-3 border px-4 py-3 text-sm ${KIND_STYLES[kind]}`}
    >
      <ToastIcon kind={kind} />
      <p className="min-w-0 flex-1 leading-snug break-words">
        <span className="sr-only">{KIND_LABELS[kind]}: </span>
        {message}
      </p>
      <button
        type="button"
        onClick={() => onDismiss(id)}
        className="-m-1 rounded p-1 opacity-70 transition-opacity hover:opacity-100"
        aria-label="Dismiss notification"
      >
        <CloseIcon className="size-4" />
      </button>
    </div>
  );
});
