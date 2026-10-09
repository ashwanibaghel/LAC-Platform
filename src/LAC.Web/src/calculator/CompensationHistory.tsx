import React, { useEffect, useState } from "react";
import type { CompensationFormState, CompensationResponse } from "./compensationContracts";
import { INITIAL_COMPENSATION_FORM_STATE } from "./compensationContracts";
import { formatInr } from "./compensationFormatters";
export interface SavedCalculation {
  id: string; title: string | null; createdAt: string; originalAreaNotation: string;
  calculatorVersion: string; conversionVersion: string; saved: boolean;
  inputs: CompensationFormState; request: unknown; response: CompensationResponse;
}
type Row = Pick<SavedCalculation, "id" | "title" | "createdAt" | "originalAreaNotation"> & { marketRate: string; finalAmount: string; summary: string };
export function CompensationHistory({ onUse }: { onUse: (inputs: CompensationFormState) => void }) {
  const [search, setSearch] = useState(""); const [page, setPage] = useState(1);
  const [items, setItems] = useState<Row[]>([]); const [total, setTotal] = useState(0);
  const [detail, setDetail] = useState<SavedCalculation | null>(null);
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState(""); const [revision, setRevision] = useState(0);
  useEffect(() => {
    const abort = new AbortController(); setBusy(true); setError(""); setItems([]);
    fetch(`/api/calculators/compensation/history?page=${page}&pageSize=10&search=${encodeURIComponent(search)}`, { signal: abort.signal })
      .then(async response => { if (!response.ok) throw new Error(response.status === 401 ? "Please sign in to view your history." : "History could not be loaded. Your saved records remain in the database."); return response.json(); })
      .then(data => { setItems(data.items); setTotal(data.total); })
      .catch(e => { if (!abort.signal.aborted) setError(e.message); })
      .finally(() => { if (!abort.signal.aborted) setBusy(false); });
    return () => abort.abort();
  }, [search, page, revision]);
  async function open(id: string) {
    setBusy(true); setError("");
    try { const response = await fetch(`/api/calculators/compensation/history/${id}`); if (!response.ok) throw new Error("Saved calculation could not be opened."); const saved: SavedCalculation = await response.json(); setDetail(saved); setTitle(saved.title ?? ""); }
    catch(e) { setError(e instanceof Error ? e.message : "Unable to open history."); } finally { setBusy(false); }
  }
  async function rename(e: React.FormEvent) {
    e.preventDefault(); if (!detail || busy) return; setBusy(true); setError("");
    try { const response = await fetch(`/api/calculators/compensation/history/${detail.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title }) }); if (!response.ok) throw new Error("Title was not saved. Try again."); const saved = await response.json(); setDetail({ ...detail, title: saved.title }); setRevision(x => x + 1); }
    catch(e) { setError(e instanceof Error ? e.message : "Title was not saved."); } finally { setBusy(false); }
  }
  return <section className="comp-history" aria-label="My compensation history">
    {error && <p className="comp-error-banner" role="alert">{error} <button onClick={() => setRevision(x => x + 1)}>Retry history</button></p>}
    {detail ? <>
      <div className="comp-history-heading"><button onClick={() => setDetail(null)}>← Back to My History</button><button disabled={busy} onClick={() => onUse(detail.inputs)}>Use as New Calculation</button></div>
      <h3>{detail.title || "Saved calculation"}</h3><p>{new Date(detail.createdAt).toLocaleString("en-IN")} · {detail.originalAreaNotation}</p>
      <form onSubmit={rename} className="comp-history-rename"><label>History title<input aria-label="History title" maxLength={200} value={title} onChange={e => setTitle(e.target.value)} /></label><button disabled={busy}>Rename</button></form>
      <div className="comp-final-card"><span className="comp-final-tag">Originally saved final compensation</span><strong className="comp-final-amount">{formatInr(detail.response.finalCompensation)}</strong><small className="comp-final-words">{detail.response.finalAmountInWords}</small></div>
      <p>Saved snapshot · {detail.calculatorVersion} · {detail.conversionVersion}. This result has not been recomputed.</p>
      <dl className="comp-history-facts">{Object.keys(INITIAL_COMPENSATION_FORM_STATE).map(key => <div key={key}><dt>{key.replace(/([A-Z])/g," $1")}</dt><dd>{String(detail.inputs[key as keyof CompensationFormState]) || "—"}</dd></div>)}</dl>
      <h4>Calculation breakdown & trace</h4>{detail.response.trace.map((step,index) => <div className="comp-history-step" key={index}><strong>{step.name}</strong><p>{step.formula}</p><code>{step.substitutedFormula}</code><b>{step.result}</b></div>)}
      <p>Canonical converted area: {detail.response.area.profileConvertedArea} {detail.response.area.rateUnit}. Applied area: {detail.response.area.appliedArea} {detail.response.area.rateUnit}{detail.response.area.usesExplicitEquivalentArea ? " (official equivalent entered)" : ""}.</p>
      <p>{detail.response.roundingPolicy}</p>
      <details><summary>Full saved response and normalized request</summary><pre>{JSON.stringify({ inputs: detail.inputs, request: detail.request, response: detail.response },null,2)}</pre></details>
    </> : <>
      <div className="comp-history-heading"><h3>My History <small>{total} calculations</small></h3><label>Search by title<input type="search" value={search} onChange={e => { setSearch(e.target.value); setPage(1); }} /></label></div>
      {busy && <p role="status">Loading history…</p>}
      {!busy && !error && !items.length && <p>No saved calculations yet. Calculate to save your first entry.</p>}
      <div className="comp-history-list">{items.map(row => <article key={row.id}><div><h4>{row.title || "Compensation calculation"}</h4><time>{new Date(row.createdAt).toLocaleString("en-IN")}</time><p>{row.originalAreaNotation} · Market rate {row.marketRate}</p><small>{row.summary}</small></div><div><strong>{formatInr({ precise: row.finalAmount, display: row.finalAmount })}</strong><button disabled={busy} onClick={() => void open(row.id)}>View Details</button></div></article>)}</div>
      <div className="comp-history-pagination"><button disabled={busy || page <= 1} onClick={() => setPage(x => x - 1)}>Previous</button><span>Page {page}</span><button disabled={busy || page * 10 >= total} onClick={() => setPage(x => x + 1)}>Next</button></div>
    </>}
  </section>;
}
