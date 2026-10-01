import React, { useEffect, useState } from "react";
import "./court-intelligence.css";

type Source = { orderDate: string | null; page: number; evidence: string; officialUrl: string };
type Fact = { category: string; field: string; value: string; page: number; evidence: string; scope: string };
type Order = { orderDate: string | null; officialUrl: string; status: string; facts: Fact[] };
type Action = { id: string; text: string; actor: string; deadlineText: string | null; dueDate: string | null; source: Source };
type Intelligence = { status: string; processingComplete: boolean; currentPosition: { text: string; source: Source }[];
  beforeNextHearing: Action[]; latestOrder: Order | null; orders: Order[] };
type Answer = { answer: string; claims: { text: string; attribution: string; source: Source }[]; insufficientEvidence: boolean };

const officialLink = (url: string) => {
  try { const parsed = new URL(url); return parsed.protocol === "https:" && parsed.hostname === "delhihighcourt.nic.in" ? url : null; }
  catch { return null; }
};
const Evidence: React.FC<{ source: Source }> = ({ source }) => <details className="court-intelligence-evidence">
  <summary>Review source evidence →</summary>
  <p>Order {source.orderDate ?? "date needs checking"} · Page {source.page}</p>
  <blockquote>{source.evidence}</blockquote>
  {officialLink(source.officialUrl) && <a href={officialLink(source.officialUrl)!} target="_blank" rel="noreferrer">Open official order ↗</a>}
</details>;

const OrderSummary: React.FC<{ order: Order }> = ({ order }) => {
  const facts = order.facts.filter(fact => fact.scope === "Current" && ["COURT_DIRECTION", "COURT_FINDING", "PROCEDURAL_EVENT"].includes(fact.category));
  return <>
    <p className="court-intelligence-order-date">{order.orderDate ?? "Order date needs checking"}</p>
    {order.status !== "Validated" && <p className="court-intelligence-review">This order needs source review. Do not rely on an older summary as a complete current position.</p>}
    {facts.length ? facts.slice(0, 4).map((fact, index) => <div key={index} className="court-intelligence-fact">
      <p>{fact.value}</p><Evidence source={{ orderDate: order.orderDate, page: fact.page, evidence: fact.evidence, officialUrl: order.officialUrl }} />
    </div>) : <p>No confirmed operative summary is available for this order.</p>}
    <p>Next hearing: {order.facts.find(f => f.field === "nextHearing" && f.scope === "Current")?.value ?? "Not confirmed in this order"}</p>
    {officialLink(order.officialUrl) && <a href={officialLink(order.officialUrl)!} target="_blank" rel="noreferrer">Review official order ↗</a>}
  </>;
};

