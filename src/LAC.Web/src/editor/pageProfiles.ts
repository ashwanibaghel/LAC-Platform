import { DELHI_LAC_NOTING_LEGAL_MIRROR_V1 } from "./notingLayoutProfile";

export type Orientation = "Portrait" | "Landscape";

export type PageProfile = {
  id: string;
  displayName: string;
  pageSize: "A4" | "Legal" | "NotingSheet";
  widthMm: number;
  heightMm: number;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
  reservedTopMm?: number;
  locked: boolean;
  isProvisional?: boolean;
  calibrationNote?: string;
};

export const LETTER_PAPER_PROFILES = {
  A4: { widthMm: 210.0, heightMm: 297.0, displayName: "A4 (210 × 297 mm)" },
  Legal: { widthMm: 215.9, heightMm: 355.6, displayName: "Legal (215.9 × 355.6 mm)" },
} as const;

// The mirrored 45 mm binding gutter is derived from notingLayoutProfile by page parity;
// it is intentionally not represented as mutable left/right draft margins.
export const DELHI_LAC_NOTING_V1: PageProfile = {
  id: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.id,
  displayName: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.displayName,
  pageSize: "Legal",
  widthMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.pageWidthMm,
  heightMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.pageHeightMm,
  marginTopMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.topMm,
  marginRightMm: 0,
  marginBottomMm: DELHI_LAC_NOTING_LEGAL_MIRROR_V1.bottomMm,
  marginLeftMm: 0,
  locked: true,
};

// Backwards compatibility alias for existing callers
export const NOTING_SHEET_PROFILE = DELHI_LAC_NOTING_V1;

export function resolvePageProfile(draft: {
  draftType: string;
  pageSize: string;
  orientation: Orientation;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
}): PageProfile {
  if (draft.draftType === "Noting") return DELHI_LAC_NOTING_V1;
  const paper = LETTER_PAPER_PROFILES[draft.pageSize as keyof typeof LETTER_PAPER_PROFILES] ?? LETTER_PAPER_PROFILES.A4;
  const landscape = draft.orientation === "Landscape";
  return {
    id: `letter-${draft.pageSize.toLowerCase()}-${draft.orientation.toLowerCase()}`,
    displayName: `${draft.pageSize} ${draft.orientation}`,
    pageSize: draft.pageSize === "Legal" ? "Legal" : "A4",
    widthMm: landscape ? paper.heightMm : paper.widthMm,
    heightMm: landscape ? paper.widthMm : paper.heightMm,
    marginTopMm: draft.marginTopMm,
    marginRightMm: draft.marginRightMm,
    marginBottomMm: draft.marginBottomMm,
    marginLeftMm: draft.marginLeftMm,
    locked: false,
  };
}

export function profilePrintSize(profile: PageProfile): string {
  return `${profile.widthMm}mm ${profile.heightMm}mm`;
}

export const MM_TO_PX = 96 / 25.4; // 3.7795275590551185 pixels per mm

export function mmToPx(mm: number): number {
  return (mm * 96) / 25.4;
}

export function pxToMm(px: number): number {
  return (px * 25.4) / 96;
}
