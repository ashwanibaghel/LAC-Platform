import React, { useCallback, useEffect, useState } from "react";
import type { CourtCaseListResponse, DhcHistoricalStatusDto, DhcObservationDto, DhcSourceReviewDto, DhcSyncStatusDto } from "./types";

export const DhcSyncPanel: React.FC = () => {
  const [status, setStatus] = useState<DhcSyncStatusDto | null>(null);
  const [historical, setHistorical] = useState<DhcHistoricalStatusDto | null>(null);
  const [pendingHistoricalAttempt, setPendingHistoricalAttempt] = useState<string | null>(null);
  const [reviews, setReviews] = useState<DhcObservationDto[]>([]);
  const [sourceReviews, setSourceReviews] = useState<DhcSourceReviewDto[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [selectedCase, setSelectedCase] = useState<Record<string, string>>({});
  const [search, setSearch] = useState<Record<string, string>>({});
  const [candidates, setCandidates] = useState<Record<string, { id: string; caseNumber: string }[]>>({});

  const refresh = useCallback(async () => {
    const [statusResult, historicalResult, reviewResult, sourceResult] = await Promise.all([
      fetch("/api/court-cases/dhc-sync/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/historical/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/review", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/sources/review", { credentials: "include" }),
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
  return (
    <section className="court-card" aria-label="Delhi High Court public cause-list sync" style={{ marginTop: 16, padding: 16 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12 }}>
        <div>
          <h2 style={{ margin: 0, fontSize: 17 }}>Delhi High Court public cause-list sync</h2>
          <small>Official listing observations only; not orders or completed hearings.</small>
        </div>
        {status.canSyncNow && <button className="secondary-button" disabled={busy} onClick={() => void syncNow()}>Sync now</button>}
      </div>
      <p style={{ margin: "9px 0" }}>
        Last success: {status.lastSuccess?.completedAt ? new Date(status.lastSuccess.completedAt).toLocaleString() : "Never"}
        {" · "}Last attempt: {attempt ? `${new Date(attempt.startedAt).toLocaleString()} (${attempt.status})` : "Never"}
        {" · "}Documents processed: {attempt?.sourceDocumentsProcessed ?? 0}
        {" · "}Accepted: {attempt?.observationsAccepted ?? 0}
        {" · "}Needs review: {attempt?.reviewCount ?? 0}
      </p>
      {attempt?.failureMessage && <p role="alert">{attempt.failureMessage}</p>}
      {message && <p role="alert">{message}</p>}
      {historical && <section aria-label="One-time historical catch-up" style={{ borderTop: "1px solid #e2e8f0", marginTop: 16, paddingTop: 12 }}>
        <h3>One-time historical catch-up</h3>
        <p>Eligible stale DHC cases: {historical.eligibleCaseCount} · Earliest register date: {historical.earliestBaseline ?? "None"}
          {" · "}Window: {historical.earliestBaseline ?? "None"} through {historical.windowEnd}
          {" · "}Skipped — no historical baseline: {historical.noBaselineCount}
          {" · "}Real proceeding exclusions: {historical.realProceedingExclusionCount}</p>
        {historical.completedRun?.completedAt ? <p>Historical backfill completed on {new Date(historical.completedRun.completedAt).toLocaleString()}.</p> :
          historical.canStart && <button className="secondary-button" disabled={busy} onClick={() => void runHistorical()}>
            {historical.lastAttempt?.status === "Failed" || historical.lastAttempt?.status === "Running"
              ? "Resume historical backfill" : "Run one-time historical backfill"}
          </button>}
        {historical.lastAttempt && <p>Status: {historical.lastAttempt.status}
          {" · "}Started: {new Date(historical.lastAttempt.startedAt).toLocaleString()}
          {" · "}Completed: {historical.lastAttempt.completedAt ? new Date(historical.lastAttempt.completedAt).toLocaleString() : "—"}
          {" · "}Eligible: {historical.lastAttempt.eligibleCaseCount}
          {" · "}Archive pages: {historical.lastAttempt.archivePagesDiscovered}
          {" · "}Supported sources: {historical.lastAttempt.sourceDocumentsDiscovered}
          {" · "}Documents processed: {historical.lastAttempt.sourceDocumentsProcessed}
          {" · "}Target matches: {historical.lastAttempt.targetCaseMatches}
          {" · "}Cases advanced: {historical.lastAttempt.casesAdvanced}
          {" · "}Needs review: {historical.lastAttempt.reviewCount}</p>}
        {historical.lastAttempt?.failureMessage && <p role="alert">{historical.lastAttempt.failureMessage}</p>}
      </section>}
      {reviews.length > 0 && <details>
        <summary>Listing observations needing review ({reviews.length})</summary>
        {reviews.map(item => <div key={item.id} style={{ borderTop: "1px solid #e2e8f0", padding: "12px 0" }}>
          <strong>{item.identity}</strong> · {item.listingDate} · page {item.pageNumber} · {item.conflictReason}
          <div>{item.rawMatchedText}</div>
          <a href={item.sourceUrl} target="_blank" rel="noreferrer">Official publication: {item.sourceTitle}</a>
          {item.documentId && <> · <a href={`/api/court-cases/dhc-sync/documents/${item.documentId}/content`} target="_blank" rel="noreferrer">Stored PDF</a></>}
          {status.canSyncNow && <div style={{ display: "flex", gap: 8, marginTop: 8, flexWrap: "wrap" }}>
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
        </div>)}
      </details>}
      {sourceReviews.length > 0 && <details>
        <summary>Publications needing source review ({sourceReviews.length})</summary>
        <ul>{sourceReviews.map(source => <li key={source.id}>
          {source.listingDate ?? "Undated"} · {source.kind} · <a href={source.sourceUrl} target="_blank" rel="noreferrer">{source.sourceTitle}</a>
          {source.failureMessage && <> · {source.failureMessage}</>}
        </li>)}</ul>
      </details>}
    </section>
  );
};
