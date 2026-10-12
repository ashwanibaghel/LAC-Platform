/**
 * Smart Core Document Intake — typed API adapter.
 *
 * Wraps the real backend endpoints introduced by codex/smart-core-document-intake.
 * See: docs/smart-core-document-intake-api.md
 */

// ─── Domain types ────────────────────────────────────────────────────────────

export type CoreDocumentRole =
  | "Award"
  | "NM"
  | "StatementA"
  | "PossessionProceeding"
  | "Unknown";

export type IntakeMatchState =
  | "MatchedExistingAward"
  | "ProposedNewAward"
  | "NeedsOfficerReview";

export type IntakeStatus = "Staged" | "NeedsConfirmation" | "Confirmed";

export interface IntakeEvidence {
  field: string;
  pageNumber: number;
  sourceText: string;
}

export interface IntakeAlternative {
  kind: string;
  value: string;
  awardId: string | null;
  reason: string | null;
}

export interface IntakeItem {
  intakeId: string;
  villageId: string;
  documentId: string;
  fileName: string;
  status: IntakeStatus;
  detectedRole: CoreDocumentRole;
  detectedAwardNumber: string | null;
  detectedAwardDate: string | null;
  detectedVillage: string | null;
  detectedAwardType: string | null;
  confidence: number;
  matchState: IntakeMatchState;
  matchedAwardId: string | null;
  matchedAwardNumber: string | null;
  evidence: IntakeEvidence[];
  alternatives: IntakeAlternative[];
  createdAt: string;
  confirmedAwardId: string | null;
  confirmedRole: CoreDocumentRole | null;
  confirmedDocumentId: string | null;
  confirmedAt: string | null;
  viewRoute: string;
  downloadRoute: string;
  isDuplicate: boolean;
}

export interface BatchUploadResult {
  items: Array<{
    fileName: string;
    intake: IntakeItem | null;
    errorStatus: number | null;
    error: string | null;
  }>;
}

export interface ConfirmExistingAwardPayload {
  documentRole: CoreDocumentRole;
  awardId: string;
}

export interface ConfirmNewAwardPayload {
  documentRole: CoreDocumentRole;
  createAward: {
    awardNumber: string;
    awardDate: string | null;
    awardType: string | null;
    confirmed: true;
  };
}

export type ConflictKind =
  | "SourceLinkedToAnotherAward"
  | "AlreadyConfirmedDifferentDecision"
  | "RoleConflict"
  | "AwardIdentityConflict"
  | "StagedOrConcurrentIntakeConflict"
  | "GenericConflict";

export interface ParsedProblemDetails {
  isConflict: boolean;
  conflictKind?: ConflictKind;
  status: number;
  title?: string;
  detail?: string;
  raw?: string;
  conflictingAwardNumber?: string | null;
  conflictingAwardId?: string | null;
  conflictingVillageName?: string | null;
  conflictingRole?: CoreDocumentRole | string | null;
  conflictingDocumentId?: string | null;
}

/**
 * Safely parse HTTP error text which may be RFC 9110 / Problem Details JSON.
 * Always returns a clean, human-readable string and parsed metadata without fabricating identity.
 */
