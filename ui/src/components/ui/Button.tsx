import type { ButtonHTMLAttributes } from 'react';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';
type Size = 'sm' | 'md';

const VARIANTS: Record<Variant, string> = {
  primary: 'bg-violet-600 text-white hover:bg-violet-500 disabled:hover:bg-violet-600',
  secondary: 'border border-zinc-700 bg-zinc-800/80 text-zinc-100 hover:bg-zinc-700/80 disabled:hover:bg-zinc-800/80',
  ghost: 'text-zinc-300 hover:bg-zinc-800 hover:text-zinc-100 disabled:hover:bg-transparent',
  danger: 'bg-red-600/90 text-white hover:bg-red-500 disabled:hover:bg-red-600/90',
};

const SIZES: Record<Size, string> = {
  sm: 'h-8 gap-1.5 px-2.5 text-xs',
  md: 'h-9 gap-2 px-3.5 text-sm',
};

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  size?: Size;
}

export function Button({ variant = 'secondary', size = 'md', type = 'button', className = '', ...props }: ButtonProps) {
  return (
    <button
      type={type}
      className={`inline-flex shrink-0 items-center justify-center rounded-md font-medium whitespace-nowrap transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${VARIANTS[variant]} ${SIZES[size]} ${className}`}
      {...props}
    />
  );
}
