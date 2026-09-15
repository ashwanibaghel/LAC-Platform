export type Orientation = "Portrait" | "Landscape";
export type PageProfile = {
  id: string;
  pageSize: "A4" | "Legal" | "NotingSheet";
  widthMm: number;
  heightMm: number;
  marginTopMm: number;
  marginRightMm: number;
  marginBottomMm: number;
  marginLeftMm: number;
  reservedTopMm?: number;
  locked: boolean;
  calibrationNote?: string;
};

export const LETTER_PAPER_PROFILES = {
  A4: { widthMm: 210, heightMm: 297 },
  Legal: { widthMm: 215.9, heightMm: 355.6 },
} as const;

// Provisional calibration only. Replace these values after measuring the real office noting sheet.
export const NOTING_SHEET_PROFILE: PageProfile = {
  id: "noting-sheet-v1-provisional",
  pageSize: "NotingSheet",
  widthMm: 210,
  heightMm: 297,
  marginTopMm: 25,
  marginRightMm: 20,
  marginBottomMm: 20,
  marginLeftMm: 25,
  reservedTopMm: 0,
  locked: true,
  calibrationNote: "Provisional A4-based noting-sheet calibration; replace after physical measurement.",
};

export function resolvePageProfile(draft: { draftType: string; pageSize: string; orientation: Orientation; marginTopMm: number; marginRightMm: number; marginBottomMm: number; marginLeftMm: number }): PageProfile {
  if (draft.draftType === "Noting") return NOTING_SHEET_PROFILE;
  const paper = LETTER_PAPER_PROFILES[draft.pageSize as keyof typeof LETTER_PAPER_PROFILES] ?? LETTER_PAPER_PROFILES.A4;
  const landscape = draft.orientation === "Landscape";
  return {
    id: `letter-${draft.pageSize.toLowerCase()}-${draft.orientation.toLowerCase()}`,
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

export function profilePrintSize(profile: PageProfile): string { return `${profile.widthMm}mm ${profile.heightMm}mm`; }
