import { describe, expect, it } from 'vitest';
import {
  autostartSavedMessage,
  autostartToggleModel,
} from '../autostartModel.ts';

const WINDOWS_DESCRIPTION =
  'Runs the hardware daemon in the notification area at Windows sign-in so fan curves, AIO LCD, and RGB lighting apply immediately without a UAC prompt.';
const LINUX_DESCRIPTION =
  'Enables the rigforge.service systemd user unit so RigForge starts automatically in your graphical session.';
const UNAVAILABLE =
  'Automatic startup management is unavailable in this session (for example, when running unpacked without the system service or scheduled task).';

describe('autostartToggleModel', () => {
  it('describes Windows sign-in startup and reflects an enabled, manageable switch', () => {
    expect(
      autostartToggleModel(
        { platform: 'Microsoft Windows 11', autostartSupported: true, autostartEnabled: true },
        false,
      ),
    ).toEqual({
      label: 'Start RigForge automatically when you sign in',
      description: WINDOWS_DESCRIPTION,
      checked: true,
      disabled: false,
    });
  });

  it('describes the Linux user unit for any platform string that names Linux', () => {
    expect(
      autostartToggleModel({ platform: 'Linux 6.8.0-45-generic', autostartSupported: true }, false).description,
    ).toBe(LINUX_DESCRIPTION);
    expect(autostartToggleModel({ platform: 'ubuntu linux', autostartSupported: true }, false).description).toBe(
      LINUX_DESCRIPTION,
    );
  });

  it('uses the Windows description when the platform is not Linux, including win32', () => {
    expect(autostartToggleModel({ platform: 'win32', autostartSupported: true }, false).description).toBe(
      WINDOWS_DESCRIPTION,
    );
  });

  it('adds the unavailable explanation only when autostart management is explicitly unsupported', () => {
    const unsupported = autostartToggleModel(
      { platform: 'Microsoft Windows 11', autostartSupported: false, autostartEnabled: true },
      false,
    );
    expect(unsupported.description).toBe(`${WINDOWS_DESCRIPTION} ${UNAVAILABLE}`);
    expect(unsupported.checked).toBe(true);
    expect(unsupported.disabled).toBe(true);

    const omitted = autostartToggleModel({ platform: 'Microsoft Windows 11' }, false);
    expect(omitted.description).toBe(WINDOWS_DESCRIPTION);
    expect(omitted.checked).toBe(false);
    expect(omitted.disabled).toBe(true);
  });

  it('disables the switch while a change is saving, even when startup management is supported', () => {
    expect(
      autostartToggleModel({ platform: 'Linux', autostartSupported: true, autostartEnabled: false }, true),
    ).toMatchObject({ checked: false, disabled: true, description: LINUX_DESCRIPTION });
  });
});

describe('autostartSavedMessage', () => {
  it('confirms sign-in startup when enabled and the manual launch path when disabled', () => {
    expect(autostartSavedMessage(true)).toBe('RigForge will start automatically when you sign in.');
    expect(autostartSavedMessage(false)).toBe(
      'Automatic startup disabled. You can still launch RigForge from the Desktop or Start Menu shortcut.',
    );
  });
});
