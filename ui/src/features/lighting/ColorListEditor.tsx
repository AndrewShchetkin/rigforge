import { useId, useRef, useState } from 'react';
import type { ColorPalette } from '../../api/contracts.ts';
import { ConfirmDialog } from '../../components/feedback/ConfirmDialog.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { CloseIcon, PencilIcon, PlusIcon, TrashIcon } from '../../components/ui/icons.tsx';
import { controlClass, helpId } from '../../components/ui/styles.ts';
import { suggestedColor } from './effectModel.ts';
import {
  canSavePaletteColors,
  COLOR_PALETTES,
  fitPaletteColors,
  MAX_SAVED_PALETTES,
  paletteNameError,
} from './palettes.ts';

interface ColorListEditorProps {
  colors: readonly string[];
  min: number;
  max: number;
  error?: string;
  palettes?: readonly ColorPalette[];
  onChange: (colors: string[]) => void;
  onSavePalette?: (name: string, colors: readonly string[]) => Promise<boolean>;
  onRenamePalette?: (palette: ColorPalette, name: string) => Promise<boolean>;
  onDeletePalette?: (palette: ColorPalette) => Promise<boolean>;
}

export function ColorListEditor({
  colors,
  min,
  max,
  error,
  palettes = [],
  onChange,
  onSavePalette,
  onRenamePalette,
  onDeletePalette,
}: ColorListEditorProps) {
  const id = useId();
  const replace = (index: number, color: string) =>
    onChange(colors.map((current, position) => (position === index ? color : current)));
  const canStore = onSavePalette !== undefined && onRenamePalette !== undefined && onDeletePalette !== undefined;

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
      {max >= 2 && (
        <div className="mt-2.5">
          <p className="mb-1.5 text-xs text-zinc-500">Palettes</p>
          <ul className="flex flex-wrap gap-1.5">
            {COLOR_PALETTES.map((palette) => (
              <li key={palette.id}>
                <PaletteChip
                  name={palette.name}
                  colors={palette.colors}
                  active={sameColors(colors, fitPaletteColors(palette.colors, min, max))}
                  onApply={() => onChange(fitPaletteColors(palette.colors, min, max))}
                />
              </li>
            ))}
          </ul>
        </div>
      )}
      {canStore && (
        <UserPalettes
          colors={colors}
          min={min}
          max={max}
          palettes={palettes}
          onChange={onChange}
          onSavePalette={onSavePalette}
          onRenamePalette={onRenamePalette}
          onDeletePalette={onDeletePalette}
        />
      )}
      {error !== undefined && (
        <p id={helpId(id)} className="mt-1.5 text-xs text-red-400">
          {error}
        </p>
      )}
    </fieldset>
  );
}

function PaletteChip({
  name,
  colors,
  active,
  onApply,
}: {
  name: string;
  colors: readonly string[];
  active: boolean;
  onApply: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={onApply}
      className={`inline-flex items-center gap-1.5 rounded-full border py-0.5 pr-2 pl-1 text-xs transition-colors ${active ? 'border-violet-400 bg-violet-500/15 text-violet-100' : 'border-zinc-700 bg-zinc-950/70 text-zinc-200 hover:border-violet-400/70'}`}
    >
      <PaletteSwatch colors={colors} />
      {name}
    </button>
  );
}

function PaletteSwatch({ colors }: { colors: readonly string[] }) {
  return (
    <span
      aria-hidden="true"
      className="h-3 w-8 rounded-full border border-white/10"
      style={{ backgroundImage: `linear-gradient(90deg, ${colors.join(', ')})` }}
    />
  );
}

