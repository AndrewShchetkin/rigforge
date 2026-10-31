import { useCallback, useMemo, useState } from 'react';
import type { DeviceDto, LcdSettings } from '../../api/contracts.ts';
import { lcdSettingsSchema } from '../../api/schemas.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { Card } from '../../components/ui/Card.tsx';
import { firstError, NO_ERRORS, toFieldErrors } from '../../lib/validation.ts';
import { useDaemonApi, useSensorOptions, useSystem } from '../../state/hooks.ts';
import { LcdSettingsForm } from './LcdSettingsForm.tsx';

export function LcdSettingsPanel({ screens }: { screens: readonly DeviceDto[] }) {
  const { profile } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const sensorOptions = useSensorOptions();
  const saved = profile.lcd;
  const [draft, setDraft] = useState<LcdSettings | null>(null);
  const [saving, setSaving] = useState(false);
  const value = draft ?? saved;

  const validation = useMemo(() => lcdSettingsSchema.safeParse(value), [value]);
  const errors = validation.success ? NO_ERRORS : toFieldErrors(validation.error);
  const limits = screens.flatMap((screen) => (screen.lcd ? [screen.lcd.maxFramesPerSecond] : []));
  const deviceMaxFps = limits.length > 0 ? Math.min(...limits) : null;

  const update = useCallback(
    (patch: Partial<LcdSettings>) => setDraft((current) => ({ ...(current ?? saved), ...patch })),
    [saved],
  );

  const save = async () => {
    if (!validation.success) {
      toast.error(`Cannot save: ${firstError(errors) ?? 'the settings are invalid'}.`);
      return;
    }
    setSaving(true);
    const result = await api.updateLcd(validation.data);
    setSaving(false);
    if (result) {
      setDraft(null);
      toast.success('LCD settings saved.');
    }
  };

  return (
    <Card
      title="Screen settings"
      actions={
        <>
          {draft && <Badge tone="warning">Unsaved changes</Badge>}
          <Button size="sm" onClick={() => setDraft(null)} disabled={!draft || saving}>
            Reset
          </Button>
          <Button size="sm" variant="primary" onClick={() => void save()} disabled={!draft || saving || !validation.success}>
            Save
          </Button>
        </>
      }
    >
      <LcdSettingsForm
        value={value}
        errors={errors}
        sensorOptions={sensorOptions}
        deviceMaxFps={deviceMaxFps}
        onChange={update}
      />
    </Card>
  );
}
