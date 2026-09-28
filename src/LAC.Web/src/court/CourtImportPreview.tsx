import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { CourtImportRowReview } from "./CourtImportRowReview";

type Batch = {
  id: string; status: string; totalRows: number; validRows: number;
  needsReviewRows: number; conflictRows: number; invalidRows: number;
  failureMessage?: string | null;
};
export type Row = {
  id: string; sourceRowNumber: number; sourceSerialNumberRaw?: string;
  rawCaseNumber?: string; rawCaseTitle?: string; rawCourt?: string;
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

export function CourtImportPreview() {
  const { batchId } = useParams();
  const [batch, setBatch] = useState<Batch | null>(null);
  const [rows, setRows] = useState<Row[]>([]);
  const [totalRows, setTotalRows] = useState(0);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState("");
  const [file, setFile] = useState<File | null>(null);
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
    if (!file) return;
    setError("");
    const form = new FormData(); form.append("file", file);
    try {
      const response = await fetch("/api/court-cases/imports", { method: "POST", credentials: "include", body: form });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error || "Upload failed.");
      location.assign(`/court-cases/imports/${result.id}`);
    } catch (e) { setError(e instanceof Error ? e.message : "Upload failed."); }
  };

  if (!batchId) return <main className="court-directory-container">
    <Link to="/court-cases">← Court directory</Link><h1>Import Court workbook</h1>
    <p>Upload stages the workbook. Court records change only after review and commit.</p>
    <input type="file" accept=".xlsx" onChange={e => setFile(e.target.files?.[0] || null)} />
    <button className="primary-button" disabled={!file} onClick={upload}>Upload and stage workbook</button>
    {error && <p role="alert">{error}</p>}
  </main>;

  const pages = Math.max(1, Math.ceil(totalRows / pageSize));
  return <main className="court-directory-container">
    <Link to="/court-cases">← Court directory</Link><h1>Court import review</h1>
    <p>Original Excel rows remain preserved. Only approved decisions can be committed.</p>
    {batch && <p>Total {batch.totalRows} · New/exact {batch.validRows} · Review {batch.needsReviewRows} · Conflicts {batch.conflictRows} · Invalid {batch.invalidRows}</p>}
    {summary && <section>
      <p>Unresolved {summary.unresolved} · Ready {summary.ready} · Committed {summary.committed} · Skipped {summary.skipped} · Failed {summary.failed}</p>
      <button className="secondary-button" disabled={!summary.safeBulkCandidates} onClick={approveSafe}>Approve safe new candidates ({summary.safeBulkCandidates})</button>{" "}
      <button className="primary-button" disabled={!summary.ready} onClick={commitApproved}>Commit approved rows ({summary.ready})</button>
    </section>}
    {batch?.failureMessage && <p role="alert">{batch.failureMessage}</p>}
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
    <label>Classification <select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }}>
      <option value="">All classifications</option>
      {["NewCandidate", "ExistingExact", "PotentialDuplicate", "IdentityConflict", "NeedsReview", "Invalid"].map(x => <option key={x}>{x}</option>)}
    </select></label>
    <div className="court-table-wrap"><table><thead><tr>
      <th>Source row / Sr. No.</th><th>Case</th><th>Court</th><th>Status</th><th>NDOH</th>
      <th>Advocate</th><th>Village / Award</th><th>Order link</th><th>Classification / issues</th><th>Review</th>
    </tr></thead><tbody>{rows.map(row => {
      let issues: string[] = [];
      try { issues = JSON.parse(row.validationIssuesJson) as string[]; } catch { issues = ["Issues could not be displayed."]; }
      return <tr key={row.id}>
        <td>{row.sourceRowNumber}<br />{row.sourceSerialNumberRaw}</td>
        <td>{row.rawCaseNumber}<br />{row.rawCaseTitle}</td><td>{row.rawCourt}</td>
        <td>{row.rawStatus}<br />{row.suggestedStatusClass}</td><td>{row.rawNdoh}<br />{row.parsedNdoh}</td>
        <td>{row.rawAdvocate}</td><td>{row.rawVillage}<br />{row.rawAwardNumber}</td>
        <td>{row.lastOrderLinkState}</td><td>{row.rowStatus}{issues.map((issue, index) => <div key={index}>{issue}</div>)}</td>
        <td>{row.resolutionAction || "Unresolved"} · {row.commitStatus}
          {row.committedCourtCaseId && <div><Link to={"/court-cases/" + row.committedCourtCaseId}>Open case</Link></div>}
          {row.commitError && <div role="alert">{row.commitError}</div>}
          {summary && row.commitStatus !== "Committed" && <>
            <div><button onClick={() => setSelected(row)}>Review decision</button></div>
            {row.resolutionAction && <div><button onClick={() => clearDecision(row)}>Clear decision</button></div>}
          </>}
        </td>
      </tr>;
    })}</tbody></table></div>
    <nav aria-label="Import preview pages" style={{ display: "flex", gap: 12, alignItems: "center", marginTop: 16 }}>
      <button className="secondary-button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
      <span>Page {page} of {pages} · {totalRows} rows</span>
      <button className="secondary-button" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
    </nav>
    {selected && batchId && <CourtImportRowReview
      key={selected.id} row={selected}
      save={async decision => {
        await mutate("/api/court-cases/imports/" + batchId + "/rows/" + selected.id + "/decision", "PUT", decision);
        setNotice("Decision saved for source row " + selected.sourceRowNumber + ".");
        setSelected(null);
      }}
      close={() => setSelected(null)}
    />}
  </main>;
}