function UserPalettes({
  colors,
  min,
  max,
  palettes,
  onChange,
  onSavePalette,
  onRenamePalette,
  onDeletePalette,
}: {
  colors: readonly string[];
  min: number;
  max: number;
  palettes: readonly ColorPalette[];
  onChange: (colors: string[]) => void;
  onSavePalette: (name: string, colors: readonly string[]) => Promise<boolean>;
  onRenamePalette: (palette: ColorPalette, name: string) => Promise<boolean>;
  onDeletePalette: (palette: ColorPalette) => Promise<boolean>;
}) {
  const nameId = useId();
  const renameHelp = helpId(`${nameId}-rename`);
  const [naming, setNaming] = useState(false);
  const [draftName, setDraftName] = useState('');
  const [nameAttempted, setNameAttempted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [renameAttempted, setRenameAttempted] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<ColorPalette | null>(null);
  const renameHandled = useRef(false);

  const canSave = canSavePaletteColors(colors, min, max);
  const atLimit = palettes.length >= MAX_SAVED_PALETTES;
  const nameError = paletteNameError(draftName, palettes);
  const visibleNameError = nameAttempted || draftName.trim().length > 0 ? nameError : undefined;
  const editingPalette = palettes.find((palette) => palette.id === editingId);
  const renameError =
    editingPalette !== undefined && renameAttempted ? paletteNameError(renameDraft, palettes, editingPalette.id) : undefined;

  const save = async () => {
    setNameAttempted(true);
    if (!canSave || atLimit || paletteNameError(draftName, palettes) !== undefined) {
      return;
    }
    setBusy(true);
    const saved = await onSavePalette(draftName.trim(), colors);
    setBusy(false);
    if (saved) {
      setNaming(false);
      setDraftName('');
      setNameAttempted(false);
    }
  };

  const startRename = (palette: ColorPalette) => {
    renameHandled.current = false;
    setRenameAttempted(false);
    setRenameDraft(palette.name);
    setEditingId(palette.id);
  };

  const commitRename = async (palette: ColorPalette) => {
    const error = paletteNameError(renameDraft, palettes, palette.id);
    if (error !== undefined) {
      setRenameAttempted(true);
      renameHandled.current = false;
      return;
    }
    if (renameDraft.trim() === palette.name.trim()) {
      setEditingId(null);
      return;
    }
    setBusy(true);
    const renamed = await onRenamePalette(palette, renameDraft.trim());
    setBusy(false);
    if (renamed) {
      setEditingId(null);
    } else {
      renameHandled.current = false;
    }
  };

  const finishRename = (palette: ColorPalette) => {
    if (renameHandled.current) {
      renameHandled.current = false;
      return;
    }
    renameHandled.current = true;
    void commitRename(palette);
  };

  const confirmDelete = async () => {
    if (pendingDelete === null) {
      return;
    }
    setBusy(true);
    const removed = await onDeletePalette(pendingDelete);
    setBusy(false);
    if (removed) {
      setPendingDelete(null);
      if (editingId === pendingDelete.id) {
        setEditingId(null);
      }
    }
  };

  return (
    <div className="mt-2.5">
      <p className="mb-1.5 text-xs text-zinc-500">My palettes</p>
      {palettes.length > 0 && (
        <ul className="mb-2 flex flex-wrap gap-1.5">
          {palettes.map((palette) => {
            const fitted = fitPaletteColors(palette.colors, min, max);
            const active = sameColors(colors, fitted);
            const editing = editingId === palette.id;
            return (
              <li
                key={palette.id}
                className={`inline-flex items-center gap-0.5 rounded-full border py-0.5 pr-1 pl-1 text-xs ${active ? 'border-violet-400 bg-violet-500/15 text-violet-100' : 'border-zinc-700 bg-zinc-950/70 text-zinc-200'}`}
              >
                {editing ? (
                  <input
                    aria-label={`Rename ${palette.name}`}
                    aria-invalid={renameError !== undefined}
                    aria-describedby={renameError !== undefined ? renameHelp : undefined}
                    value={renameDraft}
                    disabled={busy}
                    autoFocus
                    onChange={(event) => setRenameDraft(event.currentTarget.value)}
                    onBlur={() => finishRename(palette)}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter') {
                        event.preventDefault();
                        finishRename(palette);
                      } else if (event.key === 'Escape') {
                        event.preventDefault();
                        renameHandled.current = true;
                        setEditingId(null);
                      }
                    }}
                    className="h-6 w-28 rounded-full border border-zinc-600 bg-zinc-950 px-2 text-xs"
                  />
                ) : (
                  <button
                    type="button"
                    aria-pressed={active}
                    onClick={() => onChange(fitted)}
                    className="inline-flex items-center gap-1.5 rounded-full py-0.5 pr-1 pl-0.5"
                  >
                    <PaletteSwatch colors={palette.colors} />
                    {palette.name}
                  </button>
                )}
                <button
                  type="button"
                  aria-label={`Rename ${palette.name}`}
                  disabled={busy}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => startRename(palette)}
                  className="rounded-full p-1 text-zinc-400 hover:bg-zinc-800 hover:text-zinc-100 disabled:opacity-40"
                >
                  <PencilIcon className="size-3.5" />
                </button>
                <button
                  type="button"
                  aria-label={`Delete ${palette.name}`}
                  disabled={busy}
                  onMouseDown={(event) => {
                    if (editingId !== null) {
                      renameHandled.current = true;
                      event.preventDefault();
                    }
                  }}
                  onClick={() => {
                    setEditingId(null);
                    setPendingDelete(palette);
                  }}
                  className="rounded-full p-1 text-zinc-400 hover:bg-zinc-800 hover:text-red-300 disabled:opacity-40"
                >
                  <TrashIcon className="size-3.5" />
                </button>
              </li>
            );
          })}
        </ul>
      )}
      {renameError !== undefined && (
        <p id={renameHelp} className="mb-2 text-xs text-red-400">
          {renameError}
        </p>
      )}
      {naming ? (
        <div className="flex flex-wrap items-start gap-2">
          <div className="min-w-40 flex-1">
            <input
              id={nameId}
              aria-label="Palette name"
              aria-invalid={visibleNameError !== undefined}
              aria-describedby={visibleNameError !== undefined ? helpId(nameId) : undefined}
              value={draftName}
              disabled={busy}
              autoFocus
              onChange={(event) => setDraftName(event.currentTarget.value)}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault();
                  void save();
                } else if (event.key === 'Escape') {
                  event.preventDefault();
                  setNaming(false);
                  setDraftName('');
                  setNameAttempted(false);
                }
              }}
              className={controlClass}
            />
            {visibleNameError !== undefined && (
              <p id={helpId(nameId)} className="mt-1 text-xs text-red-400">
                {visibleNameError}
              </p>
            )}
          </div>
          <Button size="sm" variant="primary" onClick={() => void save()} disabled={busy || !canSave || atLimit}>
            Save
          </Button>
          <Button
            size="sm"
            variant="ghost"
            onClick={() => {
              setNaming(false);
              setDraftName('');
              setNameAttempted(false);
            }}
            disabled={busy}
          >
            Cancel
          </Button>
        </div>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" onClick={() => setNaming(true)} disabled={busy || !canSave || atLimit}>
            <PlusIcon className="size-3.5" />
            Save palette
          </Button>
          {atLimit && <p className="text-xs text-zinc-500">You can save up to {MAX_SAVED_PALETTES} palettes.</p>}
        </div>
      )}
      <ConfirmDialog
        open={pendingDelete !== null}
        title={pendingDelete ? `Delete ${pendingDelete.name}?` : 'Delete palette?'}
        confirmLabel="Delete palette"
        onConfirm={() => void confirmDelete()}
        onCancel={() => {
          if (!busy) {
            setPendingDelete(null);
          }
        }}
      >
        <p>This removes the saved palette. The colors on the effect stay as they are.</p>
      </ConfirmDialog>
    </div>
  );
}

function sameColors(current: readonly string[], next: readonly string[]): boolean {
  return current.length === next.length && current.every((color, index) => color.toLowerCase() === next[index]?.toLowerCase());
}
