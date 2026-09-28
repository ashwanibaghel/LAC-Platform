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
  const [action, setAction] = useState<Action>("ImportAsNewCase");
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
    if (action === "LinkToExistingCase" && !existing) { setError("Choose an existing Court case."); return; }
    try {
      await save({
        action, resolvedCourtCaseId: action === "LinkToExistingCase" ? existing?.id : null,
        approvedCaseNumber: action === "ImportAsNewCase" ? caseNumber : null,
        approvedCaseTitle: action === "ImportAsNewCase" ? caseTitle : null,
        approvedCourtName: action === "ImportAsNewCase" ? courtName : null,
        approvedStatus: action === "ImportAsNewCase" || applyStatus ? approvedStatus : null,
        applyStatusToExisting: action === "LinkToExistingCase" && applyStatus,
        ndohAction: action === "LinkToExistingCase" && row.parsedNdoh ? ndohAction || null : null,
        reviewerNotes: notes,
      });
    } catch (e) { setError(e instanceof Error ? e.message : "Decision failed."); }
  };

  return <section style={{ marginTop: 24, padding: 16, border: "1px solid currentColor" }}>
    <h2>Review source row {row.sourceRowNumber}</h2>
    {["PotentialDuplicate", "IdentityConflict", "NeedsReview", "Invalid"].includes(row.rowStatus) &&
      <p role="alert">Risk: {row.rowStatus}. This row is never bulk-approved. Check the source and explain your decision.</p>}
    <p>Excel: {row.rawCaseNumber} · {row.rawCaseTitle} · {row.rawCourt} · {row.rawStatus} · NDOH {row.rawNdoh || "blank"}</p>
    <p>Canonical court suggestion: {row.suggestedCourtName || "Unresolved — officer review required"}</p>
    <p>Directions (source only): {row.rawDirections || "—"}</p>
    <p>Brief facts (source only): {row.rawBriefFacts || "—"}</p>
    <p>Last-order URL (source only): {row.rawLastOrderLink || "—"} · {row.lastOrderLinkState}</p>
    <label>Decision <select value={action} onChange={e => { setAction(e.target.value as Action); setExisting(null); }}>
      <option value="ImportAsNewCase">Import as new case</option>
      <option value="LinkToExistingCase">Link/update existing case</option>
      <option value="Skip">Skip</option>
    </select></label>
    {action === "ImportAsNewCase" && <div>
      <label>Approved case number <input value={caseNumber} onChange={e => setCaseNumber(e.target.value)} /></label>{" "}
      <label>Approved title <input value={caseTitle} onChange={e => setCaseTitle(e.target.value)} /></label>{" "}
      <label>Approved court <input value={courtName} onChange={e => setCourtName(e.target.value)} /></label>
    </div>}
    {action === "LinkToExistingCase" && <div>
      <label>Search existing cases <input value={search} onChange={e => setSearch(e.target.value)} /></label>{" "}
      <button onClick={findCases} disabled={!search.trim()}>Search</button>
      {matches.map(item => <div key={item.id}>
        <button onClick={() => setExisting(item)}>Select</button> {item.caseNumber} · {item.caseTitle} · {item.courtName} · {item.currentStatus} · NDOH {item.authoritativeNextDate || "—"}
      </div>)}
      {existing && <div style={{ border: "1px solid currentColor", padding: 8 }}>
        <strong>Compare before confirming</strong>
        <p>Excel: {row.rawCaseNumber} · {row.rawCaseTitle} · {row.rawCourt} · {row.rawStatus} · NDOH {row.parsedNdoh || "—"}</p>
        <p>Selected: {existing.caseNumber} · {existing.caseTitle} · {existing.courtName} · {existing.currentStatus} · NDOH {existing.authoritativeNextDate || "—"}</p>
        <p>Case number, court and title will not be overwritten.</p>
        <label><input type="checkbox" checked={applyStatus} onChange={e => setApplyStatus(e.target.checked)} /> Apply approved status to existing case</label>
        {row.parsedNdoh && <label>NDOH decision <select value={ndohAction} onChange={e => setNdohAction(e.target.value)}>
          <option value="">Choose explicitly</option>
          <option value="KeepExisting">Keep existing NDOH</option>
          <option value="UseImported">Use imported NDOH</option>
        </select></label>}
      </div>}
    </div>}
    {(action === "ImportAsNewCase" || applyStatus) && <div>
      <label>Canonical status <select value={approvedStatus} onChange={e => setApprovedStatus(e.target.value)}>
        <option value="">Choose status</option><option value="Pending">Pending</option><option value="Disposed">Disposed</option>
      </select></label>
    </div>}
    <div><label>Reviewer notes <textarea value={notes} onChange={e => setNotes(e.target.value)} /></label></div>
    {error && <p role="alert">{error}</p>}
    <button className="primary-button" onClick={submit}>Save reviewed decision</button>{" "}
    <button onClick={close}>Cancel</button>
  </section>;
}
