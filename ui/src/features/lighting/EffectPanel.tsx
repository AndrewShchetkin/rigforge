import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type ReactNode } from 'react';
import type { ColorPalette, EffectSettings } from '../../api/contracts.ts';
import { effectSettingsSchemaFor } from '../../api/schemas.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { Spinner } from '../../components/ui/Spinner.tsx';
import { NO_ERRORS, toFieldErrors } from '../../lib/validation.ts';
import { useDaemonApi, useSensorOptions, useSystem } from '../../state/hooks.ts';
import { EffectForm } from './EffectForm.tsx';
import { isLightingEnabled } from './effectModel.ts';

const APPLY_DELAY_MS = 300;

interface PendingEdit {
  revision: number;
  /** Effect at the moment this edit started, so header power and brightness can win when the form did not touch them. */
  baseline: EffectSettings;
  settings: EffectSettings;
}

/** Effect settings with debounced auto-apply: valid edits are sent ~300 ms after the last change. */
export function EffectPanel() {
  const { profile, effects } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const sensorOptions = useSensorOptions();
  const palettes = profile.colorPalettes ?? [];

  const savePalette = useCallback(
    async (name: string, colors: readonly string[]) => {
      const saved = await api.upsertColorPalette({ id: crypto.randomUUID(), name, colors: [...colors] });
      if (saved) {
        toast.success('Palette saved.');
      }
      return saved !== null;
    },
    [api, toast],
  );

  const renamePalette = useCallback(
    async (palette: ColorPalette, name: string) => {
      const saved = await api.upsertColorPalette({ id: palette.id, name, colors: [...palette.colors] });
      if (saved) {
        toast.success('Palette renamed.');
      }
      return saved !== null;
    },
    [api, toast],
  );

  const deletePalette = useCallback(
    async (palette: ColorPalette) => {
      const saved = await api.removeColorPalette(palette.id);
      if (saved) {
        toast.success('Palette removed.');
      }
      return saved !== null;
    },
    [api, toast],
  );
  const [pending, setPending] = useState<PendingEdit | null>(null);
  const [applying, setApplying] = useState(false);
  const revision = useRef(0);
  const saved = profile.effect;
  const value = pending?.settings ?? saved;

  const descriptor = effects.find((effect) => effect.id === value.effectId);
  const validation = useMemo(() => effectSettingsSchemaFor(descriptor).safeParse(value), [descriptor, value]);
  const errors = validation.success ? NO_ERRORS : toFieldErrors(validation.error);

  const change = useCallback(
    (patch: Partial<EffectSettings>) => {
      revision.current += 1;
      const next = revision.current;
      setPending((current) => {
        const base = current?.settings ?? saved;
        return {
          revision: next,
          baseline: current?.baseline ?? saved,
          settings: { ...base, ...patch, enabled: isLightingEnabled({ enabled: patch.enabled ?? base.enabled }) },
        };
      });
    },
    [saved],
  );

  const apply = useEffectEvent(async (edit: PendingEdit, settings: EffectSettings) => {
    const latest = profile.effect;
    const editedEnabled = isLightingEnabled(settings);
    const merged: EffectSettings = {
      ...settings,
      enabled: editedEnabled === isLightingEnabled(edit.baseline) ? isLightingEnabled(latest) : editedEnabled,
      brightness: settings.brightness === edit.baseline.brightness ? latest.brightness : settings.brightness,
    };
    setApplying(true);
    await api.updateEffect(merged);
    setApplying(false);
    // Newer edits stay pending; otherwise show the daemon's state again (it has this edit, or rejected it).
    setPending((current) => (current?.revision === edit.revision ? null : current));
  });

  useEffect(() => {
    if (!pending || !validation.success) {
      return;
    }
    const settings = validation.data;
    const timer = window.setTimeout(() => void apply(pending, settings), APPLY_DELAY_MS);
    return () => window.clearTimeout(timer);
  }, [pending, validation]);

  let status: ReactNode;
  if (applying) {
    status = (
      <Badge tone="info">
        <Spinner className="size-3" />
        Applying…
      </Badge>
    );
  } else if (pending && !validation.success) {
    status = <Badge tone="danger">Not applied: invalid</Badge>;
  } else if (pending) {
    status = <Badge tone="warning">Pending…</Badge>;
  }

  return (
    <Card className="min-w-0" title="Effect" description="Changes apply automatically." actions={status}>
      {effects.length === 0 ? (
        <EmptyState title="No effects available">The daemon did not report any lighting effects.</EmptyState>
      ) : (
        <EffectForm
          effects={effects}
          value={value}
          errors={errors}
          sensorOptions={sensorOptions}
          palettes={palettes}
          onChange={change}
          onSavePalette={savePalette}
          onRenamePalette={renamePalette}
          onDeletePalette={deletePalette}
        />
      )}
    </Card>
  );
}
