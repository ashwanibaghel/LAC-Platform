import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";

type StatusObservation = { id: string; observedAt: string; rawStatus: string | null; listingDate: string | null; rawCourtNumber: string | null; rawEvidenceText: string; status: string; reviewReason: string | null };
type OrderObservation = { id: string; observedAt: string; orderDate: string | null; officialUrl: string | null; rawRemark: string | null };

export const DhcOfficialVerification: React.FC<{ caseId: string; canonicalStatus: string | null }> = ({ caseId, canonicalStatus }) => {
  const [status, setStatus] = useState<StatusObservation[]>([]);
  const [orders, setOrders] = useState<OrderObservation[]>([]);
  useEffect(() => {
    void Promise.all([
      fetch(`/api/court-cases/${caseId}/dhc-status-observations`, { credentials: "include" }),
      fetch(`/api/court-cases/${caseId}/dhc-order-observations`, { credentials: "include" }),
    ]).then(async ([a, b]) => {
      if (a.ok) setStatus(await a.json() as StatusObservation[]);
      if (b.ok) setOrders(await b.json() as OrderObservation[]);
    }).catch(() => {});
  }, [caseId]);
  if (status.length === 0 && orders.length === 0) return null;
  const latest = status[0];
  const order = orders.find(x => x.officialUrl && x.orderDate);
  const differs = latest?.rawStatus && canonicalStatus &&
    latest.rawStatus.trim().toLowerCase() !== canonicalStatus.trim().toLowerCase();
  const updated = latest?.reviewReason === "AutoStatusApplied";
  const actionNeeded = !!latest && latest.status === "NeedsReview";
  const outcome = !latest ? "OFFICIAL ORDERS" : updated ? "UPDATED" : actionNeeded ? "ACTION NEEDED" : latest.status === "Rejected" ? "REVIEWED" : "VERIFIED";
  return <section className="court-card dhc-case-outcome" aria-label="DHC Official Verification" style={{ marginTop: 16, padding: 16 }}>
    <span className={`dhc-case-outcome-label ${actionNeeded ? "attention" : ""}`}>{outcome}</span>
    <h3>Delhi High Court verification</h3>
    {latest && <>
      <p><strong>Delhi High Court:</strong> {latest.rawStatus ?? "Not stated"}{latest.listingDate && <> · <strong>Official next date:</strong> {new Date(`${latest.listingDate.slice(0, 10)}T00:00:00`).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" })}</>}</p>
      {updated ? <p role="status">LAC status was updated Pending → Disposed from an exact official DHC result.</p> :
        actionNeeded ? <p role="alert">LAC record: {canonicalStatus ?? "Not stated"}. No automatic change was made. Review the official result and choose an action.</p> :
        <p>{latest.status === "Rejected" ? `LAC record retained: ${canonicalStatus ?? "Not stated"}.` : differs ? `LAC record: ${canonicalStatus}. Check the earlier decision in history.` : "No change needed."}</p>}
      {actionNeeded && <p><Link className="dhc-resolve-link" to="/court-cases/dhc-assisted">Resolve DHC status →</Link></p>}
      <details><summary>View official result / Technical details</summary><p>Checked {new Date(latest.observedAt).toLocaleString("en-IN")}</p><p>{latest.rawEvidenceText}</p></details>
    </>}
    {order && <p>Official order dated {order.orderDate}: <a href={order.officialUrl!} target="_blank" rel="noreferrer">Open order</a></p>}
    {orders.length > 0 && <details><summary>Official order links and details ({orders.length})</summary><ul>
      {orders.filter(x => x.officialUrl).map(x => <li key={x.id}>{x.orderDate ?? "Undated"} · <a href={x.officialUrl!} target="_blank" rel="noreferrer">Official order</a>{x.rawRemark && ` · ${x.rawRemark}`}</li>)}
    </ul></details>}
  </section>;
};
