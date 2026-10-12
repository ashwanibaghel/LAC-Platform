export type PdfOwnershipLink = {
  awardId: string; awardNumber: string; awardRevision: number;
  attachmentId: string; attachmentRevision: number; villageId?: string;
  villages: { villageId: string; name: string }[]; canCorrect: boolean;
  documentId: string; documentReference: string;
};
export type PdfOwnershipConflict = {
  documentId?: string; documentReference?: string; contentPath?: string;
  sha256Hash: string; selectedAwardId: string;
  existingLinks: PdfOwnershipLink[]; hasRestrictedLinks: boolean;
};
export class LandRecordApiError extends Error {
  code?: string;
  conflicts?: PdfOwnershipConflict;
  constructor(message: string, code?: string, conflicts?: PdfOwnershipConflict) { super(message); this.code = code; this.conflicts = conflicts; }
}
export async function landRecordResponse<T>(response: Response): Promise<T> {
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new LandRecordApiError(body?.message || body?.detail || `Could not save the land record (${response.status}).`, body?.code, body?.conflicts);
  return body as T;
}
