export interface CourtCaseListItemDto {
  id: string;
  caseNumber: string;
  courtName: string;
  caseTitle: string | null;
  caseType: string | null;
  filedDate: string | null;
  currentStatus: string | null;
  disposedDate: string | null;
  responsibleOfficeDeskId: string | null;
  responsibleOfficeDeskName: string | null;
  assignedUserId: string | null;
  assignedUserDisplayName: string | null;
  authoritativeNextDate: string | null;
  activeScheduleNextDate: string | null;
  isProjectedToCalendar: boolean;
  activeScheduledEventId: string | null;
  nextHearingDate: string | null;
  lastHearingDate: string | null;
  awardsCount: number | null;
  khasrasCount: number | null;
  mattersCount: number | null;
  partiesCount: number;
  documentsCount: number;
  proceedingsCount: number;
  lastActivityAt: string | null;
  operationalNdoh: string | null;
  queueState: "Today" | "Upcoming" | "Overdue" | "NoNdoh" | "Attention" | "Disposed";
  daysFromToday: number | null;
  advocates: string[];
  sourceVillage: string | null;
  sourceAwardNumber: string | null;
  operationalNdohSource: string | null;
}

export interface CourtCaseListResponse {
  items: CourtCaseListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CourtFilterOptionDto {
  id: string;
  name: string;
  displayName?: string;
  workstreamName?: string | null;
}

export type CourtFilterDeskOption = CourtFilterOptionDto;
export type CourtFilterUserOption = CourtFilterOptionDto;

export interface CourtFilterOptionsDto {
  courtNames: string[];
  statuses: string[];
  caseTypes?: string[];
  directoryOfficers?: CourtFilterOptionDto[];
  desks: CourtFilterOptionDto[];
  officers?: CourtFilterOptionDto[];
  assignedUsers?: CourtFilterOptionDto[];
  viewDesks?: CourtFilterOptionDto[];
  createDesks?: CourtFilterOptionDto[];
  assignTargetDesks?: CourtFilterOptionDto[];
}

export interface CourtCaseLinkedAwardDto {
  awardId: string;
  awardNumber: string;
  projectName?: string | null;
  awardDate?: string | null;
  villageNames: string[];
}

export interface CourtCaseLinkedKhasraDto {
  khasraId: string;
  villageId: string;
  villageName: string;
  normalizedNumber: string;
  qualifier: string | null;
  recordedArea: number | null;
  areaUnit: string | null;
}

export interface CourtCaseLinkedMatterDto {
  matterId: string;
  title: string;
  referenceNumber: string | null;
  status: string;
  workstreamName: string | null;
  draftsCount: number | null;
  workItemsCount: number | null;
}

export interface CourtCasePartyDto {
  id: string;
  courtCaseId: string;
  partyId?: string | null;
  displayName: string;
  role: string;
  fatherOrSpouseName?: string | null;
  addressText?: string | null;
  remarks?: string | null;
  sequence?: number;
}

export interface CourtCaseRepresentativeDto {
  id: string;
  courtCaseId: string;
  courtCasePartyId?: string | null;
  displayName: string;
  representativeType: string;
  representsRole?: string | null;
  contactText?: string | null;
  remarks?: string | null;
}

export interface CourtCaseCapabilitiesDto {
  canEdit: boolean;
  canAssign: boolean;
  canManageProceedings: boolean;
  canManageDocuments: boolean;
  canPromoteToCalendar: boolean;
  canLinkAward: boolean;
  canLinkKhasra: boolean;
  canLinkMatter: boolean;
}

export interface CourtCaseDetailDto {
  id: string;
  caseNumber: string;
  courtName: string;
  caseTitle: string | null;
  caseType: string | null;
  filedDate: string | null;
  currentStatus: string | null;
  disposedDate: string | null;
  remarks: string | null;
  revision: number;
  responsibleOfficeDeskId: string | null;
  responsibleOfficeDeskName: string | null;
  assignedUserId: string | null;
  assignedUserDisplayName: string | null;
  authoritativeNextDate: string | null;
  activeScheduleNextDate: string | null;
  isProjectedToCalendar: boolean;
  activeScheduledEventId: string | null;
  nextHearingDate: string | null;
  lastHearingDate: string | null;
  lastOrderType: string | null;
  restraintNature: string | null;
  lastSummary: string | null;
  awards: CourtCaseLinkedAwardDto[];
  khasras: CourtCaseLinkedKhasraDto[];
  matters: CourtCaseLinkedMatterDto[];
  parties: CourtCasePartyDto[];
  representatives: CourtCaseRepresentativeDto[];
  awardsCount: number | null;
  khasrasCount: number | null;
  mattersCount: number | null;
  partiesCount: number;
  documentsCount: number;
  proceedingsCount: number;
  eventsCount: number;
  capabilities: CourtCaseCapabilitiesDto;
  operationalNdoh: string | null;
  operationalNdohSource: string | null;
}

export interface DhcObservationDto {
  id: string;
  courtCaseId: string | null;
  identity: string;
  listingDate: string;
  observedAt: string;
  sourceTitle: string;
  sourceUrl: string;
  documentId: string | null;
  pageNumber: number;
  rawMatchedText: string;
  status: string;
  conflictReason: string | null;
  sourceKind: string;
  mode: string;
}

export interface DhcSyncRunDto {
  id: string;
  startedAt: string;
  completedAt: string | null;
  status: string;
  sourceDocumentsDiscovered: number;
  sourceDocumentsProcessed: number;
  observationsCreated: number;
  observationsAccepted: number;
  reviewCount: number;
  failureMessage: string | null;
  mode: string;
  windowStart: string | null;
  windowEnd: string | null;
  eligibleCaseCount: number;
  archivePagesDiscovered: number;
  targetCaseMatches: number;
  casesAdvanced: number;
}

export interface DhcHistoricalStatusDto {
  eligibleCaseCount: number;
  noBaselineCount: number;
  realProceedingExclusionCount: number;
  earliestBaseline: string | null;
  windowEnd: string;
  lastAttempt: DhcSyncRunDto | null;
  completedRun: DhcSyncRunDto | null;
  canStart: boolean;
}

export interface DhcSyncStatusDto {
  lastAttempt: DhcSyncRunDto | null;
  lastSuccess: DhcSyncRunDto | null;
  canSyncNow: boolean;
}

export interface DhcSourceReviewDto {
  id: string;
  sourceTitle: string;
  sourceUrl: string;
  listingDate: string | null;
  kind: string;
  failureMessage: string | null;
}

export interface CourtProceedingDto {
  id: string;
  courtCaseId: string;
  proceedingDate: string | null;
  orderType: string | null;
  restraintNature: string | null;
  summary: string | null;
  nextDate: string | null;
  createdAt: string;
  isAuthoritative?: boolean;
  createdByDisplayName?: string | null;
  sourceKind?: string | null;
}

export interface CourtCaseDocumentDto {
  id: string;
  courtCaseId: string;
  documentId: string;
  originalFileName: string;
  documentType: string;
  documentRole: string | null;
  displayName: string | null;
  courtProceedingId: string | null;
  fileSize?: number | null;
  mimeType?: string | null;
  uploadedAt: string;
}

export interface CourtCaseTimelineEventDto {
  id: string;
  sequenceNumber: number;
  action: string;
  actionAt: string;
  actorUserId: string;
  actorDisplayName: string;
  actorDesignation?: string | null;
  sourceDeskName?: string | null;
  targetDeskName?: string | null;
  sourceUserName?: string | null;
  targetUserName?: string | null;
  oldStatus?: string | null;
  newStatus?: string | null;
  reason?: string | null;
  notes?: string | null;
}

export type CourtCaseEventDto = CourtCaseTimelineEventDto;

export interface CourtCaseLinkedWorkDto {
  matters: CourtCaseLinkedMatterDto[];
  totalDraftsCount: number;
  totalWorkItemsCount: number;
}
