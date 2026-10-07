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
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled";
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
  isReceived?: boolean;
  receivedAt?: string | null;
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
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled";
  categoryId?: string;
  categoryName?: string;
  workstreamId?: string;
  workstreamName?: string;
  revision: number;
  mainDocumentId?: string;
  mainDocumentFileName?: string;
  currentAssignment?: DakAssignment;
  routingState?: "Unassigned" | "WithHolder" | "InTransit" | "LegacyUnconfirmed";
  physicalState?: "Unknown" | "NotPresent" | "AtRecordedLocation" | "Held" | "InTransit" | "ReturnPending";
  processingCycle?: number;
  pendingTransfer?: PendingTransfer | null;
  resolution?: {
    resolvedAt: string;
    resolvedByUserId: string;
    completionAttested: boolean;
    remarks: string;
  } | null;
  attachments: DakAttachment[];
  villageLinks: DakLinkItem[];
  awardLinks: DakLinkItem[];
  matterLinks: DakLinkItem[];
  khasraLinks: DakLinkItem[];
  createdAt: string;
  createdBy?: string;
  updatedAt: string;
  updatedBy?: string;
}

export interface PendingTransfer {
  id: string;
  senderUserId: string;
  fromHolderUserId?: string | null;
  fromDeskId?: string | null;
  toDeskId: string;
  toUserId: string;
  toUserDisplayName?: string;
  destinationKind: "Officer" | "RecordRoom";
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

export interface DakMovement {
  id: string;
  sequenceNumber: number;
  action: "Registered" | "Marked" | "Forwarded" | "Returned" | "Disposed" | "Cancelled";
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
  status: "Registered" | "InProcess" | "Disposed" | "Cancelled";
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
