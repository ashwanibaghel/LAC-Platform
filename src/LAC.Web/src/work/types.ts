export type WorkItemPriority = 'Routine' | 'Urgent' | 'Immediate';

export type WorkItemStatus =
  | 'Assigned'
  | 'InProgress'
  | 'SubmittedForReview'
  | 'ReturnedForCorrection'
  | 'Completed'
  | 'Cancelled';

export type WorkItemDueFilter = 'all' | 'overdue' | 'today' | 'week' | 'upcoming' | 'none';
export type WorkItemRelationshipFilter = 'all' | 'assigned' | 'requested' | 'review' | 'contributing' | 'waiting';

export type WorkItemContributorStatus = 'Active' | 'Submitted' | 'Returned' | 'Accepted' | 'Removed';

export interface WorkItemContributorDetail {
  contributorId: string;
  userId: string;
  displayName: string;
  designation?: string | null;
  instructions?: string | null;
  status: WorkItemContributorStatus;
  isActive: boolean;
  addedByUserId: string;
  addedByDisplayName: string;
  addedAt: string;
  submittedAt?: string | null;
  reviewedAt?: string | null;
  reviewedByUserId?: string | null;
  reviewedByDisplayName?: string | null;
}

export interface WorkItemCapabilities {
  canReassign: boolean;
  canAddContributor: boolean;
  canRemoveContributor: boolean;
  canContribute: boolean;
  canSubmitContribution: boolean;
  canReviewContributions: boolean;
  canAddUpdate: boolean;
  canUploadAttachment: boolean;
  canStartWork: boolean;
}

export interface ContributorOption {
  userId: string;
  displayName: string;
  designation?: string | null;
  desks: string[];
}

export interface WorkItemSummary {
  totalOpen: number;
  assignedToMe: number;
  requestedByMe: number;
  overdue: number;
  dueToday: number;
  dueThisWeek: number;
  needsReview: number;
  returnedToMe: number;
  helping: number;
  waitingOnOthers: number;
}

export interface MyWorkItem {
  id: string;
  title: string;
  priority: WorkItemPriority;
  status: WorkItemStatus;
  dueAt: string | null;
  dueState: 'overdue' | 'today' | 'upcoming' | 'none';
  workstreamId: string;
  workstreamName: string;
  requestedByUserId: string;
  requestedByDisplayName: string;
  requestedByDesignation?: string | null;
  officeDeskId: string;
  officeDeskName: string;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  assignedUserDesignation?: string | null;
  revision: number;
  lastActivityAt: string;
  createdAt: string;
  hasLinkedMatter: boolean;
  linkedMatterTitle?: string | null;
  hasLinkedDak: boolean;
  linkedDakSubject?: string | null;
}

export interface MyWorkResponse {
  summary: WorkItemSummary;
  items: MyWorkItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface WorkItemAssignmentDetail {
  id: string;
  officeDeskId: string;
  officeDeskCode: string;
  officeDeskName: string;
  assignedUserId?: string | null;
  assignedUserDisplayName?: string | null;
  assignedUserDesignation?: string | null;
  assignedByUserId: string;
  assignedByDisplayName: string;
  assignedAt: string;
  firstSeenAt?: string | null;
  firstActionAt?: string | null;
  isActive: boolean;
}

export interface WorkItemUpdateDetail {
  id: string;
  message: string;
  addedByUserId: string;
  addedByDisplayName: string;
  addedByDesignation?: string | null;
  addedAt: string;
}

export interface WorkItemAttachmentDetail {
  id: string;
  documentId: string;
  fileName: string;
  title?: string | null;
  attachmentType?: string | null;
  contentType: string;
  fileSizeBytes: number;
  createdAt: string;
}

export interface WorkItemMatterLinkDetail {
  matterId: string;
  isAuthorized: boolean;
  title?: string | null;
  referenceNumber?: string | null;
  status?: string | null;
  matterType?: string | null;
}

export interface WorkItemDakLinkDetail {
  dakId: string;
  isAuthorized: boolean;
  diaryNumber?: string | null;
  subject?: string | null;
  status?: string | null;
  priority?: string | null;
}

export interface WorkItemDetail {
  id: string;
  title: string;
  instructions?: string | null;
  priority: WorkItemPriority;
  status: WorkItemStatus;
  origin: string;
  dueAt?: string | null;
  revision: number;
  workstreamId: string;
  workstreamName: string;
  requestedByUserId: string;
  requestedByDisplayName: string;
  requestedByDesignation?: string | null;
  lastActivityAt: string;
  completedAt?: string | null;
  createdAt: string;
  currentAssignment?: WorkItemAssignmentDetail | null;
  contributors: WorkItemContributorDetail[];
  capabilities: WorkItemCapabilities;
  updates: WorkItemUpdateDetail[];
  attachments: WorkItemAttachmentDetail[];
  matterLinks: WorkItemMatterLinkDetail[];
  dakLinks: WorkItemDakLinkDetail[];
}

export interface WorkItemEvent {
  id: string;
  sequenceNumber: number;
  action: string;
  actionText: string;
  actionByUserId: string;
  actionByDisplayName: string;
  actionByDesignation?: string | null;
  actionAt: string;
  fromStatus?: string | null;
  toStatus?: string | null;
  remarks?: string | null;
}

export interface WorkstreamOption {
  id: string;
  code: string;
  name: string;
  description?: string | null;
}

export interface DeskMemberOption {
  userId: string;
  username: string;
  displayName: string;
  designation?: string | null;
  isPrimaryDesk: boolean;
}

export interface DeskOption {
  id: string;
  code: string;
  name: string;
  workstreamId?: string | null;
  members: DeskMemberOption[];
}

export interface AssignmentOptionsResponse {
  desks: DeskOption[];
}

export interface CreateContextResponse {
  workstreams: WorkstreamOption[];
}
