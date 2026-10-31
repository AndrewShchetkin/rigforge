import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react';
import { ToastContext, type ToastApi, type ToastKind } from './toastContext.ts';
import { ToastViewport, type ToastMessage } from './Toasts.tsx';

const MAX_VISIBLE = 5;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const nextId = useRef(0);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((toast) => toast.id !== id));
  }, []);

  const api = useMemo<ToastApi>(() => {
    const show = (kind: ToastKind, message: string) => {
      nextId.current += 1;
      const toast: ToastMessage = { id: nextId.current, kind, message };
      setToasts((current) => [...current.slice(1 - MAX_VISIBLE), toast]);
    };
    return {
      error: (message) => show('error', message),
      success: (message) => show('success', message),
      info: (message) => show('info', message),
    };
  }, []);

  return (
    <ToastContext value={api}>
      {children}
      <ToastViewport toasts={toasts} onDismiss={dismiss} />
    </ToastContext>
  );
}
