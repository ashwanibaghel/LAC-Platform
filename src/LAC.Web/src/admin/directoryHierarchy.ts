import type { OfficeAccountDetail } from "./officeV3Types.ts";

export function helperAccessLabel(codes: string[] | null | undefined): string {
  if (!codes?.length) return "None";
  return codes.some(code => !code.endsWith(".View") && code !== "Audit.View") ? "Read + Write" : "Read Only";
}

// Authority order is the existing office hierarchy, not a fabricated reporting parent.
// Helpers are emitted only immediately beneath their actual, authorized supervising officer.
export function directoryHierarchy(accounts: OfficeAccountDetail[], matches: OfficeAccountDetail[]) {
  const ids = new Set(matches.map(account => account.id));
  const rank = { SYSTEM_ADMIN: 4, OFFICE_ADMIN: 3, OFFICE_SUPERVISOR: 2, STANDARD_OFFICER: 1, HELPER: 0 };
  const roots = accounts.filter(account => !account.supervisingOfficerId)
    .sort((a, b) => rank[b.authority] - rank[a.authority] || a.fullName.localeCompare(b.fullName) || a.username.localeCompare(b.username));
  return roots.flatMap(parent => {
    const children = matches.filter(account => account.supervisingOfficerId === parent.id)
      .sort((a, b) => a.fullName.localeCompare(b.fullName) || a.username.localeCompare(b.username));
    return ids.has(parent.id) || children.length ? [parent, ...children] : [];
  });
}