export const CourtIntelligence: React.FC<{ caseId: string }> = ({ caseId }) => {
  const [data, setData] = useState<Intelligence | null>(null);
  const [unavailable, setUnavailable] = useState(false);
  const [question, setQuestion] = useState("");
  const [answer, setAnswer] = useState<Answer | null>(null);
  const [asking, setAsking] = useState(false);
  const [askError, setAskError] = useState("");
  const activeCase = React.useRef(caseId);
  activeCase.current = caseId;
  useEffect(() => {
    const controller = new AbortController();
    setData(null); setUnavailable(false); setQuestion(""); setAnswer(null); setAskError("");
    fetch(`/api/court-cases/${caseId}/intelligence`, { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if (response.status === 204) return null;
        if (!response.ok) throw new Error("Unavailable");
        return response.json() as Promise<Intelligence>;
      }).then(result => { if (!controller.signal.aborted) setData(result); })
      .catch(() => { if (!controller.signal.aborted) setUnavailable(true); });
    return () => controller.abort();
  }, [caseId]);
  const ask = async (event: React.FormEvent) => {
    event.preventDefault();
    if (asking || !question.trim()) return;
    const requestedCase = caseId;
    setAsking(true); setAnswer(null); setAskError("");
    try {
      const response = await fetch(`/api/court-cases/${caseId}/intelligence/ask`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ question }), cache: "no-store",
      });
      if (!response.ok) throw new Error("Unavailable");
      const result = await response.json() as Answer;
      if (activeCase.current === requestedCase) setAnswer(result);
    } catch {
      if (activeCase.current === requestedCase) setAskError("Question answering is temporarily unavailable. Case intelligence remains available.");
    } finally { setAsking(false); }
  };
  const suggestions = data ? [
    ...(data.latestOrder?.facts.some(fact => fact.category === "COURT_DIRECTION" && fact.scope === "Current") ? ["What did the latest order direct?"] : []),
    ...(data.beforeNextHearing.length ? ["What does LAC need to do before the next hearing?"] : []),
    ...(data.orders.some(order => order.facts.some(fact => fact.field === "compensation")) ? ["What has happened regarding compensation?"] : []),
  ] : [];

  return <section className="court-intelligence" aria-label="Court Intelligence">
    <header><span className="court-intelligence-eyebrow">COURT INTELLIGENCE</span><h3>Your matter, at a glance</h3>
      <p>AI-assisted summary. Verify source evidence before official action.</p></header>
    {!data ? <p className="court-intelligence-empty">{unavailable ? "Court intelligence is temporarily unavailable. Your Court records remain available." : "No reviewed intelligence is available yet. Court records and official order links remain unchanged."}</p> : <>
      {(data.status !== "Validated" || !data.processingComplete) && <p className="court-intelligence-review">Some source orders need checking{!data.processingComplete ? "; processing is not complete" : ""}. This is not a complete account of current obligations.</p>}
      <div className="court-intelligence-grid"><article><h4>Current position</h4>
        {data.currentPosition.length ? data.currentPosition.map((line, index) => <div key={index}><p>{line.text}</p><Evidence source={line.source} /></div>) : <p>Current position is not yet confirmed from the available orders.</p>}
      </article><article className="court-intelligence-actions"><h4>Before next hearing</h4>
        {data.beforeNextHearing.length ? data.beforeNextHearing.map(action => <div key={action.id} className="court-intelligence-action">
          <p>{action.text}</p><small>{action.actor} · {action.deadlineText ?? "No deadline stated"}{action.dueDate ? ` · Due ${action.dueDate}` : ""}</small>
          <p className="court-intelligence-unconfirmed">Completion not confirmed by a later order.</p><Evidence source={action.source} />
        </div>) : <p>No source-confirmed outstanding LAC action identified. This does not certify that all obligations are complete.</p>}
      </article></div>
      <article className="court-intelligence-latest"><h4>Latest order</h4>{data.latestOrder && <OrderSummary order={data.latestOrder} />}</article>
      <article className="court-intelligence-ask"><h4>Ask this case</h4>
        <form onSubmit={ask}><label className="court-intelligence-question-label" htmlFor={`case-question-${caseId}`}>Ask about this case</label>
          <div className="court-intelligence-question-row"><input id={`case-question-${caseId}`} value={question} maxLength={600} onChange={event => setQuestion(event.target.value)} placeholder="What would you like to understand?" />
            <button type="submit" disabled={asking || !question.trim()}>{asking ? "Checking evidence…" : "Ask"}</button></div></form>
        {!!suggestions.length && <div className="court-intelligence-suggestions">{suggestions.map(suggestion => <button key={suggestion} type="button" onClick={() => setQuestion(suggestion)}>{suggestion}</button>)}</div>}
        {askError && <p role="status">{askError}</p>}
        {answer && <div className="court-intelligence-answer" aria-live="polite">{answer.claims.length ? answer.claims.map((claim, index) => <div key={index}>
          <small>{claim.attribution} · {claim.source.orderDate}</small><p>{claim.text}</p><Evidence source={claim.source} /></div>) : <p>{answer.answer}</p>}</div>}
      </article>
      <details className="court-intelligence-history"><summary>Order history · Timeline ({data.orders.length})</summary>
        {data.orders.map((order, index) => <details key={`${order.officialUrl}-${index}`}><summary>{order.orderDate ?? "Date not confirmed"} · {order.status === "Validated" ? "Source-backed summary" : "Source review needed"}</summary><OrderSummary order={order} /></details>)}
      </details>
    </>}
  </section>;
};
