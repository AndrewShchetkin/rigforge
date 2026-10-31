import { usePreviewStream } from '../../state/hooks.ts';
import { EffectPanel } from './EffectPanel.tsx';
import { LayoutEditor } from './LayoutEditor.tsx';

export function LightingPage() {
  usePreviewStream('Leds');
  return (
    <div className="grid items-start gap-6 2xl:grid-cols-[24rem_minmax(0,1fr)]">
      <EffectPanel />
      <LayoutEditor />
    </div>
  );
}
