import { useCallback, useEffect, useEffectEvent, useMemo, useRef, useState, type ReactNode } from 'react';
import type { EffectSettings } from '../../api/contracts.ts';
import { effectSettingsSchemaFor } from '../../api/schemas.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { EmptyState } from '../../components/ui/EmptyState.tsx';
import { Spinner } from '../../components/ui/Spinner.tsx';
import { NO_ERRORS, toFieldErrors } from '../../lib/validation.ts';
import { useDaemonApi, useSensorOptions, useSystem } from '../../state/hooks.ts';
import { EffectForm } from './EffectForm.tsx';

const APPLY_DELAY_MS = 300;

interface PendingEdit {
  revision: number;
  settings: EffectSettings;
}

/** Effect settings with debounced auto-apply: valid edits are sent ~300 ms after the last change. */
export function EffectPanel() {
  const { profile, effects } = useSystem();
  const api = useDaemonApi();
  const sensorOptions = useSensorOptions();
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
      setPending((current) => ({ revision: next, settings: { ...(current?.settings ?? saved), ...patch } }));
    },
    [saved],
  );

  const apply = useEffectEvent(async (edit: PendingEdit, settings: EffectSettings) => {
    setApplying(true);
    await api.updateEffect(settings);
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
    <Card title="Effect" description="Changes apply automatically." actions={status}>
      {effects.length === 0 ? (
        <EmptyState title="No effects available">The daemon did not report any lighting effects.</EmptyState>
      ) : (
        <EffectForm effects={effects} value={value} errors={errors} sensorOptions={sensorOptions} onChange={change} />
      )}
    </Card>
  );
}
