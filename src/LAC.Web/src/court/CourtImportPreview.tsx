import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";

type Batch = {
  id: string; status: string; totalRows: number; validRows: number;
  needsReviewRows: number; conflictRows: number; invalidRows: number;
  failureMessage?: string | null;
};
type Row = {
  id: string; sourceRowNumber: number; sourceSerialNumberRaw?: string;
  rawCaseNumber?: string; rawCaseTitle?: string; rawCourt?: string;
  rawStatus?: string; suggestedStatusClass?: string; rawNdoh?: string;
  parsedNdoh?: string; rawAdvocate?: string; rawVillage?: string;
  rawAwardNumber?: string; lastOrderLinkState?: string; rowStatus: string;
  validationIssuesJson: string;
};
type Page = { items: Row[]; totalCount: number };
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
  }, [batchId, page, status]);

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
    <p>Preview only — no Court records have been changed.</p>
    <input type="file" accept=".xlsx" onChange={e => setFile(e.target.files?.[0] || null)} />
    <button className="primary-button" disabled={!file} onClick={upload}>Upload and stage workbook</button>
    {error && <p role="alert">{error}</p>}
  </main>;

  const pages = Math.max(1, Math.ceil(totalRows / pageSize));
  return <main className="court-directory-container">
    <Link to="/court-cases">← Court directory</Link><h1>Court import preview</h1>
    <p>Preview only — no Court records have been changed.</p>
    {batch && <p>Total {batch.totalRows} · New/exact {batch.validRows} · Review {batch.needsReviewRows} · Conflicts {batch.conflictRows} · Invalid {batch.invalidRows}</p>}
    {batch?.failureMessage && <p role="alert">{batch.failureMessage}</p>}
    {error && <p role="alert">{error}</p>}
    <label>Classification <select value={status} onChange={e => { setStatus(e.target.value); setPage(1); }}>
      <option value="">All classifications</option>
      {["NewCandidate", "ExistingExact", "PotentialDuplicate", "IdentityConflict", "NeedsReview", "Invalid"].map(x => <option key={x}>{x}</option>)}
    </select></label>
    <div className="court-table-wrap"><table><thead><tr>
      <th>Source row / Sr. No.</th><th>Case</th><th>Court</th><th>Status</th><th>NDOH</th>
      <th>Advocate</th><th>Village / Award</th><th>Order link</th><th>Classification / issues</th>
    </tr></thead><tbody>{rows.map(row => {
      let issues: string[] = [];
      try { issues = JSON.parse(row.validationIssuesJson) as string[]; } catch { issues = ["Issues could not be displayed."]; }
      return <tr key={row.id}>
        <td>{row.sourceRowNumber}<br />{row.sourceSerialNumberRaw}</td>
        <td>{row.rawCaseNumber}<br />{row.rawCaseTitle}</td><td>{row.rawCourt}</td>
        <td>{row.rawStatus}<br />{row.suggestedStatusClass}</td><td>{row.rawNdoh}<br />{row.parsedNdoh}</td>
        <td>{row.rawAdvocate}</td><td>{row.rawVillage}<br />{row.rawAwardNumber}</td>
        <td>{row.lastOrderLinkState}</td><td>{row.rowStatus}{issues.map((issue, index) => <div key={index}>{issue}</div>)}</td>
      </tr>;
    })}</tbody></table></div>
    <nav aria-label="Import preview pages" style={{ display: "flex", gap: 12, alignItems: "center", marginTop: 16 }}>
      <button className="secondary-button" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
      <span>Page {page} of {pages} · {totalRows} rows</span>
      <button className="secondary-button" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
    </nav>
  </main>;
}
