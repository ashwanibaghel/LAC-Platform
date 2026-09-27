import { useEffect, useState, type RefObject } from "react";

type Field = { key: string; label: string; kind?: "date" | "number" | "textarea"; hint?: string };
const fields: Record<string, Field[]> = {
  AwardCore: [{ key: "awardNumber", label: "Award number" }, { key: "awardDate", label: "Award date", kind: "date" }, { key: "awardType", label: "Award type", hint: "Main, Supplementary or Corrigendum only if the PDF identifies it. Permanent is the nature of acquisition." }, { key: "natureOfAcquisition", label: "Nature of acquisition" }, { key: "awardedAreaText", label: "Total area awarded in this Award", hint: "Copy the stated total with its units, e.g. 12 Bigha 8 Biswa. Do not use notified area or sum Khasras." }, { key: "parentAwardReferenceSuggestion", label: "Parent / Main Award reference", hint: "Copy the cited main Award number for a supplementary or correction Award. Otherwise leave blank." }, { key: "purpose", label: "Purpose of acquisition", kind: "textarea" }],
  Notification: [{ key: "sectionType", label: "Legal framework / section" }, { key: "notificationNumber", label: "Notification number" }, { key: "notificationDate", label: "Notification date", kind: "date" }],
  PossessionEvent: [{ key: "possessionDate", label: "Possession date", kind: "date" }, { key: "eventType", label: "Event type" }, { key: "status", label: "Exact source status" }, { key: "possessionAreaText", label: "Source possession area" }, { key: "possessionAreaUnit", label: "Source area unit" }, { key: "khasraReferences", label: "Source Khasra references" }],
  CourtCase: [{ key: "caseNumber", label: "Case number" }, { key: "caseType", label: "Case type" }, { key: "courtName", label: "Court (only if stated)" }, { key: "status", label: "Exact status / order wording" }, { key: "khasraReferences", label: "Source Khasra references" }, { key: "relatedAreaText", label: "Source area reference" }, { key: "parties", label: "Source parties (only if stated)" }],
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
  useEffect(() => { setText(current => {
    if (value == null && current === "") return current;
    if (value != null && current.trim() !== "" && Number(current) === Number(value)) return current;
    return String(value ?? "");
  }); }, [value]);
  return <input aria-label={label} type="number" step="any" value={text} onChange={e => { setText(e.target.value); onChange(factValue("number", e.target.value) as number | null); }} />;
}

