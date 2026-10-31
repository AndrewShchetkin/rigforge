import { useId } from 'react';
import { Button } from '../../components/ui/Button.tsx';
import { CloseIcon, PlusIcon } from '../../components/ui/icons.tsx';
import { helpId } from '../../components/ui/styles.ts';
import { suggestedColor } from './effectModel.ts';

interface ColorListEditorProps {
  colors: readonly string[];
  min: number;
  max: number;
  error?: string;
  onChange: (colors: string[]) => void;
}

export function ColorListEditor({ colors, min, max, error, onChange }: ColorListEditorProps) {
  const id = useId();
  const replace = (index: number, color: string) =>
    onChange(colors.map((current, position) => (position === index ? color : current)));

  return (
    <fieldset aria-describedby={error !== undefined ? helpId(id) : undefined}>
      <legend className="mb-1.5 text-sm text-zinc-300">
        Colors <span className="text-xs text-zinc-500">({min === max ? min : `${min}–${max}`})</span>
      </legend>
      <ul className="flex flex-wrap items-center gap-2">
        {colors.map((color, index) => (
          <li key={index} className="flex items-center gap-0.5 rounded-md border border-zinc-700 bg-zinc-950/80 p-1">
            <input
              type="color"
              value={color.toLowerCase()}
              aria-label={`Color ${index + 1}`}
              onChange={(event) => replace(index, event.currentTarget.value.toUpperCase())}
              className="h-7 w-9 cursor-pointer rounded border-0 bg-transparent p-0"
            />
            <button
              type="button"
              onClick={() => onChange(colors.filter((_, position) => position !== index))}
              disabled={colors.length <= min}
              aria-label={`Remove color ${index + 1}`}
              className="rounded p-1 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100 disabled:pointer-events-none disabled:opacity-30"
            >
              <CloseIcon className="size-3.5" />
            </button>
          </li>
        ))}
        <li>
          <Button size="sm" onClick={() => onChange([...colors, suggestedColor(colors.length)])} disabled={colors.length >= max}>
            <PlusIcon className="size-3.5" />
            Add color
          </Button>
        </li>
      </ul>
      {error !== undefined && (
        <p id={helpId(id)} className="mt-1.5 text-xs text-red-400">
          {error}
        </p>
      )}
    </fieldset>
  );
}
