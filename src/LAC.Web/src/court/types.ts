export interface CourtCaseListItemDto {
  id: string;
  caseNumber: string;
  courtName: string;
  caseTitle: string | null;
  caseType: string | null;
  currentStatus: string | null;
  filedDate: string | null;
  nextDate: string | null;
  responsibleOfficeDeskId: string | null;
  responsibleOfficeDeskName: string | null;
  assignedUserId: string | null;
  assignedUserDisplayName: string | null;
  linkedAwardCount: number;
  linkedMatterCount: number;
  linkedDocumentCount: number;
  revision: number;
  hasUpcomingHearing: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CourtCaseListResponse {
  items: CourtCaseListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface CourtFilterDeskOption {
  id: string;
  name: string;
  workstreamName: string;
}

export interface CourtFilterUserOption {
  id: string;
  displayName: string;
}

export interface CourtFilterOptionsDto {
  courtNames: string[];
  statuses: string[];
  desks: CourtFilterDeskOption[];
  assignedUsers: CourtFilterUserOption[];
}

export interface CourtCaseLinkedAwardDto {
  awardId: string;
  awardNumber: string;
  awardDate: string | null;
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
  matterType: string;
  status: string;
  workstreamName: string;
  draftCount: number;
  workItemCount: number;
}

export interface CourtCasePartyDto {
  id: string;
  partyType: string;
  partyName: string;
  advocateName: string | null;
  contactDetails: string | null;
  isPrimary: boolean;
}

export interface CourtCaseRepresentativeDto {
  id: string;
  representativeType: string;
  name: string;
  designation: string | null;
  barRegistrationNumber: string | null;
  contactDetails: string | null;
  isLeadCounsel: boolean;
}

export interface CourtCaseDetailDto {
  id: string;
  caseNumber: string;
  courtName: string;
  caseTitle: string | null;
  caseType: string | null;
  currentStatus: string | null;
  filedDate: string | null;
  disposedDate: string | null;
  remarks: string | null;
  revision: number;
  responsibleOfficeDeskId: string | null;
  responsibleOfficeDeskName: string | null;
  assignedUserId: string | null;
  assignedUserDisplayName: string | null;
  nextHearingDate: string | null;
  activeScheduledEventId: string | null;
  latestProceedingSummary: string | null;
  proceedingCount: number;
  documentCount: number;
  eventCount: number;
  awards: CourtCaseLinkedAwardDto[];
  khasras: CourtCaseLinkedKhasraDto[];
  matters: CourtCaseLinkedMatterDto[];
  parties: CourtCasePartyDto[];
  representatives: CourtCaseRepresentativeDto[];
}

export interface CourtProceedingDto {
  id: string;
  courtCaseId: string;
  proceedingDate: string | null;
  orderType: string | null;
  restraintNature: string | null;
  summary: string | null;
  nextDate: string | null;
  isAuthoritativeNdoh: boolean;
  createdByDisplayName: string | null;
  createdAt: string;
}

export interface CourtCaseDocumentDto {
  id: string;
  documentId: string;
  documentRole: string | null;
  displayName: string | null;
  originalFileName: string;
  documentType: string;
  fileSizeBytes: number;
  uploadedAt: string;
  uploadedByDisplayName: string | null;
  courtProceedingId: string | null;
}

export interface CourtCaseEventDto {
  id: string;
  sequenceNumber: number;
  eventType: string;
  description: string;
  payloadJson: string | null;
  actorUserId: string | null;
  actorDisplayNameSnapshot: string | null;
  createdAt: string;
}

export interface CourtCaseLinkedWorkDto {
  matters: CourtCaseLinkedMatterDto[];
  totalDraftCount: number;
  totalWorkItemCount: number;
}
