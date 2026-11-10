import type { ReactNode, SVGProps } from 'react';

type IconProps = SVGProps<SVGSVGElement>;

function BaseIcon({ children, ...props }: IconProps & { children: ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width="1em"
      height="1em"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...props}
    >
      {children}
    </svg>
  );
}

export function DashboardIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <rect x="3" y="3" width="7" height="9" rx="1.5" />
      <rect x="14" y="3" width="7" height="5" rx="1.5" />
      <rect x="14" y="12" width="7" height="9" rx="1.5" />
      <rect x="3" y="16" width="7" height="5" rx="1.5" />
    </BaseIcon>
  );
}

export function LightingIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M9 18h6M10 21h4" />
      <path d="M12 3a6 6 0 0 0-3.6 10.8c.4.3.6.8.6 1.3V16h6v-.9c0-.5.2-1 .6-1.3A6 6 0 0 0 12 3Z" />
    </BaseIcon>
  );
}

export function CoolingIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="1.5" />
      <path d="M12 10.5c-.5-2.5.3-4.8 2.4-5.4M13.5 12c2.5-.5 4.8.3 5.4 2.4M12 13.5c.5 2.5-.3 4.8-2.4 5.4M10.5 12c-2.5.5-4.8-.3-5.4-2.4" />
    </BaseIcon>
  );
}

export function LcdIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M7.5 14a4.5 4.5 0 0 1 9 0M12 14l2.2-3" />
    </BaseIcon>
  );
}

export function DevicesIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <rect x="6" y="6" width="12" height="12" rx="2" />
      <path d="M9 2v4M15 2v4M9 18v4M15 18v4M2 9h4M2 15h4M18 9h4M18 15h4" />
    </BaseIcon>
  );
}

export function RefreshIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M20 11a8 8 0 0 0-14.3-4.9L4 8M4 3v5h5" />
      <path d="M4 13a8 8 0 0 0 14.3 4.9L20 16M20 21v-5h-5" />
    </BaseIcon>
  );
}

export function WarningIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M10.3 4.2 2.6 18a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0Z" />
      <path d="M12 9.5v4M12 17h.01" />
    </BaseIcon>
  );
}

export function InfoIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 11v5M12 8h.01" />
    </BaseIcon>
  );
}

export function CheckCircleIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="m8 12.5 2.7 2.7L16 9.5" />
    </BaseIcon>
  );
}

export function ErrorIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="m9 9 6 6M15 9l-6 6" />
    </BaseIcon>
  );
}

export function CloseIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M6 6l12 12M18 6 6 18" />
    </BaseIcon>
  );
}

export function PlusIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 5v14M5 12h14" />
    </BaseIcon>
  );
}

export function TrashIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3" />
    </BaseIcon>
  );
}

export function GridIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <rect x="4" y="4" width="6" height="6" rx="1" />
      <rect x="14" y="4" width="6" height="6" rx="1" />
      <rect x="4" y="14" width="6" height="6" rx="1" />
      <rect x="14" y="14" width="6" height="6" rx="1" />
    </BaseIcon>
  );
}

export function SearchIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4 4" />
    </BaseIcon>
  );
}

export function UndoIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M9 14 4 9l5-5" />
      <path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11" />
    </BaseIcon>
  );
}

export function BlinkIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M13 2.5 5.5 13H11l-1 8.5L18.5 10H13l0-7.5Z" />
    </BaseIcon>
  );
}

export function DownloadIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 4v11" />
      <path d="m7.5 11.5 4.5 4.5 4.5-4.5" />
      <path d="M5 20h14" />
    </BaseIcon>
  );
}

export function UploadIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 16V5" />
      <path d="m7.5 8.5 4.5-4.5 4.5 4.5" />
      <path d="M5 20h14" />
    </BaseIcon>
  );
}

export function CopyIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <rect x="9" y="9" width="11" height="11" rx="2" />
      <path d="M5 15V6a2 2 0 0 1 2-2h9" />
    </BaseIcon>
  );
}

export function ShieldIcon(props: IconProps) {
  return (
    <BaseIcon {...props}>
      <path d="M12 3.2 5.5 6v5.6c0 3.6 2.5 6.2 6.5 7.6 4-1.4 6.5-4 6.5-7.6V6L12 3.2Z" />
      <path d="m9.2 12 1.9 1.9 3.7-3.8" />
    </BaseIcon>
  );
}
