import { useMemo, useState } from "react";
import { BULK_FORMATS, normalizedRowsForExcel, parseBulkRevenue, parseSeparateUnitLists, sumBulkRevenueRows, type BulkFormat } from "./bulkRevenueParser";

type InputMethod = "combined" | "separate";
type BulkTotal = ReturnType<typeof sumBulkRevenueRows>;
const previewLimit = 200;
const units = ["sqm", "sqyd", "sqft", "hectare", "acre", "sqkm"] as const;
const labels: Record<(typeof units)[number], string> = { sqm: "Square Metre", sqyd: "Square Yard / Gaj", sqft: "Square Feet", hectare: "Hectare", acre: "Acre", sqkm: "Square Kilometre" };
const number = (value: number) => value.toLocaleString("en-IN", { maximumFractionDigits: 10 });
const revenueNotation = (area: BulkTotal["revenue"]) => `${area.bigha}-${area.biswa}-${area.biswansi}`;
const revenueFormal = (area: BulkTotal["revenue"]) => `${area.bigha} Bigha ${String(area.biswa).padStart(2, "0")} Biswa ${String(area.biswansi).padStart(2, "0")} Biswansi`;
const copy = async (value: string) => {
  if (navigator.clipboard?.writeText) {
    try { await navigator.clipboard.writeText(value); return; } catch { /* use the local selection fallback */ }
  }
  const field = document.createElement("textarea");
  field.value = value;
  field.style.position = "fixed";
  field.style.left = "-9999px";
  document.body.appendChild(field);
  field.select();
  try { if (!document.execCommand("copy")) throw new Error("Copy is unavailable in this browser."); }
  finally { field.remove(); }
};

