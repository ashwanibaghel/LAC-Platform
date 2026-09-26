import { useEffect, useState, type RefObject } from "react";

type Field = { key: string; label: string; kind?: "date" | "number" | "textarea" };
const fields: Record<string, Field[]> = {
  AwardCore: [{ key: "awardNumber", label: "Award number" }, { key: "awardDate", label: "Award date", kind: "date" }, { key: "awardType", label: "Award type" }, { key: "natureOfAcquisition", label: "Nature of acquisition" }, { key: "awardedAreaText", label: "Awarded / acquisition area" }, { key: "parentAwardReferenceSuggestion", label: "Parent/Main Award reference" }, { key: "purpose", label: "Purpose of acquisition", kind: "textarea" }],
  Notification: [{ key: "sectionType", label: "Legal framework / section" }, { key: "notificationNumber", label: "Notification number" }, { key: "notificationDate", label: "Notification date", kind: "date" }],
  PossessionEvent: [{ key: "possessionDate", label: "Possession date", kind: "date" }, { key: "eventType", label: "Event type" }, { key: "status", label: "Exact source status" }, { key: "possessionAreaText", label: "Source possession area" }, { key: "possessionAreaUnit", label: "Source area unit" }, { key: "khasraReferences", label: "Source Khasra references" }],
  CourtCase: [{ key: "caseNumber", label: "Case number" }, { key: "caseType", label: "Case type" }, { key: "courtName", label: "Court (only if stated)" }, { key: "status", label: "Exact status / order wording" }, { key: "khasraReferences", label: "Source Khasra references" }, { key: "relatedAreaText", label: "Source area reference" }, { key: "parties", label: "Source parties (only if stated)" }],
  Claim: [{ key: "claimReference", label: "Claim reference" }, { key: "claimDate", label: "Claim date", kind: "date" }, { key: "claimText", label: "Claim text", kind: "textarea" }],
  AwardLandClass: [{ key: "code", label: "Class code" }, { key: "description", label: "Description" }],
  AwardValuationRule: [{ key: "ruleType", label: "Valuation rule" }, { key: "rateAmount", label: "Rate amount", kind: "number" }, { key: "rateUnit", label: "Rate unit" }, { key: "legalSection", label: "Legal section" }],
  AwardCompensationRule: [{ key: "ruleType", label: "Compensation rule" }, { key: "ratePercent", label: "Rate percent", kind: "number" }, { key: "rateAmount", label: "Rate amount", kind: "number" }, { key: "legalSection", label: "Legal section" }],
  AwardAreaIssue: [{ key: "issueType", label: "Issue" }, { key: "notificationAreaBigha", label: "Notification area (Bigha)", kind: "number" }, { key: "fieldBookAreaBigha", label: "Field-book area (Bigha)", kind: "number" }, { key: "differenceBigha", label: "Difference (Bigha)", kind: "number" }],
  AwardSupplementaryMatter: [{ key: "matterType", label: "Matter" }, { key: "description", label: "Description", kind: "textarea" }],
};

function factValue(kind: Field["kind"], raw: string): string | number | null {
  if (raw === "") return null;
  if (kind !== "number") return raw;
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
}

function NumberField({ label, value, onChange }: { label: string; value: unknown; onChange: (value: number | null) => void }) {
  // Preserve partial decimal typing such as `125.` while the payload stays numeric.
  const [text, setText] = useState(String(value ?? ""));
  return <input aria-label={label} type="number" step="any" value={text} onChange={e => { setText(e.target.value); onChange(factValue("number", e.target.value) as number | null); }} />;
}

export function AwardFactEditor({ type, draft, onChange, awardId, firstInputRef }: { type: string; draft: Record<string, unknown>; onChange: (next: Record<string, unknown>) => void; awardId?: string; firstInputRef: RefObject<HTMLInputElement | null> }) {
  const [villages, setVillages] = useState<{ id: string; name: string }[]>([]);
  useEffect(() => {
    if (type !== "AwardVillage" || !awardId) return;
    let live = true;
    fetch(`/api/awards/${awardId}/workspace`, { credentials: "include" }).then(response => response.ok ? response.json() : Promise.reject()).then(data => { if (live) setVillages(data.villages || []); }).catch(() => { if (live) setVillages([]); });
    return () => { live = false; };
  }, [type, awardId]);

  if (type === "AwardVillage") return <div className="award-wb-other"><p>Detected Village: {String(draft.villageName || "—")}. Select its official spelling linked to this Award.</p><label>Official Village<select aria-label="Official Village" value={String(draft.exactCanonicalVillageName || "")} onChange={e => onChange({ ...draft, villageName: e.target.value, exactCanonicalVillageName: e.target.value })}><option value="">Select the official Village</option>{villages.map(v => <option key={v.id} value={v.name}>{v.name}</option>)}</select></label></div>;
  const schema = fields[type];
  if (!schema) return <div className="award-wb-other"><p>This source finding has no structured editor. Keep it for individual review.</p></div>;
  return <div className="award-wb-other"><p>Compare every correction with the original PDF before confirming.</p>{schema.map((field, index) => <label key={field.key}>{field.label}{field.kind === "textarea" ? <textarea aria-label={field.label} value={String(draft[field.key] ?? "")} onChange={e => onChange({ ...draft, [field.key]: factValue(field.kind, e.target.value) })} /> : field.kind === "number" ? <NumberField label={field.label} value={draft[field.key]} onChange={value => onChange({ ...draft, [field.key]: value })} /> : <input ref={index === 0 ? firstInputRef : undefined} aria-label={field.label} type={field.kind || "text"} value={String(draft[field.key] ?? "")} onChange={e => onChange({ ...draft, [field.key]: factValue(field.kind, e.target.value) })} />}</label>)}{type === "AwardCore" && <small>Parent Award reference remains evidence; it does not create a link automatically.</small>}{type === "CourtCase" && <small>A case reference alone does not establish a stay or link affected parcels.</small>}</div>;
}
