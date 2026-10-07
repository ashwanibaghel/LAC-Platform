export interface DakListItem {
  recordStatus: string;
  id: string;
  diaryNumber: string;
  receivedDate: string;
  subject: string;
  senderName: string;
  senderDepartment?: string;
  inwardMode: string;
  priority: "Routine" | "Urgent" | "Immediate";
  dueDate?: string;
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled" | "Resolved";
  categoryName?: string;
  workstreamName?: string;
  assignedDeskName?: string;
  assignedUserDisplayName?: string;
  hasDocument: boolean;
  revision: number;
  createdAt: string;
}

export interface DakAssignment {
  id: string;
  officeDeskId: string;
  deskCode: string;
  deskName: string;
  assignedUserId?: string;
  assignedUserDisplayName?: string;
  assignedByUserId?: string;
  assignedByDisplayName: string;
  assignedAt: string;
  instructions?: string;
  isActive: boolean;
  isDeskActive: boolean;
  isUserEligible: boolean;
  needsAttention: boolean;
  receivedAt?: string | null;
  isConfirmed?: boolean;
}

export interface DakAttachment {
  id: string;
  documentId: string;
  originalFileName: string;
  title: string;
  attachmentType: string;
  sequenceOrder: number;
  createdAt: string;
}

export interface DakLinkItem {
  linkId: string;
  entityId: string | null;
  canOpen: boolean;
  displayName: string;
  entityType: "Village" | "Award" | "Matter" | "Khasra";
}

export interface DakDetail {
  recordStatus: string;
  id: string;
  diaryNumber: string;
  receivedDate: string;
  subject: string;
  senderName: string;
  senderDesignation?: string;
  senderDepartment?: string;
  senderAddress?: string;
  senderReferenceNumber?: string;
  senderLetterDate?: string;
  inwardMode: string;
  priority: "Routine" | "Urgent" | "Immediate";
  dueDate?: string;
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled" | "Resolved";
  categoryId?: string;
  categoryName?: string;
  workstreamId?: string;
  workstreamName?: string;
  revision: number;
  mainDocumentId?: string;
  mainDocumentFileName?: string;
  currentAssignment?: DakAssignment | null;
  attachments: DakAttachment[];
  villageLinks: DakLinkItem[];
  awardLinks: DakLinkItem[];
  matterLinks: DakLinkItem[];
  khasraLinks: DakLinkItem[];
  createdAt: string;
  createdBy?: string;
  updatedAt: string;
  updatedBy?: string;
  routingState: "Unassigned" | "WithHolder" | "InTransit" | "LegacyUnconfirmed";
  physicalState: "Unknown" | "NotPresent" | "AtRecordedLocation" | "Held" | "InTransit" | "ReturnPending";
  processingCycle: number;
  pendingTransferId?: string | null;
  pendingReceiverUserId?: string | null;
  needsAttention: boolean;
  resolvedAt?: string | null;
  resolvedByUserId?: string | null;
  resolutionRemarks?: string | null;
}

export interface DakTransferItem {
  id: string;
  senderUserId: string;
  fromHolderUserId?: string | null;
  fromDeskId?: string | null;
  toDeskId: string;
  toUserId: string;
  destinationKind: "Officer" | "RecordRoom" | string;
  purpose: string;
  state: "Pending" | "Received" | "PulledBack";
  includesPhysicalOriginal: boolean;
  sentAt: string;
  receivedAt?: string | null;
  physicalReceivedAt?: string | null;
  pulledBackAt?: string | null;
  pullBackReason?: string | null;
  physicalReturnedAt?: string | null;
  physicalReturnProvenance?: string | null;
  remarks?: string | null;
  instructions?: string | null;
}

export type DakMovementActionType =
  | "Registered"
  | "Marked"
  | "Forwarded"
  | "Returned"
  | "Disposed"
  | "Cancelled"
  | "Received"
  | "PulledBack"
  | "PhysicalReturnConfirmed"
  | "Resolved"
  | "Reopened"
  | "CustodyConfirmed";

export interface DakMovementStateChanges {
  before?: Record<string, unknown>;
  after?: Record<string, unknown>;
  completionAttested?: boolean | null;
}

export interface DakMovement {
  id: string;
  sequenceNumber: number;
  action: DakMovementActionType;
  fromDeskId?: string;
  fromDeskCode?: string;
  fromDeskName?: string;
  fromUserId?: string;
  fromUserDisplayName?: string;
  toDeskId?: string;
  toDeskCode?: string;
  toDeskName?: string;
  toUserId?: string;
  toUserDisplayName?: string;
  actionByUserId: string;
  actionByDisplayName: string;
  actionAt: string;
  remarks?: string;
  instructions?: string;
  transferId?: string | null;
  eventVersion?: number;
  stateChanges?: DakMovementStateChanges | null;
}

export interface DakCategory {
  id: string;
  code: string;
  name: string;
  description?: string;
  defaultPriority: "Routine" | "Urgent" | "Immediate";
  defaultWorkstreamId?: string;
  defaultWorkstreamName?: string;
  isActive: boolean;
}

export interface MyDeskSummary {
  total: number;
  immediate: number;
  urgent: number;
  overdue: number;
  dueToday: number;
  assignedToMe: number;
  unallocated: number;
}

export interface MyDeskDesk {
  id: string;
  code: string;
  name: string;
  isPrimary: boolean;
}

export interface MyDeskAssignment {
  deskId: string;
  deskCode: string;
  deskName: string;
  assignedUserId?: string;
  assignedUserDisplayName?: string;
  assignedAt: string;
  handlerState: "Unallocated" | "AssignedToMe" | "AssignedToOther";
}

export interface MyDeskItem {
  id: string;
  diaryNumber: string;
  receivedDate: string;
  subject: string;
  senderName: string;
  senderDepartment?: string;
  priority: "Routine" | "Urgent" | "Immediate";
  dueDate?: string;
  workstreamName?: string;
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled" | "Resolved";
  revision: number;
  assignment: MyDeskAssignment;
}

export interface MyDeskResponse {
  summary: MyDeskSummary;
  desks: MyDeskDesk[];
  items: MyDeskItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface PhysicalOriginalInfo {
  hasPhysicalOriginal: boolean | null;
  deskId: string | null;
  userId: string | null;
  locationNote: string | null;
  provenanceNote: string | null;
  updatedAt: string | null;
  updatedByUserId: string | null;
  revision: number;
}

export interface PhysicalOriginalUpdateRequest {
  hasPhysicalOriginal: boolean | null;
  deskId?: string | null;
  userId?: string | null;
  locationNote?: string | null;
  provenanceNote: string;
  expectedRevision: number;
}

export interface DakDeliveryQueueItem {
  id: string;
  diaryNumber: string;
  status: string;
  routingState: string;
  physicalState: string;
  revision: number;
}

export interface DakDeliveryQueueResponse {
  items: DakDeliveryQueueItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}
