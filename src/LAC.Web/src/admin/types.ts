export interface Designation {
  id: string;
  code: string;
  name: string;
}

export interface Workstream {
  id: string;
  code: string;
  name: string;
  description?: string;
  isPrimary?: boolean;
  isActive?: boolean;
}

export interface DeskItem {
  id: string;
  code: string;
  name: string;
  description?: string;
  workstreamId?: string;
  workstreamCode?: string;
  workstreamName?: string;
  isActive: boolean;
  activeMembersCount?: number;
  createdAt?: string;
}

export interface UserDeskMembershipItem {
  id: string;
  officeDeskId: string;
  deskCode: string;
  deskName: string;
  workstreamName?: string;
  isPrimary: boolean;
  isActive: boolean;
  assignedAt: string;
  removedAt?: string;
}

export interface PermissionDefinition {
  id: string;
  code: string;
  name: string;
  description?: string;
  category: string;
}

export interface RolePermissionItem {
  permissionId: string;
  code: string;
  name: string;
  category: string;
  scopeMode: "All" | "Workstream" | "Assigned" | "Own" | string;
}

export interface RoleDetail {
  id: string;
  code: string;
  name: string;
  description?: string;
  isSystemRole: boolean;
  isActive: boolean;
  permissions: RolePermissionItem[];
}

export interface OfficerItem {
  id: string;
  username: string;
  displayName: string;
  designation?: Designation;
  isActive: boolean;
  lastLoginAt?: string;
  createdAt: string;
  roles: string[];
  workstreams: string[];
  primaryDeskName?: string;
  activeDesksCount: number;
}

export interface OfficerDetail {
  id: string;
  username: string;
  displayName: string;
  designationId?: string;
  designation?: Designation;
  isActive: boolean;
  lastLoginAt?: string;
  passwordChangedAt?: string;
  createdAt: string;
  roleIds: string[];
  roles: { id: string; code: string; name: string; isSystemRole: boolean }[];
  workstreams: { id: string; code: string; name: string; isPrimary: boolean }[];
  desks: UserDeskMembershipItem[];
}

/**
 * Model distinction:
 * Work represents a specific statutory responsibility or task category
 * (e.g. Statement-A, LR Verification, Award Formulation, Compensation Disbursement),
 * distinct from an operational seat (OfficeDesk) or functional branch (Workstream).
 */
export interface WorkCatalogItem {
  id: string;
  code: string;
  name: string;
  workstreamCode: string;
  workstreamName: string;
  description: string;
  statutoryBasis?: string;
  associatedPermissions: string[];
  defaultScopeMode: "All" | "Workstream" | "Assigned" | "Own";
  isStandard: boolean;
  isActive: boolean;
}

/**
 * Officer Assistant / DEO Delegation Model:
 * Represents assistant account delegation operating on behalf of a supervising officer.
 */
export interface OfficerAssistantDelegation {
  id: string;
  assistantUserId: string;
  assistantUsername: string;
  assistantDisplayName: string;
  supervisingOfficerId: string;
  supervisingOfficerName: string;
  supervisingOfficerDesignation: string;
  delegatedWorkCodes: string[];
  delegatedPermissionCodes: string[];
  notes?: string;
  isActive: boolean;
  delegatedAt: string;
  revokedAt?: string;
}
