import { useCallback, useMemo, useState } from 'react';
import type { FanChannelDto, FanCurve, FanCurvePoint, FanStatusDto } from '../../api/contracts.ts';
import { fanCurveSchemaFor } from '../../api/schemas.ts';
import { ConfirmDialog } from '../../components/feedback/ConfirmDialog.tsx';
import { useToast } from '../../components/feedback/useToast.ts';
import { Badge } from '../../components/ui/Badge.tsx';
import { Button } from '../../components/ui/Button.tsx';
import { TrashIcon } from '../../components/ui/icons.tsx';
import { firstError, NO_ERRORS, toFieldErrors } from '../../lib/validation.ts';
import { useDaemonApi, useSensorOptions } from '../../state/hooks.ts';
import { CurveEditor } from './CurveEditor.tsx';
import { CurveSettingsForm } from './CurveSettingsForm.tsx';

interface FanCurvePanelProps {
  curve: FanCurve;
  channel: FanChannelDto;
  status: FanStatusDto | null;
}

/** Local draft of a saved curve: edit, validate, save or remove. */
export function FanCurvePanel({ curve, channel, status }: FanCurvePanelProps) {
  const api = useDaemonApi();
  const toast = useToast();
  const sensorOptions = useSensorOptions();
  const [draft, setDraft] = useState<FanCurve | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const value = draft ?? curve;

  const validation = useMemo(() => fanCurveSchemaFor(channel).safeParse(value), [channel, value]);
  const errors = validation.success ? NO_ERRORS : toFieldErrors(validation.error);
  const pointsError = Object.entries(errors).find(([path]) => path.startsWith('points'))?.[1];

  const update = useCallback(
    (patch: Partial<FanCurve>) => setDraft((current) => ({ ...(current ?? curve), ...patch })),
    [curve],
  );
  const changePoints = useCallback((points: FanCurvePoint[]) => update({ points }), [update]);

  const save = async () => {
    if (!validation.success) {
      toast.error(`Cannot save: ${firstError(errors) ?? 'the curve is invalid'}.`);
      return;
    }
    setBusy(true);
    const result = await api.upsertFanCurve(validation.data);
    setBusy(false);
    if (result) {
      setDraft(null);
      toast.success(`Saved the curve for ${channel.name}.`);
    }
  };

  const remove = async () => {
    setConfirmingRemove(false);
    setBusy(true);
    const result = await api.removeFanCurve(curve.id);
    setBusy(false);
    if (result) {
      toast.info(`${channel.name} is back under firmware/BIOS control.`);
    }
  };

  return (
    <div className="space-y-5">
      <div className="space-y-1.5">
        <CurveEditor
          label={channel.name}
          points={value.points}
          onChange={changePoints}
          hysteresis={value.hysteresisCelsius}
          showHysteresis
          minDuty={value.minDuty}
          fixedDuty={value.mode === 'Fixed' ? value.fixedDuty : null}
          liveTemperature={status?.temperature ?? null}
          liveDuty={status ? status.appliedDuty : null}
        />
        {pointsError !== undefined && <p className="text-xs text-red-400">{pointsError}</p>}
      </div>

      <CurveSettingsForm
        value={value}
        channel={channel}
        errors={errors}
        sensorOptions={sensorOptions}
        onChange={update}
      />

      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-zinc-800 pt-4">
        <Button variant="ghost" onClick={() => setConfirmingRemove(true)} disabled={busy}>
          <TrashIcon className="size-4" />
          Remove curve
        </Button>
        <div className="flex flex-wrap items-center gap-2">
          {draft && <Badge tone="warning">Unsaved changes</Badge>}
          <Button onClick={() => setDraft(null)} disabled={!draft || busy}>
            Reset
          </Button>
          <Button variant="primary" onClick={() => void save()} disabled={!draft || busy || !validation.success}>
            Save curve
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={confirmingRemove}
        title={`Remove the curve for ${channel.name}?`}
        confirmLabel="Remove curve"
        onConfirm={() => void remove()}
        onCancel={() => setConfirmingRemove(false)}
      >
        <p>
          RigForge stops driving this {channel.kind === 'Pump' ? 'pump' : 'fan'} and the channel returns to
          firmware/BIOS control, which decides its speed from then on.
        </p>
        <p>You can enable control again at any time.</p>
      </ConfirmDialog>
    </div>
  );
}
