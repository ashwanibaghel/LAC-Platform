import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { CourtImportRowReview } from "./CourtImportRowReview";
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
type ReviewSummary = { unresolved: number; ready: number; committed: number; skipped: number; failed: number; safeBulkCandidates: number };
const pageSize = 25;
const classifications = [
  ["", "All rows"], ["NewCandidate", "New candidates"], ["NeedsReview", "Needs review"],
  ["PotentialDuplicate", "Duplicates"], ["IdentityConflict", "Conflicts"],
  ["ExistingExact", "Existing matches"], ["Invalid", "Invalid"],
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
  const [file, setFile] = useState<File | null>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [summary, setSummary] = useState<ReviewSummary | null>(null);
  const [selected, setSelected] = useState<Row | null>(null);

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
    fetch(`/api/court-cases/imports/${batchId}/rows?${query}`, { credentials: "include", signal: controller.signal })
      .then(async r => { if (!r.ok) throw new Error("Import rows could not be loaded."); return r.json() as Promise<Page>; })
      .then(data => { setRows(data.items); setTotalRows(data.totalCount); })
      .catch(e => { if (e.name !== "AbortError") setError(e.message); });
    return () => controller.abort();
  }, [batchId, page, status, refresh]);

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

  const approveSafe = async () => {
    if (!batchId || !summary?.safeBulkCandidates) return;
    if (!confirm("Approve only " + summary.safeBulkCandidates + " deterministic NewCandidate rows? Other rows remain unresolved.")) return;
    try {
      await mutate("/api/court-cases/imports/" + batchId + "/approve-safe", "POST");
      setNotice("Safe rows approved; no Court records created yet.");
    } catch (e) { setError(e instanceof Error ? e.message : "Approval failed."); }
  };

  const commitApproved = async () => {
    if (!batchId || !summary?.ready) return;
    if (!confirm("Only " + summary.ready + " reviewed/approved rows will be written to Court records. Unresolved rows remain staging. Continue?")) return;
    try {
      const result = await mutate("/api/court-cases/imports/" + batchId + "/commit", "POST");
      setNotice(result.committedThisRun + " row(s) committed; " + (result.failures?.length || 0) + " failed.");
    } catch (e) { setError(e instanceof Error ? e.message : "Commit failed."); }
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
      <h1>Import Court workbook</h1>
      <p>Bring cases from an Excel register into a review queue. Nothing is added to Court Matters until you approve and commit the rows.</p>
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
          <button className="primary-button" type="button" disabled={!file || uploading} onClick={() => void upload()}>{uploading ? "Uploading workbook…" : "Upload and stage workbook →"}</button>
        </div>
      </section>
      <aside className="court-import-guide" aria-label="Import process">
        <h2>What happens next</h2>
        <ol><li><span>1</span><div><strong>Upload</strong><p>Read the workbook into a staging area.</p></div></li>
          <li><span>2</span><div><strong>Review</strong><p>Check matches, conflicts and rows needing decisions.</p></div></li>
          <li><span>3</span><div><strong>Commit</strong><p>Only approved rows update Court Matters.</p></div></li></ol>
        <div className="court-import-safety"><strong>Your register stays unchanged</strong><p>The uploaded rows are preserved for review. Uploading alone does not create or change a Court case.</p></div>
      </aside>
    </div>
  </main>;

  const pages = Math.max(1, Math.ceil(totalRows / pageSize));
  return <main className="court-import-page court-import-review-page">
    <Link className="court-import-back" to="/court-cases">← Court Matters</Link>
    <header className="court-import-header"><span className="court-import-eyebrow">COURT REGISTER</span><h1>Review imported rows</h1>
      <p>Original Excel rows remain preserved. Only approved decisions can be committed to Court Matters.</p></header>
    {batch && <div className="court-import-batch-stats" aria-label="Workbook classification summary">
      <span><strong>{batch.totalRows}</strong>Total rows</span><span><strong>{batch.validRows}</strong>New / exact</span>
      <span><strong>{batch.needsReviewRows}</strong>Needs review</span><span><strong>{batch.conflictRows}</strong>Conflicts</span>
      <span><strong>{batch.invalidRows}</strong>Invalid</span></div>}
    {summary && <section className="court-import-next-step" aria-label="Recommended next action">
      <div className="court-import-next-step-copy"><span className="court-import-eyebrow">NEXT ACTION</span>
        <h2>{summary.safeBulkCandidates > 0 ? `Approve ${summary.safeBulkCandidates} safe new-case candidates` :
          summary.ready > 0 ? `Commit ${summary.ready} approved rows` : summary.unresolved > 0 ? "Review rows that need a decision" : "Import review complete"}</h2>
        <p>{summary.safeBulkCandidates > 0 ? "These are deterministic new-case matches. Approval prepares them for commit; it does not change Court records yet." :
          summary.ready > 0 ? "Only reviewed and approved rows will be written to Court Matters. Unresolved rows stay in staging." :
          summary.unresolved > 0 ? "Open a row below, compare its source details, and choose how it should be handled." :
          "No unresolved rows remain in this batch."}</p>
        <div className="court-import-progress-text">{summary.unresolved} unresolved <span>·</span> {summary.ready} ready to commit <span>·</span> {summary.committed} committed</div>
      </div>
      <div className="court-import-next-step-actions">
        {summary.safeBulkCandidates > 0 && <button className="primary-button" onClick={approveSafe}>Approve safe rows ({summary.safeBulkCandidates})</button>}
        {summary.ready > 0 && <button className={summary.safeBulkCandidates > 0 ? "secondary-button" : "primary-button"} onClick={commitApproved}>Commit approved ({summary.ready})</button>}
        {summary.safeBulkCandidates === 0 && summary.ready === 0 && summary.unresolved > 0 &&
          <button className="primary-button" onClick={() => document.getElementById("court-import-rows")?.scrollIntoView({ behavior: "smooth" })}>Review rows below ↓</button>}
      </div>
    </section>}
    {batch?.failureMessage && <p className="court-import-error" role="alert">{batch.failureMessage}</p>}
    {error && <p className="court-import-error" role="alert">{error}</p>}
    {notice && <p className="court-import-notice" role="status">{notice}</p>}
    <div className="court-import-review-toolbar" id="court-import-rows"><h2>Workbook rows <small>{totalRows.toLocaleString("en-IN")} in {classificationLabel(status).toLowerCase()}</small></h2>
      <span>Choose a row to inspect its full source details and record a decision.</span></div>
    <nav className="court-import-classification-tabs" aria-label="Import row classifications">
      {classifications.map(([key, label]) => <button type="button" key={key || "all"} className={status === key ? "active" : ""}
        aria-current={status === key ? "page" : undefined} onClick={() => { setStatus(key); setPage(1); }}>{label}</button>)}
    </nav>
    <div className="court-table-wrap court-import-table-wrap"><table className="court-import-review-table"><thead><tr>
      <th>Excel row</th><th>Case from workbook</th><th>Key details</th><th>Classification</th><th>Decision</th>
    </tr></thead><tbody>{rows.length === 0 ? <tr><td colSpan={5} className="court-import-no-rows">No rows in this classification.</td></tr> : rows.map(row => {
      let issues: string[] = [];
      try { issues = JSON.parse(row.validationIssuesJson) as string[]; } catch { issues = ["Issues could not be displayed."]; }
      return <tr key={row.id}>
        <td className="court-import-row-number"><strong>#{row.sourceRowNumber}</strong><small>Sr. {row.sourceSerialNumberRaw || "—"}</small></td>
        <td className="court-import-case"><strong>{row.rawCaseNumber || "Case number missing"}</strong>
          <span title={row.rawCaseTitle || ""}>{row.rawCaseTitle || "No party title supplied"}</span></td>
        <td className="court-import-key-details"><strong>{row.suggestedCourtName || row.rawCourt || "Court not identified"}</strong>
          <span>NDOH {row.rawNdoh || "not supplied"} · {row.rawStatus || "Status not supplied"}</span>
          <span>{row.rawVillage || "Village not supplied"}{row.rawAwardNumber ? ` · Award ${row.rawAwardNumber}` : ""}</span></td>
        <td><span className={`court-import-classification court-import-classification-${row.rowStatus.toLowerCase()}`}>{classificationLabel(row.rowStatus)}</span>
          {issues.length > 0 && <span className="court-import-issue" title={issues.join(" · ")}>{issues[0]}{issues.length > 1 ? ` +${issues.length - 1} more` : ""}</span>}</td>
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
  </main>;
}
