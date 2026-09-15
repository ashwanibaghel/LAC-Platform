export type NotingReservedSide = "left" | "right";

export type NotingPageGeometry = {
  pageNumber: number;
  reservedSide: NotingReservedSide;
  pageWidthMm: number;
  pageHeightMm: number;
  topMm: number;
  bottomMm: number;
  writingHeightMm: number;
  gutterMm: number;
  writingWidthMm: number;
  separatorXmm: number;
  separatorTopMm: number;
  separatorBottomMm: number;
  contentLeftMm: number;
  contentRightMm: number;
  contentWidthMm: number;
};

/**
 * Fixed physical layout for Delhi LAC noting sheets.
 *
 * The gutter is mirrored by page parity only. It never mirrors text, writing
 * direction, or table column order. The paginator will later consume
 * getNotingPageGeometry(pageNumber) when it creates page-aware writing zones.
 */
export const DELHI_LAC_NOTING_LEGAL_MIRROR_V1 = Object.freeze({
  id: "delhi-lac-noting-legal-mirror-v1",
  displayName: "Delhi LAC Noting Sheet",
  pageSize: "Legal" as const,
  orientation: "Portrait" as const,
  pageWidthMm: 215.9,
  pageHeightMm: 355.6,
  topMm: 25,
  bottomMm: 25,
  gutterMm: 45,
  writingWidthMm: 170.9,
  writingHeightMm: 305.6,
  mirrorByPageParity: true,
  printModel: Object.freeze({
    pageSize: "Legal Portrait",
    oddPageReservedSide: "left" as const,
    evenPageReservedSide: "right" as const,
    topMm: 25,
    bottomMm: 25,
    gutterMm: 45,
    browserSupportNote: "The final page-aware print stylesheet must be verified in the target Chromium print path before physical parity is claimed.",
  }),
});

export function getNotingPageGeometry(pageNumber: number): NotingPageGeometry {
  if (!Number.isInteger(pageNumber) || pageNumber < 1) {
    throw new RangeError("Noting page number must be a positive integer.");
  }

  const profile = DELHI_LAC_NOTING_LEGAL_MIRROR_V1;
  const reservedSide: NotingReservedSide = pageNumber % 2 === 1 ? "left" : "right";
  const separatorXmm = reservedSide === "left"
    ? profile.gutterMm
    : profile.pageWidthMm - profile.gutterMm;

  return {
    pageNumber,
    reservedSide,
    pageWidthMm: profile.pageWidthMm,
    pageHeightMm: profile.pageHeightMm,
    topMm: profile.topMm,
    bottomMm: profile.bottomMm,
    writingHeightMm: profile.writingHeightMm,
    gutterMm: profile.gutterMm,
    writingWidthMm: profile.writingWidthMm,
    separatorXmm,
    separatorTopMm: profile.topMm,
    separatorBottomMm: profile.topMm + profile.writingHeightMm,
    contentLeftMm: reservedSide === "left" ? profile.gutterMm : 0,
    contentRightMm: reservedSide === "right" ? profile.gutterMm : 0,
    contentWidthMm: profile.writingWidthMm,
  };
}
