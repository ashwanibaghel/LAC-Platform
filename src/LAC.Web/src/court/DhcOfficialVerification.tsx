import React, { useEffect, useState } from "react";

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
  return <section className="court-card" aria-label="DHC Official Verification" style={{ marginTop: 16, padding: 16 }}>
    <h3>DHC Official Verification</h3>
    {latest && <>
      <p>Last checked: {new Date(latest.observedAt).toLocaleString()}</p>
      <p>Official case status: {latest.rawStatus ?? "Not stated"} · Listing date: {latest.listingDate ?? "Not stated"} · Court: {latest.rawCourtNumber ?? "Not stated"}</p>
      {differs && <p role="alert">Official DHC status differs from LAC record — review required.</p>}
      {latest.reviewReason && <p role="alert">Official evidence needs review: {latest.reviewReason}.</p>}
      <details><summary>View captured official result</summary><p>{latest.rawEvidenceText}</p></details>
    </>}
    {order && <p>Latest official DHC order: {order.orderDate} · <a href={order.officialUrl!} target="_blank" rel="noreferrer">Open official order</a></p>}
    {orders.length > 0 && <details><summary>Previous official order links ({orders.length})</summary><ul>
      {orders.filter(x => x.officialUrl).map(x => <li key={x.id}>{x.orderDate ?? "Undated"} · <a href={x.officialUrl!} target="_blank" rel="noreferrer">Official order</a>{x.rawRemark && ` · ${x.rawRemark}`}</li>)}
    </ul></details>}
  </section>;
};
