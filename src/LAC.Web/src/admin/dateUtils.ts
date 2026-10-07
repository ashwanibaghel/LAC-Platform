/**
 * Date / Time Contract Utilities for LAC Platform RBAC
 *
 * Invariants:
 * - ISO 8601 offsets
 * - ValidFrom inclusive, ValidTo exclusive
 * - Stored UTC on server
 * - UI renders office/client-local calendar dates
 * - Never treat YYYY-MM-DD as UTC midnight accidentally (avoids new Date("YYYY-MM-DD").toISOString())
 */

/**
 * Converts a YYYY-MM-DD string from an HTML date input into an ISO 8601 string
 * representing local midnight on that calendar date with an explicit timezone offset.
 * Example in IST (+05:30): "2026-10-07" -> "2026-10-07T00:00:00+05:30"
 */
export function localDateInputToIso(dateStr?: string | null): string | null {
  if (!dateStr || !dateStr.trim()) return null;
  const parts = dateStr.trim().split("-");
  if (parts.length !== 3) return null;
  const year = parseInt(parts[0], 10);
  const month = parseInt(parts[1], 10) - 1;
  const day = parseInt(parts[2], 10);
  if (isNaN(year) || isNaN(month) || isNaN(day)) return null;

  const localDate = new Date(year, month, day, 0, 0, 0, 0);
  const offsetMinutes = -localDate.getTimezoneOffset();
  const sign = offsetMinutes >= 0 ? "+" : "-";
  const absOffset = Math.abs(offsetMinutes);
  const offHours = String(Math.floor(absOffset / 60)).padStart(2, "0");
  const offMins = String(absOffset % 60).padStart(2, "0");
  const offsetStr = `${sign}${offHours}:${offMins}`;

  const y = localDate.getFullYear();
  const m = String(localDate.getMonth() + 1).padStart(2, "0");
  const d = String(localDate.getDate()).padStart(2, "0");
  const hh = String(localDate.getHours()).padStart(2, "0");
  const mm = String(localDate.getMinutes()).padStart(2, "0");
  const ss = String(localDate.getSeconds()).padStart(2, "0");

  return `${y}-${m}-${d}T${hh}:${mm}:${ss}${offsetStr}`;
}

/**
 * Derives the local calendar date (YYYY-MM-DD) from an ISO timestamp in the browser/office local timezone.
 * Avoids iso.split("T")[0] which incorrectly treats UTC dates as local.
 */
export function isoToLocalDateInput(isoString?: string | null): string {
  if (!isoString || !isoString.trim()) return "";
  const d = new Date(isoString);
  if (isNaN(d.getTime())) return "";
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

/**
 * Returns today's calendar date in YYYY-MM-DD format based on browser/office local time.
 */
export function getTodayLocalDateInput(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${y}-${m}-${day}`;
}

/**
 * Formats an ISO string for display to user in local timezone.
 */
export function formatLocalDate(isoString?: string | null): string {
  if (!isoString) return "None";
  const d = new Date(isoString);
  if (isNaN(d.getTime())) return isoString;
  return d.toLocaleDateString("en-IN", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/**
 * Validates that validTo > validFrom when validTo is provided.
 * Accepts ISO strings or YYYY-MM-DD strings.
 */
export function validateDateRange(
  validFromVal?: string | null,
  validToVal?: string | null
): { valid: boolean; error?: string } {
  if (!validFromVal || !validFromVal.trim()) {
    return { valid: false, error: "Valid From date is required." };
  }
  const fromTime = new Date(validFromVal.includes("T") ? validFromVal : localDateInputToIso(validFromVal)!).getTime();
  if (isNaN(fromTime)) {
    return { valid: false, error: "Valid From date is invalid." };
  }
  if (!validToVal || !validToVal.trim()) {
    return { valid: true };
  }
  const toTime = new Date(validToVal.includes("T") ? validToVal : localDateInputToIso(validToVal)!).getTime();
  if (isNaN(toTime)) {
    return { valid: false, error: "Valid To date is invalid." };
  }
  if (toTime <= fromTime) {
    return { valid: false, error: "Valid To (exclusive) must be strictly greater than Valid From." };
  }
  return { valid: true };
}
