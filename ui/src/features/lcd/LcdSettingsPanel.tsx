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

export function LcdSettingsPanel({
  screens,
  deviceId,
  onDeviceId,
}: {
  screens: readonly DeviceDto[];
  /** `null` edits the global default. A device id edits that screen’s override. */
  deviceId: string | null;
  onDeviceId: (deviceId: string | null) => void;
}) {
  const { profile } = useSystem();
  const api = useDaemonApi();
  const toast = useToast();
  const sensorOptions = useSensorOptions();
  const draftKey = deviceId ? `device:${deviceId}` : 'global';
  const [drafts, setDrafts] = useState<Partial<Record<string, LcdSettings>>>({});
  const [saving, setSaving] = useState(false);
  const selectedScreen = deviceId ? screens.find((screen) => screen.id === deviceId) : undefined;
  const hasOverride = deviceId !== null && profile.displays?.[deviceId] !== undefined;
  const saved = deviceId === null ? profile.lcd : (profile.displays?.[deviceId] ?? profile.lcd);
  const draft = drafts[draftKey];
  const value = draft ?? saved;

  const validation = useMemo(() => lcdSettingsSchema.safeParse(value), [value]);
  const errors = validation.success ? NO_ERRORS : toFieldErrors(validation.error);
  const limits = screens.flatMap((screen) => (screen.lcd ? [screen.lcd.maxFramesPerSecond] : []));
  const selectedLimit = selectedScreen?.lcd?.maxFramesPerSecond ?? null;
  const deviceMaxFps = selectedLimit ?? (limits.length > 0 ? Math.min(...limits) : null);

  const update = useCallback(
    (patch: Partial<LcdSettings>) => {
      setDrafts((current) => {
        const base = current[draftKey] ?? saved;
        return { ...current, [draftKey]: { ...base, ...patch } };
      });
    },
    [draftKey, saved],
  );

  const save = async () => {
    if (!validation.success) {
      toast.error(`Cannot save: ${firstError(errors) ?? 'the settings are invalid'}.`);
      return;
    }
    const key = draftKey;
    const targetId = deviceId;
    setSaving(true);
    const result = targetId === null ? await api.updateLcd(validation.data) : await api.upsertDisplay(targetId, validation.data);
    setSaving(false);
    if (result) {
      setDrafts((current) => dropDraft(current, key));
      toast.success(targetId === null ? 'LCD settings saved.' : 'Screen override saved.');
    }
  };

  const restoreGlobalDefault = async () => {
    if (deviceId === null) {
      return;
    }
    const key = draftKey;
    const targetId = deviceId;
    setSaving(true);
    const result = await api.removeDisplay(targetId);
    setSaving(false);
    if (result) {
      setDrafts((current) => dropDraft(current, key));
      toast.success('This screen now uses the global default.');
    }
  };

  return (
    <Card
      title="Screen settings"
      description={
        deviceId
          ? hasOverride
            ? 'These settings are stored on this screen.'
            : 'This screen inherits the global default until you save an override.'
          : screens.length > 0
            ? 'Used by every screen that does not have its own override.'
            : undefined
      }
      actions={
        <>
          {deviceId && (
            <Badge tone={hasOverride ? 'accent' : 'info'}>{hasOverride ? 'Custom override' : 'Using global default'}</Badge>
          )}
          {draft && <Badge tone="warning">Unsaved changes</Badge>}
          {deviceId && hasOverride && (
            <Button size="sm" onClick={() => void restoreGlobalDefault()} disabled={saving}>
              Use global default
            </Button>
          )}
          <Button size="sm" onClick={() => setDrafts((current) => dropDraft(current, draftKey))} disabled={draft === undefined || saving}>
            Reset
          </Button>
          <Button size="sm" variant="primary" onClick={() => void save()} disabled={!draft || saving || !validation.success}>
            Save
          </Button>
        </>
      }
    >
      <div className="space-y-5">
        {screens.length > 0 && <TargetTabs screens={screens} deviceId={deviceId} onDeviceId={onDeviceId} />}
        {selectedScreen?.lcd && (
          <p className="text-xs text-zinc-500">
            {selectedScreen.lcd.width} × {selectedScreen.lcd.height} · {selectedScreen.lcd.isCircular ? 'Circular' : 'Rectangular'}
          </p>
        )}
        <LcdSettingsForm
          value={value}
          errors={errors}
          sensorOptions={sensorOptions}
          deviceMaxFps={deviceMaxFps}
          onChange={update}
        />
      </div>
    </Card>
  );
}

function dropDraft(
  drafts: Partial<Record<string, LcdSettings>>,
  key: string,
): Partial<Record<string, LcdSettings>> {
  if (drafts[key] === undefined) {
    return drafts;
  }
  const next = { ...drafts };
  delete next[key];
  return next;
}

function TargetTabs({
  screens,
  deviceId,
  onDeviceId,
}: {
  screens: readonly DeviceDto[];
  deviceId: string | null;
  onDeviceId: (deviceId: string | null) => void;
}) {
  return (
    <div role="tablist" aria-label="LCD settings target" className="flex flex-wrap gap-1.5">
      <button
        type="button"
        role="tab"
        aria-selected={deviceId === null}
        onClick={() => onDeviceId(null)}
        className={tabClass(deviceId === null)}
      >
        Global default
      </button>
      {screens.map((screen) => {
        const selected = deviceId === screen.id;
        return (
          <button
            key={screen.id}
            type="button"
            role="tab"
            aria-selected={selected}
            onClick={() => onDeviceId(screen.id)}
            className={`${tabClass(selected)} max-w-52 truncate`}
          >
            {screen.name}
          </button>
        );
      })}
    </div>
  );
}

function tabClass(selected: boolean): string {
  return `border px-2.5 py-1 text-left text-xs font-medium transition-colors ${
    selected ? 'border-accent/60 bg-accent/15 text-accent' : 'border-line bg-base text-dim hover:text-ink'
  }`;
}
