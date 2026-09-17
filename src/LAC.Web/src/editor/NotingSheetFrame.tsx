import type { CSSProperties } from "react";
import { getNotingPageGeometry } from "./notingLayoutProfile";

export function NotingSheetFrame({ pageNumber }: { pageNumber: number }) {
  const geometry = getNotingPageGeometry(pageNumber);
  const style = {
    "--noting-separator-x": `${geometry.separatorXmm}mm`,
    "--noting-separator-top": `${geometry.separatorTopMm}mm`,
    "--noting-separator-height": `${geometry.writingHeightMm}mm`,
  } as CSSProperties;

  return (
    <div className={`draft-sheet-card noting-sheet-frame noting-sheet-frame-${geometry.reservedSide}`} style={style}>
      <div className="noting-sheet-separator" aria-hidden="true" />
      <div className="draft-sheet-badge">
        Noting Sheet · Page {pageNumber} · {geometry.reservedSide === "left" ? "Left" : "Right"} 45 mm binding gutter
      </div>
    </div>
  );
}
