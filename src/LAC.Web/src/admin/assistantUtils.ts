import type { AllocationInput, AllocationScope, Allocation } from "./types.ts";
import { isoToLocalDateInput, localDateInputToIso } from "./dateUtils.ts";

/**
 * Formats a scope for human display.
 */
export function formatScopeLabel(
  scope: AllocationScope,
  districts?: { id: string; name: string }[],
  subdivisions?: { id: string; name: string; districtId: string }[],
  villages?: { id: string; name: string; subDivisionId: string }[]
): string {
  if (scope.kind === "Global") return "Global (All Jurisdictions)";
  if (scope.kind === "District") {
    const d = districts?.find((x) => x.id === scope.districtId);
    return `District: ${d?.name || scope.districtId || "Specified"}`;
  }
  if (scope.kind === "Subdivision") {
    const s = subdivisions?.find((x) => x.id === scope.subDivisionId);
    return `Subdivision: ${s?.name || scope.subDivisionId || "Specified"}`;
  }
  if (scope.kind === "Village") {
    const v = villages?.find((x) => x.id === scope.villageId);
    return `Village: ${v?.name || scope.villageId || "Specified"}`;
  }
  return "Unknown Scope";
}

export interface BuildChildAllocationParams {
  parent: Allocation;
  selectedScopeIndices: number[];
  childValidFrom: string; // YYYY-MM-DD
  childValidTo?: string | null; // YYYY-MM-DD
  childReason?: string | null;
}

export type BuildChildAllocationResult =
  | { valid: true; child: AllocationInput }
  | { valid: false; error: string };

/**
 * Validates and constructs a bounded child allocation derived strictly from a parent supervisor allocation.
 *
 * Invariants:
 * 1. WorkDefinition is inherited/read-only from parent.
 * 2. delegatedFromAllocationId = parent.id.
 * 3. Work Order Reference is inherited from parent; never invented.
 * 4. User must choose one or more scopes strictly from parent's permitted scopes.
 * 5. Child cannot contain a scope outside parent scopes.
 * 6. Child ValidFrom cannot start before parent ValidFrom.
 * 7. If parent ValidTo is bounded, child ValidTo is required and cannot exceed parent ValidTo.
 * 8. If parent ValidTo is null, child may be bounded or unbounded.
 * 9. ValidTo is exclusive and must be strictly greater than ValidFrom.
 * 10. Never broaden the parent allocation.
 */
export function buildChildAllocation(params: BuildChildAllocationParams): BuildChildAllocationResult {
  const { parent, selectedScopeIndices, childValidFrom, childValidTo, childReason } = params;

  if (!parent) {
    return { valid: false, error: "Parent allocation is required." };
  }

  if (!selectedScopeIndices || selectedScopeIndices.length === 0) {
    return { valid: false, error: "Please select at least one scope from the parent allocation." };
  }

  for (const idx of selectedScopeIndices) {
    if (idx < 0 || idx >= (parent.scopes?.length || 0)) {
      return { valid: false, error: "Selected scope is outside the parent allocation's permitted scopes." };
    }
  }

  if (!childValidFrom || !childValidFrom.trim()) {
    return { valid: false, error: "Child Valid From date is required." };
  }

  const parentFromLocalDate = isoToLocalDateInput(parent.validFrom);
  const parentToLocalDate = parent.validTo ? isoToLocalDateInput(parent.validTo) : "";

  if (childValidFrom.trim() < parentFromLocalDate) {
    return {
      valid: false,
      error: `Child Valid From (${childValidFrom}) cannot start before parent Valid From (${parentFromLocalDate}).`,
    };
  }

  const trimmedChildValidTo = childValidTo?.trim() || "";

  if (parentToLocalDate) {
    if (!trimmedChildValidTo) {
      return {
        valid: false,
        error: `Parent allocation is bounded (${parentToLocalDate}). Child Valid To (exclusive) is required.`,
      };
    }
    if (trimmedChildValidTo > parentToLocalDate) {
      return {
        valid: false,
        error: `Child Valid To (${trimmedChildValidTo}) cannot exceed parent Valid To (${parentToLocalDate}).`,
      };
    }
  }

  if (trimmedChildValidTo && trimmedChildValidTo <= childValidFrom.trim()) {
    return {
      valid: false,
      error: "Child Valid To (exclusive) must be strictly greater than Child Valid From.",
    };
  }

  const chosenScopes = selectedScopeIndices.map((i) => parent.scopes[i]);

  const child: AllocationInput = {
    workDefinitionId: parent.workDefinitionId,
    delegatedFromAllocationId: parent.id,
    workOrderReference: parent.workOrderReference,
    scopes: chosenScopes,
    validFrom: localDateInputToIso(childValidFrom.trim())!,
    validTo: trimmedChildValidTo ? localDateInputToIso(trimmedChildValidTo) : null,
    reason:
      (childReason && childReason.trim()) ||
      `Delegated from ${parent.workName} (${parent.workOrderReference})`,
  };

  return { valid: true, child };
}