export function AwardFactEditor({ type, draft, onChange, awardId, khasraMatches, firstInputRef }: { type: string; draft: Record<string, unknown>; onChange: (next: Record<string, unknown>) => void; awardId?: string; khasraMatches?: { sourceReference: string; khasraId?: string | null; displayNumber?: string | null }[] | null; firstInputRef: RefObject<HTMLInputElement | null> }) {
  const [villages, setVillages] = useState<{ id: string; name: string }[]>([]);
  useEffect(() => {
    if (type !== "AwardVillage" || !awardId) return;
    let live = true;
    fetch(`/api/awards/${awardId}/workspace`, { credentials: "include" }).then(response => response.ok ? response.json() : Promise.reject()).then(data => { if (live) setVillages(data.villages || []); }).catch(() => { if (live) setVillages([]); });
    return () => { live = false; };
  }, [type, awardId]);

  if (type === "AwardVillage") return <div className="award-wb-other"><label>Official Village<select aria-label="Official Village" value={String(draft.exactCanonicalVillageName || "")} onChange={e => onChange({ ...draft, villageName: e.target.value, exactCanonicalVillageName: e.target.value })}><option value="">Select the official Village</option>{villages.map(v => <option key={v.id} value={v.name}>{v.name}</option>)}</select></label><details className="award-wb-diagnostics"><summary>Details / Diagnostics</summary><p>Detected Village: {String(draft.villageName || "—")}. Select its official spelling linked to this Award.</p></details></div>;
  if (type === "Claim") return <div className="award-wb-other award-wb-claim">
    <label>Claimant as recorded<input ref={firstInputRef} aria-label="Claimant as recorded" value={String(draft.claimantText ?? "")} onChange={e => onChange({ ...draft, claimantText: e.target.value || null })}/></label>
    <label>Khasra references<input aria-label="Khasra references" value={String(draft.khasraReferences ?? "")} onChange={e => onChange({ ...draft, khasraReferences: e.target.value || null })}/></label>
    {khasraMatches && khasraMatches.length > 0 && <small>Exact village-master check: {khasraMatches.map(match => `${match.sourceReference} ${match.khasraId ? `matches ${match.displayNumber}` : "has no exact match"}`).join("; ")}. This is a reference check, not a ClaimKhasra link.</small>}
    <label>Claimed area<input aria-label="Claimed area" value={String(draft.claimedAreaText ?? "")} onChange={e => onChange({ ...draft, claimedAreaText: e.target.value || null })}/></label>
    <label>Claim details<textarea aria-label="Claim details" value={String(draft.claimText ?? "")} onChange={e => onChange({ ...draft, claimText: e.target.value || null })}/></label>
    <div className="award-wb-claim-rate"><label>Claimed land rate<NumberField label="Claimed land rate" value={draft.claimedRateAmount} onChange={value => onChange({ ...draft, claimedRateAmount: value })}/></label><label>Per unit<input aria-label="Claimed land rate unit" value={String(draft.claimedRateUnit ?? "")} onChange={e => onChange({ ...draft, claimedRateUnit: e.target.value || null })}/></label></div>
    <label>Source serial<input aria-label="Source serial" value={String(draft.sourceSerialNumber ?? "")} onChange={e => onChange({ ...draft, sourceSerialNumber: e.target.value || null })}/></label>
    {Boolean(draft.sourceSerialNumber) && !draft.claimantText && <small className="award-wb-warning">Claimant text was not read. Check the source row and enter the name as recorded before confirming.</small>}
    {!draft.claimText && <small className="award-wb-warning">Claim text was not read. Check the source row; enter any printed claim before confirming a blank cell.</small>}
    <details className="award-wb-diagnostics"><summary>Other claim fields</summary><label>Claim reference<input aria-label="Claim reference" value={String(draft.claimReference ?? "")} onChange={e => onChange({ ...draft, claimReference: e.target.value || null })}/></label><label>Claim date<input aria-label="Claim date" type="date" value={String(draft.claimDate ?? "")} onChange={e => onChange({ ...draft, claimDate: e.target.value || null })}/></label><label>Claimed amount<NumberField label="Claimed amount" value={draft.claimedAmount} onChange={value => onChange({ ...draft, claimedAmount: value })}/></label></details>
    <small>Claimant means the person named in this Award schedule. It does not establish ownership or payment. Khasra references stay unlinked source evidence.</small>
  </div>;
  const schema = fields[type];
  if (!schema) return <div className="award-wb-other"><p>This source finding has no structured editor. Keep it for individual review.</p></div>;
  return <div className="award-wb-other">{schema.map((field, index) => <label key={field.key}>{field.label}{field.kind === "textarea" ? <textarea aria-label={field.label} value={String(draft[field.key] ?? "")} onChange={e => onChange({ ...draft, [field.key]: factValue(field.kind, e.target.value) })} /> : field.kind === "number" ? <NumberField label={field.label} value={draft[field.key]} onChange={value => onChange({ ...draft, [field.key]: value })} /> : <input ref={index === 0 ? firstInputRef : undefined} aria-label={field.label} aria-describedby={field.hint ? `award-${field.key}-hint` : undefined} type={field.kind || "text"} value={String(draft[field.key] ?? "")} onChange={e => onChange({ ...draft, [field.key]: factValue(field.kind, e.target.value) })} />}{field.hint && <small id={`award-${field.key}-hint`} className="award-wb-field-hint">{field.hint}</small>}</label>)}{(type === "AwardCore" || type === "CourtCase") && <details className="award-wb-diagnostics"><summary>Details / Diagnostics</summary><p>{type === "AwardCore" ? "Parent Award reference remains evidence; it does not create a link automatically." : "A case reference alone does not establish a stay or link affected parcels."}</p></details>}</div>;
}
