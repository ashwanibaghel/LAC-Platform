import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { CourtImportRowReview } from "./CourtImportRowReview";
import { courtImportReviewGuidance } from "./courtImportReviewGuidance";
import "./court.css";

type Batch = {
  id: string; status: string; totalRows: number; validRows: number;
  needsReviewRows: number; conflictRows: number; invalidRows: number;
  failureMessage?: string | null;
};
export type Row = {
  id: string; sourceRowNumber: number; sourceSerialNumberRaw?: string;
  rawCaseNumber?: string; rawCaseTitle?: string; rawCourt?: string; suggestedCourtName?: string;
  rawStatus?: string; suggestedStatusClass?: string; rawNdoh?: string;
  parsedNdoh?: string; rawAdvocate?: string; rawVillage?: string;
  rawAwardNumber?: string; lastOrderLinkState?: string; rowStatus: string;
  validationIssuesJson: string; rawDirections?: string; rawBriefFacts?: string;
  rawLastOrderLink?: string; resolutionAction?: string; resolvedCourtCaseId?: string;
  approvedCaseNumber?: string; approvedCaseTitle?: string; approvedCourtName?: string;
  approvedStatus?: string; applyStatusToExisting: boolean; ndohAction?: string;
  reviewerNotes?: string; commitStatus: string; committedCourtCaseId?: string;
  commitError?: string;
};
type Page = { items: Row[]; totalCount: number };
type ReviewSummary = { unresolved: number; ready: number; committed: number; skipped: number; failed: number; safeBulkCandidates: number; retryableSafe: number };
const pageSize = 25;
const classifications = [
  ["", "All records"], ["NewCandidate", "Ready to add"], ["NeedsReview", "Needs attention"],
  ["PotentialDuplicate", "Possible duplicates"], ["IdentityConflict", "Conflicting details"],
  ["ExistingExact", "Already added"], ["Invalid", "Cannot add"],
] as const;
const classificationLabel = (value: string) => classifications.find(([key]) => key === value)?.[1] ?? value;
const decisionLabel = (value?: string) => value === "ImportAsNewCase" ? "Approved as new" :
  value === "LinkToExistingCase" ? "Linked to existing" : value === "Skip" ? "Skipped" : "Awaiting decision";

