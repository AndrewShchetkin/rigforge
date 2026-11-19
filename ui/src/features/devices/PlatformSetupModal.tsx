import { useEffect, useId, useRef, useState } from 'react';
import type { DaemonInfoDto } from '../../api/contracts.ts';
import { useToast } from '../../components/feedback/useToast.ts';
import { Button } from '../../components/ui/Button.tsx';
import { CloseIcon, CopyIcon } from '../../components/ui/icons.tsx';
import { PanelCorners } from '../../components/ui/PanelFrame.tsx';
import { Toggle } from '../../components/ui/Toggle.tsx';
import { formatMegabytesAdaptive } from '../../lib/format.ts';
import { useDaemonApi } from '../../state/hooks.ts';
import { autostartSavedMessage, autostartToggleModel } from './autostartModel.ts';
import type { DeviceSupportCounts } from './devicesModel.ts';
import { isLinuxPlatform, isWindowsPlatform } from './devicesModel.ts';

interface SetupCommand {
  title: string;
  detail: string;
  command: string;
}

const LINUX_COMMANDS: readonly SetupCommand[] = [
  {
    title: 'Install udev rules',
    detail:
      'Writes 60-rigforge.rules so your user can open supported HID and USB devices, then reloads udev. The shell needs permission to write /etc/udev/rules.d.',
    command:
      'rigforge-scan udev -o /etc/udev/rules.d/60-rigforge.rules && sudo udevadm control --reload-rules && sudo udevadm trigger',
  },
  {
    title: 'Enable i2c-dev',
    detail:
      'Loads the i2c device interface used for Kingston FURY DDR5 RGB. The generated rules also tag /dev/i2c-* with uaccess. modprobe lasts until the next reboot.',
    command: 'sudo modprobe i2c-dev',
  },
  {
    title: 'Enable the user service',
    detail:
      'Installs the systemd user service so RigForge starts when you log in, and adds a RigForge shortcut to the desktop application menu. The unit in this repo is deploy/linux/rigforge.service, and it runs in the graphical session.',
    command: './deploy/linux/install-linux.sh',
  },
];

const WINDOWS_LOGON_TASK = '.\\deploy\\windows\\Install-RigForgeTask.ps1';

type SetupOs = 'linux' | 'windows';

function formatStartedAt(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(date);
}

function CommandCard({
  title,
  detail,
  command,
  onCopy,
}: {
  title: string;
  detail: string;
  command: string;
  onCopy: (command: string) => Promise<void>;
}) {
  return (
    <div className="space-y-2 rounded-lg border border-zinc-800 bg-zinc-950/50 p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <h3 className="text-sm font-medium text-zinc-100">{title}</h3>
        <Button size="sm" onClick={() => void onCopy(command)} aria-label={`Copy ${title} command`}>
          <CopyIcon className="size-3.5" />
          Copy command
        </Button>
      </div>
      <p className="text-xs leading-relaxed text-zinc-400">{detail}</p>
      <pre className="overflow-x-auto rounded-md border border-zinc-800 bg-zinc-950 px-3 py-2 font-mono text-xs leading-relaxed text-zinc-200">
        <code>{command}</code>
      </pre>
    </div>
  );
}

