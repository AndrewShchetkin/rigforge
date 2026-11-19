const CORNER = "pointer-events-none absolute z-3 text-[13px] leading-none text-[#4e4e4e] group-hover:text-accent";

/** Box-drawing corners for a terminal panel. Brighten when the panel is hovered. */
export function PanelCorners() {
  return (
    <>
      <span aria-hidden="true" className={`${CORNER} -top-2 -left-px`}>
        ┌
      </span>
      <span aria-hidden="true" className={`${CORNER} -top-2 -right-px`}>
        ┐
      </span>
      <span aria-hidden="true" className={`${CORNER} -bottom-2 -left-px`}>
        └
      </span>
      <span aria-hidden="true" className={`${CORNER} -right-px -bottom-2`}>
        ┘
      </span>
    </>
  );
}

export const panelClass = "group relative border border-line bg-panel";

export const panelTitleClass =
  "absolute top-0 left-2.5 z-2 -translate-y-1/2 bg-base px-1.5 text-[11px] font-medium tracking-wide text-dim";
