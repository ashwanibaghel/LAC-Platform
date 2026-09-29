type ReviewableRow = {
  rowStatus: string;
  rawCaseNumber?: string;
  validationIssuesJson: string;
};

const issueLabels: Record<string, string> = {
  "NDOH is not a deterministic date.": "The hearing date could not be read reliably.",
  "Case status is blank.": "The case status is missing from the workbook.",
  "Case status needs classification review.": "The workbook status is not a standard Pending or Disposed value.",
  "Court name is not in the approved alias list.": "The court name is not recognized automatically.",
  "Last-order link is not an http/https URL.": "The last-order link is not a usable web address.",
};

export function courtImportReviewGuidance(row: ReviewableRow): { reasons: string[]; nextStep: string } {
  let sourceIssues: string[] = [];
  try {
    const parsed: unknown = JSON.parse(row.validationIssuesJson || "[]");
    if (Array.isArray(parsed)) sourceIssues = parsed.filter((issue): issue is string => typeof issue === "string");
  } catch {
    sourceIssues = ["Stored validation details could not be read. Check the workbook entry manually."];
  }
  const reasons = sourceIssues.map(issue => issueLabels[issue] ?? issue);
  const references = row.rawCaseNumber?.match(/\d+\s*\/\s*(?:19|20)\d{2}/g) ?? [];

  if (row.rowStatus === "NeedsReview" && reasons.length === 0) {
    reasons.push(!row.rawCaseNumber?.trim()
      ? "The case number is missing, so LAC cannot identify a single court case."
      : references.length > 1
        ? "This cell contains more than one case reference. LAC cannot safely choose one case identity automatically."
        : "The case number is not in a single standard case type + number/year format, so automatic matching was skipped.");
  }
  if (row.rowStatus === "PotentialDuplicate")
    reasons.unshift("This case reference also appears in another imported row or matches multiple existing cases.");
  if (row.rowStatus === "IdentityConflict")
    reasons.unshift("The case reference matches another record, but the party title or identity details differ.");
  if (row.rowStatus === "Invalid" && reasons.length === 0)
    reasons.push("This row could not be classified safely from the workbook details.");

  const nextStep = row.rowStatus === "NeedsReview" && references.length > 1
    ? "Check the official case or order document. If these references are for one case, enter the one approved case number; otherwise link the exact existing case or leave this row out."
    : "Compare the source details. Create a case only if its identity is clear, link an exact existing case if it is already recorded, or leave the row out until it can be verified.";
  return { reasons, nextStep };
}
