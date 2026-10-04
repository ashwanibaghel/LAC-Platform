/**
 * Smart Core Document Intake Typed Adapter Contract.
 * 
 * Prepares the frontend for the upcoming Codex Smart Core Document Intake service
 * while safely routing to the current production API endpoints.
 */

export type CoreDocumentRole =
  | "Award"
  | "NM"
  | "StatementA"
  | "PossessionProceeding";

export interface IntakeFileItem {
  file: File;
  role: CoreDocumentRole;
  targetAwardId?: string;
  error?: string;
}

export interface SmartCoreIntakeDetectedAward {
  awardNumber: string;
  awardDate?: string;
  role: CoreDocumentRole;
  confidence: number;
  fileName: string;
}

export interface SmartCoreIntakeResult {
  sessionId?: string;
  detectedAwards: SmartCoreIntakeDetectedAward[];
  status: "Success" | "Partial" | "Failed";
  message: string;
}

export interface ISmartCoreIntakeAdapter {
  isSmartIntakeAvailable(): Promise<boolean>;
  uploadSingleDocument(
    awardId: string,
    role: CoreDocumentRole,
    file: File
  ): Promise<{ ok: boolean; documentId?: string; error?: string }>;
}

export class ProductionCoreIntakeAdapter implements ISmartCoreIntakeAdapter {
  private baseUrl: string;

  constructor(baseUrl: string = "/api") {
    this.baseUrl = baseUrl;
  }

  /**
   * Health probe for the upcoming Codex Smart Core Document Intake API.
   * Returns false gracefully if the endpoint is not yet mounted.
   */
  async isSmartIntakeAvailable(): Promise<boolean> {
    try {
      const res = await fetch(`${this.baseUrl}/core-intake/status`, {
        method: "GET",
        credentials: "include",
      });
      return res.ok;
    } catch {
      return false;
    }
  }

  /**
   * Reliable upload using the live authoritative LAC endpoint:
   * POST /api/awards/{awardId}/core-documents?role={role}
   */
  async uploadSingleDocument(
    awardId: string,
    role: CoreDocumentRole,
    file: File
  ): Promise<{ ok: boolean; documentId?: string; error?: string }> {
    try {
      const form = new FormData();
      form.append("file", file);
      const res = await fetch(
        `${this.baseUrl}/awards/${awardId}/core-documents?role=${encodeURIComponent(
          role
        )}`,
        {
          method: "POST",
          body: form,
          credentials: "include",
        }
      );

      if (!res.ok) {
        const text = await res.text();
        return { ok: false, error: text || `Upload failed (${res.status})` };
      }

      const data = await res.json().catch(() => null);
      return { ok: true, documentId: data?.documentId };
    } catch (err: any) {
      return { ok: false, error: err.message || "Network error during upload" };
    }
  }
}

export const coreIntakeAdapter = new ProductionCoreIntakeAdapter();

/**
 * Utility to guess role from common Delhi LAC document naming patterns.
 */
export function guessDocumentRole(filename: string): CoreDocumentRole {
  const lower = filename.toLowerCase();
  if (lower.includes("naksha") || lower.includes("muntazmin") || lower.includes("nm")) {
    return "NM";
  }
  if (lower.includes("statement") || lower.includes("stmt") || lower.includes("stat_a")) {
    return "StatementA";
  }
  if (
    lower.includes("possession") ||
    lower.includes("kabza") ||
    lower.includes("proceeding")
  ) {
    return "PossessionProceeding";
  }
  return "Award";
}
