/**
 * Configurable parameters and helpers for Dak module
 */

// Configurable threshold for long-pending receipt alerts (in hours)
export const DAK_PENDING_RECEIPT_ALERT_HOURS = 48;

/**
 * Format internal backend status to user-facing terminology
 * Specifically translates "Disposed" to "Resolved" per design requirement
 */
export function formatDakStatus(status: string): string {
  if (status === "Disposed") return "Resolved";
  return status;
}

/**
 * Checks whether an item or assignment has been pending receipt/action longer than configured threshold
 */
export function isLongPendingReceipt(
  timestamp?: string | null,
  thresholdHours: number = DAK_PENDING_RECEIPT_ALERT_HOURS
): boolean {
  if (!timestamp) return false;
  const date = new Date(timestamp);
  if (isNaN(date.getTime())) return false;
  const elapsedHours = (Date.now() - date.getTime()) / (1000 * 60 * 60);
  return elapsedHours >= thresholdHours;
}

/**
 * Returns human-friendly elapsed time string (e.g. "2h ago", "3d ago")
 */
export function formatElapsedTime(timestamp?: string | null): string {
  if (!timestamp) return "";
  const date = new Date(timestamp);
  if (isNaN(date.getTime())) return "";
  const diffMs = Date.now() - date.getTime();
  if (diffMs < 0) return "just now";
  const diffMins = Math.floor(diffMs / (1000 * 60));
  if (diffMins < 60) return `${diffMins}m ago`;
  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays}d ago`;
}
