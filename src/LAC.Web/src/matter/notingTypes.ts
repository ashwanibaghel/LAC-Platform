export interface NoteAnchor {
  version: 1;
  start: number;
  end: number;
  quote: string;
  quoteHash: string;
}

export interface PageRegion {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface NoteCitation {
  anchor: NoteAnchor;
  documentId: string;
  documentVersion: number;
  documentHash: string | null;
  page?: number | null;
  region?: PageRegion | null;
}

export interface MatterOfficialNoteDto {
  id: string;
  matterId: string;
  number: number;
  version: number;
  contentJson: string;
  canonicalText: string;
  textHash: string;
  authorUserId: string;
  authorName: string;
  designation: string;
  deskId?: string | null;
  deskName?: string | null;
  submittedAt: string;
  sourceDakId?: string | null;
  citations: NoteCitation[];
  documentManifestJson: string;
}

export interface MatterWorkingDraftDto {
  id?: string;
  matterId?: string;
  authorUserId?: string;
  revision: number;
  contentJson: string | null;
  canonicalText: string | null;
  sourceDakId?: string | null;
  citationsJson?: string;
  updatedAt?: string;
}

export interface MatterNoteRemarkDto {
  id: string;
  matterId: string;
  noteId: string;
  anchorJson: string;
  text: string;
  state: "Open" | "Addressed";
  revision: number;
  authorUserId: string;
  authorName: string;
  designation: string;
  createdAt: string;
  addressedByUserId?: string | null;
  addressedAt?: string | null;
}

export interface MatterPdfAnnotationDto {
  id: string;
  matterId: string;
  documentId: string;
  documentVersion: number;
  documentHash: string | null;
  page: number;
  regionJson: string;
  quote?: string | null;
  text?: string | null;
  authorUserId: string;
  authorName: string;
  createdAt: string;
}

export interface MatterDocumentSummaryDto {
  id: string;
  filename: string;
  mime: string;
  version: number;
  hash: string | null;
  displayName?: string | null;
  documentRole?: string | null;
  contentUrl: string;
}

export interface NotingCapabilities {
  canEditDraft: boolean;
  canSubmit: boolean;
  canRemark: boolean;
  canAnnotate: boolean;
  canSend: boolean;
  nativeRoutingPolicyPending: boolean;
}

export interface MatterNotingWorkspaceData {
  matterId: string;
  revision: number;
  villageId: string;
  sourceDakId: string | null;
  capabilities: NotingCapabilities;
  notes: MatterOfficialNoteDto[];
  documents: MatterDocumentSummaryDto[];
}

export interface MatterOptionItem {
  id: string;
  title: string;
  revision: number;
}

export interface MatterOptionsResponse {
  subject: string;
  villageId: string;
  dakRevision: number;
  matters: MatterOptionItem[];
}

export interface SendWorkingNoteCommand {
  sourceDakId: string;
  expectedDakRevision: number;
  toDeskId: string;
  toUserId: string;
  action: "Forwarded" | "Returned";
  includesPhysicalOriginal: boolean;
  instructions?: string;
  remarks?: string;
}
