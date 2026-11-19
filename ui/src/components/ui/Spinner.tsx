import { useSpinnerFrame } from './motion.ts';

export function Spinner({ className = '' }: { className?: string }) {
  const frame = useSpinnerFrame();
  return (
    <span aria-hidden="true" className={`inline-block ${className}`}>
      {frame}
    </span>
  );
}