export function CourtImportPreview() {
  const { batchId } = useParams();
  const [batch, setBatch] = useState<Batch | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [totalRows, setTotalRows] = useState(0);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const [workState, setWorkState] = useState<"pending" | "committed" | "all">("pending");
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [summary, setSummary] = useState<ReviewSummary | null>(null);
  const [selected, setSelected] = useState<Row | null>(null);
  const [confirmation, setConfirmation] = useState<"addReady" | "commitApproved" | null>(null);
  const [confirmationBusy, setConfirmationBusy] = useState(false);
  const [commitAcknowledged, setCommitAcknowledged] = useState(false);

  useEffect(() => {
    if (!batchId) return;
    const controller = new AbortController();
    fetch(`/api/court-cases/imports/${batchId}`, { credentials: "include", signal: controller.signal })
      .then(async r => { if (!r.ok) throw new Error("Import batch could not be loaded."); return r.json() as Promise<Batch>; })
      .then(setBatch).catch(e => { if (e.name !== "AbortError") setError(e.message); });
    return () => controller.abort();
  }, [batchId]);

  useEffect(() => {
    if (!batchId) return;
    const controller = new AbortController();
    const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
    if (status) query.set("rowStatus", status);
    query.set("workState", workState);
    fetch(`/api/court-cases/imports/${batchId}/rows?${query}`, { credentials: "include", signal: controller.signal })
      .then(async r => { if (!r.ok) throw new Error("Import rows could not be loaded."); return r.json() as Promise<Page>; })
      .then(data => { setRows(data.items); setTotalRows(data.totalCount); })
      .catch(e => { if (e.name !== "AbortError") setError(e.message); });
    return () => controller.abort();
  }, [batchId, page, status, workState, refresh]);

  useEffect(() => {
    if (!batchId) return;
    const controller = new AbortController();
    fetch("/api/court-cases/imports/" + batchId + "/review-summary", { credentials: "include", signal: controller.signal })
      .then(async r => r.ok ? await r.json() as ReviewSummary : null)
      .then(setSummary).catch(e => { if (e.name !== "AbortError") setError("Review counts could not be loaded."); });
    return () => controller.abort();
  }, [batchId, refresh]);

  const mutate = async (url: string, method: string, body?: object) => {
    setError(""); setNotice("");
    const response = await fetch(url, {
      method, credentials: "include", headers: body ? { "Content-Type": "application/json" } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || result.title || "Review action failed.");
    setRefresh(x => x + 1);
    return result;
  };

  const addReady = async () => {
    if (!batchId || !(summary?.safeBulkCandidates || summary?.retryableSafe)) return;
    setConfirmationBusy(true);
    try {
      const result = await mutate("/api/court-cases/imports/" + batchId + "/add-ready", "POST");
      setNotice(`${result.committedThisRun} Court Matters added. ${result.summary.unresolved + result.summary.failed} records still need attention.`);
      if (result.failures?.length) setError(`${result.failures.length} ready records could not be added. You can retry them here.`);
      setConfirmation(null);
    } catch (e) { setError(e instanceof Error ? e.message : "Could not add ready Court Matters."); }
    finally { setConfirmationBusy(false); }
  };

  const commitApproved = async () => {
    if (!batchId || !summary?.ready) return;
    setConfirmationBusy(true);
    try {
      const result = await mutate("/api/court-cases/imports/" + batchId + "/commit", "POST");
      setNotice(result.committedThisRun + " row(s) committed; " + (result.failures?.length || 0) + " failed.");
      setConfirmation(null);
    } catch (e) { setError(e instanceof Error ? e.message : "Commit failed."); }
    finally { setConfirmationBusy(false); }
  };

  const clearDecision = async (row: Row) => {
    if (!batchId) return;
    try {
      await mutate("/api/court-cases/imports/" + batchId + "/rows/" + row.id + "/decision", "DELETE");
      setNotice("Source row " + row.sourceRowNumber + " reopened.");
      setSelected(null);
    } catch (e) { setError(e instanceof Error ? e.message : "Reopen failed."); }
  };

  const upload = async () => {
    if (!file || uploading) return;
    setError("");
    setUploading(true);
    const form = new FormData(); form.append("file", file);
    try {
      const response = await fetch("/api/court-cases/imports", { method: "POST", credentials: "include", body: form });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || "Upload failed.");
      location.assign(`/court-cases/imports/${result.id}`);
    } catch (e) { setError(e instanceof Error ? e.message : "Upload failed."); }
    finally { setUploading(false); }
  };

  if (!batchId) return <main className="court-import-page">
    <Link className="court-import-back" to="/court-cases">← Court Matters</Link>
    <header className="court-import-header">
      <span className="court-import-eyebrow">COURT REGISTER</span>
      <h1>Import Court Register</h1>
      <p>Select the existing LAC court Excel workbook. We will check it before anything is added.</p>
    </header>
    <div className="court-import-layout">
      <section className="court-import-upload" aria-labelledby="court-import-upload-title">
        <div className="court-import-upload-heading"><span className="court-import-file-icon" aria-hidden="true">XLSX</span><div>
          <h2 id="court-import-upload-title">Select a workbook</h2><p>Choose an Excel <strong>.xlsx</strong> file from your computer.</p>
        </div></div>
        <div className="court-import-file-control">
          <input id="court-import-file" type="file" accept=".xlsx" onChange={e => setFile(e.target.files?.[0] || null)} />
          <label className="court-import-choose" htmlFor="court-import-file">Choose Excel file</label>
          <div className="court-import-file-name" aria-live="polite">{file ? <><strong>{file.name}</strong><span>{(file.size / 1024).toLocaleString("en-IN", { maximumFractionDigits: 0 })} KB</span></> : <span>No file selected</span>}</div>
        </div>
        {error && <div className="court-import-error" role="alert">{error}</div>}
        <div className="court-import-upload-footer">
          <span>Only .xlsx workbooks are accepted.</span>
          <button className="primary-button" type="button" disabled={!file || uploading} onClick={() => void upload()}>{uploading ? "Checking Excel…" : "Check Excel →"}</button>
        </div>
      </section>
      <aside className="court-import-guide" aria-label="Import process">
        <h2>Three simple steps</h2>
        <ol><li><span>1</span><div><strong>Choose Excel</strong><p>Select your court register.</p></div></li>
          <li><span>2</span><div><strong>We check it</strong><p>Ready cases are separated from records needing attention.</p></div></li>
          <li><span>3</span><div><strong>Add ready cases</strong><p>You decide when to add them.</p></div></li></ol>
        <div className="court-import-safety"><strong>Your Excel file stays unchanged</strong><p>Records needing attention can be reviewed later.</p></div>
      </aside>
    </div>
  </main>;

  const pages = Math.max(1, Math.ceil(totalRows / pageSize));
  const safeActionCount = (summary?.safeBulkCandidates ?? 0) + (summary?.retryableSafe ?? 0);
  return <main className="court-import-page court-import-review-page">
    <Link className="court-import-back" to="/court-cases">← Court Matters</Link>
    <header className="court-import-header"><span className="court-import-eyebrow">COURT REGISTER</span><h1>Excel check results</h1>
      <p>Add the ready Court Matters now. Records needing attention can be reviewed later.</p></header>
    {batch && summary && <div className="court-import-batch-stats" aria-label="Import progress summary">
      <span><strong>{batch.totalRows - summary.committed}</strong>Still to check</span>
      <span><strong>{summary.committed}</strong>Added to Court Matters</span>
      <span><strong>{batch.totalRows}</strong>Records found</span></div>}
    {summary && <section className="court-import-next-step" aria-label="Recommended next action">
      <div className="court-import-next-step-copy"><span className="court-import-eyebrow">NEXT ACTION</span>
        <h2>{safeActionCount > 0 ? `${safeActionCount} Court Matters are ready to add` :
          summary.ready > 0 ? `${summary.ready} reviewed records are ready to add` : summary.unresolved > 0 ? "Review remaining records when convenient" : "Excel check complete"}</h2>
        <p>{safeActionCount > 0 ? `${summary.unresolved - summary.safeBulkCandidates} records need attention later. They will not be added by this action.` :
          summary.ready > 0 ? "Only your reviewed records will be added. Other records remain untouched." :
          summary.unresolved > 0 ? "You can use Court Matters now and return to these records later." :
          "No unresolved rows remain in this batch."}</p>
        <div className="court-import-progress-text">{summary.unresolved} still to check <span>·</span> {summary.committed} already added</div>
      </div>
      <div className="court-import-next-step-actions">
        {safeActionCount > 0 && <button className="primary-button" onClick={() => { setError(""); setConfirmation("addReady"); }}>Add {safeActionCount} Court Matters</button>}
        {safeActionCount > 0 && summary.unresolved > summary.safeBulkCandidates &&
          <button className="secondary-button" onClick={() => document.getElementById("court-import-rows")?.scrollIntoView({ behavior: "smooth" })}>Review {summary.unresolved - summary.safeBulkCandidates} later</button>}
        {summary.ready > 0 && <button className={safeActionCount > 0 ? "secondary-button" : "primary-button"} onClick={() => { setError(""); setCommitAcknowledged(false); setConfirmation("commitApproved"); }}>Add {summary.ready} reviewed records</button>}
        {safeActionCount === 0 && summary.ready === 0 && summary.unresolved > 0 &&
          <button className="primary-button" onClick={() => document.getElementById("court-import-rows")?.scrollIntoView({ behavior: "smooth" })}>Review rows below ↓</button>}
      </div>
    </section>}
    {batch?.failureMessage && <p className="court-import-error" role="alert">{batch.failureMessage}</p>}
    {error && <p className="court-import-error" role="alert">{error}</p>}
    {notice && <div className="court-import-notice" role="status"><p>{notice}</p>
      <div><Link className="primary-button" to="/court-cases">Open Court Matters</Link>
        {summary && summary.unresolved > 0 && <button className="secondary-button" onClick={() => document.getElementById("court-import-rows")?.scrollIntoView({ behavior: "smooth" })}>Review remaining later</button>}</div>
    </div>}
    <div className="court-import-review-toolbar" id="court-import-rows"><h2>{workState === "pending" ? "Pending work" : workState === "committed" ? "Imported history" : "All source rows"} <small>{totalRows.toLocaleString("en-IN")} in {classificationLabel(status).toLowerCase()}</small></h2>
      <span>{workState === "pending" ? "Committed rows are hidden from this working queue." : "Original workbook rows are preserved for audit."}</span></div>
    <nav className="court-import-work-tabs" aria-label="Import work status">
      {([ ["pending", "Pending work"], ["committed", "Imported history"], ["all", "All source rows"] ] as const).map(([key, label]) =>
        <button type="button" key={key} className={workState === key ? "active" : ""} aria-current={workState === key ? "page" : undefined}
          onClick={() => { setWorkState(key); setPage(1); }}>{label}{key === "pending" && batch && summary ? ` (${batch.totalRows - summary.committed})` : key === "committed" && summary ? ` (${summary.committed})` : ""}</button>)}
    </nav>
    <nav className="court-import-classification-tabs" aria-label="Import row classifications">
      {classifications.map(([key, label]) => <button type="button" key={key || "all"} className={status === key ? "active" : ""}
        aria-current={status === key ? "page" : undefined} onClick={() => { setStatus(key); setPage(1); }}>{label}</button>)}
    </nav>
    <div className="court-table-wrap court-import-table-wrap"><table className="court-import-review-table"><thead><tr>
      <th>No.</th><th>Case from workbook</th><th>Key details</th><th>Classification</th><th>Decision</th>
    </tr></thead><tbody>{rows.length === 0 ? <tr><td colSpan={5} className="court-import-no-rows">{workState === "pending" && !status ? "No pending rows. This import batch is complete." : "No rows in this view."}</td></tr> : rows.map((row, index) => {
      const { reasons } = courtImportReviewGuidance(row);
      return <tr key={row.id}>
        <td className="court-import-row-number"><strong>#{(page - 1) * pageSize + index + 1}</strong>
          <small>Excel row {row.sourceRowNumber}</small>{row.sourceSerialNumberRaw && <small>Sr. {row.sourceSerialNumberRaw}</small>}</td>
        <td className="court-import-case"><strong>{row.rawCaseNumber || "Case number missing"}</strong>
          <span title={row.rawCaseTitle || ""}>{row.rawCaseTitle || "No party title supplied"}</span></td>
        <td className="court-import-key-details"><strong>{row.suggestedCourtName || row.rawCourt || "Court not identified"}</strong>
          <span>NDOH {row.rawNdoh || "not supplied"} · {row.rawStatus || "Status not supplied"}</span>
          <span>{row.rawVillage || "Village not supplied"}{row.rawAwardNumber ? ` · Award ${row.rawAwardNumber}` : ""}</span></td>
        <td><span className={`court-import-classification court-import-classification-${row.rowStatus.toLowerCase()}`}>{classificationLabel(row.rowStatus)}</span>
          {reasons.length > 0 && <span className="court-import-issue" title={reasons.join(" · ")}>{reasons[0]}{reasons.length > 1 ? ` +${reasons.length - 1} more` : ""}</span>}</td>
        <td className="court-import-row-action"><span className={row.resolutionAction ? "decided" : ""}>{decisionLabel(row.resolutionAction)}</span>
          {row.commitStatus === "Committed" ? row.committedCourtCaseId && <Link to={"/court-cases/" + row.committedCourtCaseId}>Open case →</Link> :
            <button type="button" onClick={() => setSelected(row)}>{row.resolutionAction ? "Edit decision" : "Review row"} →</button>}
          {row.commitError && <small role="alert">{row.commitError}</small>}
          {row.resolutionAction && row.commitStatus !== "Committed" && <button type="button" className="court-import-clear-decision" onClick={() => clearDecision(row)}>Clear decision</button>}
        </td>
      </tr>;
    })}</tbody></table></div>
    <nav className="court-import-pagination" aria-label="Import preview pages">
      <button className="secondary-button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
      <span>Page {page} of {pages} · {totalRows} rows</span>
      <button className="secondary-button" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
    </nav>
    {selected && batchId && <div className="court-import-review-overlay" role="presentation">
      <div className="court-import-review-dialog" role="dialog" aria-modal="true" aria-label={`Review Excel row ${selected.sourceRowNumber}`}>
        <CourtImportRowReview key={selected.id} row={selected}
          save={async decision => {
            await mutate("/api/court-cases/imports/" + batchId + "/rows/" + selected.id + "/decision", "PUT", decision);
            setNotice("Decision saved for source row " + selected.sourceRowNumber + ".");
            setSelected(null);
          }}
          close={() => setSelected(null)} />
      </div></div>}
    {confirmation && summary && <div className="court-import-confirm-overlay" role="presentation">
      <section className="court-import-confirm-dialog" role="dialog" aria-modal="true" aria-labelledby="court-import-confirm-title"
        onKeyDown={event => { if (event.key === "Escape" && !confirmationBusy) setConfirmation(null); }}>
        <span className="court-import-eyebrow">ADD COURT MATTERS</span>
        <h2 id="court-import-confirm-title">{confirmation === "addReady"
          ? `Add ${safeActionCount} ready Court Matters?`
          : `Add ${summary.ready} reviewed Court Matters?`}</h2>
        <p>{confirmation === "addReady"
          ? `Only these ready cases will be added. ${summary.unresolved - summary.safeBulkCandidates} records needing attention will stay here for later.`
          : "Only records you have reviewed will be added. Other records stay here for later."}</p>
        {confirmation === "commitApproved" && <label className="court-import-confirm-check"><input type="checkbox" checked={commitAcknowledged}
          onChange={event => setCommitAcknowledged(event.target.checked)} /> I have reviewed these records and want to add them to Court Matters.</label>}
        {error && <div className="court-import-error" role="alert">{error}</div>}
        <div className="court-import-confirm-actions"><button type="button" className="secondary-button" autoFocus disabled={confirmationBusy} onClick={() => setConfirmation(null)}>Cancel</button>
          <button type="button" className="primary-button" disabled={confirmationBusy || confirmation === "commitApproved" && !commitAcknowledged}
            onClick={() => void (confirmation === "addReady" ? addReady() : commitApproved())}>
            {confirmationBusy ? "Adding…" : confirmation === "addReady" ? `Add ${safeActionCount} Court Matters` : `Add ${summary.ready} records`}</button></div>
      </section></div>}
  </main>;
}
