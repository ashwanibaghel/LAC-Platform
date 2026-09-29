import React, { useCallback, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";

type PreviewCase = { courtCaseId: string; caseNumber: string; operationalNdoh: string | null; reason: string; identityNeedsReview: boolean };
type Preview = { recommendedCount: number; noNdohCount: number; overdueCount: number; reviewCount: number; skippedIdentityCount: number; cases: PreviewCase[] };
type RunItem = { id: string; courtCaseId: string; caseNumber: string; queueOrder: number; reason: string; status: string; failureCode: string | null; failureMessage: string | null };
type Run = { id: string; status: string; ownerName: string; isOwner: boolean; totalCases: number; completedCases: number; updatedCases: number; noChangeCases: number; needsReviewCases: number; failedCases: number; failureMessage: string | null; items: RunItem[] };
type Challenge = { kind: "Text" | "Image"; officialText: string | null; imageAvailable: boolean; operation: string };
type Review = { id: string; courtCaseId: string; observedAt: string; rawCaseNumber: string; rawStatus: string | null; listingDate: string | null; rawCourtNumber: string | null; rawEvidenceText: string; reviewReason: string | null };

const base = "/api/court-cases/dhc-assisted";

export const DhcAssistedPage: React.FC = () => {
  const [params, setParams] = useSearchParams();
  const runId = params.get("run");
  const [preview, setPreview] = useState<Preview | null>(null);
  const [run, setRun] = useState<Run | null>(null);
  const [challenge, setChallenge] = useState<Challenge | null>(null);
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

  const load = useCallback(async () => {
    if (runId) {
      const response = await fetch(`${base}/runs/${runId}`, { credentials: "include", cache: "no-store" });
      if (!response.ok) throw new Error("Assisted run status unavailable.");
      const next = await response.json() as Run;
      setRun(next);
      if (next.isOwner && ["WaitingForCaptcha", "PausedForCaptcha"].includes(next.status)) {
        const challengeResponse = await fetch(`${base}/runs/${runId}/captcha`, { credentials: "include", cache: "no-store" });
        if (challengeResponse.ok) setChallenge(await challengeResponse.json() as Challenge);
        else setChallenge(null);
      } else setChallenge(null);
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

  return <main className="court-page" style={{ padding: "24px", maxWidth: 1050, margin: "0 auto" }}>
    <p><Link to="/court-cases">← Court queue</Link></p>
    <h1>Delhi High Court assisted verification</h1>
    <p>Official case status and order links, checked with a code entered by an officer.</p>
    {message && <p role="alert">{message}</p>}
    {!runId && preview && <section className="court-card" style={{ padding: 20 }}>
      <h2>{preview.recommendedCount} matters recommended for verification</h2>
      <p>No NDOH: {preview.noNdohCount} · Overdue: {preview.overdueCount} · Cause-list review: {preview.reviewCount}</p>
      {preview.skippedIdentityCount > 0 && <p>{preview.skippedIdentityCount} case identities need review before official search.</p>}
      <fieldset><legend>Verification scope</legend>
        {[["Recommended", "All recommended"], ["NoNdoh", "No NDOH only"], ["Overdue", "Overdue only"], ["Selected", "Selected cases"]].map(([value, label]) =>
          <label key={value} style={{ display: "inline-block", marginRight: 20 }}>
            <input type="radio" name="scope" checked={scope === value} onChange={() => setScope(value)} /> {label}
          </label>)}
      </fieldset>
      <table><thead><tr><th>Select</th><th>Case number</th><th>Current NDOH</th><th>Reason</th></tr></thead><tbody>
        {preview.cases.filter(item => scope === "Selected" || item.reason || item.identityNeedsReview).map(item => <tr key={item.courtCaseId}>
          <td><input type="checkbox" aria-label={`Select ${item.caseNumber}`} disabled={item.identityNeedsReview}
            checked={selected.includes(item.courtCaseId)} onChange={event => setSelected(previous =>
              event.target.checked ? [...previous, item.courtCaseId] : previous.filter(id => id !== item.courtCaseId))} /></td>
          <td><Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link></td>
          <td>{item.operationalNdoh ?? "No NDOH"}</td>
          <td>{item.identityNeedsReview ? "Skipped — case identity needs review" : item.reason || "Officer selected"}</td>
        </tr>)}
      </tbody></table>
      <button disabled={busy || (scope === "Selected" && selected.length === 0)} onClick={() => void start()}>Start verification</button>
    </section>}
    {run && <section className="court-card" style={{ padding: 20 }}>
      <h2>{run.status === "Completed" ? "Assisted verification complete" : "Assisted verification progress"}</h2>
      <p>Status: {run.status} · Run by {run.ownerName}</p>
      <progress value={run.completedCases} max={run.totalCases} /> {run.completedCases} / {run.totalCases} checked
      <p>Updated: {run.updatedCases} · No change: {run.noChangeCases} · Not found: {run.items.filter(item => item.status === "NotFound").length} · Needs review: {run.needsReviewCases} · Failed: {run.failedCases} · Remaining: {Math.max(0, run.totalCases - run.completedCases)}</p>
      {run.failureMessage && <p role="alert">{run.failureMessage}</p>}
      {run.isOwner && ["WaitingForCaptcha", "PausedForCaptcha"].includes(run.status) && challenge &&
        <section aria-label="Official DHC verification code" style={{ border: "1px solid #cbd5e1", padding: 20, maxWidth: 520 }}>
          <h3>Delhi High Court verification required</h3>
          <p>To continue {challenge.operation.toLowerCase()}, enter the security code shown by the official Delhi High Court website.</p>
          <div>Official DHC verification code:</div>
          {challenge.kind === "Text" ? <div aria-label="Official DHC security code" style={{ fontSize: 30, letterSpacing: 5, border: "1px solid #94a3b8", padding: 12, display: "inline-block", margin: "8px 0" }}>{challenge.officialText}</div> :
            <img alt="Official DHC verification code" src={`${base}/runs/${runId}/captcha/image?v=${imageVersion}`} />}
          <label style={{ display: "block", marginTop: 12 }}>Enter the code shown above:
            <input autoComplete="off" spellCheck={false} value={answer} onChange={event => setAnswer(event.target.value)} />
          </label>
          <button disabled={busy || !answer.trim()} onClick={() => void verify()}>Verify &amp; continue</button>{" "}
          <button className="secondary-button" disabled={busy} onClick={() => void refresh()}>Refresh challenge</button>
          <small style={{ display: "block", marginTop: 12 }}>This verification code is entered manually by you. LAC Platform does not solve or bypass it.</small>
        </section>}
      {run.isOwner && !["Completed", "Cancelled", "Failed"].includes(run.status) && <button className="secondary-button" disabled={busy} onClick={() => void cancel()}>Cancel session</button>}
      {run.isOwner && ["Interrupted", "Failed"].includes(run.status) && <button onClick={() => void fetch(`${base}/runs/${run.id}/resume`, { method: "POST", credentials: "include" }).then(() => load())}>Resume with new code</button>}
      <h3>Cases</h3>
      <table><thead><tr><th>Case</th><th>Reason</th><th>Result</th></tr></thead><tbody>
        {run.items.map(item => <tr key={item.id}><td><Link to={`/court-cases/${item.courtCaseId}`}>{item.caseNumber}</Link></td><td>{item.reason}</td><td>{item.status}{item.failureMessage ? ` — ${item.failureMessage}` : ""}</td></tr>)}
      </tbody></table>
    </section>}
    {reviews.length > 0 && <section className="court-card" style={{ padding: 20, marginTop: 18 }}>
      <h2>Official evidence needing officer review</h2>
      <p>These are observations, not changes to the LAC case record. Confirm the exact case and date before accepting.</p>
      {reviews.map(row => <article key={row.id} style={{ borderTop: "1px solid #cbd5e1", padding: "14px 0" }}>
        <p><Link to={`/court-cases/${row.courtCaseId}`}>{row.rawCaseNumber}</Link> · {new Date(row.observedAt).toLocaleString()}</p>
        <p>Official status: {row.rawStatus ?? "Not stated"} · Listing date: {row.listingDate ?? "Not stated"} · Court: {row.rawCourtNumber ?? "Not stated"}</p>
        <p>Review reason: {row.reviewReason ?? "Official evidence needs review"}</p>
        <details><summary>Captured official result</summary><p>{row.rawEvidenceText}</p></details>
        <label>Reason for decision <input value={reviewReasons[row.id] ?? ""}
          onChange={event => setReviewReasons(previous => ({ ...previous, [row.id]: event.target.value }))} /></label>{" "}
        <button disabled={busy || !reviewReasons[row.id]?.trim()} onClick={() => void decide(row.id, true)}>Accept evidence</button>{" "}
        <button className="secondary-button" disabled={busy || !reviewReasons[row.id]?.trim()}
          onClick={() => void decide(row.id, false)}>Reject evidence</button>
      </article>)}
    </section>}
  </main>;
};
