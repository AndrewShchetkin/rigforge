import { use } from 'react';
import { ToastContext, type ToastApi } from './toastContext.ts';

export function useToast(): ToastApi {
  const toast = use(ToastContext);
  if (!toast) {
    throw new Error('useToast() must be used inside <ToastProvider>.');
  }
  return toast;
}
