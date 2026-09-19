export interface DakListItem {
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
  assignedByDisplayName: string;
  assignedAt: string;
  instructions?: string;
  isActive: boolean;
  isDeskActive: boolean;
  isUserEligible: boolean;
  needsAttention: boolean;
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
  entityId: string;
  displayName: string;
  entityType: "Village" | "Award" | "Matter" | "Khasra";
}

export interface DakDetail {
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
