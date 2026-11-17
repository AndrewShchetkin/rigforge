import { isLinuxPlatform } from './devicesModel.ts';

const AUTOSTART_TOGGLE_LABEL = 'Start RigForge automatically when you sign in';

const WINDOWS_AUTOSTART_DESCRIPTION =
  'Runs the hardware daemon in the notification area at Windows sign-in so fan curves, AIO LCD, and RGB lighting apply immediately without a UAC prompt.';

const LINUX_AUTOSTART_DESCRIPTION =
  'Enables the rigforge.service systemd user unit so RigForge starts automatically in your graphical session.';

const AUTOSTART_UNAVAILABLE_NOTE =
  'Automatic startup management is unavailable in this session (for example, when running unpacked without the system service or scheduled task).';

export interface AutostartToggleSource {
  platform: string;
  autostartSupported?: boolean;
  autostartEnabled?: boolean;
}

export interface AutostartToggleModel {
  label: string;
  description: string;
  checked: boolean;
  disabled: boolean;
}

/** Copy and switch state for the sign-in startup control. Linux is detected from the platform string. */
export function autostartToggleModel(info: AutostartToggleSource, saving: boolean): AutostartToggleModel {
  const summary = isLinuxPlatform(info.platform) ? LINUX_AUTOSTART_DESCRIPTION : WINDOWS_AUTOSTART_DESCRIPTION;
  const description = info.autostartSupported === false ? `${summary} ${AUTOSTART_UNAVAILABLE_NOTE}` : summary;
  return {
    label: AUTOSTART_TOGGLE_LABEL,
    description,
    checked: Boolean(info.autostartEnabled),
    disabled: !info.autostartSupported || saving,
  };
}

/** Toast after `SetAutostart` succeeds. */
export function autostartSavedMessage(enabled: boolean): string {
  return enabled
    ? 'RigForge will start automatically when you sign in.'
    : 'Automatic startup disabled. You can still launch RigForge from the Desktop or Start Menu shortcut.';
}
