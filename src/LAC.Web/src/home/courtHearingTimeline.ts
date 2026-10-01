import type { CourtCaseListItemDto } from "../court/types";

export type UrgentImportSummary = { urgentTotal: number; upcomingNext7Days: number; overduePending: number; targetBatchId: string | null; officeToday: string };
export type UrgentImportEntry = { importBatchId: string; importRowId: string; sourceRowNumber: number; rawCaseNumber?: string; rawCaseTitle?: string; suggestedCourtName?: string; rawCourt?: string; parsedNdoh: string; source: "Court Excel"; reviewPending: true };
export type UrgentImportResponse = { summary: UrgentImportSummary; items: UrgentImportEntry[] };
export const importReviewLink = (row: UrgentImportEntry) => `/court-cases/imports/${row.importBatchId}?priority=urgent&reviewRow=${row.importRowId}`;
export const workbookDateLabel = (date: string, today: string) => {
  const days = Math.round((Date.parse(date) - Date.parse(today)) / 86400000);
  return days < 0 ? "Overdue · Review pending" : days === 0 ? "Today" : days === 1 ? "Tomorrow" : `In ${days} days`;
};
export function courtHearingTimeline(canonical: CourtCaseListItemDto[], workbook: UrgentImportEntry[], today: string) {
  const entries = [
    ...canonical.map(item => ({ kind: "canonical" as const, date: item.operationalNdoh ?? "9999-12-31", key: item.id, item })),
    ...workbook.map(item => ({ kind: "workbook" as const, date: item.parsedNdoh, key: `${item.importBatchId}:${item.importRowId}`, item })),
  ];
  return entries.sort((a, b) => {
    const aOverdue = a.kind === "workbook" && a.date < today;
    const bOverdue = b.kind === "workbook" && b.date < today;
    return Number(aOverdue) - Number(bOverdue) || (aOverdue && bOverdue ? b.date.localeCompare(a.date) : a.date.localeCompare(b.date)) || a.key.localeCompare(b.key);
  });
}
