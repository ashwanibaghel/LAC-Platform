import React, { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { CourtCaseListResponse, DhcHistoricalStatusDto, DhcObservationDto, DhcSourceReviewDto, DhcSyncStatusDto } from "./types";

export const DhcSyncPanel: React.FC = () => {
  const [status, setStatus] = useState<DhcSyncStatusDto | null>(null);
  const [historical, setHistorical] = useState<DhcHistoricalStatusDto | null>(null);
  const [assisted, setAssisted] = useState<{ recommendedCount: number; noNdohCount: number; overdueCount: number; reviewCount: number } | null>(null);
  const [activeAssistedRun, setActiveAssistedRun] = useState<string | null>(null);
  const [pendingHistoricalAttempt, setPendingHistoricalAttempt] = useState<string | null>(null);
  const [reviews, setReviews] = useState<DhcObservationDto[]>([]);
  const [sourceReviews, setSourceReviews] = useState<DhcSourceReviewDto[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [selectedCase, setSelectedCase] = useState<Record<string, string>>({});
  const [search, setSearch] = useState<Record<string, string>>({});
  const [candidates, setCandidates] = useState<Record<string, { id: string; caseNumber: string }[]>>({});

  const refresh = useCallback(async () => {
    const [statusResult, historicalResult, reviewResult, sourceResult, assistedResult, activeResult] = await Promise.all([
      fetch("/api/court-cases/dhc-sync/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/historical/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/review", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/sources/review", { credentials: "include" }),
      fetch("/api/court-cases/dhc-assisted/preview", { credentials: "include" }),
      fetch("/api/court-cases/dhc-assisted/active", { credentials: "include" }),
    ]);
    if (statusResult.ok) setStatus(await statusResult.json() as DhcSyncStatusDto);
    if (historicalResult.ok) {
      const next = await historicalResult.json() as DhcHistoricalStatusDto;
      setHistorical(next);
      setPendingHistoricalAttempt(previous => previous !== null && next.lastAttempt?.id !== previous &&
        (next.lastAttempt?.status === "Completed" || next.lastAttempt?.status === "Failed") ? null : previous);
    }
    if (reviewResult.ok) setReviews(await reviewResult.json() as DhcObservationDto[]);
    if (sourceResult.ok) setSourceReviews(await sourceResult.json() as DhcSourceReviewDto[]);
    if (assistedResult.ok) setAssisted(await assistedResult.json() as typeof assisted);
    if (activeResult.ok) setActiveAssistedRun((await activeResult.json() as { runId: string | null }).runId);
  }, []);

  useEffect(() => { void refresh().catch(() => setMessage("DHC sync status unavailable.")); }, [refresh]);
  useEffect(() => {
    if (pendingHistoricalAttempt === null && historical?.lastAttempt?.status !== "Running") return;
    const timer = window.setInterval(() => { void refresh().catch(() => {}); }, 5000);
    return () => window.clearInterval(timer);
  }, [pendingHistoricalAttempt, historical?.lastAttempt?.status, refresh]);

  const syncNow = async () => {
    setBusy(true); setMessage(null);
    try {
      const response = await fetch("/api/court-cases/dhc-sync/run", { method: "POST", credentials: "include" });
      if (!response.ok) throw new Error("Sync request failed or another cycle is running.");
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Sync failed."); }
    finally { setBusy(false); }
  };

  const runHistorical = async () => {
    if (!historical || !window.confirm(`Run the one-time public DHC historical backfill for ${historical.eligibleCaseCount} eligible stale cases? This may take hours and cannot normally be started again after completion.`)) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch("/api/court-cases/dhc-sync/historical/run", { method: "POST", credentials: "include" });
      if (!response.ok) throw new Error("Historical backfill request failed or is already complete.");
      setPendingHistoricalAttempt(historical.lastAttempt?.id ?? "none");
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Historical backfill failed."); }
    finally { setBusy(false); }
  };

  const findCase = async (id: string) => {
    const term = search[id]?.trim();
    if (!term) return;
    const response = await fetch(`/api/court-cases?caseNumber=${encodeURIComponent(term)}&courtName=${encodeURIComponent("Delhi High Court")}&pageSize=20`, { credentials: "include" });
    if (!response.ok) { setMessage("Case search failed."); return; }
    const result = await response.json() as CourtCaseListResponse;
    setCandidates(previous => ({ ...previous, [id]: result.items.map(item => ({ id: item.id, caseNumber: item.caseNumber })) }));
  };

  const decide = async (item: DhcObservationDto, accept: boolean) => {
    const reason = window.prompt(accept ? "Reason for accepting this evidenced listing" : "Reason for rejecting this observation");
    if (!reason?.trim()) return;
    const courtCaseId = selectedCase[item.id] || item.courtCaseId;
    if (accept && !courtCaseId) { setMessage("Select a canonical Delhi High Court case first."); return; }
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`/api/court-cases/dhc-sync/review/${item.id}/decision`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accept, courtCaseId, listingDate: item.listingDate, reason: reason.trim() }),
      });
      if (!response.ok) throw new Error("Decision was not accepted; check evidence and case identity.");
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Decision failed."); }
    finally { setBusy(false); }
  };

  if (!status) return null;
  const attempt = status.lastAttempt;
  const syncTone = attempt?.status === "Failed" ? "error" : (attempt?.reviewCount ?? 0) > 0 ? "attention" : status.lastSuccess ? "healthy" : "neutral";
  const syncLabel = attempt?.status === "Failed" ? "Needs attention" : attempt?.status === "Running" ? "Checking now" : status.lastSuccess ? "Active" : "Not checked yet";
  return <section className="dhc-sync-section" aria-label="Delhi High Court public cause-list sync">
    <div className="dhc-sync-heading"><div><h2>Delhi High Court</h2><p>Official cause-list and assisted verification status</p></div></div>
    <div className="dhc-sync-grid">
      <section className="dhc-sync-card" aria-label="Automatic cause list">
        <div className="dhc-card-kicker"><span className={`dhc-status-dot ${syncTone}`} />AUTOMATIC SYNC</div>
        <h3>Automatic cause list</h3>
        <strong className="dhc-card-value">{syncLabel}</strong>
        <p className="dhc-card-meta">{attempt ? `Last checked ${new Date(attempt.completedAt ?? attempt.startedAt).toLocaleString("en-IN", { day: "2-digit", month: "short", hour: "numeric", minute: "2-digit" })}` : "Awaiting first check"}</p>
        <div className="dhc-card-stats"><span>{attempt?.sourceDocumentsProcessed ?? 0} documents</span><span>{attempt?.observationsAccepted ?? 0} updates</span>
          {(attempt?.reviewCount ?? 0) > 0 && <span className="attention">{(attempt?.reviewCount ?? 0).toLocaleString("en-IN")} review</span>}</div>
        <div className="dhc-card-action">{status.canSyncNow && <button className="secondary-button" disabled={busy} onClick={() => void syncNow()}>Sync now</button>}</div>
        <details className="dhc-card-details"><summary>View sync details</summary>
          <dl><div><dt>Last success</dt><dd>{status.lastSuccess?.completedAt ? new Date(status.lastSuccess.completedAt).toLocaleString() : "Never"}</dd></div>
            <div><dt>Last attempt</dt><dd>{attempt ? `${new Date(attempt.startedAt).toLocaleString()} (${attempt.status})` : "Never"}</dd></div>
            <div><dt>Documents</dt><dd>{attempt?.sourceDocumentsProcessed ?? 0}</dd></div>
            <div><dt>Accepted</dt><dd>{attempt?.observationsAccepted ?? 0}</dd></div>
            <div><dt>Needs review</dt><dd>{attempt?.reviewCount ?? 0}</dd></div></dl>
          {attempt?.failureMessage && <p role="alert">{attempt.failureMessage}</p>}
        </details>
      </section>
      <section className="dhc-sync-card" aria-label="One-time historical catch-up">
        <div className="dhc-card-kicker">HISTORICAL CATCH-UP</div>
        <h3>Historical catch-up</h3>
        <strong className="dhc-card-value">{historical?.completedRun ? "Completed" : historical?.eligibleCaseCount ? `${historical.eligibleCaseCount.toLocaleString("en-IN")} stale matters` : "No stale matters eligible"}</strong>
        <p className="dhc-card-meta">{historical?.completedRun?.completedAt ? new Date(historical.completedRun.completedAt).toLocaleDateString("en-IN") :
          historical?.eligibleCaseCount ? `Earliest: ${historical.earliestBaseline ?? "Not available"}` : `Checked through ${historical?.windowEnd ?? "—"}`}</p>
        <div className="dhc-card-stats">{historical?.completedRun ? <span>{historical.completedRun.casesAdvanced} matters advanced</span> :
          <span>Eligible: {historical?.eligibleCaseCount ?? 0}</span>}</div>
        <div className="dhc-card-action">{historical?.canStart ? <button className="secondary-button" disabled={busy} onClick={() => void runHistorical()}>
          {historical.lastAttempt?.status === "Failed" || historical.lastAttempt?.status === "Running" ? "Resume catch-up" : "Run one-time catch-up"}
        </button> : <span className="dhc-no-action">No action required</span>}</div>
        {historical && <details className="dhc-card-details"><summary>View details</summary>
          <dl><div><dt>Window</dt><dd>{historical.earliestBaseline ?? "None"} through {historical.windowEnd}</dd></div>
            <div><dt>No baseline</dt><dd>{historical.noBaselineCount}</dd></div><div><dt>Proceeding exclusions</dt><dd>{historical.realProceedingExclusionCount}</dd></div>
            {historical.lastAttempt && <><div><dt>Status</dt><dd>{historical.lastAttempt.status}</dd></div>
              <div><dt>Archive pages</dt><dd>{historical.lastAttempt.archivePagesDiscovered}</dd></div>
              <div><dt>Supported sources</dt><dd>{historical.lastAttempt.sourceDocumentsDiscovered}</dd></div>
              <div><dt>Documents processed</dt><dd>{historical.lastAttempt.sourceDocumentsProcessed}</dd></div>
              <div><dt>Target matches</dt><dd>{historical.lastAttempt.targetCaseMatches}</dd></div>
              <div><dt>Cases advanced</dt><dd>{historical.lastAttempt.casesAdvanced}</dd></div>
              <div><dt>Needs review</dt><dd>{historical.lastAttempt.reviewCount}</dd></div></>}</dl>
          {historical.lastAttempt?.failureMessage && <p role="alert">{historical.lastAttempt.failureMessage}</p>}
        </details>}
      </section>
      <section className="dhc-sync-card" aria-label="Assisted case status">
        <div className="dhc-card-kicker">ASSISTED VERIFICATION</div>
        <h3>Assisted case status</h3>
        <strong className="dhc-card-value">{assisted?.recommendedCount ?? 0} need verification</strong>
        <p className="dhc-card-meta">Officer-led official case check</p>
        <div className="dhc-card-stats"><span>No NDOH {assisted?.noNdohCount ?? 0}</span><span>Overdue {assisted?.overdueCount ?? 0}</span><span>Review {assisted?.reviewCount ?? 0}</span></div>
        <div className="dhc-card-action"><Link className="secondary-button" to={activeAssistedRun ? `/court-cases/dhc-assisted?run=${activeAssistedRun}` : "/court-cases/dhc-assisted"}>
          {activeAssistedRun ? "Continue session →" : "Start verification →"}</Link></div>
      </section>
    </div>
    {message && <p className="dhc-inline-message" role="alert">{message}</p>}
    {(reviews.length > 0 || sourceReviews.length > 0) && <details className="dhc-attention" aria-label="DHC evidence needing attention">
      <summary><strong>Needs attention</strong><span>Listing evidence reviews <b>{reviews.length.toLocaleString("en-IN")}</b></span>
        <span>Source publication reviews <b>{sourceReviews.length.toLocaleString("en-IN")}</b></span><em>Review ▾</em></summary>
      <div className="dhc-review-details">
        {reviews.length > 0 && <section><h3>Listing observations needing review ({reviews.length})</h3>
          {reviews.map(item => <div key={item.id} className="dhc-review-item">
            <strong>{item.identity}</strong> · {item.listingDate} · page {item.pageNumber} · {item.conflictReason}
            <div>{item.rawMatchedText}</div>
            <a href={item.sourceUrl} target="_blank" rel="noreferrer">Official publication: {item.sourceTitle}</a>
            {item.documentId && <> · <a href={`/api/court-cases/dhc-sync/documents/${item.documentId}/content`} target="_blank" rel="noreferrer">Stored PDF</a></>}
            {status.canSyncNow && <div className="dhc-review-actions">
              <input aria-label="Search canonical case number" placeholder="Find exact case number" value={search[item.id] ?? ""}
                onChange={event => setSearch(previous => ({ ...previous, [item.id]: event.target.value }))} />
              <button className="secondary-button" onClick={() => void findCase(item.id)}>Find case</button>
              <select aria-label="Select canonical case" value={selectedCase[item.id] ?? item.courtCaseId ?? ""}
                onChange={event => setSelectedCase(previous => ({ ...previous, [item.id]: event.target.value }))}>
                <option value="">Select exact case</option>
                {item.courtCaseId && <option value={item.courtCaseId}>Previously matched case</option>}
                {(candidates[item.id] ?? []).map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.caseNumber}</option>)}
              </select>
              {item.sourceKind === "OrdinaryListing" && item.mode !== "HistoricalBackfill" && <button className="secondary-button" disabled={busy} onClick={() => void decide(item, true)}>Accept evidenced date</button>}
              <button className="secondary-button" disabled={busy} onClick={() => void decide(item, false)}>Reject</button>
            </div>}
          </div>)}</section>}
        {sourceReviews.length > 0 && <section><h3>Publications needing source review ({sourceReviews.length})</h3>
          <ul>{sourceReviews.map(source => <li key={source.id}>
            {source.listingDate ?? "Undated"} · {source.kind} · <a href={source.sourceUrl} target="_blank" rel="noreferrer">{source.sourceTitle}</a>
            {source.failureMessage && <> · {source.failureMessage}</>}
          </li>)}</ul>
        </section>}
      </div>
    </details>}
  </section>;
};
