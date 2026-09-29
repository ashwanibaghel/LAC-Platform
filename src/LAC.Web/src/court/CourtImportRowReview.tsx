import { useState } from "react";
import type { Row } from "./CourtImportPreview";

type Action = "ImportAsNewCase" | "LinkToExistingCase" | "Skip";
type ExistingCase = {
  id: string; caseNumber: string; courtName: string; caseTitle?: string;
  currentStatus?: string; authoritativeNextDate?: string;
};
type Decision = {
  action: Action; resolvedCourtCaseId?: string | null; approvedCaseNumber?: string | null;
  approvedCaseTitle?: string | null; approvedCourtName?: string | null;
  approvedStatus?: string | null; applyStatusToExisting: boolean;
  ndohAction?: string | null; reviewerNotes?: string | null;
};

export function CourtImportRowReview({ row, save, close }: {
  row: Row; save: (decision: Decision) => Promise<void>; close: () => void;
}) {
  const [action, setAction] = useState<Action | "">(
    row.resolutionAction === "ImportAsNewCase" || row.resolutionAction === "LinkToExistingCase" || row.resolutionAction === "Skip"
      ? row.resolutionAction : row.rowStatus === "NewCandidate" ? "ImportAsNewCase" : "");
  const [caseNumber, setCaseNumber] = useState(row.approvedCaseNumber || row.rawCaseNumber || "");
  const [caseTitle, setCaseTitle] = useState(row.approvedCaseTitle || row.rawCaseTitle || "");
  const [courtName, setCourtName] = useState(row.approvedCourtName || row.suggestedCourtName || "");
  const [approvedStatus, setApprovedStatus] = useState(row.approvedStatus ||
    (["Pending", "Disposed"].includes(row.suggestedStatusClass || "") ? row.suggestedStatusClass || "" : ""));
  const [notes, setNotes] = useState(row.reviewerNotes || "");
  const [applyStatus, setApplyStatus] = useState(row.applyStatusToExisting || false);
  const [ndohAction, setNdohAction] = useState(row.ndohAction || "");
  const [search, setSearch] = useState("");
  const [matches, setMatches] = useState<ExistingCase[]>([]);
  const [existing, setExisting] = useState<ExistingCase | null>(null);
  const [error, setError] = useState("");
  const [saving, setSaving] = useState(false);

  const findCases = async () => {
    setError("");
    try {
      const response = await fetch("/api/court-cases?" + new URLSearchParams({ search, pageSize: "20" }), { credentials: "include" });
      if (!response.ok) throw new Error("Existing cases could not be searched.");
      const data = await response.json();
      setMatches(data.items || []);
    } catch (e) { setError(e instanceof Error ? e.message : "Search failed."); }
  };

  const submit = async () => {
    setError("");
    if (!action) { setError("Choose how this row should be handled."); return; }
    if (action === "LinkToExistingCase" && !existing) { setError("Choose an existing Court case."); return; }
    setSaving(true);
    try {
      await save({
        action, resolvedCourtCaseId: action === "LinkToExistingCase" ? existing?.id : null,
        approvedCaseNumber: action === "ImportAsNewCase" ? caseNumber : null,
        approvedCaseTitle: action === "ImportAsNewCase" ? caseTitle : null,
        approvedCourtName: action === "ImportAsNewCase" ? courtName : null,
        approvedStatus: action === "ImportAsNewCase" || action === "LinkToExistingCase" && applyStatus ? approvedStatus : null,
        applyStatusToExisting: action === "LinkToExistingCase" && applyStatus,
        ndohAction: action === "LinkToExistingCase" && row.parsedNdoh ? ndohAction || null : null,
        reviewerNotes: notes,
      });
    } catch (e) { setError(e instanceof Error ? e.message : "Decision failed."); }
    finally { setSaving(false); }
  };

  return <section className="court-import-row-review">
    <header className="court-import-row-review-header"><div><span className="court-import-eyebrow">EXCEL ROW {row.sourceRowNumber}</span>
      <h2>Review this case</h2><p>Compare the workbook entry, then choose one decision.</p></div>
      <button type="button" aria-label="Close review" onClick={close}>✕</button></header>
    <div className="court-import-row-review-body">
      {["PotentialDuplicate", "IdentityConflict", "NeedsReview", "Invalid"].includes(row.rowStatus) &&
        <div className="court-import-risk" role="alert"><strong>Manual check required</strong><span>{row.rowStatus} rows are never bulk-approved. Check the source carefully and explain your decision.</span></div>}
      <section className="court-import-source-summary"><h3>From the workbook</h3>
        <strong className="court-import-source-case">{row.rawCaseNumber || "Case number missing"}</strong>
        <p>{row.rawCaseTitle || "No party title supplied"}</p>
        <dl><div><dt>Court</dt><dd>{row.rawCourt || "—"}</dd></div><div><dt>Suggested court</dt><dd>{row.suggestedCourtName || "Needs officer review"}</dd></div>
          <div><dt>Status</dt><dd>{row.rawStatus || "—"}</dd></div><div><dt>NDOH</dt><dd>{row.rawNdoh || "—"}</dd></div>
          <div><dt>Advocate</dt><dd>{row.rawAdvocate || "—"}</dd></div><div><dt>Village / award</dt><dd>{row.rawVillage || "—"}{row.rawAwardNumber ? ` · ${row.rawAwardNumber}` : ""}</dd></div></dl>
        <details><summary>View source directions, facts and order link</summary>
          <dl><div><dt>Directions</dt><dd>{row.rawDirections || "—"}</dd></div><div><dt>Brief facts</dt><dd>{row.rawBriefFacts || "—"}</dd></div>
            <div><dt>Last-order URL</dt><dd>{row.rawLastOrderLink || "—"} · {row.lastOrderLinkState}</dd></div></dl></details>
      </section>
      <section className="court-import-decision-section"><h3>What should happen to this row?</h3>
        <div className="court-import-decision-options">
          {([ ["ImportAsNewCase", "Create a new case", "Use the approved details below"],
              ["LinkToExistingCase", "Link to an existing case", "Find and compare the exact case"],
              ["Skip", "Do not import", "Leave this row out of Court Matters"] ] as const).map(([value, label, hint]) =>
            <label key={value} className={action === value ? "selected" : ""}><input type="radio" name="court-import-decision" value={value}
              checked={action === value} onChange={() => { setAction(value); setExisting(null); }} /><span><strong>{label}</strong><small>{hint}</small></span></label>)}
        </div>
      </section>
      {action === "ImportAsNewCase" && <section className="court-import-decision-fields"><h3>Approved case details</h3>
        <label>Case number<input value={caseNumber} onChange={e => setCaseNumber(e.target.value)} /></label>
        <label>Case title<input value={caseTitle} onChange={e => setCaseTitle(e.target.value)} /></label>
        <label>Court<input value={courtName} onChange={e => setCourtName(e.target.value)} /></label>
      </section>}
      {action === "LinkToExistingCase" && <section className="court-import-decision-fields"><h3>Find the existing case</h3>
        <div className="court-import-existing-search"><label>Search case number or title<input value={search} onChange={e => setSearch(e.target.value)} /></label>
          <button className="secondary-button" type="button" onClick={findCases} disabled={!search.trim()}>Search</button></div>
        <div className="court-import-existing-results">{matches.map(item => <button type="button" className={existing?.id === item.id ? "selected" : ""} key={item.id} onClick={() => setExisting(item)}>
          <strong>{item.caseNumber}</strong><span>{item.caseTitle} · {item.courtName} · {item.currentStatus} · NDOH {item.authoritativeNextDate || "—"}</span></button>)}</div>
        {existing && <div className="court-import-compare"><strong>Compare before confirming</strong>
          <p><b>Excel:</b> {row.rawCaseNumber} · {row.rawCaseTitle} · {row.rawCourt} · {row.rawStatus} · NDOH {row.parsedNdoh || "—"}</p>
          <p><b>Selected:</b> {existing.caseNumber} · {existing.caseTitle} · {existing.courtName} · {existing.currentStatus} · NDOH {existing.authoritativeNextDate || "—"}</p>
          <small>Case number, court and title will not be overwritten.</small>
          <label className="court-import-check"><input type="checkbox" checked={applyStatus} onChange={e => setApplyStatus(e.target.checked)} /> Apply approved status to existing case</label>
          {row.parsedNdoh && <label>NDOH decision<select value={ndohAction} onChange={e => setNdohAction(e.target.value)}>
            <option value="">Choose explicitly</option><option value="KeepExisting">Keep existing NDOH</option><option value="UseImported">Use imported NDOH</option>
          </select></label>}
        </div>}
      </section>}
      {(action === "ImportAsNewCase" || action === "LinkToExistingCase" && applyStatus) && <div className="court-import-decision-fields">
        <label>Canonical status<select value={approvedStatus} onChange={e => setApprovedStatus(e.target.value)}>
          <option value="">Choose status</option><option value="Pending">Pending</option><option value="Disposed">Disposed</option>
        </select></label></div>}
      {action && <div className="court-import-decision-fields"><label>Reason / reviewer notes<textarea rows={3} value={notes} onChange={e => setNotes(e.target.value)} /></label></div>}
      {error && <div className="court-import-error" role="alert">{error}</div>}
    </div>
    <footer className="court-import-row-review-footer"><button type="button" className="secondary-button" onClick={close}>Cancel</button>
      <button type="button" className="primary-button" disabled={!action || saving} onClick={() => void submit()}>{saving ? "Saving…" : "Save decision"}</button></footer>
  </section>;
}
