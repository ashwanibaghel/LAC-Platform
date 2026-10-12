export type RecordStatus = "Active" | "Inactive" | "Archived";
export type CoreRole = "Award" | "PossessionProceeding" | "StatementA" | "NM" | "TemporaryOffer" | "TemporaryProceedings";
export type CoreAttachment = {
  attachmentId: string; documentId: string; role: CoreRole; originalFileName: string;
  mimeType?: string | null; uploadedAt: string; sourceHash?: string | null;
  villageId?: string | null; legalDate?: string | null; referenceNumber?: string | null;
  partNumber?: string | null; title?: string | null; remarks?: string | null;
  revision: number; isDetached: boolean; isSharedSource: boolean;
  possessionEventId?: string | null; needsReview: boolean;
};
export type StaffNotification = {
  id: string; sectionType: string; notificationNumber: string; notificationDate?: string | null;
  isFinalized: boolean; numberAbsentReason?: string | null; revision: number; needsReview: boolean;
  documents: { documentId: string; originalFileName: string; sha256Hash?: string | null }[];
};
export type StaffAward = {
  id: string; awardNumber: string; awardDate?: string | null; awardType?: string | null;
  actRegime?: string | null; purpose?: string | null; acquisitionProjectId?: string | null;
  remarks?: string | null; status: string; revision: number; recordStatus: RecordStatus;
  archiveKind?: "Archive" | "Void" | null; metadataProvenance: "Manual" | "SourceReviewed";
  villageIds: string[]; villages: { villageId: string; name: string }[]; attachments: CoreAttachment[]; notifications: StaffNotification[];
  sourceFacts: { factName: string; verifiedBy: string; verifiedAt: string; documentId: string; pageNumber: number; confirmedValueJson: string }[];
};
export type AwardMetadataInput = {
  mutationId: string; expectedRevision: number; awardNumber: string; awardDate?: string | null;
  awardType?: string | null; actRegime?: string | null; purpose?: string | null;
  acquisitionProjectId?: string | null; remarks?: string | null; reason: string;
};
export type AwardLifecycleInput = { mutationId: string; expectedRevision: number; action: "Archive" | "Void" | "Restore"; reason: string };
export type AwardMutationResult = { awardId: string; revision: number; recordStatus: RecordStatus; archiveKind?: string | null };
export type AwardPdfLinkResolutionInput = {
  mutationId: string; action: "RelinkIncorrectAttachment" | "VoidDuplicateAndRelink" | "ShareSource";
  sourceAwardId: string; targetAwardId: string; villageId: string;
  expectedSourceAwardRevision: number; expectedTargetAwardRevision: number; expectedSourceAttachmentRevision: number;
  expectedSha256Hash: string; officerConfirmed: boolean; reason: string;
  sourcePages?: number[] | null; coverageDescription?: string | null;
};
export type AwardPdfLinkResolutionResult = {
  documentId: string; awardId: string; attachmentId: string; attachmentRevision: number;
  resolution: AwardPdfLinkResolutionInput["action"]; sourcePath: string;
};
