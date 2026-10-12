import type {
  MatterNotingWorkspaceData,
  MatterWorkingDraftDto,
  OfficeSessionInfo,
  OfficeSessionConfigResponse,
  OfficeSaveReceiptDto,
  MatterOfficePreviewDto,
  GreenSheetMetadataDto,
  SendWorkingNoteCommand
} from "./notingTypes";

export function generateUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

async function handleResponse<T>(res: Response, defaultMessage: string): Promise<T> {
  if (!res.ok) {
    let errorDetail = defaultMessage;
    try {
      const body = await res.json();
      errorDetail = body.message || body.detail || body.title || defaultMessage;
    } catch {
      // ignore
    }
    const err: any = new Error(errorDetail);
    err.status = res.status;
    throw err;
  }
  if (res.status === 204) {
    return null as any;
  }
  return res.json();
}

export const notingOfficeApi = {
  async getNotingWorkspace(matterId: string): Promise<MatterNotingWorkspaceData> {
    const res = await fetch(`/api/matters/${matterId}/noting`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    });
    return handleResponse<MatterNotingWorkspaceData>(res, "Failed to load noting workspace");
  },

  async getWorkingDraft(matterId: string): Promise<{ draft: MatterWorkingDraftDto; etagRevision: number }> {
    const res = await fetch(`/api/matters/${matterId}/noting/draft`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    });
    if (!res.ok) {
      if (res.status === 404) {
        return { draft: { revision: 0, contentJson: null, canonicalText: null }, etagRevision: 0 };
      }
      return handleResponse(res, "Failed to load working draft");
    }
    const draft: MatterWorkingDraftDto = await res.json();
    const etag = res.headers.get("ETag");
    const etagRevision = etag ? parseInt(etag.replace(/"/g, ""), 10) : draft.revision;
    return { draft, etagRevision: isNaN(etagRevision) ? draft.revision : etagRevision };
  },

  async createOfficeSession(
    matterId: string,
    sourceDakId: string | null,
    resumeSnapshotId: string | null,
    workingRevision: number
  ): Promise<OfficeSessionInfo> {
    const res = await fetch(`/api/matters/${matterId}/noting/office/sessions`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${workingRevision}"`
      },
      body: JSON.stringify({
        sourceDakId: sourceDakId || null,
        resumeSnapshotId: resumeSnapshotId || null
      })
    });
    return handleResponse<OfficeSessionInfo>(res, "Failed to create ONLYOFFICE noting session");
  },

  async getSessionConfig(sessionId: string): Promise<OfficeSessionConfigResponse> {
    const res = await fetch(`/api/noting-office/sessions/${sessionId}/config`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    });
    return handleResponse<OfficeSessionConfigResponse>(res, "Failed to load ONLYOFFICE configuration");
  },

  async requestSave(
    sessionId: string,
    clientGeneration: number,
    sessionRevision: number
  ): Promise<OfficeSaveReceiptDto> {
    const res = await fetch(`/api/noting-office/sessions/${sessionId}/saves`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${sessionRevision}"`
      },
      body: JSON.stringify({ clientGeneration })
    });
    return handleResponse<OfficeSaveReceiptDto>(res, "Failed to request ONLYOFFICE save");
  },

  async getSaveStatus(sessionId: string, saveId: string): Promise<OfficeSaveReceiptDto> {
    const res = await fetch(`/api/noting-office/sessions/${sessionId}/saves/${saveId}`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    });
    return handleResponse<OfficeSaveReceiptDto>(res, "Failed to check save receipt status");
  },

  async createPreview(
    sessionId: string,
    saveRequestId: string,
    sessionRevision: number
  ): Promise<MatterOfficePreviewDto> {
    const res = await fetch(`/api/noting-office/sessions/${sessionId}/previews`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${sessionRevision}"`
      },
      body: JSON.stringify({ saveRequestId })
    });
    return handleResponse<MatterOfficePreviewDto>(res, "Failed to generate canonical preview");
  },

  async confirmPreview(
    matterId: string,
    previewId: string,
    canonicalHash: string,
    resetCitations: boolean,
    workingRevision: number
  ): Promise<MatterWorkingDraftDto> {
    const res = await fetch(`/api/matters/${matterId}/noting/office/confirm`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${workingRevision}"`
      },
      body: JSON.stringify({
        previewId,
        canonicalHash,
        resetCitations
      })
    });
    return handleResponse<MatterWorkingDraftDto>(res, "Failed to confirm canonical preview");
  },

  async revokeSession(sessionId: string, sessionRevision: number): Promise<void> {
    const res = await fetch(`/api/noting-office/sessions/${sessionId}/revoke`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${sessionRevision}"`
      }
    });
    await handleResponse<any>(res, "Failed to revoke ONLYOFFICE session");
  },

  async createGreenSheet(
    matterId: string,
    previewId: string | null = null
  ): Promise<GreenSheetMetadataDto> {
    const res = await fetch(`/api/matters/${matterId}/noting/green-sheets`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid()
      },
      body: JSON.stringify({ previewId: previewId || null })
    });
    return handleResponse<GreenSheetMetadataDto>(res, "Failed to generate Green Sheet PDF");
  },

  async getGreenSheet(sheetId: string): Promise<GreenSheetMetadataDto> {
    const res = await fetch(`/api/noting-green-sheets/${sheetId}`, {
      credentials: "include",
      headers: { "Cache-Control": "no-store" }
    });
    return handleResponse<GreenSheetMetadataDto>(res, "Failed to load Green Sheet metadata");
  },

  async submitOfficeNote(
    matterId: string,
    sourceDakId: string | null,
    officePreviewId: string,
    canonicalHash: string,
    workingRevision: number
  ): Promise<{ noteNumber: number; officialNoteId: string; newRevision: number }> {
    const res = await fetch(`/api/matters/${matterId}/noting/submit`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${workingRevision}"`
      },
      body: JSON.stringify({
        sourceDakId: sourceDakId || null,
        officePreviewId,
        canonicalHash
      })
    });
    return handleResponse<any>(res, "Failed to submit official note");
  },

  async sendOfficeNote(
    matterId: string,
    command: SendWorkingNoteCommand,
    workingRevision: number
  ): Promise<{ success: boolean; noteNumber: number; transactionId?: string }> {
    const res = await fetch(`/api/matters/${matterId}/noting/send`, {
      method: "POST",
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        "Idempotency-Key": generateUuid(),
        "If-Match": `"${workingRevision}"`
      },
      body: JSON.stringify(command)
    });
    return handleResponse<any>(res, "Failed to send matter note and dispatch Dak");
  }
};
