import React, { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

type PreviewCase = { courtCaseId: string; caseNumber: string; operationalNdoh: string | null; reason: string; identityNeedsReview: boolean };
type Preview = { recommendedCount: number; noNdohCount: number; overdueCount: number; reviewCount: number; skippedIdentityCount: number; cases: PreviewCase[] };
type RunItem = { id: string; courtCaseId: string; caseNumber: string; queueOrder: number; reason: string; status: string; failureCode: string | null; failureMessage: string | null };
type Run = { id: string; startedAt: string; status: string; phase: "StatusLookup" | "OrderLookup"; ownerName: string; isOwner: boolean; totalCases: number; completedCases: number; updatedCases: number; noChangeCases: number; needsReviewCases: number; failedCases: number; captchaChallenges: number; failureMessage: string | null; items: RunItem[] };
type Challenge = { kind: "Text" | "Image"; officialText: string | null; imageAvailable: boolean; operation: string };
type Review = { id: string; courtCaseId: string; caseNumber: string; observedAt: string; rawCaseNumber: string; rawStatus: string | null; canonicalStatus: string | null; listingDate: string | null; rawCourtNumber: string | null; rawEvidenceText: string; reviewReason: string | null };
type CaseResult = { observedAt: string; rawStatus: string | null; listingDate: string | null; status: string; reviewReason: string | null };

const base = "/api/court-cases/dhc-assisted";
const statusDecisionReason = "Officer confirmed the exact official Delhi High Court status shown in assisted verification.";
const keepDecisionReason = "Officer reviewed the official Delhi High Court result and retained the current LAC record.";
const displayDate = (value: string) => new Date(`${value.slice(0, 10)}T00:00:00`).toLocaleDateString("en-IN", { day: "numeric", month: "short", year: "numeric" });
const caseResultLabel = (result: CaseResult) => {
  if (result.reviewReason === "AutoStatusApplied") return "LAC status updated to Disposed";
  if (result.status === "NeedsReview") return "Action needed";
  if (result.status === "Rejected") return "Reviewed · LAC record kept";
  if (result.status === "Accepted") return result.listingDate ? "Official date available" : "Verified · no action needed";
  return "Official result recorded";
};

export const DhcAssistedPage: React.FC = () => {
  const [params, setParams] = useSearchParams();
  const runId = params.get("run");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const [challenge, setChallenge] = useState<Challenge | null>(null);
  const challengeKey = useRef<string | null>(null);
  const [scope, setScope] = useState("Recommended");
  const [selected, setSelected] = useState<string[]>([]);
  const [answer, setAnswer] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [imageVersion, setImageVersion] = useState(0);
  const [reviews, setReviews] = useState<Review[]>([]);
  const [reviewReasons, setReviewReasons] = useState<Record<string, string>>({});
  const [caseResults, setCaseResults] = useState<Record<string, CaseResult | null>>({});
  const requestedResults = useRef(new Set<string>());

  const loadReviews = useCallback(async () => {
    const response = await fetch(`${base}/reviews`, { credentials: "include", cache: "no-store" });
    if (response.ok) setReviews(await response.json() as Review[]);
  }, []);
  useEffect(() => { void loadReviews().catch(() => {}); }, [loadReviews]);

  const decide = async (id: string, accept: boolean, standardReason?: string) => {
    const reason = standardReason ?? reviewReasons[id]?.trim();
    if (!reason) { setMessage("Enter a reason before recording a review decision."); return; }
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/reviews/${id}/decision`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accept, reason }),
      });
      if (!response.ok) throw new Error(accept ? "This evidence cannot be accepted automatically. Check the exact identity and current date." : "Review decision could not be saved.");
      setReviewReasons(previous => ({ ...previous, [id]: "" }));
      const courtCaseId = reviews.find(row => row.id === id)?.courtCaseId;
      if (runId && courtCaseId) {
        const key = `${runId}:${courtCaseId}`;
        requestedResults.current.delete(key);
        setCaseResults(previous => ({ ...previous, [key]: null }));
      }
      await loadReviews();
      if (runId) await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const confirmStatus = async (id: string) => {
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/reviews/${id}/confirm-canonical-status`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason: statusDecisionReason }),
      });
      if (!response.ok) throw new Error("The office status could not be updated. Open the case and review the official result.");
      setReviewReasons(previous => ({ ...previous, [id]: "" }));
      const courtCaseId = reviews.find(row => row.id === id)?.courtCaseId;
      if (runId && courtCaseId) {
        const key = `${runId}:${courtCaseId}`;
        requestedResults.current.delete(key);
        setCaseResults(previous => ({ ...previous, [key]: null }));
      }
      await loadReviews();
      if (runId) await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const load = useCallback(async () => {
    if (runId) {
      const response = await fetch(`${base}/runs/${runId}`, { credentials: "include", cache: "no-store" });
      if (!response.ok) throw new Error("Assisted run status unavailable.");
      const next = await response.json() as Run;
      setRun(next);
      if (next.isOwner && ["WaitingForCaptcha", "PausedForCaptcha"].includes(next.status)) {
        const key = `${runId}:${next.captchaChallenges}`;
        if (challengeKey.current !== key) {
          const challengeResponse = await fetch(`${base}/runs/${runId}/captcha`, { credentials: "include", cache: "no-store" });
          if (challengeResponse.ok) {
            setChallenge(await challengeResponse.json() as Challenge);
            challengeKey.current = key;
          } else setChallenge(null);
        }
      } else { setChallenge(null); challengeKey.current = null; }
    } else {
      const response = await fetch(`${base}/preview`, { credentials: "include" });
      if (!response.ok) throw new Error("Assisted queue preview is unavailable or requires Court view/edit access.");
      setPreview(await response.json() as Preview);
    }
  }, [runId]);

  useEffect(() => { void load().catch(error => setMessage(String(error))); }, [load]);
  useEffect(() => {
    if (!runId || !run || ["Completed", "Cancelled", "Failed", "Interrupted"].includes(run.status)) return;
    const timer = window.setInterval(() => { void load().catch(() => {}); }, 3000);
    return () => window.clearInterval(timer);
  }, [runId, run?.status, load]);

  // Load each finished case once, at the result stage; never present an older run's observation as this run's result.
  useEffect(() => {
    if (!runId || !run || (run.completedCases === 0 && run.phase === "StatusLookup")) return;
    const finished = run.items.filter(item => ["StatusCaptured", "Completed", "NeedsReview"].includes(item.status) ||
      run.phase === "OrderLookup" && !["NotFound", "Skipped", "Cancelled"].includes(item.status));
    for (const item of finished) {
      const key = `${runId}:${item.courtCaseId}`;
      if (requestedResults.current.has(key)) continue;
      requestedResults.current.add(key);
      void fetch(`/api/court-cases/${item.courtCaseId}/dhc-status-observations`, { credentials: "include", cache: "no-store" })
        .then(async response => {
          if (!response.ok) throw new Error("Official result unavailable.");
          const observations = await response.json() as CaseResult[];
          const current = observations.find(row => new Date(row.observedAt).getTime() >= new Date(run.startedAt).getTime()) ?? null;
          setCaseResults(previous => ({ ...previous, [key]: current }));
        }).catch(() => { setCaseResults(previous => ({ ...previous, [key]: null })); });
    }
  }, [runId, run]);

  const start = async () => {
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ scope, caseIds: scope === "Selected" ? selected : null }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null;
        throw new Error(problem?.detail || problem?.title || "Could not start verification. Please try again.");
      }
      const result = await response.json() as { runId: string };
      setParams({ run: result.runId });
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const verify = async () => {
    const typed = answer;
    setAnswer(""); // Never retain a submitted answer in browser state.
    if (!typed.trim() || !runId) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs/${runId}/captcha`, {
        method: "POST", credentials: "include", cache: "no-store",
        headers: { "Content-Type": "application/json" }, body: JSON.stringify({ answer: typed }),
      });
      if (!response.ok) throw new Error("Official verification request was not accepted.");
      const result = await response.json() as { accepted: boolean };
      if (!result.accepted) setMessage("Verification code was not accepted. Please enter the new challenge.");
      setImageVersion(version => version + 1);
      await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const refresh = async () => {
    if (!runId) return;
    setAnswer(""); setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs/${runId}/captcha/refresh`, { method: "POST", credentials: "include", cache: "no-store" });
      if (!response.ok) throw new Error("Could not refresh the official challenge.");
      setChallenge(await response.json() as Challenge);
      setImageVersion(version => version + 1);
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const cancel = async () => {
    if (!runId || !window.confirm("Cancel this assisted verification? Completed evidence will be preserved.")) return;
    setBusy(true);
    try {
      await fetch(`${base}/runs/${runId}/cancel`, { method: "POST", credentials: "include" });
      await load();
    } finally { setBusy(false); }
  };

  const orderAction = async (action: "orders" | "finish") => {
    if (!runId) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs/${runId}/${action}`, { method: "POST", credentials: "include" });
      if (!response.ok) throw new Error("Could not change the verification phase.");
      await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const resume = async () => {
    if (!runId) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs/${runId}/resume`, { method: "POST", credentials: "include" });
      if (!response.ok) throw new Error("Could not reopen this verification run.");
      await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const currentItem = run?.items.find(item => ["CheckingStatus", "CheckingOrders", "CaptchaRequired"].includes(item.status))
    ?? run?.items.find(item => item.status === "Queued");
  const currentPosition = run && currentItem ? run.items.findIndex(item => item.id === currentItem.id) + 1 : 0;
  const statusComplete = !!run && run.completedCases >= run.totalCases;
  const statusResultsVisible = !!run && (run.completedCases > 0 || run.phase === "OrderLookup");
  const previewRows = preview?.cases.filter(item => scope === "Selected" || item.reason || item.identityNeedsReview) ?? [];
  const orderCount = run?.items.filter(item => item.status === "Completed" ||
    item.status === "NeedsReview" && ["OrderIdentityMismatch", "OrderDateNeedsReview"].includes(item.failureCode ?? "")).length ?? 0;
  const reviewMessage = (reason: string | null) => ({
    DateConflict: "Two official dates differ. Compare the case record before deciding.",
    StatusDifference: "The office status and Delhi High Court status differ.",
    LocalIdentityConflict: "More than one office case has this number.",
    AmbiguousOfficialRows: "The official search returned more than one row.",
    MultipleExactRows: "The official search returned more than one matching case.",
    IdentityMismatch: "The official case number does not match this office case.",
    OfficialStatusUnclear: "The official status could not be understood safely.",
  } as Record<string, string>)[reason ?? ""] ?? "This official result needs a closer look.";

  return <main className="court-assisted-page">
    <Link className="court-assisted-back" to="/court-cases">← Court Matters</Link>
    <div className="court-assisted-header"><div><h1>Delhi High Court verification</h1><p>Official case-status check</p></div>
      {run && <span className="court-assisted-status">{run.status === "ReadyForOrders" ? "Cases checked" : run.status === "Running" ? "Checking" : run.status === "PausedForCaptcha" ? "Code needed" : run.status === "WaitingForCaptcha" ? "Code needed" : run.status}</span>}</div>
    {message && <p className="court-assisted-alert" role="alert">{message}</p>}
    {!runId && preview && <div className="court-assisted-preview">
      <section className="court-assisted-main-card" aria-label="Recommended cases for verification">
        <div className="court-assisted-card-heading"><div><h2>Priority cases</h2><p>Pending Delhi High Court matters whose next date is missing, overdue, or needs confirmation. To check others, choose “Selected cases”.</p></div></div>
        <fieldset className="court-assisted-scope"><legend>Verification scope</legend><div>
          {[["Recommended", "Priority cases"], ["NoNdoh", "No NDOH"], ["Overdue", "Overdue"], ["Selected", "Selected cases"]].map(([value, label]) =>
            <label key={value} className={scope === value ? "active" : ""}><input type="radio" name="scope" checked={scope === value} onChange={() => setScope(value)} />{label}</label>)}
        </div></fieldset>
        {preview.skippedIdentityCount > 0 && <p className="court-assisted-note">{preview.skippedIdentityCount} case identities need review before official search.</p>}
        <div className="court-assisted-table-wrap"><table><thead><tr><th>Select</th><th>Case number</th><th>Current NDOH</th><th>Reason</th></tr></thead><tbody>
          {previewRows.map(item => <tr key={item.courtCaseId}>
            <td><input type="checkbox" aria-label={`Select ${item.caseNumber}`} disabled={item.identityNeedsReview}
              checked={selected.includes(item.courtCaseId)} onChange={event => setSelected(previous =>
                event.target.checked ? [...previous, item.courtCaseId] : previous.filter(id => id !== item.courtCaseId))} /></td>
            <td><Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link></td>
            <td>{item.operationalNdoh ?? "No NDOH"}</td>
            <td>{item.identityNeedsReview ? "Skipped — case identity needs review" : item.reason || "Officer selected"}</td>
          </tr>)}
        </tbody></table></div>
        <div className="court-assisted-footer"><button className="primary-button" disabled={busy || (scope === "Selected" && selected.length === 0)} onClick={() => void start()}>Check now</button></div>
      </section>
      <aside className="court-assisted-summary"><h2>Verification summary</h2><strong>{preview.recommendedCount}</strong><span>Need verification</span>
        <dl><div><dt>No NDOH</dt><dd>{preview.noNdohCount}</dd></div><div><dt>Overdue</dt><dd>{preview.overdueCount}</dd></div>
          <div><dt>Review</dt><dd>{preview.reviewCount}</dd></div></dl>
        <p>Verification code is entered manually by the officer.</p>
      </aside>
    </div>}
    {run && <div className="court-assisted-run-layout">
      <section className="court-assisted-main-card" aria-label="Assisted verification work">
        <div className="court-assisted-card-heading"><div><h2>{run.phase === "OrderLookup" && ["Interrupted", "Failed"].includes(run.status)
          ? "Order-link check interrupted" : run.status === "Completed" ? "Delhi High Court check complete" : "Checking Delhi High Court"}</h2>
          <p>{run.phase === "OrderLookup" && ["Interrupted", "Failed"].includes(run.status)
            ? "Case-status check is complete. Order-link checking has not finished."
            : run.phase === "StatusLookup" ? "Checking case status" : "Checking order links"} · Run by {run.ownerName}</p></div></div>
        {run.phase === "OrderLookup" && <p className="court-assisted-phase-note">Case status checking is complete. {run.status === "Interrupted" || run.status === "Failed" ? "Order-link checking was interrupted." : "Order-link checking is separate."}</p>}
        {run.failureMessage && ["Interrupted", "Failed"].includes(run.status) && <details className="court-assisted-technical"><summary>Interruption details</summary><p>{run.failureMessage}</p></details>}
        {run.isOwner && run.status === "ReadyForOrders" && <section className="court-assisted-decision" aria-label="Order verification choice">
          <span className="court-assisted-eyebrow">STATUS CHECK COMPLETE</span><h3>All {run.totalCases} case statuses checked.</h3>
          <p>{run.noChangeCases} no change · {run.updatedCases} updated · {run.needsReviewCases} need attention</p>
          <p>Check latest official order links too? Order search is separate and may ask for another Delhi High Court verification code.</p>
          <div><button className="primary-button" disabled={busy} onClick={() => void orderAction("orders")}>Check order links</button>
            <button className="secondary-button" disabled={busy} onClick={() => void orderAction("finish")}>Done</button></div>
        </section>}
        {run.isOwner && ["WaitingForCaptcha", "PausedForCaptcha"].includes(run.status) && challenge &&
          <section className="court-assisted-challenge" aria-label="Official DHC verification code">
            <span className="court-assisted-eyebrow">OFFICIAL CHECK</span><h3>{run.status === "PausedForCaptcha" ? "Delhi High Court needs another verification code" : "Delhi High Court verification"}</h3>
            {currentItem && <p>{run.phase === "OrderLookup" ? "Checking order links for" : `Checking case ${currentPosition} of ${run.totalCases}:`} <strong>{currentItem.caseNumber}</strong></p>}
            {run.status === "PausedForCaptcha" && <p>{run.phase === "OrderLookup" ? `All ${run.totalCases} case statuses are checked. Delhi High Court needs another verification code to continue order-link checking.` : `${run.completedCases} of ${run.totalCases} cases are already checked. Delhi High Court needs another verification code to continue.`}</p>}
            <span className="court-assisted-code-label">Official DHC verification code</span>
            {challenge.kind === "Text" ? <div className="court-assisted-code" aria-label="Official DHC security code">{challenge.officialText}</div> :
              <img className="court-assisted-code-image" alt="Official DHC verification code" src={`${base}/runs/${runId}/captcha/image?v=${imageVersion}`} />}
            <label className="court-assisted-answer">Enter code<input autoComplete="off" spellCheck={false} value={answer} onChange={event => setAnswer(event.target.value)} /></label>
            <div className="court-assisted-challenge-actions"><button className="primary-button" disabled={busy || !answer.trim()} onClick={() => void verify()}>Enter verification code</button>
              <button className="secondary-button" disabled={busy} onClick={() => void refresh()}>Refresh challenge</button></div>
            <small>The code is entered manually by the officer.</small>
          </section>}
        {run.isOwner && ["Interrupted", "Failed"].includes(run.status) && <div className="court-assisted-reopen"><button className="primary-button" disabled={busy} onClick={() => void resume()}>
          {run.phase === "OrderLookup" ? "Resume order check" : run.phase === "StatusLookup" && run.items.length === run.totalCases &&
            run.items.every(item => ["StatusCaptured", "Completed", "NeedsReview", "NotFound", "Skipped"].includes(item.status))
            ? "View verification results" : "Resume DHC check"}</button></div>}
        {currentItem && run.status === "Running" && <div className="court-assisted-current"><span>{run.phase === "OrderLookup" ? "Checking official order links" : `Checking case ${currentPosition} of ${run.totalCases}`}</span><strong>{currentItem.caseNumber}</strong></div>}
        {statusResultsVisible && <section className="court-assisted-results" aria-label="Individual official case results">
          <h3>{statusComplete ? `All ${run.totalCases} case statuses checked.` : `${run.completedCases} of ${run.totalCases} case statuses checked.`}</h3>
          <p>Official case-status results{run.phase === "OrderLookup" ? " · Order links are checked separately" : ""}</p>
          <div className="court-assisted-result-list">{run.items.map(item => {
            const result = caseResults[`${runId}:${item.courtCaseId}`];
            const done = ["StatusCaptured", "Completed", "NeedsReview", "NotFound", "Skipped"].includes(item.status) || run.phase === "OrderLookup";
            return <article key={item.id}>
              <Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link>
              {result ? <>
                <span>DHC: {result.rawStatus ?? "Status not stated"}{result.listingDate ? ` · Next date: ${displayDate(result.listingDate)}` : " · Next date: —"}</span>
                <strong>{caseResultLabel(result)}</strong>
              </> : item.status === "NotFound" ? <span>No official case result found in this check</span> : <span>{done ? item.failureMessage || (item.status === "Skipped" ? "Not checked — case details need review" : result === null ? "Official result unavailable for this check" : "Official result is being loaded") : "Waiting to be checked"}</span>}
            </article>;
          })}</div>
        </section>}
        {run.isOwner && !["Completed", "Cancelled", "Failed", "ReadyForOrders", "Interrupted"].includes(run.status) &&
          <button className="court-assisted-cancel" disabled={busy} onClick={() => void cancel()}>Cancel session</button>}
      </section>
      <aside className="court-assisted-progress" aria-label="Verification progress and queue">
        {run.phase === "OrderLookup" ? <div className="court-assisted-phase-progress">
          <section aria-label="Case-status check progress"><h2>Case-status check</h2>
            <strong>{run.completedCases} of {run.totalCases} complete</strong>
            <progress value={run.completedCases} max={Math.max(1, run.totalCases)} /></section>
          <section aria-label="Order-link check progress"><h2>Order-link check</h2>
            <strong>{orderCount} order searches completed</strong><span>{run.status}</span></section>
        </div> : <><h2>Case-status check</h2><strong>{run.completedCases} <span>/ {run.totalCases}</span></strong>
          <p>case statuses checked</p><progress value={run.completedCases} max={Math.max(1, run.totalCases)} /></>}
        <div className="court-assisted-metrics"><span>Updated <b>{run.updatedCases}</b></span><span>No change <b>{run.noChangeCases}</b></span>
          <span>Review <b>{run.needsReviewCases}</b></span><span>Not found <b>{run.items.filter(item => item.status === "NotFound").length}</b></span>
          <span>Failed <b>{run.failedCases}</b></span><span>Remaining status checks <b>{Math.max(0, run.totalCases - run.completedCases)}</b></span></div>
        <h3>Queue</h3><ol>{run.items.map(item => {
          const result = caseResults[`${runId}:${item.courtCaseId}`];
          return <li key={item.id}>
            <span aria-hidden="true">{["Completed", "StatusCaptured"].includes(item.status) ? "✓" : ["CheckingStatus", "CheckingOrders", "CaptchaRequired"].includes(item.status) ? "→" : "○"}</span>
            <Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link><small>{result ? caseResultLabel(result) : result === null ? "Official result unavailable" : ["StatusCaptured", "Completed"].includes(item.status) ? "Checked" : item.status === "NeedsReview" ? "Needs attention" : item.status === "CaptchaRequired" ? "Code needed" : item.status === "Queued" ? "Waiting" : item.status === "CheckingStatus" ? "Checking" : item.status === "NotFound" ? "Not found" : item.status}</small>
          </li>;
        })}</ol>
      </aside>
    </div>}
    {reviews.length > 0 && <section className="court-assisted-reviews" aria-label="Official evidence needing officer review">
      <div className="court-assisted-review-heading"><h2>Cases needing attention</h2><p>These official results were not applied automatically. Compare them with the office record.</p></div>
      <div className="court-assisted-review-grid">{reviews.map(row => {
        const safeStatusDecision = row.reviewReason === "StatusDifference" &&
          row.rawStatus?.trim().toLowerCase() === "disposed" && row.canonicalStatus?.trim().toLowerCase() === "pending";
        return <article key={row.id} className="court-assisted-review-card">
          <div className="court-assisted-review-title"><Link to={`/court-cases/${row.courtCaseId}`}>{row.caseNumber}</Link><span>ACTION NEEDED</span></div>
          {safeStatusDecision ? <>
            <div className="court-assisted-status-comparison"><p>Delhi High Court says: <strong>DISPOSED</strong></p>
              <p>LAC record says: <strong>PENDING</strong></p></div>
            <p>No automatic change was made. Choose the correct office action.</p>
            <div className="court-assisted-review-actions"><button className="primary-button" disabled={busy} onClick={() => void confirmStatus(row.id)}>Update LAC status to Disposed</button>
              <button className="secondary-button" disabled={busy} onClick={() => void decide(row.id, false, keepDecisionReason)}>Keep LAC as Pending</button></div>
          </> : <>
            <p>{reviewMessage(row.reviewReason)}</p>
            <dl><div><dt>Delhi High Court</dt><dd>{row.rawStatus ?? "Not stated"}</dd></div><div><dt>LAC record</dt><dd>{row.canonicalStatus ?? "Not stated"}</dd></div>
              <div><dt>Official next date</dt><dd>{row.listingDate ? displayDate(row.listingDate) : "Not stated"}</dd></div></dl>
            <label>Reason for decision<input value={reviewReasons[row.id] ?? ""}
              onChange={event => setReviewReasons(previous => ({ ...previous, [row.id]: event.target.value }))} /></label>
            <div className="court-assisted-review-actions"><button className="secondary-button" disabled={busy || !reviewReasons[row.id]?.trim()} onClick={() => void decide(row.id, true)}>Use official result</button>
              <button className="secondary-button" disabled={busy || !reviewReasons[row.id]?.trim()} onClick={() => void decide(row.id, false)}>Keep LAC record</button></div>
          </>}
          <details><summary>View official result / Technical details</summary><p>Checked {new Date(row.observedAt).toLocaleString("en-IN")}</p>
            <p>Official case number as shown: {row.rawCaseNumber}</p>
            <p>Official next date: {row.listingDate ?? "Not stated"} · Court: {row.rawCourtNumber ?? "Not stated"}</p>
            <p>{row.rawEvidenceText}</p></details>
        </article>;
      })}</div>
    </section>}
  </main>;
};
