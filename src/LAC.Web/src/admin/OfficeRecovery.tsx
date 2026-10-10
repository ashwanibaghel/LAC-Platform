import { useRef, useState } from "react";
import { useAuth } from "../auth/AuthProvider";

type Target = { id: string; username: string; fullName: string; isActive: boolean; revision: number };
export function OfficeRecovery() {
  const { user } = useAuth();
  const [username, setUsername] = useState("");
  const [target, setTarget] = useState<Target | null>(null);
  const [credential, setCredential] = useState("");
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  async function run(reset: boolean) {
    if (submitting.current) return;
    submitting.current = true; setBusy(true); setError(""); setMessage("");
    try {
      const response = await fetch(reset ? `/api/office/accounts/recovery/${target!.id}/${target!.isActive ? "reset-credential" : "enable"}` : "/api/office/accounts/recovery/lookup", {
        method: "POST", credentials: "include", headers: { "Content-Type": "application/json" },
        body: JSON.stringify(reset ? { expectedRevision: target!.revision } : { username: username.trim() }),
      });
      if (!response.ok) throw new Error(response.status === 409 ? "Account changed. Look up the exact username again." : "Account recovery is unavailable for this request.");
      const data = await response.json();
      if (reset) { setCredential(data.temporaryCredential || ""); setMessage(data.temporaryCredential ? "Temporary credential issued. Existing sessions ended." : "Account enabled. Look up the exact username again if credential recovery is needed."); setTarget(null); }
      else { setCredential(""); setTarget(data); }
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Recovery failed."); setTarget(null); }
    finally { submitting.current = false; setBusy(false); }
  }
  if (user?.authority !== "SYSTEM_ADMIN") return <p role="alert">Technical recovery requires a System Administrator.</p>;
  return <details className="rbac-recovery" open>
    <summary>Technical recovery by exact username</summary>
    <p>Office accounts are excluded from this directory. Use the exact official username for a deliberate credential recovery.</p>
    <form onSubmit={event => { event.preventDefault(); void run(false); }}>
      <label>Exact official username <input required value={username} onChange={event => { setUsername(event.target.value); setTarget(null); setCredential(""); }} /></label>
      <button className="rbac-btn-outline" disabled={busy}>Look Up Account</button>
    </form>
    {error && <p role="alert">{error}</p>}
    {message && <p role="status">{message}</p>}
    {target && <div>
      <p>{target.isActive ? "Reset credential for" : "Enable inactive account"} <strong>{target.fullName} ({target.username})</strong>? Existing sessions will end.</p>
      <button className="rbac-btn-danger" disabled={busy} onClick={() => void run(true)}>{target.isActive ? "Confirm Credential Recovery" : "Confirm Enable Account"}</button>
      <button className="rbac-btn-outline" disabled={busy} onClick={() => setTarget(null)}>Cancel</button>
    </div>}
    {credential && <div><label>Temporary credential <input type="password" readOnly value={credential} autoComplete="off" /></label>
      <button className="rbac-btn-outline" onClick={() => void navigator.clipboard.writeText(credential)}>Copy Credential</button>
      <button className="rbac-btn-outline" onClick={() => setCredential("")}>Dismiss</button></div>}
  </details>;
}
