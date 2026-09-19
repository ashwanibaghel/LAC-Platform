export type OutwardStatus = "Registered" | "Dispatched" | "Cancelled";

export type OutwardEventAction =
  | "Registered"
  | "MetadataUpdated"
  | "MainDocumentChanged"
  | "AttachmentAdded"
  | "AttachmentRemoved"
  | "DakLinkAdded"
  | "DakLinkRemoved"
  | "Dispatched"
  | "Cancelled";

export interface OutwardListItem {
  id: string;
  outwardNumber: string;
  outwardDate: string;
  subject: string;
  recipientName: string;
  recipientDesignation?: string;
  issuingDeskId: string;
  issuingDeskName: string;
  issuingDeskCode: string;
  workstreamId?: string;
  workstreamName?: string;
  status: OutwardStatus;
  revision: number;
  hasDocument: boolean;
  attachmentCount: number;
  dispatchMode?: string;
  dispatchDate?: string;
  dispatchReferenceNumber?: string;
  cancellationReason?: string;
  createdAt: string;
  updatedAt: string;
}

export interface OutwardAttachmentItem {
  id: string;
  documentId: string;
  originalFileName: string;
  title: string;
  attachmentType: string;
  fileSizeBytes: number;
  mimeType: string;
  sequenceOrder: number;
  createdAt: string;
}

export interface OutwardDakLinkItem {
  linkId: string;
  dakId: string;
  diaryNumber: string;
  receivedDate: string;
  subject: string;
  senderName: string;
  relationshipType: string;
  isPrimary: boolean;
  remarks?: string;
  linkedAt: string;
}

export interface OutwardEventItem {
  id: string;
  sequenceNumber: number;
  action: OutwardEventAction;
  actionByUserId: string;
  actionByDisplayName: string;
  actionAt: string;
  remarks?: string;
  documentId?: string;
  documentFileName?: string;
  targetEntityId?: string;
  targetEntitySnapshot?: string;
  dispatchDate?: string;
  dispatchMode?: string;
  dispatchReferenceNumber?: string;
  cancellationReason?: string;
}

export interface OutwardDetail {
  id: string;
  outwardNumber: string;
  outwardDate: string;
  subject: string;
  recipientName: string;
  recipientDesignation?: string;
  recipientDepartment?: string;
  recipientAddress?: string;
  recipientEmail?: string;
  recipientPhone?: string;
  issuingDeskId: string;
  issuingDeskName: string;
  issuingDeskCode: string;
  workstreamId?: string;
  workstreamName?: string;
  officeReferenceNumber?: string;
  remarks?: string;
  status: OutwardStatus;
  revision: number;
  mainDocumentId?: string;
  mainDocumentFileName?: string;
  dispatchDate?: string;
  dispatchMode?: string;
  dispatchReferenceNumber?: string;
  dispatchedByUserId?: string;
  dispatchedByDisplayName?: string;
  dispatchedAt?: string;
  cancellationReason?: string;
  cancelledByUserId?: string;
  cancelledByDisplayName?: string;
  cancelledAt?: string;
  matterId?: string;
  matterNumber?: string;
  matterTitle?: string;
  attachments: OutwardAttachmentItem[];
  dakLinks: OutwardDakLinkItem[];
  events: OutwardEventItem[];
  createdAt: string;
  updatedAt: string;
  createdBy?: string;
  updatedBy?: string;
}

export interface OutwardDeskOption {
  id: string;
  name: string;
  code: string;
  isPrimary: boolean;
}

export interface OutwardWorkstreamOption {
  id: string;
  name: string;
  code: string;
}

export interface OutwardRegistrationContext {
  desks: OutwardDeskOption[];
  workstreams: OutwardWorkstreamOption[];
  dispatchModes: string[];
}

export interface OutwardListResponse {
  items: OutwardListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}
