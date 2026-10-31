import { memo } from 'react';
import type { LcdInfoDto } from '../../api/contracts.ts';

interface LcdPreviewProps {
  /** base64 PNG of the latest frame. */
  png: string | null;
  lcd: LcdInfoDto | null;
}

/** The frame the daemon renders for the AIO screen, shaped like the screen. */
export const LcdPreview = memo(function LcdPreview({ png, lcd }: LcdPreviewProps) {
  const circular = lcd?.isCircular ?? true;
  return (
    <div className="flex justify-center">
      <div
        className={`w-full max-w-64 overflow-hidden border-4 border-zinc-800 bg-black shadow-inner shadow-black ${circular ? 'rounded-full' : 'rounded-xl'}`}
        style={{ aspectRatio: lcd ? `${lcd.width} / ${lcd.height}` : '1 / 1' }}
      >
        {png ? (
          <img src={`data:image/png;base64,${png}`} alt="Live preview of the LCD screen" className="size-full object-cover" />
        ) : (
          <div role="status" className="flex size-full items-center justify-center px-8 text-center text-xs text-zinc-500">
            Waiting for the first frame…
          </div>
        )}
      </div>
    </div>
  );
});
