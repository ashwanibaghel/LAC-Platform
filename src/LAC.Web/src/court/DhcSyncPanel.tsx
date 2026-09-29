import React, { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { dailyCauseListOutcome } from "./DhcDailyCauseListOutcome";
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
  const [syncInProgress, setSyncInProgress] = useState(false);
  const [confirmHistorical, setConfirmHistorical] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [selectedCase, setSelectedCase] = useState<Record<string, string>>({});
  const [search, setSearch] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [candidates, setCandidates] = useState<Record<string, { id: string; caseNumber: string }[]>>({});
  const [isOpen, setIsOpen] = useState(false);

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
  useEffect(() => {
    if (!isOpen) return;
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === "Escape") setIsOpen(false); };
    window.addEventListener("keydown", closeOnEscape);
    return () => window.removeEventListener("keydown", closeOnEscape);
  }, [isOpen]);

  const syncNow = async () => {
    setSyncInProgress(true); setMessage(null);
    try {
      const response = await fetch("/api/court-cases/dhc-sync/run", { method: "POST", credentials: "include" });
      if (!response.ok) throw new Error("Sync request failed or another cycle is running.");
      await refresh();
    } catch (error) { setMessage(error instanceof Error ? error.message : "Sync failed."); }
    finally { setSyncInProgress(false); }
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
  const syncTone = attempt?.status === "Failed" ? "error" : status.lastSuccess ? "healthy" : "neutral";
  const checkedAt = attempt?.completedAt ?? attempt?.startedAt;
  const historyRun = historical?.lastAttempt;
  const historicalProgress = historyRun?.status === "Running"
    ? `Checking historical publications… ${historyRun.sourceDocumentsProcessed} of ${historyRun.sourceDocumentsDiscovered} checked`
    : historyRun?.status === "Completed"
    ? `Historical check complete · ${historyRun.sourceDocumentsProcessed} of ${historyRun.sourceDocumentsDiscovered} checked · ${historyRun.reviewCount} needs review`
    : historyRun?.status === "Failed"
    ? `Historical check paused · ${historyRun.sourceDocumentsProcessed} of ${historyRun.sourceDocumentsDiscovered} checked`
    : null;
  const attentionCount = reviews.length + sourceReviews.length;
  const dailyOutcome = dailyCauseListOutcome(attempt, watchedCount, syncInProgress);
  const badgeCount = (assisted?.recommendedCount ?? 0) + attentionCount;
  return (
    <section className="dhc-sync-section" aria-label="Delhi High Court public cause-list sync">
      {/* Sleek Circular Animated Launcher Button */}
      <button
        type="button"
        className={`dhc-circle-launcher ${syncTone} ${attentionCount > 0 ? "has-attention" : ""}`}
        onClick={() => setIsOpen(open => !open)}
        title="Delhi High Court Live Sync & Verification Hub (Click to open)"
        aria-label="Delhi High Court Sync and Verification Hub"
        aria-expanded={isOpen}
      >
        <span className="dhc-circle-icon" aria-hidden="true">⚖️</span>
        <span className={`dhc-circle-pulse ${syncTone}`} aria-hidden="true" />
        {badgeCount > 0 && (
          <span className="dhc-circle-badge" title={`${badgeCount} items need attention or verification`}>
            {badgeCount > 9 ? "9+" : badgeCount}
          </span>
        )}
      </button>

      {/* Balloon-Popping Animated Center Popover Dialog */}
      {isOpen && (
        <div
          className="dhc-popup-overlay"
          onClick={(e) => {
            if (e.target === e.currentTarget) setIsOpen(false);
          }}
          role="presentation"
        >
        <div className="dhc-popup-container" role="dialog" aria-modal="true" aria-label="Delhi High Court Intelligence Hub">
          <div className="dhc-popup-header">
            <div className="dhc-popup-title-group">
              <span className="dhc-popup-emblem" aria-hidden="true">⚖️</span>
              <div>
                <h3>Delhi High Court Intelligence Hub</h3>
                <p>Cause-list synchronization, case verification & official order tracking</p>
              </div>
            </div>
            <button
              type="button"
              className="dhc-popup-close-btn"
              onClick={() => setIsOpen(false)}
              aria-label="Close High Court Hub"
              title="Close (Esc)"
            >
              ✕
            </button>
          </div>

          <div className="dhc-daily-summary">
            {/* Top Status & Schedule Banner */}
            <div className="dhc-summary-info">
              <div className="dhc-court-badge-title">
                <span className="dhc-court-icon" aria-hidden="true">⚖️</span>
                <h2>Delhi High Court Live Hub</h2>
                <span className={`dhc-status-dot ${syncTone}`} title={attempt?.status ?? "Status"} />
              </div>
              <div className="dhc-status-meta">
                <p>
                  <span className="dhc-status-text">
                    {attempt?.status === "Failed"
                      ? "Last automatic check was interrupted"
                      : attempt?.status === "Running"
                      ? "Checking official cause lists…"
                      : "Automatic updates active"}
                  </span> · Every 5 hours
                  {checkedAt && (
                    <span className="dhc-last-checked"> · Last checked {new Date(checkedAt).toLocaleString("en-IN", { hour: "numeric", minute: "2-digit" })}</span>
                  )}
                </p>
                <span className="dhc-status-divider" aria-hidden="true">·</span>
                <strong className="dhc-assisted-pill">{assisted?.recommendedCount ?? 0} cases need official checking</strong>
                {attentionCount > 0 && (
                  <span className="dhc-daily-attention">{attentionCount} DHC item{attentionCount === 1 ? "" : "s"} still need review</span>
                )}
              </div>
            </div>

            {/* Clear, Intuitive Action Cards */}
            <div className="dhc-hub-cards-grid">
              {/* Card 1: Daily Cause List Sync */}
              <div className="dhc-hub-card">
                <div className="dhc-hub-card-header">
                  <span className="dhc-hub-card-icon" aria-hidden="true">📋</span>
                  <div>
                    <h4>Daily Cause List</h4>
                    <span className="dhc-hub-card-sub">Next Date of Hearing (NDOH)</span>
                  </div>
                </div>
                <div className={`dhc-hub-outcome ${dailyOutcome.tone}`} role="status" aria-live="polite">
                  <strong>{dailyOutcome.title}</strong>
                  {dailyOutcome.lines.map(line => <p key={line}>{line}</p>)}
                </div>
                {status.canSyncNow && (
                  <button
                    type="button"
                    className="secondary-button dhc-hub-action-btn"
                    disabled={syncInProgress || busy}
                    onClick={() => void syncNow()}
                    title="Fetch latest cause list from Delhi High Court"
                  >
                    {syncInProgress ? "Checking cause lists…" : "🔄 Sync now"}
                  </button>
                )}
                {attempt?.status === "Completed" && <details className="dhc-hub-detail" aria-label="Last completed DHC cause-list check">
                  <summary>View last check details</summary>
                  <div className="dhc-hub-detail-stats">
                    <span>Status: <b>Completed</b></span>
                    <span>Publications found: <b>{attempt.sourceDocumentsDiscovered}</b></span>
                    <span>Processed this cycle: <b>{attempt.sourceDocumentsProcessed}</b></span>
                    <span>New listing entries: <b>{attempt.observationsCreated}</b></span>
                    <span>Confirmed entries: <b>{attempt.observationsAccepted}</b></span>
                    <span>New review items: <b>{attempt.reviewCount}</b></span>
                  </div>
                  <small>Listing entries can include more than one publication for a case; they are not a count of distinct cases.</small>
                </details>}
                <details className="dhc-hub-watched" onToggle={event => { if (event.currentTarget.open) void loadWatchedCases(); }}>
                  <summary>DHC cases being watched{watchedCount !== null ? ` (${watchedCount})` : ""}</summary>
                  {watchError ? <p>Could not load the case list. Close and reopen to retry.</p> : watchedCases === null ? <p>Loading cases…</p> : watchedCases.length === 0 ? <p>No Delhi High Court cases are currently registered.</p> :
                    <ul>{watchedCases.map(item => <li key={item.id}><Link to={`/court-cases/${item.id}`}>{item.caseNumber}</Link>
                      {item.operationalNdoh && <span>Current date: {displayDate(item.operationalNdoh)} · {item.operationalNdohSource ?? "Office record"}</span>}
                      <small>{item.operationalNdohSource === "DHC Cause List" && item.operationalNdoh
                        ? `Official date found · ${displayDate(item.operationalNdoh)} · ${item.daysFromToday === 0 ? "Today" : item.daysFromToday === 1 ? "Tomorrow" : item.daysFromToday !== null ? `${item.daysFromToday} days away` : "Date recorded"}`
                        : "No current DHC cause-list date recorded"}</small></li>)}</ul>}
                </details>
              </div>

              {/* Card 2: Official Case Verification */}
              <div className="dhc-hub-card highlight">
                <div className="dhc-hub-card-header">
                  <span className="dhc-hub-card-icon" aria-hidden="true">⚖️</span>
                  <div>
                    <h4>Official Case Verification</h4>
                    <span className="dhc-hub-card-sub">Orders & Disposal Check</span>
                  </div>
                </div>
                <p className="dhc-hub-card-desc">
                  Verifies latest official court orders and disposal status directly from the High Court portal.
                </p>
                <div className="dhc-hub-card-meta">
                  <strong className="dhc-hub-pending-count">
                    {assisted?.recommendedCount ?? 0} cases ready to verify
                  </strong>
                </div>
                <Link
                  className="primary-button dhc-hub-action-btn highlight"
                  to={activeAssistedRun ? `/court-cases/dhc-assisted?run=${activeAssistedRun}` : "/court-cases/dhc-assisted"}
                  onClick={() => setIsOpen(false)}
                >
                  {activeAssistedRun ? activeCheck ? checkButton(activeCheck) : "View check progress" : "Check now"}
                </Link>
              </div>
            </div>

            {/* Collapsible Technical Details (Hidden from normal officers) */}
            <details className="dhc-more">
              <summary>Technical details & history ▾</summary>
              <div className="dhc-more-content">
                <p className="dhc-more-timestamp">
                  Last successful automatic check:{" "}
                  {status.lastSuccess?.completedAt
                    ? new Date(status.lastSuccess.completedAt).toLocaleString("en-IN")
                    : "Not yet completed"}
                </p>
                {attempt?.status === "Failed" && (
                  <p className="dhc-more-error">Automatic check was interrupted. The next scheduled check will try again.</p>
                )}

                <details className="dhc-tech-details">
                  <summary>Technical and history details</summary>
                  <div className="dhc-tech-summary-grid">
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Cycle Status</span>
                      <strong className={`dhc-stat-val ${syncTone}`}>{attempt?.status ?? "Idle"}</strong>
                    </div>
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Publications</span>
                      <strong className="dhc-stat-val">
                        {attempt?.sourceDocumentsProcessed ?? 0} / {attempt?.sourceDocumentsDiscovered ?? 0}
                      </strong>
                    </div>
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Archive pages scanned</span>
                      <strong className="dhc-stat-val">{historyRun?.archivePagesDiscovered ?? 0}</strong>
                    </div>
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Publications found</span>
                      <strong className="dhc-stat-val">{historyRun?.sourceDocumentsDiscovered ?? 0}</strong>
                    </div>
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Publications checked</span>
                      <strong className="dhc-stat-val">{historyRun?.sourceDocumentsProcessed ?? 0}</strong>
                    </div>
                    <div className="dhc-tech-stat">
                      <span className="dhc-stat-lbl">Needs review</span>
                      <strong className="dhc-stat-val">{historyRun?.reviewCount ?? 0}</strong>
                    </div>
                  </div>
                  <details className="dhc-raw-json-details">
                    <summary>View Diagnostic JSON Payload</summary>
                    <pre className="dhc-clean-pre">{JSON.stringify({ latestRun: attempt, historicalRun: historical?.lastAttempt }, null, 2)}</pre>
                  </details>
                </details>

                {(reviews.length > 0 || sourceReviews.length > 0) && (
                  <details className="dhc-attention" aria-label="DHC evidence needing attention">
                    <summary>
                      <strong>Needs attention</strong>
                      <span>Listing evidence reviews <b>{reviews.length.toLocaleString("en-IN")}</b></span>
                      <span>Source publication reviews <b>{sourceReviews.length.toLocaleString("en-IN")}</b></span>
                      <em>Review ▾</em>
                    </summary>
                    <div className="dhc-review-details">
                      {reviews.length > 0 && (
                        <section>
                          <h3>Cases needing attention ({reviews.length})</h3>
                          {reviews.map(item => (
                            <div key={item.id} className="dhc-review-item">
                              <strong>{item.identity}</strong> · {item.listingDate} · page {item.pageNumber} · {item.conflictReason}
                              <div>{item.rawMatchedText}</div>
                              <a href={item.sourceUrl} target="_blank" rel="noreferrer">Official publication: {item.sourceTitle}</a>
                              {item.documentId && <> · <a href={`/api/court-cases/dhc-sync/documents/${item.documentId}/content`} target="_blank" rel="noreferrer">Stored PDF</a></>}
                              {status.canSyncNow && (
                                <div className="dhc-review-actions">
                                  <input
                                    aria-label="Search case number"
                                    placeholder="Find exact case number"
                                    value={search[item.id] ?? ""}
                                    onChange={event => setSearch(previous => ({ ...previous, [item.id]: event.target.value }))}
                                  />
                                  <button className="secondary-button" onClick={() => void findCase(item.id)}>Find case</button>
                                  <select
                                    aria-label="Select matching case"
                                    value={selectedCase[item.id] ?? item.courtCaseId ?? ""}
                                    onChange={event => setSelectedCase(previous => ({ ...previous, [item.id]: event.target.value }))}
                                  >
                                    <option value="">Select exact case</option>
                                    {item.courtCaseId && <option value={item.courtCaseId}>Previously matched case</option>}
                                    {(candidates[item.id] ?? []).map(candidate => (
                                      <option key={candidate.id} value={candidate.id}>{candidate.caseNumber}</option>
                                    ))}
                                  </select>
                                  <input
                                    aria-label="Reason for decision"
                                    placeholder="Why are you choosing this?"
                                    value={reasons[item.id] ?? ""}
                                    onChange={event => setReasons(previous => ({ ...previous, [item.id]: event.target.value }))}
                                  />
                                  {item.sourceKind === "OrdinaryListing" && item.mode !== "HistoricalBackfill" && (
                                    <button className="secondary-button" disabled={busy || !reasons[item.id]?.trim()} onClick={() => void decide(item, true)}>
                                      Use this date
                                    </button>
                                  )}
                                  <button className="secondary-button" disabled={busy || !reasons[item.id]?.trim()} onClick={() => void decide(item, false)}>
                                    Do not use
                                  </button>
                                </div>
                              )}
                            </div>
                          ))}
                        </section>
                      )}
                      {sourceReviews.length > 0 && (
                        <section>
                          <h3>Publications needing source review ({sourceReviews.length})</h3>
                          <ul>
                            {sourceReviews.map(source => (
                              <li key={source.id}>
                                {source.listingDate ?? "Undated"} · {source.kind} · <a href={source.sourceUrl} target="_blank" rel="noreferrer">{source.sourceTitle}</a>
                                {source.failureMessage && <> · {source.failureMessage}</>}
                              </li>
                            ))}
                          </ul>
                        </section>
                      )}
                    </div>
                  </details>
                )}
              </div>
            </details>
          </div>
          {historical && (historyRun || (!historical.completedRun && historical.eligibleCaseCount > 0)) && (
            <div className="dhc-history-banner">
              <span>
                {historicalProgress ?? `${historical.eligibleCaseCount} old DHC matters can be checked against earlier cause lists.`}
                {historyRun?.status === "Running" && historyRun.reviewCount > 0 &&
                  <> · {historyRun.reviewCount} publication needs review; continuing with the remaining publications.</>}
              </span>
              {historical.canStart && (
                <button className="secondary-button" disabled={busy} onClick={() => setConfirmHistorical(true)}>
                  {historical.lastAttempt?.status === "Failed" ? "Continue" : "Run one-time history check"}
                </button>
              )}
            </div>
          )}
          {confirmHistorical && historical && (
            <div className="dhc-history-confirm" role="dialog" aria-label="Confirm historical check">
              <strong>{historical.lastAttempt?.status === "Failed" ? "Continue historical check?" : "Start the one-time historical check?"}</strong>
              <p>{historical.eligibleCaseCount} older DHC matters are eligible. This can take time; completed progress is kept if the connection stops.</p>
              <div>
                <button className="secondary-button" disabled={busy} onClick={() => setConfirmHistorical(false)}>Cancel</button>
                <button className="primary-button" disabled={busy} onClick={() => void runHistorical()}>Continue</button>
              </div>
            </div>
          )}
          {message && <p className="dhc-inline-message" role="alert">{message}</p>}
        </div>
      </div>
      )}
    </section>
  );
};
