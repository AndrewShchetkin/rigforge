import { usePreviewStream } from '../../state/hooks.ts';
import { EffectPanel } from './EffectPanel.tsx';
import { LayoutEditor } from './LayoutEditor.tsx';

export function LightingPage() {
  usePreviewStream('Leds');
  return (
    <div className="grid min-w-0 items-start gap-6 xl:grid-cols-[minmax(24rem,36rem)_minmax(0,1fr)]">
      <EffectPanel />
      <LayoutEditor />
    </div>
  );
}
