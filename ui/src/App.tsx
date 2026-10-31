import { ToastProvider } from './components/feedback/ToastProvider.tsx';
import { AppShell } from './components/layout/AppShell.tsx';
import { DaemonProvider } from './state/DaemonContext.tsx';

export default function App() {
  return (
    <ToastProvider>
      <DaemonProvider>
        <AppShell />
      </DaemonProvider>
    </ToastProvider>
  );
}