export function BulkRevenueSum() {
  const [method, setMethod] = useState<InputMethod>("combined");
  const [format, setFormat] = useState<BulkFormat>("auto");
  const [paste, setPaste] = useState("");
  const [lists, setLists] = useState({ bigha: "", biswa: "", biswansi: "" });
  const [calculated, setCalculated] = useState<{ key: string; total: BulkTotal } | null>(null);
  const [calculationError, setCalculationError] = useState("");
  const sourceKey = JSON.stringify([method, format, paste, lists]);
  const parsed = useMemo(() => method === "combined" ? parseBulkRevenue(paste, format) : parseSeparateUnitLists(lists.bigha, lists.biswa, lists.biswansi), [method, format, paste, lists]);
  const total = calculated?.key === sourceKey ? calculated.total : null;
  const hasReferences = parsed.rows.some(row => row.reference);
  const ready = !parsed.unresolved && parsed.rows.length > 0 && parsed.errorCount === 0;
  const calculate = () => {
    setCalculationError("");
    try { setCalculated({ key: sourceKey, total: sumBulkRevenueRows(parsed) }); }
    catch (error) { setCalculated(null); setCalculationError(error instanceof Error ? error.message : "Unable to calculate."); }
  };
  const clear = () => { setPaste(""); setLists({ bigha: "", biswa: "", biswansi: "" }); setFormat("auto"); setCalculated(null); setCalculationError(""); };
  const copyAll = () => { if (!total) return; const lines = [revenueFormal(total.revenue), revenueNotation(total.revenue), `Records calculated: ${total.records}`, `Total Bigha: ${total.totals.totalBigha}`, `Total Biswa: ${total.totals.totalBiswa}`, `Total Biswansi: ${total.totalBiswansi}`, ...units.map(unit => `${labels[unit]}: ${total.conversions[unit]}`)]; void copy(lines.join("\n")); };
  return <div className="bulk-revenue">
    <div className="calc-entry-tabs bulk-method-tabs" aria-label="Bulk input method">
      <button className={method === "combined" ? "active" : ""} onClick={() => setMethod("combined")}>Combined / Excel Paste</button>
      <button className={method === "separate" ? "active" : ""} onClick={() => setMethod("separate")}>Separate Unit Lists</button>
    </div>
    {method === "combined" ? <div className="bulk-input">
      <label>Format<select value={format} onChange={event => setFormat(event.target.value as BulkFormat)}>{(["auto", "shorthand", "columns3", "columns4", "transposed"] as BulkFormat[]).map(id => <option key={id} value={id}>{BULK_FORMATS[id]}</option>)}</select></label>
      <label>Paste revenue areas<textarea value={paste} onChange={event => setPaste(event.target.value)} rows={5} placeholder={"22-3-2\n11-2-15\n4-16-0"} /></label>
    </div> : <div className="bulk-unit-lists">{(["bigha", "biswa", "biswansi"] as const).map(unit => <label key={unit}>{unit}<textarea rows={4} value={lists[unit]} onChange={event => setLists({ ...lists, [unit]: event.target.value })} placeholder={unit === "bigha" ? "22,11,4,2" : unit === "biswa" ? "3,2,16,9" : "2,15,0,1"} /></label>)}</div>}
    <div className="bulk-preview-summary" aria-live="polite">
      <span><b>Detected:</b> {parsed.unresolved ? "Format needs selection" : parsed.label}</span>
      <span><b>{parsed.recordCount}</b> records</span><span><b>{parsed.validCount}</b> valid</span><span className={parsed.errorCount ? "calc-error" : ""}><b>{parsed.errorCount}</b> errors</span>
    </div>
    {parsed.unresolved && <p className="calc-error">Could not safely determine the pasted layout. Choose the input format.</p>}
    {parsed.errorCount > 0 && <div className="bulk-errors" role="alert">{parsed.rows.filter(row => row.error).slice(0, 20).map(row => <div key={row.index}>Row {row.sourceRow} — {row.error}</div>)}{parsed.errorCount > 20 && <div>…and {parsed.errorCount - 20} more errors in the preview.</div>}</div>}
    {parsed.rows.length > 0 && <div className="bulk-table-wrap"><table className="bulk-preview-table"><thead><tr><th>#</th>{hasReferences && <th>Reference</th>}<th>Bigha</th><th>Biswa</th><th>Biswansi</th><th>Normalized</th><th>Status</th></tr></thead><tbody>{parsed.rows.slice(0, previewLimit).map(row => <tr key={row.index} className={row.error ? "bulk-invalid" : ""}><td>{row.index}</td>{hasReferences && <td>{row.reference}</td>}<td>{row.cells[0] ?? ""}</td><td>{row.cells[1] ?? ""}</td><td>{row.cells[2] ?? ""}</td><td>{row.value ? revenueNotation(row.value) : "—"}</td><td>{row.error ? <span title={row.error}>Error: {row.error}</span> : "Valid"}</td></tr>)}</tbody></table></div>}
    {parsed.rows.length > previewLimit && <p className="calc-note">Showing first {previewLimit} of {number(parsed.rows.length)} parsed records.</p>}
    <div className="bulk-actions"><button className="bulk-primary" onClick={calculate} disabled={!ready}>Calculate Total</button><button onClick={clear}>Clear &amp; Paste New List</button></div>
    {calculationError && <p className="calc-error">{calculationError}</p>}
    {total && <section className="calc-result revenue-total bulk-total"><span>BULK AREA TOTAL · {number(total.records)} records calculated</span><strong>{revenueFormal(total.revenue)}</strong><b>{revenueNotation(total.revenue)}</b><div className="bulk-total-details"><span>Total Bigha <b>{number(total.totals.totalBigha)}</b></span><span>Total Biswa <b>{number(total.totals.totalBiswa)}</b></span><span>Total Biswansi <b>{number(total.totalBiswansi)}</b></span>{units.map(unit => <span key={unit}>{labels[unit]} <b>{number(total.conversions[unit])}</b></span>)}</div><div className="bulk-copy-actions"><button onClick={() => void copy(`${revenueFormal(total.revenue)}\n${revenueNotation(total.revenue)}`)}>Copy Revenue Total</button><button onClick={copyAll}>Copy All Totals</button><button onClick={() => void copy(normalizedRowsForExcel(parsed))}>Copy Normalized Rows</button></div></section>}
  </div>;
}
