export interface Designation {
  id: string;
  code: string;
  name: string;
}

export interface Workstream {
  id: string;
  code: string;
  name: string;
  isPrimary: boolean;
}

export interface Desk {
  id: string;
  code: string;
  name: string;
  isPrimary: boolean;
}

export interface PermissionScope {
  code: string;
  scope: string;
}

export interface CurrentUser {
  id: string;
  username: string;
  displayName: string;
  designation?: Designation;
  roles: string[];
  permissions: PermissionScope[];
  workstreams: Workstream[];
  desks: Desk[];
  mustChangePassword?: boolean;
  temporaryCredentialExpiresAt?: string | null;
  supervisingOfficerId?: string | null;
  customDesignation?: string | null;
  authority?: "SYSTEM_ADMIN" | "OFFICE_ADMIN" | "OFFICE_SUPERVISOR" | "STANDARD_OFFICER" | "HELPER";
}


export interface AuthContextType {
  user: CurrentUser | null;
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  hasPermission: (permissionCode: string) => boolean;
  refreshUser: () => Promise<void>;
}
