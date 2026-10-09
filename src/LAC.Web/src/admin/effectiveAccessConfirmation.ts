import type { CanonicalDesignation, LandAccessLevel, OfficeAuthority } from "./officeV3Types.ts";

export function isCanonicalAdmSelection(designation: Pick<CanonicalDesignation, "code"> | undefined, custom: boolean): boolean {
  return !custom && designation?.code === "ADM";
}

export function getCreateConfirmationAuthority(proposed: OfficeAuthority, actor: OfficeAuthority | undefined, canonicalAdm: boolean): OfficeAuthority {
  // The frozen backend promotes canonical ADM on creation, even after a stale supervisor selection.
  return actor === "SYSTEM_ADMIN" && canonicalAdm ? "OFFICE_ADMIN" : proposed;
}

export function getEffectiveAccessSummary(authority: OfficeAuthority, selectedWorkAccess: string, registry: boolean, land: LandAccessLevel) {
  const fullOfficeAccess = authority === "SYSTEM_ADMIN" || authority === "OFFICE_ADMIN" || authority === "OFFICE_SUPERVISOR";
  return {
    fullOfficeAccess,
    workAccess: fullOfficeAccess ? "Full Office Access" : selectedWorkAccess,
    inwardDakRegistry: fullOfficeAccess || registry ? "Can Register Inward Dak" : "No Registry Rights",
    landRecords: fullOfficeAccess || land === "ViewWrite" ? "View + Write" : land === "ViewOnly" ? "View Only" : "None"
  };
}

export function effectiveAccessSummaryItems(access: ReturnType<typeof getEffectiveAccessSummary>) {
  return [
    { label: "Work Access", value: access.workAccess },
    { label: "Inward Dak Registry", value: access.inwardDakRegistry },
    { label: "Land Records", value: access.landRecords }
  ];
}

export function effectiveAccessChanges(before: ReturnType<typeof getEffectiveAccessSummary>, after: ReturnType<typeof getEffectiveAccessSummary>) {
  const changes: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[] = [];
  if (before.workAccess !== after.workAccess && (before.fullOfficeAccess || after.fullOfficeAccess)) {
    changes.push({ label: "Work Access Change", value: `${before.workAccess} → ${after.workAccess}` });
  }
  if (before.inwardDakRegistry !== after.inwardDakRegistry) {
    changes.push({
      label: "Inward Dak Change", value: `${before.inwardDakRegistry} → ${after.inwardDakRegistry}`,
      isAddition: after.inwardDakRegistry === "Can Register Inward Dak",
      isRemoval: after.inwardDakRegistry === "No Registry Rights"
    });
  }
  if (before.landRecords !== after.landRecords) {
    changes.push({ label: "Land Records Change", value: `${before.landRecords} → ${after.landRecords}` });
  }
  return changes;
}
