import React, { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { CourtCaseListItemDto, CourtCaseListResponse, DhcHistoricalStatusDto, DhcObservationDto, DhcSourceReviewDto, DhcSyncStatusDto } from "./types";

type ActiveCheck = { status: string; phase: string; completedCases: number; totalCases: number };
const watchedUrl = "/api/court-cases?courtName=Delhi%20High%20Court";
const displayDate = (value: string) => new Date(`${value.slice(0, 10)}T00:00:00`).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" });
const checkButton = (run: ActiveCheck | null) => !run ? "Check now" :
  run.status === "Completed" || run.status === "ReadyForOrders" ? "View verification results" :
  run.status === "WaitingForCaptcha" || run.status === "PausedForCaptcha" ? "Enter verification code" :
  run.phase === "OrderLookup" && ["Interrupted", "Failed"].includes(run.status) ? "Resume order check" :
  ["Interrupted", "Failed"].includes(run.status) ? "Resume DHC check" : "View check progress";

export const DhcSyncPanel: React.FC = () => {
  const [status, setStatus] = useState<DhcSyncStatusDto | null>(null);
  const [historical, setHistorical] = useState<DhcHistoricalStatusDto | null>(null);
  const [assisted, setAssisted] = useState<{ recommendedCount: number; noNdohCount: number; overdueCount: number; reviewCount: number } | null>(null);
  const [activeAssistedRun, setActiveAssistedRun] = useState<string | null>(null);
  const [activeCheck, setActiveCheck] = useState<ActiveCheck | null>(null);
  const [watchedCount, setWatchedCount] = useState<number | null>(null);
  const [watchedCases, setWatchedCases] = useState<CourtCaseListItemDto[] | null>(null);
  const [watchError, setWatchError] = useState(false);
  const [pendingHistoricalAttempt, setPendingHistoricalAttempt] = useState<string | null>(null);
  const [reviews, setReviews] = useState<DhcObservationDto[]>([]);
  const [sourceReviews, setSourceReviews] = useState<DhcSourceReviewDto[]>([]);
  const [busy, setBusy] = useState(false);
  const [confirmHistorical, setConfirmHistorical] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [selectedCase, setSelectedCase] = useState<Record<string, string>>({});
  const [search, setSearch] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [candidates, setCandidates] = useState<Record<string, { id: string; caseNumber: string }[]>>({});

  const refresh = useCallback(async () => {
    const [statusResult, historicalResult, reviewResult, sourceResult, assistedResult, activeResult, watchedResult] = await Promise.all([
      fetch("/api/court-cases/dhc-sync/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/historical/status", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/review", { credentials: "include" }),
      fetch("/api/court-cases/dhc-sync/sources/review", { credentials: "include" }),
      fetch("/api/court-cases/dhc-assisted/preview", { credentials: "include" }),
      fetch("/api/court-cases/dhc-assisted/active", { credentials: "include" }),
      fetch(`${watchedUrl}&page=1&pageSize=1`, { credentials: "include" }),
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
    if (watchedResult.ok) setWatchedCount((await watchedResult.json() as CourtCaseListResponse).totalCount);
    if (activeResult.ok) {
      const id = (await activeResult.json() as { runId: string | null }).runId;
      setActiveAssistedRun(id);
      if (id) {
        const response = await fetch(`/api/court-cases/dhc-assisted/runs/${id}`, { credentials: "include", cache: "no-store" });
        setActiveCheck(response.ok ? await response.json() as ActiveCheck : null);
      } else setActiveCheck(null);
    }
  }, []);

  const loadWatchedCases = async () => {
    if (watchedCases) return;
    setWatchError(false);
    try {
      const first = await fetch(`${watchedUrl}&page=1&pageSize=100`, { credentials: "include" });
      if (!first.ok) throw new Error();
      const page = await first.json() as CourtCaseListResponse;
      const all = [...page.items];
      for (let number = 2; all.length < page.totalCount; number++) {
        const next = await fetch(`${watchedUrl}&page=${number}&pageSize=100`, { credentials: "include" });
        if (!next.ok) throw new Error();
        const result = await next.json() as CourtCaseListResponse;
        if (result.items.length === 0) break;
        all.push(...result.items);
      }
      setWatchedCases(all);
    } catch { setWatchError(true); }
  };

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
    if (!historical) return;
    setConfirmHistorical(false);
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
    const reason = reasons[item.id]?.trim();
    if (!reason) { setMessage("Enter a reason for this decision."); return; }
    const courtCaseId = selectedCase[item.id] || item.courtCaseId;
    if (accept && !courtCaseId) { setMessage("Select the matching Delhi High Court case first."); return; }
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`/api/court-cases/dhc-sync/review/${item.id}/decision`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accept, courtCaseId, listingDate: item.listingDate, reason: reason.trim() }),
      });
      if (!response.ok) throw new Error("The decision was not saved. Check the case number and official date.");
      setReasons(previous => ({ ...previous, [item.id]: "" }));
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Decision failed."); }
    finally { setBusy(false); }
  };

  if (!status) return null;
  const attempt = status.lastAttempt;
  const completed = status.lastSuccess;
  const syncTone = attempt?.status === "Failed" ? "error" : status.lastSuccess ? "healthy" : "neutral";
  const checkedAt = attempt?.completedAt ?? attempt?.startedAt;
  return <section className="dhc-sync-section" aria-label="Delhi High Court public cause-list sync">
    <div className="dhc-daily-summary">
      <div><h2>Delhi High Court</h2><p><span className={`dhc-status-dot ${syncTone}`} /> {attempt?.status === "Failed" ? "Last automatic check was interrupted" : attempt?.status === "Running" ? "Checking official cause lists" : "Automatic updates active"} · Every 5 hours
        {checkedAt && <> · Last checked {new Date(checkedAt).toLocaleString("en-IN", { hour: "numeric", minute: "2-digit" })}</>}</p>
        <strong>{assisted?.recommendedCount ?? 0} cases need official checking</strong>
        {reviews.length + sourceReviews.length > 0 && <p className="dhc-daily-attention">Needs attention: {reviews.length + sourceReviews.length} · Open More to review</p>}</div>
      <Link className="primary-button" to={activeAssistedRun ? `/court-cases/dhc-assisted?run=${activeAssistedRun}` : "/court-cases/dhc-assisted"}>
        {activeAssistedRun ? checkButton(activeCheck) : "Check now"}</Link>
    </div>
    {completed && <div className="dhc-cycle-result" aria-label="Last completed DHC cause-list check">
      <strong>Last completed cause-list check</strong>
      <span>{completed.completedAt ? new Date(completed.completedAt).toLocaleString("en-IN", { day: "numeric", month: "short", hour: "numeric", minute: "2-digit" }) : "Time unavailable"}</span>
      <div className="dhc-cycle-numbers">
        <span><b>{watchedCount ?? "—"}</b>DHC cases being watched</span>
        <span><b>{completed.sourceDocumentsProcessed}</b>official publications checked</span>
        <span><b>{completed.observationsCreated}</b>new listing entries found</span>
        <span><b>{completed.observationsAccepted}</b>official listing entries confirmed</span>
        <span><b>{completed.reviewCount}</b>need attention</span>
      </div>
      <small>Listing entries can include more than one publication for a case; they are not a count of distinct cases.</small>
    </div>}
    <details className="dhc-watched" onToggle={event => { if (event.currentTarget.open) void loadWatchedCases(); }}>
      <summary>DHC cases being watched{watchedCount !== null ? ` (${watchedCount})` : ""}</summary>
      {watchError ? <p>Could not load the case list. Close and reopen to retry.</p> : watchedCases === null ? <p>Loading cases…</p> : watchedCases.length === 0 ? <p>No Delhi High Court cases are currently registered.</p> :
        <ul>{watchedCases.map(item => <li key={item.id}><Link to={`/court-cases/${item.id}`}>{item.caseNumber}</Link>
          {item.operationalNdoh && <span>Current date: {displayDate(item.operationalNdoh)} · {item.operationalNdohSource ?? "Office record"}</span>}
          <small>{item.operationalNdohSource === "DHC Cause List" && item.operationalNdoh
            ? `Official date found · ${displayDate(item.operationalNdoh)} · ${item.daysFromToday === 0 ? "Today" : item.daysFromToday === 1 ? "Tomorrow" : item.daysFromToday !== null ? `${item.daysFromToday} days away` : "Date recorded"}`
            : "No current DHC cause-list date recorded"}</small></li>)}</ul>}
    </details>
    {historical && !historical.completedRun && historical.eligibleCaseCount > 0 &&
      <div className="dhc-history-banner"><span>{historical.lastAttempt?.status === "Failed" ?
        `Historical check paused. Connection was interrupted; completed progress is safe. ${historical.lastAttempt.sourceDocumentsProcessed} of ${historical.lastAttempt.sourceDocumentsDiscovered} publications checked.` :
        `${historical.eligibleCaseCount} old DHC matters can be checked against earlier cause lists.`}</span>
        {historical.canStart && <button className="secondary-button" disabled={busy} onClick={() => setConfirmHistorical(true)}>
          {historical.lastAttempt?.status === "Failed" ? "Continue" : "Run one-time history check"}</button>}</div>}
    {confirmHistorical && historical && <div className="dhc-history-confirm" role="dialog" aria-label="Confirm historical check">
      <strong>{historical.lastAttempt?.status === "Failed" ? "Continue historical check?" : "Start the one-time historical check?"}</strong>
      <p>{historical.eligibleCaseCount} older DHC matters are eligible. This can take time; completed progress is kept if the connection stops.</p>
      <div><button className="secondary-button" disabled={busy} onClick={() => setConfirmHistorical(false)}>Cancel</button>
        <button className="primary-button" disabled={busy} onClick={() => void runHistorical()}>Continue</button></div>
    </div>}
    {message && <p className="dhc-inline-message" role="alert">{message}</p>}
    <details className="dhc-more"><summary>More</summary><div className="dhc-more-content">
      {status.canSyncNow && <button className="secondary-button" disabled={busy} onClick={() => void syncNow()}>Sync now</button>}
      <p>Last successful automatic check: {status.lastSuccess?.completedAt ? new Date(status.lastSuccess.completedAt).toLocaleString("en-IN") : "Not yet completed"}</p>
      {attempt?.status === "Failed" && <p>Automatic check was interrupted. The next scheduled check will try again.</p>}
      <details><summary>Technical and history details</summary><pre>{JSON.stringify({ latestRun: attempt, historicalRun: historical?.lastAttempt }, null, 2)}</pre></details>
    {(reviews.length > 0 || sourceReviews.length > 0) && <details className="dhc-attention" aria-label="DHC evidence needing attention">
      <summary><strong>Needs attention</strong><span>Listing evidence reviews <b>{reviews.length.toLocaleString("en-IN")}</b></span>
        <span>Source publication reviews <b>{sourceReviews.length.toLocaleString("en-IN")}</b></span><em>Review ▾</em></summary>
      <div className="dhc-review-details">
        {reviews.length > 0 && <section><h3>Cases needing attention ({reviews.length})</h3>
          {reviews.map(item => <div key={item.id} className="dhc-review-item">
            <strong>{item.identity}</strong> · {item.listingDate} · page {item.pageNumber} · {item.conflictReason}
            <div>{item.rawMatchedText}</div>
            <a href={item.sourceUrl} target="_blank" rel="noreferrer">Official publication: {item.sourceTitle}</a>
            {item.documentId && <> · <a href={`/api/court-cases/dhc-sync/documents/${item.documentId}/content`} target="_blank" rel="noreferrer">Stored PDF</a></>}
            {status.canSyncNow && <div className="dhc-review-actions">
              <input aria-label="Search case number" placeholder="Find exact case number" value={search[item.id] ?? ""}
                onChange={event => setSearch(previous => ({ ...previous, [item.id]: event.target.value }))} />
              <button className="secondary-button" onClick={() => void findCase(item.id)}>Find case</button>
              <select aria-label="Select matching case" value={selectedCase[item.id] ?? item.courtCaseId ?? ""}
                onChange={event => setSelectedCase(previous => ({ ...previous, [item.id]: event.target.value }))}>
                <option value="">Select exact case</option>
                {item.courtCaseId && <option value={item.courtCaseId}>Previously matched case</option>}
                {(candidates[item.id] ?? []).map(candidate => <option key={candidate.id} value={candidate.id}>{candidate.caseNumber}</option>)}
              </select>
              <input aria-label="Reason for decision" placeholder="Why are you choosing this?" value={reasons[item.id] ?? ""}
                onChange={event => setReasons(previous => ({ ...previous, [item.id]: event.target.value }))} />
              {item.sourceKind === "OrdinaryListing" && item.mode !== "HistoricalBackfill" && <button className="secondary-button" disabled={busy || !reasons[item.id]?.trim()} onClick={() => void decide(item, true)}>Use this date</button>}
              <button className="secondary-button" disabled={busy || !reasons[item.id]?.trim()} onClick={() => void decide(item, false)}>Do not use</button>
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
    </div></details>
  </section>;
};
