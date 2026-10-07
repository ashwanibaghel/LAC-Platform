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

// -------------------------------------------------------------
// Frozen Codex RBAC Backend Contract Types (from docs/anti-rbac-frontend-contract.md)
// -------------------------------------------------------------

export type ScopeKind = "Global" | "District" | "Subdivision" | "Village";

export type WorkKind =
  | "LandAcquisition"
  | "Award"
  | "LandRecords"
  | "Nm"
  | "Enm"
  | "Possession"
  | "Accounts"
  | "Compensation"
  | "StatementA"
  | "Court"
  | "Rti"
  | "Correspondence"
  | "General";

export interface AllocationScope {
  kind: ScopeKind; // required: no implicit Global
  districtId?: string | null;
  subDivisionId?: string | null;
  villageId?: string | null;
}

export interface AllocationInput {
  workDefinitionId: string;
  validFrom: string;
  validTo: string | null;
  workOrderReference: string;
  reason: string | null;
  scopes: AllocationScope[]; // 1..100 explicit targets, union within allocation
  delegatedFromAllocationId?: string | null; // required for assistant grants
}

export interface Allocation extends AllocationInput {
  id: string;
  userId: string;
  workCode: string;
  workName: string;
  revision: number;
  revokedAt: string | null;
  revokedByUserId: string | null;
}

export interface IdRevision {
  id: string;
  revision: number;
}

export interface AccountOptionsResponse {
  canAssignRoles: boolean;
  canManageAllocations: boolean;
  designations: Designation[];
  roles: { id: string; code: string; name: string }[];
  works: { id: string; code: string; name: string; kind: WorkKind; workstreamId: string }[];
  districts: { id: string; name: string }[];
  subdivisions: { id: string; name: string; districtId: string }[];
  villages: { id: string; name: string; subDivisionId: string }[];
}

export interface WorkDefinitionItem {
  id: string;
  code: string;
  name: string;
  description: string;
  kind: WorkKind;
  workstreamId: string;
  isActive: boolean;
  revision: number;
}

export interface CreateOfficer {
  username: string;
  displayName: string;
  password?: string | null;
  designationId: string | null;
  roleIds?: string[] | null;
  workstreamIds?: string[] | null;
  primaryWorkstreamId?: string | null;
  allocations?: AllocationInput[] | null;
}

export type EditOfficer = Omit<CreateOfficer, "username" | "password">;

export interface AssistantInput {
  displayName: string;
  designationId: string | null;
  roleIds: string[];
  permissionCodes: string[];
  allocations: AllocationInput[];
  deskIds: string[];
}

export interface OfficerAssistantSummary {
  id: string;
  username: string;
  displayName: string;
  designationId: string | null;
  isActive: boolean;
  supervisingOfficerId: string;
  assistantRevision: number;
  mustChangePassword?: boolean;
}

export interface DelegationOptionsResponse {
  allocations: Allocation[];
  roles: { id: string; code: string; name: string; permissions: { code: string; scopeMode: string }[] }[];
  permissions: { code: string; scopeMode: string }[];
  desks: { id: string; code: string; name: string }[];
  serverTime: string;
}

export interface OfficerAssistantDetail {
  id: string;
  username: string;
  displayName: string;
  designationId: string | null;
  isActive: boolean;
  supervisingOfficerId: string;
  assistantRevision: number;
  roleIds: string[];
  permissionCodes: string[];
  allocations: Allocation[];
  deskIds: string[];
}

export interface TemporaryCredentialResponse {
  id?: string;
  message?: string;
  temporaryCredential?: string;
  credentialExpiresAt?: string;
  assistantRevision?: number;
}

export interface RbacAuditLog {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  changedAt: string;
  changedBy?: string | null;
  actorUserId?: string | null;
  onBehalfOfUserId?: string | null;
  actorDisplayNameSnapshot?: string | null;
  onBehalfOfDisplayNameSnapshot?: string | null;
  actorLabel?: string | null;
  oldValues?: string | null;
  newValues?: string | null;
}

export interface RbacAuditResponse {
  total: number;
  page: number;
  pageSize: number;
  items: RbacAuditLog[];
}

export interface WorkCatalogItem {
  id: string;
  code: string;
  name: string;
  workstreamCode?: string;
  workstreamName?: string;
  description: string;
  kind?: WorkKind;
  statutoryBasis?: string;
  associatedPermissions?: string[];
  defaultScopeMode?: string;
  isStandard?: boolean;
  isActive: boolean;
  revision?: number;
}

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
