// LAC Platform Office Account & Authority Model V3 Frontend Types
// Matches frozen backend contract at docs/rbac-office-access-v3-contract.md

export type OfficeAuthority =
  | "HELPER"
  | "STANDARD_OFFICER"
  | "OFFICE_SUPERVISOR"
  | "OFFICE_ADMIN"
  | "SYSTEM_ADMIN";

export type OfficeModule =
  | "DakMatters"
  | "Court"
  | "Rti"
  | "Accounts"
  | "RecordRoom";

export type LandAccessLevel = "None" | "ViewOnly" | "ViewWrite";

export type HelperAccessLevel = "None" | "ReadOnly" | "ReadWrite";

export interface CanonicalDesignation {
  id: string;
  code: string;
  name: string;
}

export interface OfficeDeskOption {
  id: string;
  code: string;
  name: string;
  workstreamId: string | null;
}

export interface OfficeAccountOptions {
  authority: OfficeAuthority;
  canAssignOfficeSupervisor: boolean;
  canAssignOfficeAdmin: boolean;
  canAssignSystemAdmin: boolean;
  authorities: OfficeAuthority[];
  modules: OfficeModule[];
  landAccess: LandAccessLevel[];
  designations: CanonicalDesignation[];
  desks: OfficeDeskOption[];
}

export interface OfficeAccountDetail {
  capabilities?: { canEdit: boolean; canResetCredential: boolean; canToggleStatus: boolean; canAddHelper: boolean };
  id: string;
  username: string;
  fullName: string;
  designationId: string | null;
  customDesignation: string | null;
  effectiveDesignation: string | null;
  authority: OfficeAuthority;
  officeAccessManaged: boolean;
  isActive: boolean;
  landAccess: LandAccessLevel;
  canRegisterInwardDak: boolean;
  supervisingOfficerId: string | null;
  assistantRevision: number;
  revision: number;
  modules: OfficeModule[];
  helperPermissionCodes: string[] | null;
  deskIds: string[];
}

export interface OfficeAccountInput {
  fullName: string;
  designationId: string | null;
  customDesignation: string | null;
  authority: OfficeAuthority;
  modules: OfficeModule[];
  canRegisterInwardDak: boolean;
  landAccess: LandAccessLevel;
  deskIds: string[];
}

export interface CreateOfficeAccountRequest {
  username: string;
  account: OfficeAccountInput;
}

export interface CreateOfficeAccountResponse {
  account: OfficeAccountDetail;
  temporaryCredential: string;
  credentialExpiresAt: string | null;
}

export interface UpdateOfficeAccountRequest {
  account: OfficeAccountInput;
  expectedRevision: number;
}

export interface OfficeHelperInput {
  fullName: string;
  designationId: string | null;
  customDesignation: string | null;
  deskId: string | null;
  access: HelperAccessLevel;
  permissionCodes?: string[] | null;
  allocations?: any[] | null;
}

export interface CreateOfficeHelperRequest {
  username: string;
  helper: OfficeHelperInput;
}

export interface UpdateOfficeHelperRequest {
  helper: OfficeHelperInput;
  expectedRevision: number; // target.assistantRevision
}

export interface HelperOptionsResponse {
  designation: { id: string; name: string };
  desks: { id: string; name: string }[];
  access: HelperAccessLevel[];
}

export interface RevisionRequest {
  expectedRevision: number;
}