export function PlatformSetupModal({
  open,
  info,
  counts,
  onClose,
}: {
  open: boolean;
  info: DaemonInfoDto;
  counts: DeviceSupportCounts;
  onClose: () => void;
}) {
  const toast = useToast();
  const api = useDaemonApi();
  const dialogRef = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const linux = isLinuxPlatform(info.platform);
  const windows = isWindowsPlatform(info.platform);
  const [tab, setTab] = useState<SetupOs>(linux ? 'linux' : 'windows');
  const [savingAutostart, setSavingAutostart] = useState(false);
  const autostart = autostartToggleModel(info, savingAutostart);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) {
      return;
    }
    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
    return () => {
      if (dialog.open) {
        dialog.close();
      }
    };
  }, [open]);

  const changeAutostart = async (enabled: boolean) => {
    setSavingAutostart(true);
    try {
      const result = await api.setAutostart(enabled);
      if (result) {
        toast.success(autostartSavedMessage(enabled));
      }
    } finally {
      setSavingAutostart(false);
    }
  };

  const copyCommand = async (command: string) => {
    try {
      await navigator.clipboard.writeText(command);
      toast.success('Command copied.');
    } catch {
      toast.error('Could not copy. Select the command and copy it manually.');
    }
  };

  return (
    <dialog
      ref={dialogRef}
      aria-labelledby={titleId}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClick={(event) => {
        if (event.target === event.currentTarget) {
          onClose();
        }
      }}
      data-panel
      className="group relative m-auto max-h-[min(44rem,calc(100dvh-2rem))] w-[min(42rem,calc(100vw-2rem))] overflow-y-auto border border-line bg-panel p-0 text-ink backdrop:bg-black/70"
    >
      <PanelCorners />
      <div className="flex items-start justify-between gap-3 border-b border-line px-5 py-4">
        <div className="min-w-0">
          <h2 id={titleId} className="text-base font-semibold">
            System, startup, and permissions
          </h2>
          <p className="mt-1 text-xs text-zinc-400">Automatic sign-in launch, USB access, and sensor drivers.</p>
        </div>
        <Button variant="ghost" size="sm" onClick={onClose} aria-label="Close">
          <CloseIcon className="size-4" />
        </Button>
      </div>

      <div className="space-y-4 px-5 py-4">
        <section className="border border-accent/40 bg-accent/10 px-4 py-3 text-accent">
          <h3 className="text-sm font-semibold text-zinc-50">Startup with PC</h3>
          <div className="mt-3">
            <Toggle
              label={autostart.label}
              description={autostart.description}
              checked={autostart.checked}
              disabled={autostart.disabled}
              onChange={(enabled) => void changeAutostart(enabled)}
            />
          </div>
        </section>

        <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-1 text-sm">
          <dt className="text-zinc-500">Platform</dt>
          <dd className="text-zinc-100">{info.platform}</dd>
          <dt className="text-zinc-500">Daemon</dt>
          <dd className="text-zinc-100">v{info.version}</dd>
          <dt className="text-zinc-500">Started</dt>
          <dd className="text-zinc-100">{formatStartedAt(info.startedAt)}</dd>
          <dt className="text-zinc-500">Working set</dt>
          <dd className="text-zinc-100">{formatMegabytesAdaptive(info.workingSetBytes)}</dd>
          <dt className="text-zinc-500">Devices</dt>
          <dd className="text-zinc-100">
            {counts.All} detected · {counts.Supported} active · {counts.TelemetryOnly} sensors · {counts.Unmanaged}{' '}
            unmanaged
          </dd>
        </dl>

        <p className="text-xs leading-relaxed text-zinc-400">
          Commands are copied to the clipboard. Review them in a terminal before you run them. Close NZXT CAM,
          SignalRGB, and Armoury Crate LightingService before the first live run; the daemon lists them as warnings
          while they are open.
        </p>

        <div role="group" aria-label="Operating system" className="inline-flex rounded-md border border-zinc-700 bg-zinc-950/80 p-0.5">
          <OsTab label="Linux" selected={tab === 'linux'} thisPc={linux} onSelect={() => setTab('linux')} />
          <OsTab label="Windows" selected={tab === 'windows'} thisPc={windows} onSelect={() => setTab('windows')} />
        </div>

        {tab === 'linux' ? (
          <div className="space-y-3">
            <p className="text-sm leading-relaxed text-zinc-300">
              On Linux the daemon runs as your user. udev rules grant access to supported controllers,{' '}
              <code className="font-mono text-[0.92em]">i2c-dev</code> exposes SMBus for DRAM lighting, and a systemd
              user service starts RigForge in the graphical session.{' '}
              <code className="font-mono text-[0.92em]">./deploy/linux/install-linux.sh</code> also adds a RigForge
              shortcut to the desktop application menu.{' '}
              <code className="font-mono text-[0.92em]">./deploy/linux/uninstall-linux.sh</code> removes the service and
              udev rules and keeps profiles in <code className="font-mono text-[0.92em]">~/.config/rigforge</code>.
            </p>
            <ol className="space-y-3">
              {LINUX_COMMANDS.map((item) => (
                <li key={item.title}>
                  <CommandCard title={item.title} detail={item.detail} command={item.command} onCopy={copyCommand} />
                </li>
              ))}
            </ol>
          </div>
        ) : (
          <div className="space-y-3">
            <p className="text-sm leading-relaxed text-zinc-300">
              Windows opens RGB controllers with user-mode USB HID, so lighting uses the drivers Windows already
              provides. CPU, Super I/O, and Kingston FURY SMBus access go through the signed PawnIO driver. The daemon
              is a logon task in your interactive session, which is what those sensors and later screen capture need.
            </p>
            <p className="text-sm leading-relaxed text-zinc-300">
              <span className="font-medium text-zinc-100">RigForge Setup</span> creates shortcuts on the Desktop and in
              the Start Menu, registers the <code className="font-mono text-[0.92em]">RigForge</code> sign-in task, and
              adds the app to Windows Installed apps. Uninstall from Installed apps and choose whether to keep profiles
              in <code className="font-mono text-[0.92em]">%LOCALAPPDATA%\RigForge</code>.
            </p>
            <ul className="space-y-2 text-sm leading-relaxed text-zinc-300">
              <li className="rounded-lg border border-zinc-800 bg-zinc-950/40 px-3 py-2">
                <span className="font-medium text-zinc-100">USB HID.</span> User-mode reports only. RigForge does not
                install a kernel driver for RGB controllers.
              </li>
              <li className="rounded-lg border border-zinc-800 bg-zinc-950/40 px-3 py-2">
                <span className="font-medium text-zinc-100">PawnIO.</span> Signed SMBus and sensor access. Addresses
                outside the Kingston FURY window stay blocked.
              </li>
            </ul>
            <CommandCard
              title="Developer setup from source (optional)"
              detail="Only needed when you build RigForge directly from the repository. Run from an elevated PowerShell at the repository root. The script publishes the daemon and registers the logon task."
              command={WINDOWS_LOGON_TASK}
              onCopy={copyCommand}
            />
          </div>
        )}
      </div>

      <div className="flex justify-end border-t border-zinc-800 px-5 py-3">
        <Button onClick={onClose}>Close</Button>
      </div>
    </dialog>
  );
}

function OsTab({
  label,
  selected,
  thisPc,
  onSelect,
}: {
  label: string;
  selected: boolean;
  thisPc: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={`px-3 py-1 text-sm transition-colors ${selected ? 'bg-accent/15 text-accent' : 'text-dim hover:text-ink'}`}
    >
      {label}
      {thisPc && <span className="ml-1.5 text-[10px] font-semibold tracking-wide uppercase opacity-80">This PC</span>}
    </button>
  );
}
