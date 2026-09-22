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
  // Backward compatibility helpers
  nextDate?: string | null;
  linkedAwardCount?: number | null;
  linkedMatterCount?: number | null;
  linkedDocumentCount?: number;
  hasUpcomingHearing?: boolean;
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
  desks: CourtFilterOptionDto[];
  officers?: CourtFilterOptionDto[];
  assignedUsers?: CourtFilterOptionDto[];
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
  draftsCount: number;
  workItemsCount: number;
  // Compatibility
  draftCount?: number;
  workItemCount?: number;
  matterType?: string;
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
  // Compatibility
  partyName?: string;
  partyType?: string;
  advocateName?: string | null;
  contactDetails?: string | null;
  isPrimary?: boolean;
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
  // Compatibility
  name?: string;
  designation?: string | null;
  barRegistrationNumber?: string | null;
  contactDetails?: string | null;
  isLeadCounsel?: boolean;
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
  // Compatibility
  latestProceedingSummary?: string | null;
  proceedingCount?: number;
  documentCount?: number;
  eventCount?: number;
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
  isAuthoritativeNdoh?: boolean;
  createdByDisplayName?: string | null;
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
  fileSizeBytes?: number | null;
  mimeType?: string | null;
  uploadedAt: string;
  uploadedByDisplayName?: string | null;
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
  // Compatibility
  eventType?: string;
  description?: string;
  payloadJson?: string | null;
  actorDisplayNameSnapshot?: string | null;
  createdAt?: string;
}

export type CourtCaseEventDto = CourtCaseTimelineEventDto;

export interface CourtCaseLinkedWorkDto {
  matters: CourtCaseLinkedMatterDto[];
  totalDraftCount: number;
  totalWorkItemCount: number;
}