export function parseProblemDetails(
  rawText: string,
  statusCode: number
): ParsedProblemDetails {
  let isConflict = statusCode === 409;
  let title: string | undefined;
  let detail: string | undefined;
  let conflictingAwardNumber: string | null = null;
  let conflictingAwardId: string | null = null;
  let conflictingVillageName: string | null = null;
  let conflictingRole: CoreDocumentRole | string | null = null;
  let conflictingDocumentId: string | null = null;
  let conflictKind: ConflictKind | undefined;

  try {
    const data = JSON.parse(rawText);
    if (data && typeof data === "object") {
      title = data.title;
      detail = data.detail || (typeof data.error === "string" ? data.error : undefined);
      if (data.status === 409 || title?.toLowerCase().includes("conflict")) {
        isConflict = true;
      }
      if (data.conflictingAwardNumber) conflictingAwardNumber = String(data.conflictingAwardNumber);
      if (data.conflictingAwardId) conflictingAwardId = String(data.conflictingAwardId);
      if (data.conflictingVillageName) conflictingVillageName = String(data.conflictingVillageName);
      if (data.conflictingRole) conflictingRole = data.conflictingRole;
      if (data.conflictingDocumentId) conflictingDocumentId = String(data.conflictingDocumentId);
      if (data.conflictKind && typeof data.conflictKind === "string") {
        conflictKind = data.conflictKind as ConflictKind;
      }
    }
  } catch {
    // Plain text error
    detail = rawText;
  }

  const combined = `${title || ""} ${detail || rawText}`.toLowerCase();
  if (
    combined.includes("already linked") ||
    combined.includes("already attached") ||
    combined.includes("conflict") ||
    combined.includes("different decision") ||
    statusCode === 409
  ) {
    isConflict = true;
  }

  if (isConflict && !conflictKind) {
    if (combined.includes("different decision") || combined.includes("already confirmed")) {
      conflictKind = "AlreadyConfirmedDifferentDecision";
    } else if (
      combined.includes("role conflict") ||
      combined.includes("already has an active") ||
      combined.includes("duplicate role") ||
      combined.includes("role already fulfilled")
    ) {
      conflictKind = "RoleConflict";
    } else if (
      combined.includes("identity conflict") ||
      combined.includes("award mismatch") ||
      combined.includes("award number mismatch")
    ) {
      conflictKind = "AwardIdentityConflict";
    } else if (
      combined.includes("concurrent") ||
      combined.includes("staged") ||
      combined.includes("concurrency")
    ) {
      conflictKind = "StagedOrConcurrentIntakeConflict";
    } else if (
      combined.includes("already linked") ||
      combined.includes("already attached") ||
      combined.includes("linked to another award") ||
      combined.includes("ownership conflict")
    ) {
      conflictKind = "SourceLinkedToAnotherAward";
    } else {
      conflictKind = "GenericConflict";
    }
  }

  // Authoritatively extract Award number from message only if explicitly present
  if (!conflictingAwardNumber) {
    const awardMatch = (detail || rawText).match(/Award\s+([0-9]+[A-Za-z0-9_\-\/]+)/i);
    if (awardMatch) {
      conflictingAwardNumber = awardMatch[1];
    }
  }

  // Authoritatively extract Village from message only if explicitly present
  if (!conflictingVillageName) {
    const villageMatch = (detail || rawText).match(/Village\s+([^,.;\n]+)/i);
    if (villageMatch) {
      conflictingVillageName = villageMatch[1].trim();
    }
  }

  return {
    isConflict,
    conflictKind,
    status: statusCode,
    title,
    detail: detail || title || (statusCode === 409 ? "Conflict" : `Request failed (${statusCode})`),
    raw: rawText,
    conflictingAwardNumber,
    conflictingAwardId,
    conflictingVillageName,
    conflictingRole,
    conflictingDocumentId,
  };
}

// ─── Adapter ──────────────────────────────────────────────────────────────────

class SmartCoreIntakeAdapter {
  private baseUrl: string;

  constructor(baseUrl = "/api") {
    this.baseUrl = baseUrl;
  }

  /**
   * Batch upload: send up to 20 PDFs at once.
   * Uses `files` (plural) multipart field which always returns the batch envelope.
   */
  async uploadBatch(
    villageId: string,
    files: File[]
  ): Promise<
    | { ok: true; result: BatchUploadResult }
    | { ok: false; error: string; parsedProblem?: ParsedProblemDetails }
  > {
    try {
      const form = new FormData();
      for (const f of files) {
        form.append("files", f);
      }
      const res = await fetch(
        `${this.baseUrl}/villages/${villageId}/core-document-intake`,
        { method: "POST", body: form, credentials: "include" }
      );
      if (!res.ok) {
        const text = await res.text();
        const parsed = parseProblemDetails(text, res.status);
        return {
          ok: false,
          error: parsed.detail || `Upload failed (${res.status})`,
          parsedProblem: parsed,
        };
      }
      const data: BatchUploadResult = await res.json();
      // Ensure per-item errors don't leak raw JSON
      if (Array.isArray(data.items)) {
        for (const item of data.items) {
          if (item.error) {
            const itemParsed = parseProblemDetails(item.error, item.errorStatus || 400);
            item.error = itemParsed.detail || item.error;
          }
        }
      }
      return { ok: true, result: data };
    } catch (err: any) {
      return { ok: false, error: err.message || "Network error" };
    }
  }

