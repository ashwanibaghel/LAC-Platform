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

// ONLYOFFICE Noting API contracts

export type OfficeCompositionVersion = "OfficeNoteV1" | "ContinuousGreenEnvelopeV2";

export interface OfficeSessionInfo {
  id: string;
  matterId: string;
  actorUserId: string;
  sourceDakId: string | null;
  workingRevision: number;
  revision: number;
  state: "Active" | "Frozen" | "Reviewed" | "Submitted" | "Revoked";
  expiresAt: string;
  latestSnapshotId: string | null;
  compositionVersion?: OfficeCompositionVersion;
}

export interface OfficeSessionConfigResponse {
  session: OfficeSessionInfo;
  scriptUrl: string;
  config: any;
}

export interface OfficeSaveReceiptDto {
  id: string;
  sessionId: string;
  clientGeneration: number;
  sessionRevision: number;
  state: "Pending" | "Committed" | "Failed";
  deadline: string;
  snapshotId: string | null;
  sha256: string | null;
  version: number | null;
  failureCode: string | null;
}

export interface OfficeSaveResolutionDto {
  noChanges: boolean;
  snapshotId: string;
  sha256: string;
  snapshotVersion: number;
  sessionRevision: number;
  committedReceipt: OfficeSaveReceiptDto | null;
}

export interface MatterOfficePreviewDto {
  id: string;
  matterId: string;
  sessionId: string;
  actorUserId: string;
  snapshotId: string;
  sourceDakId: string | null;
  workingRevision: number;
  converterVersion: string;
  contentJson: string;
  canonicalText: string;
  canonicalHash: string;
  createdAt: string;
  nativeCaptureJson?: string | null;
}

export interface ScreenOnlyGuideDto {
  page: number;
  widthMm: number;
  heightMm: number;
  xMm: number;
  color: string;
  print: boolean;
}

export interface GreenSheetMetadataDto {
  id: string;
  matterId: string;
  policy: string;
  manifestJson: string;
  pdfSha256: string;
  pageCount: number;
  rendererVersion: string;
  fontManifestSha256: string;
  createdAt: string;
  pdfUrl: string;
  actualSizePrintingRequired: boolean;
  screenOnlyGuides: ScreenOnlyGuideDto[];
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
  officePreviewId?: string;
  canonicalHash?: string;
}
