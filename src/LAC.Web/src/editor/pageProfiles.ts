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

// PROVISIONAL CALIBRATION ONLY.
// We do not yet possess verified physical noting sheet measurements from the Delhi LAC.
// This profile must be calibrated against an authentic blank physical noting sheet and test print.
export const DELHI_LAC_NOTING_V1: PageProfile = {
  id: "delhi-lac-noting-v1-provisional",
  displayName: "Delhi LAC Noting Sheet (Provisional)",
  pageSize: "NotingSheet",
  widthMm: 210.0,
  heightMm: 297.0,
  marginTopMm: 25.0,
  marginRightMm: 20.0,
  marginBottomMm: 20.0,
  marginLeftMm: 25.0, // Clearance for physical thread file-binding (tag/dori)
  reservedTopMm: 0,
  locked: true,
  isProvisional: true,
  calibrationNote: "Provisional calibration based on standard A4 file dimensions. Exact millimetre values to be updated upon physical measurement of authentic blank LAC noting sheets.",
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

