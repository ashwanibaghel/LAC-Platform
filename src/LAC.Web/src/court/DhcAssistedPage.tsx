import React, { useCallback, useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

type PreviewCase = { courtCaseId: string; caseNumber: string; operationalNdoh: string | null; reason: string; identityNeedsReview: boolean };
type Preview = { recommendedCount: number; noNdohCount: number; overdueCount: number; reviewCount: number; skippedIdentityCount: number; cases: PreviewCase[] };
type RunItem = { id: string; courtCaseId: string; caseNumber: string; queueOrder: number; reason: string; status: string; failureCode: string | null; failureMessage: string | null };
type Run = { id: string; status: string; phase: "StatusLookup" | "OrderLookup"; ownerName: string; isOwner: boolean; totalCases: number; completedCases: number; updatedCases: number; noChangeCases: number; needsReviewCases: number; failedCases: number; captchaChallenges: number; failureMessage: string | null; items: RunItem[] };
type Challenge = { kind: "Text" | "Image"; officialText: string | null; imageAvailable: boolean; operation: string };
type Review = { id: string; courtCaseId: string; observedAt: string; rawCaseNumber: string; rawStatus: string | null; canonicalStatus: string | null; listingDate: string | null; rawCourtNumber: string | null; rawEvidenceText: string; reviewReason: string | null };

const base = "/api/court-cases/dhc-assisted";

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

  const loadReviews = useCallback(async () => {
    const response = await fetch(`${base}/reviews`, { credentials: "include", cache: "no-store" });
    if (response.ok) setReviews(await response.json() as Review[]);
  }, []);
  useEffect(() => { void loadReviews().catch(() => {}); }, [loadReviews]);

  const decide = async (id: string, accept: boolean) => {
    const reason = reviewReasons[id]?.trim();
    if (!reason) { setMessage("Enter a reason before recording a review decision."); return; }
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/reviews/${id}/decision`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ accept, reason }),
      });
      if (!response.ok) throw new Error(accept ? "This evidence cannot be accepted automatically. Check the exact identity and current date." : "Review decision could not be saved.");
      setReviewReasons(previous => ({ ...previous, [id]: "" }));
      await loadReviews();
      if (runId) await load();
    } catch (error) { setMessage(String(error)); }
    finally { setBusy(false); }
  };

  const confirmStatus = async (id: string) => {
    const reason = reviewReasons[id]?.trim();
    if (!reason) { setMessage("Enter a reason before confirming the LAC case status."); return; }
    if (!window.confirm("Confirm LAC status as Disposed? This changes the canonical court case status and records an audit event.")) return;
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/reviews/${id}/confirm-canonical-status`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason }),
      });
      if (!response.ok) throw new Error("Canonical status could not be confirmed. Reload the case and review exact evidence.");
      setReviewReasons(previous => ({ ...previous, [id]: "" }));
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

  const start = async () => {
    setBusy(true); setMessage(null);
    try {
      const response = await fetch(`${base}/runs`, {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ scope, caseIds: scope === "Selected" ? selected : null }),
      });
      if (!response.ok) throw new Error("Could not start verification. Another officer may have an active session.");
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
  const previewRows = preview?.cases.filter(item => scope === "Selected" || item.reason || item.identityNeedsReview) ?? [];
  const orderCount = run?.items.filter(item => item.status === "Completed" ||
    item.status === "NeedsReview" && ["StatusDifference", "DateConflict", "OrderIdentityMismatch", "OrderDateNeedsReview", "UnsupportedOrderCaseType"].includes(item.failureCode ?? "")).length ?? 0;

  return <main className="court-assisted-page">
    <Link className="court-assisted-back" to="/court-cases">← Court Matters</Link>
    <div className="court-assisted-header"><div><h1>Delhi High Court verification</h1><p>Official case-status check</p></div>
      {run && <span className="court-assisted-status">{run.status === "ReadyForOrders" ? "Status checked" : run.status}</span>}</div>
    {message && <p className="court-assisted-alert" role="alert">{message}</p>}
    {!runId && preview && <div className="court-assisted-preview">
      <section className="court-assisted-main-card" aria-label="Recommended cases for verification">
        <div className="court-assisted-card-heading"><div><h2>Recommended matters</h2><p>Choose the cases to check against the official Delhi High Court status form.</p></div></div>
        <fieldset className="court-assisted-scope"><legend>Verification scope</legend><div>
          {[["Recommended", "All recommended"], ["NoNdoh", "No NDOH"], ["Overdue", "Overdue"], ["Selected", "Selected cases"]].map(([value, label]) =>
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
        <div className="court-assisted-footer"><button className="primary-button" disabled={busy || (scope === "Selected" && selected.length === 0)} onClick={() => void start()}>Start verification</button></div>
      </section>
      <aside className="court-assisted-summary"><h2>Verification summary</h2><strong>{preview.recommendedCount}</strong><span>Need verification</span>
        <dl><div><dt>No NDOH</dt><dd>{preview.noNdohCount}</dd></div><div><dt>Overdue</dt><dd>{preview.overdueCount}</dd></div>
          <div><dt>Review</dt><dd>{preview.reviewCount}</dd></div></dl>
        <p>Verification code is entered manually by the officer.</p>
      </aside>
    </div>}
    {run && <div className="court-assisted-run-layout">
      <section className="court-assisted-main-card" aria-label="Assisted verification work">
        <div className="court-assisted-card-heading"><div><h2>{run.status === "Completed" ? "Assisted verification complete" : "Assisted verification progress"}</h2>
          <p>{run.phase === "StatusLookup" ? "Phase 1 of 2 · Case status verification" : "Phase 2 of 2 · Official order links"} · Run by {run.ownerName}</p></div></div>
        {run.failureMessage && <p className="court-assisted-alert" role="alert">{run.failureMessage}</p>}
        {run.isOwner && run.status === "ReadyForOrders" && <section className="court-assisted-decision" aria-label="Order verification choice">
          <span className="court-assisted-eyebrow">STATUS PHASE COMPLETE</span><h3>Case-status verification complete</h3>
          <p>{run.completedCases} matters checked. Order links are a separate optional phase.</p>
          <div><button className="primary-button" disabled={busy} onClick={() => void orderAction("orders")}>Check official order links</button>
            <button className="secondary-button" disabled={busy} onClick={() => void orderAction("finish")}>Finish session</button></div>
        </section>}
        {run.isOwner && ["WaitingForCaptcha", "PausedForCaptcha"].includes(run.status) && challenge &&
          <section className="court-assisted-challenge" aria-label="Official DHC verification code">
            <span className="court-assisted-eyebrow">HUMAN VERIFICATION</span><h3>Delhi High Court verification required</h3>
            {currentItem && <p>Current case <strong>{currentItem.caseNumber}</strong></p>}
            <p>To continue {challenge.operation.toLowerCase()}, enter the code shown by the official court website.</p>
            <span className="court-assisted-code-label">Official DHC verification code</span>
            {challenge.kind === "Text" ? <div className="court-assisted-code" aria-label="Official DHC security code">{challenge.officialText}</div> :
              <img className="court-assisted-code-image" alt="Official DHC verification code" src={`${base}/runs/${runId}/captcha/image?v=${imageVersion}`} />}
            <label className="court-assisted-answer">Enter code<input autoComplete="off" spellCheck={false} value={answer} onChange={event => setAnswer(event.target.value)} /></label>
            <div className="court-assisted-challenge-actions"><button className="primary-button" disabled={busy || !answer.trim()} onClick={() => void verify()}>Verify &amp; continue</button>
              <button className="secondary-button" disabled={busy} onClick={() => void refresh()}>Refresh challenge</button></div>
            <small>This verification code is entered manually by you. LAC Platform does not solve or bypass it.</small>
          </section>}
        {run.isOwner && ["Interrupted", "Failed"].includes(run.status) && <div className="court-assisted-reopen"><button className="primary-button" disabled={busy} onClick={() => void resume()}>
          {run.phase === "StatusLookup" && run.items.length === run.totalCases &&
            run.items.every(item => ["StatusCaptured", "Completed", "NeedsReview", "NotFound", "Skipped"].includes(item.status))
            ? "Return to order/finish choice" : "Resume with new code"}</button></div>}
        {currentItem && run.status === "Running" && <div className="court-assisted-current"><span>Current case</span><strong>{currentItem.caseNumber}</strong><small>{currentItem.status}</small></div>}
        {run.isOwner && !["Completed", "Cancelled", "Failed", "ReadyForOrders", "Interrupted"].includes(run.status) &&
          <button className="court-assisted-cancel" disabled={busy} onClick={() => void cancel()}>Cancel session</button>}
      </section>
      <aside className="court-assisted-progress" aria-label="Verification progress and queue">
        <h2>Progress</h2><strong>{run.completedCases} <span>/ {run.totalCases}</span></strong><p>case statuses checked</p>
        <progress value={run.completedCases} max={Math.max(1, run.totalCases)} />
        <div className="court-assisted-metrics"><span>Updated <b>{run.updatedCases}</b></span><span>No change <b>{run.noChangeCases}</b></span>
          <span>Review <b>{run.needsReviewCases}</b></span><span>Not found <b>{run.items.filter(item => item.status === "NotFound").length}</b></span>
          <span>Failed <b>{run.failedCases}</b></span><span>Remaining <b>{Math.max(0, run.totalCases - run.completedCases)}</b></span>
          {run.phase === "OrderLookup" && <span>Order searches <b>{orderCount}</b></span>}</div>
        <h3>Queue</h3><ol>{run.items.map(item => <li key={item.id}>
          <span aria-hidden="true">{["Completed", "StatusCaptured"].includes(item.status) ? "✓" : ["CheckingStatus", "CheckingOrders", "CaptchaRequired"].includes(item.status) ? "→" : "○"}</span>
          <Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link><small>{item.status === "StatusCaptured" ? "Case status checked" : item.status}{item.failureMessage ? ` — ${item.failureMessage}` : ""}</small>
        </li>)}</ol>
      </aside>
    </div>}
    {reviews.length > 0 && <section className="court-assisted-reviews" aria-label="Official evidence needing officer review">
      <div className="court-assisted-review-heading"><h2>Official evidence needing officer review</h2><p>These observations do not change the LAC case record until an officer confirms status separately.</p></div>
      <div className="court-assisted-review-grid">{reviews.map(row => <article key={row.id} className="court-assisted-review-card">
        <div className="court-assisted-review-title"><Link to={`/court-cases/${row.courtCaseId}`}>{row.rawCaseNumber}</Link><span>NEEDS REVIEW</span></div>
        <dl><div><dt>Official</dt><dd>{row.rawStatus ?? "Not stated"}</dd></div><div><dt>LAC</dt><dd>{row.canonicalStatus ?? "Not stated"}</dd></div>
          <div><dt>Listing</dt><dd>{row.listingDate ?? "Not stated"}</dd></div><div><dt>Court</dt><dd>{row.rawCourtNumber ?? "Not stated"}</dd></div></dl>
        <small>{new Date(row.observedAt).toLocaleString()} · {row.reviewReason ?? "Official evidence needs review"}</small>
        <details><summary>Captured evidence</summary><p>{row.rawEvidenceText}</p></details>
        <label>Reason for decision<input value={reviewReasons[row.id] ?? ""}
          onChange={event => setReviewReasons(previous => ({ ...previous, [row.id]: event.target.value }))} /></label>
        <div className="court-assisted-review-actions"><button className="secondary-button" disabled={busy || !reviewReasons[row.id]?.trim()} onClick={() => void decide(row.id, true)}>Accept evidence</button>
          <button className="secondary-button" disabled={busy || !reviewReasons[row.id]?.trim()} onClick={() => void decide(row.id, false)}>Keep LAC record</button></div>
        {row.rawStatus?.trim().toLowerCase() === "disposed" && row.canonicalStatus?.trim().toLowerCase() !== "disposed" &&
          <button className="court-assisted-danger" disabled={busy || !reviewReasons[row.id]?.trim()}
            onClick={() => void confirmStatus(row.id)}>Confirm LAC status as Disposed</button>}
      </article>)}</div>
    </section>}
  </main>;
};