  /**
   * Confirm: link an intake to an existing Award.
   */
  async confirmExistingAward(
    intakeId: string,
    payload: ConfirmExistingAwardPayload
  ): Promise<
    | { ok: true; intake: IntakeItem }
    | { ok: false; error: string; parsedProblem?: ParsedProblemDetails }
  > {
    return this._confirm(intakeId, payload);
  }

  /**
   * Confirm: create a new Award from this intake.
   * Requires officer to have explicitly reviewed and provided createAward details.
   */
  async confirmNewAward(
    intakeId: string,
    payload: ConfirmNewAwardPayload
  ): Promise<
    | { ok: true; intake: IntakeItem }
    | { ok: false; error: string; parsedProblem?: ParsedProblemDetails }
  > {
    return this._confirm(intakeId, payload);
  }

  private async _confirm(
    intakeId: string,
    payload: ConfirmExistingAwardPayload | ConfirmNewAwardPayload
  ): Promise<
    | { ok: true; intake: IntakeItem }
    | { ok: false; error: string; parsedProblem?: ParsedProblemDetails }
  > {
    try {
      const res = await fetch(
        `${this.baseUrl}/core-document-intakes/${intakeId}/confirm`,
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
          credentials: "include",
        }
      );
      if (!res.ok) {
        const text = await res.text();
        const parsed = parseProblemDetails(text, res.status);
        return {
          ok: false,
          error: parsed.detail || `Confirmation failed (${res.status})`,
          parsedProblem: parsed,
        };
      }
      const intake: IntakeItem = await res.json();
      return { ok: true, intake };
    } catch (err: any) {
      return { ok: false, error: err.message || "Network error" };
    }
  }

  /**
   * Get a single intake by ID.
   */
  async getIntake(intakeId: string): Promise<IntakeItem | null> {
    try {
      const res = await fetch(
        `${this.baseUrl}/core-document-intakes/${intakeId}`,
        { credentials: "include" }
      );
      if (!res.ok) return null;
      return await res.json();
    } catch {
      return null;
    }
  }

  /**
   * List all intakes for a village (newest first).
   */
  async listIntakes(villageId: string, take = 50): Promise<IntakeItem[]> {
    try {
      const res = await fetch(
        `${this.baseUrl}/villages/${villageId}/core-document-intakes?skip=0&take=${take}`,
        { credentials: "include" }
      );
      if (!res.ok) return [];
      const data = await res.json();
      return Array.isArray(data) ? data : [];
    } catch {
      return [];
    }
  }
}

export const smartIntake = new SmartCoreIntakeAdapter();

// ─── Utility helpers ─────────────────────────────────────────────────────────

/**
 * Guess role from common Delhi LAC document naming patterns (client-side hint only).
 * The backend classifier is authoritative; this just pre-selects the dropdown.
 */
export function guessDocumentRole(filename: string): CoreDocumentRole {
  const lower = filename.toLowerCase();
  if (lower.includes("naksha") || lower.includes("muntazmin") || lower.includes("_nm") || lower.match(/\bnm\b/)) {
    return "NM";
  }
  if (lower.includes("statement") || lower.includes("stmt") || lower.includes("stat_a") || lower.includes("stmta")) {
    return "StatementA";
  }
  if (lower.includes("possession") || lower.includes("kabza") || lower.includes("proceeding")) {
    return "PossessionProceeding";
  }
  return "Award";
}

/** Officer-friendly confidence label. */
export function confidenceLabel(confidence: number): {
  text: string;
  level: "high" | "medium" | "low";
} {
  if (confidence >= 0.85) return { text: "High confidence", level: "high" };
  if (confidence >= 0.55) return { text: "Moderate confidence", level: "medium" };
  return { text: "Low confidence — review carefully", level: "low" };
}

/** Friendly display for role values. */
export function roleLabel(role: CoreDocumentRole | string | null): string {
  switch (role) {
    case "Award": return "Award PDF";
    case "NM": return "Naksha Muntazmin (NM)";
    case "StatementA": return "Statement A";
    case "PossessionProceeding": return "Possession Proceeding";
    default: return role || "Unknown";
  }
}

/** Friendly display for matchState values. */
export function matchStateLabel(state: IntakeMatchState): string {
  switch (state) {
    case "MatchedExistingAward": return "Matched to existing Award";
    case "ProposedNewAward": return "New Award proposed";
    case "NeedsOfficerReview": return "Needs officer review";
  }
}
