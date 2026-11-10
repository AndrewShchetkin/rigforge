import { useRef, useState } from 'react';
import { useToast } from '../../components/feedback/useToast.ts';
import { Button } from '../../components/ui/Button.tsx';
import { DownloadIcon, UploadIcon } from '../../components/ui/icons.tsx';
import { Spinner } from '../../components/ui/Spinner.tsx';
import { useDaemonApi } from '../../state/hooks.ts';
import { formatProfileJson, isProfileImportWithinLimit, parseProfileJson, profileFileName } from './devicesModel.ts';

function downloadTextFile(filename: string, contents: string): void {
  const blob = new Blob([contents], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.rel = 'noopener';
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
}

export function ProfileTransferBar() {
  const api = useDaemonApi();
  const toast = useToast();
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState<'export' | 'import' | null>(null);

  const exportProfile = async () => {
    if (busy) {
      return;
    }
    setBusy('export');
    try {
      const exported = await api.exportProfile();
      if (!exported) {
        return;
      }
      const filename = profileFileName(new Date());
      try {
        downloadTextFile(filename, formatProfileJson(exported));
      } catch {
        toast.error('Could not start the profile download.');
        return;
      }
      toast.success(`Exported ${filename}.`);
    } finally {
      setBusy(null);
    }
  };

  const importFile = async (file: File) => {
    if (busy) {
      return;
    }
    if (!isProfileImportWithinLimit(file.size)) {
      toast.error('Profile files must be 1 MB or smaller.');
      return;
    }
    setBusy('import');
    try {
      let text: string;
      try {
        text = await file.text();
      } catch {
        toast.error('Could not read the profile file.');
        return;
      }
      const parsed = parseProfileJson(text);
      if (!parsed.ok) {
        toast.error(parsed.message);
        return;
      }
      const saved = await api.importProfile(parsed.profile);
      if (saved) {
        toast.success('Profile imported.');
      }
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Profile transfer">
      <input
        ref={inputRef}
        type="file"
        accept=".json,application/json"
        className="sr-only"
        tabIndex={-1}
        aria-label="Profile JSON file"
        onChange={(event) => {
          const input = event.currentTarget;
          const file = input.files?.[0];
          input.value = '';
          if (file) {
            void importFile(file);
          }
        }}
      />
      <Button onClick={() => void exportProfile()} disabled={busy !== null} aria-busy={busy === 'export'}>
        {busy === 'export' ? <Spinner /> : <DownloadIcon className="size-4" />}
        Export profile
      </Button>
      <Button
        onClick={() => inputRef.current?.click()}
        disabled={busy !== null}
        aria-busy={busy === 'import'}
      >
        {busy === 'import' ? <Spinner /> : <UploadIcon className="size-4" />}
        Import profile
      </Button>
    </div>
  );
}
