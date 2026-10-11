const preferredTypes = [
  "Court Case", "Compensation", "Land Acquisition", "Demarcation", "Possession", "Other",
];

// Display preferences must never act as an allowlist for the open MatterType field.
export function groupMattersByType<T extends { matterType?: string | null }>(items: readonly T[]): Array<[string, T[]]> {
  const groups = new Map<string, T[]>();
  for (const item of items) {
    const type = item.matterType?.trim() || "Other";
    const group = groups.get(type);
    if (group) group.push(item);
    else groups.set(type, [item]);
  }
  const rank = (type: string) => {
    const index = preferredTypes.indexOf(type);
    return index < 0 ? preferredTypes.length : index;
  };
  return [...groups].sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b));
}
