import { useEffect, useState } from "react";
import { Link } from "react-router-dom";

type ConfirmedOrder = { courtCaseId: string; caseNumber: string; courtOrderIntelligenceId: string; orderDate: string; officialUrl: string; sourceKind: string; reviewedAt: string; reviewReason: string; origin: string };
export function ConfirmedCourtOrders({ entity, id }: { entity: "villages" | "awards" | "khasras"; id: string }) {
  const [orders, setOrders] = useState<ConfirmedOrder[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController(); setOrders([]); setError("");
    fetch(`/api/${entity}/${id}/court-orders`, { credentials: "include", signal: controller.signal, cache: "no-store" })
      .then(async response => { if (response.status === 403) return []; if (!response.ok) throw new Error("Confirmed Court orders could not be loaded."); return response.json(); })
      .then(result => { if (!controller.signal.aborted) setOrders(result); })
      .catch(error => { if (!controller.signal.aborted) setError(error.message); });
    return () => controller.abort();
  }, [entity, id]);
  if (!orders.length && !error) return null;
  return <section className="section confirmed-court-orders"><h2>Confirmed Court orders</h2>
    <p>Officer-reviewed source links. Court facts and office records retain their separate provenance.</p>
    {error && <p role="alert">{error}</p>}
    <table className="data-table"><thead><tr><th>Case</th><th>Order date</th><th>Source</th><th>Review</th></tr></thead><tbody>{orders.map(order => <tr key={order.courtOrderIntelligenceId}>
      <td><Link to={`/court-cases/${order.courtCaseId}?order=${encodeURIComponent(order.officialUrl)}`}>{order.caseNumber}</Link></td><td>{order.orderDate}</td>
      <td><a href={order.officialUrl} target="_blank" rel="noreferrer">Official {order.sourceKind === "Corrigendum" ? "corrigendum" : "order"}</a></td>
      <td>{order.reviewReason}<small style={{ display: "block" }}>{order.origin} · {new Date(order.reviewedAt).toLocaleDateString("en-IN")}</small></td>
    </tr>)}</tbody></table>
  </section>;
}
