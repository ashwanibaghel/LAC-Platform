import { useCallback, useEffect, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { notingOfficeApi as api, generateUuid } from "./notingOfficeApi";
import { awaitCommittedOfficeSave, submittedNoteNumber, validateOfficeSaveResolution } from "./officeSaveBarrier";
import { OnlyOfficeNoteFrame } from "./OnlyOfficeNoteFrame";
import { AuthorizedPdfPanel } from "./AuthorizedPdfPanel";
import { SendReturnModal } from "./SendReturnModal";
import type { NativeSynchronization } from "./nativeSynchronization";
import type { GreenSheetMetadataDto, MatterNotingWorkspaceData, MatterOfficePreviewDto, OfficeSaveReceiptDto, OfficeSessionInfo } from "./notingTypes";
import "./noting.css";
import "./notingV4.css";
async function read<T>(url: string): Promise<T> {
    const r = await fetch(url, { credentials: "include", cache: "no-store" });
    if (!r.ok) {
        const b = await r.json().catch(() => ({}));
        throw new Error(b.message || `Request rejected (${r.status}).`);
    }
    return r.json();
}
type SaveCommand = {
    session: string;
    revision: number;
    generation: number;
    key: string;
    url: string;
    captureGeneration: number;
};
type Transfer = {
    id: string;
    toUserId: string;
    state: string;
    includesPhysicalOriginal: boolean;
};
export function GreenNotingV4Workspace() {
    const { id: matterId = "" } = useParams();
    const [query] = useSearchParams();
    const { user, logout } = useAuth();
    const [workspace, setWorkspace] = useState<MatterNotingWorkspaceData | null>(null);
    const [heading, setHeading] = useState("Matter Green Noting");
    const [dak, setDak] = useState<{
        id: string;
        diaryNumber: string;
        revision: number;
        transfers: Transfer[];
    } | null>(null);
    const [selectedDakId, setSelectedDakId] = useState<string | null>(query.get("sourceDakId"));
    const [linkedDaks, setLinkedDaks] = useState<Array<{
        dakId: string;
        diaryNumber: string;
    }>>([]);
    const [revision, setRevision] = useState(0);
    const [session, setSession] = useState<OfficeSessionInfo | null>(null);
    const sessionRef = useRef<OfficeSessionInfo | null>(null);
    const bridge = useRef<NativeSynchronization | null>(null);
    const [ready, setReady] = useState(false);
    const [busy, setBusy] = useState(false);
    const busyRef = useRef(false);
    const [blocked, setBlocked] = useState(false);
    const [status, setStatus] = useState("Server draft not opened.");
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const [preview, setPreview] = useState<MatterOfficePreviewDto | null>(null);
    const [reviewOpen, setReviewOpen] = useState(false);
    const [accepted, setAccepted] = useState(false);
    const [confirmed, setConfirmed] = useState(false);
    const [sendOpen, setSendOpen] = useState(false);
    const [historyOpen, setHistoryOpen] = useState(false);
    const [sheet, setSheet] = useState<GreenSheetMetadataDto | null>(null);
    const [sheetOpen, setSheetOpen] = useState(false);
    const [documentId, setDocumentId] = useState("");
    const [mode, setMode] = useState<"split" | "noting" | "document">("split");
    const [divider, setDivider] = useState(55);
    const panels = useRef<HTMLDivElement>(null);
    const dirtyGeneration = useRef(0);
    const saveGeneration = useRef(0);
    const command = useRef<SaveCommand | null>(null);
    const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
    const terminalFailure = useRef(false);
    const confirmKey = useRef<{
        signature: string;
        key: string;
    } | null>(null);
    const mounted = useRef(true);
    const saveAction = useRef<(review?: boolean, retry?: boolean) => Promise<void>>(async () => { });
    const receiveCommand = useRef<{
        key: string;
        revision: number;
        transfer: string;
    } | null>(null);
    const sourceDak = selectedDakId;
    const publishedSheetId = query.get("greenSheetId");
    const updateSession = (s: OfficeSessionInfo) => { sessionRef.current = s; setSession(s); };
    const stopTimer = () => { if (timer.current)
        clearTimeout(timer.current); timer.current = null; };
    const lock = () => { if (busyRef.current)
        return false; busyRef.current = true; setBusy(true); stopTimer(); setError(""); return true; };
    const unlock = () => { busyRef.current = false; if (mounted.current) {
        setBusy(false);
        setBlocked(false);
    } };
    const refresh = useCallback(async () => {
        const context = await read<{
            matter: {
                title: string;
            };
            daks: Array<{
                dakId: string;
                diaryNumber: string;
            }>;
        }>(`/api/matters/${matterId}/context`);
        const did = selectedDakId || (context.daks.length === 1 ? context.daks[0].dakId : null);
        const [ws, draft] = await Promise.all([api.getNotingWorkspace(matterId, did), api.getWorkingDraft(matterId)]);
        if (!mounted.current)
            return;
        setWorkspace(ws);
        setRevision(draft.etagRevision);
        setHeading(context.matter.title);
        setLinkedDaks(context.daks);
        if (did !== selectedDakId)
            setSelectedDakId(did);
        setDocumentId(old => ws.documents.some(d => d.id === old && d.mime === "application/pdf") ? old : ws.documents.find(d => d.mime === "application/pdf")?.id || "");
        if (did) {
            const [detail, movement] = await Promise.all([read<{
                    id: string;
                    diaryNumber: string;
                    revision: number;
                }>(`/api/dak/${did}`), read<{
                    items: Transfer[];
                }>(`/api/dak/${did}/transfers`)]);
            if (mounted.current)
                setDak({ ...detail, transfers: movement.items });
        }
        else
            setDak(null);
    }, [matterId, selectedDakId]);
    useEffect(() => { mounted.current = true; void refresh().catch(e => setError(e.message)); return () => { mounted.current = false; stopTimer(); }; }, [refresh]);
    useEffect(() => {
        if (!publishedSheetId)
            return;
        let active = true;
        void api.getGreenSheet(publishedSheetId).then(result => {
            if (result.matterId !== matterId)
                throw new Error("Published Legal sheet belongs to a different Matter.");
            if (active) {
                setSheet(result);
                setSheetOpen(true);
            }
        }).catch(e => { if (active)
            setError(e.message); });
        return () => { active = false; };
    }, [matterId, publishedSheetId]);
    useEffect(() => { const warn = (event: BeforeUnloadEvent) => { if (command.current || status.startsWith("Unsaved") || busyRef.current) {
        event.preventDefault();
    } }; window.addEventListener("beforeunload", warn); return () => window.removeEventListener("beforeunload", warn); }, [status]);
    const open = async () => {
        if (!lock())
            return;
        try {
            const current = await api.getWorkingDraft(matterId);
            setRevision(current.etagRevision);
            // The server resolves its current working draft. After submission it is
            // cleared, so a new note must never auto-copy a submitted snapshot.
            const s = await api.createOfficeSession(matterId, sourceDak, null, current.etagRevision, "ContinuousGreenEnvelopeV2");
            updateSession(s);
            dirtyGeneration.current = 0;
            saveGeneration.current = 0;
            command.current = null;
            terminalFailure.current = false;
            setReady(false);
            setPreview(null);
            setConfirmed(false);
            setReviewOpen(false);
            setSheet(null);setSheetOpen(false);
            setAccepted(false);
            setSheetOpen(false);
            setStatus("Server snapshot restored; new editor session opening.");
        }
        catch (e) {
            setError((e as Error).message);
        }
        finally {
            unlock();
        }
    };
    const capture = async () => {
        if (!bridge.current)
            throw new Error("Native editor is not ready.");
        setBlocked(true);
        return bridge.current.quiesceAndExport();
    };
    const save = async (review = false, retry = false) => {
        if (!sessionRef.current || !lock())
            return;
        if (review)
            setMode("split");
        try {
            let s = sessionRef.current;
            if (s.state !== "Active") {
                if (review && (s.state === "Frozen" || s.state === "Reviewed")) {
                    const frozen = await api.getFrozenPreview(s.id);
                    updateSession(frozen.session);
                    setPreview(frozen.preview);
                    setReviewOpen(true);
                    setStatus("Frozen canonical review verified by server.");
                    return;
                }
                throw new Error("Editing lease is not active. Reopen the server draft.");
            }
            let nativeUrl: string;
            let receipt: OfficeSaveReceiptDto | null = null;
            let noChanges = false;
            const covered = dirtyGeneration.current;
            if (retry && command.current) {
                setBlocked(true);
                setStatus("Retrying the same server save request…");
                const c = command.current;
                const initial = await api.requestSave(c.session, c.generation, c.revision, c.key, c.url);
                receipt = await awaitCommittedOfficeSave(initial, () => api.getSaveStatus(c.session, initial.id), r => { terminalFailure.current = r.state === "Failed"; updateSession({ ...s, revision: r.sessionRevision, latestSnapshotId: r.snapshotId || s.latestSnapshotId }); });
                command.current = null;
                if (dirtyGeneration.current !== c.captureGeneration)
                    throw new Error("Retry committed older captured content. Save current edits before Review.");
                nativeUrl = await capture();
            }
            else {
                if (command.current && !terminalFailure.current)
                    throw new Error("Resolve the pending save with Retry save before issuing another command.");
                setStatus("Checking actual native content against the server…");
                nativeUrl = await capture();
                const live = await api.getSessionConfig(s.id);
                s = live.session;
                updateSession(s);
                const resolution = validateOfficeSaveResolution(await api.checkNativeSave(s.id, nativeUrl, s.revision), s.id, s.revision);
                if (resolution.noChanges) {
                    noChanges = true;
                    receipt = resolution.committedReceipt;
                    setStatus(receipt ? "No new changes to save." : "No new changes to save. Server snapshot restored.");
                }
                else {
                    const c: SaveCommand = terminalFailure.current && command.current?.captureGeneration === covered ? command.current : { session: s.id, revision: s.revision, generation: ++saveGeneration.current, key: generateUuid(), url: nativeUrl, captureGeneration: covered };
                    command.current = c;
                    terminalFailure.current = false;
                    setStatus("Saving to server — waiting for committed callback…");
                    const initial = await api.requestSave(c.session, c.generation, c.revision, c.key, c.url);
                    receipt = await awaitCommittedOfficeSave(initial, () => api.getSaveStatus(c.session, initial.id), r => { terminalFailure.current = r.state === "Failed"; updateSession({ ...s, revision: r.sessionRevision, latestSnapshotId: r.snapshotId || s.latestSnapshotId }); });
                }
            }
            if (receipt) {
                updateSession({ ...s, revision: receipt.sessionRevision, latestSnapshotId: receipt.snapshotId });
                command.current = null;
                terminalFailure.current = false;
                if (!noChanges) {
                    const currentUrl = await capture();
                    const proof = validateOfficeSaveResolution(await api.checkNativeSave(s.id, currentUrl, receipt.sessionRevision), s.id, receipt.sessionRevision);
                    if (!proof.noChanges || proof.snapshotId !== receipt.snapshotId || proof.committedReceipt?.id !== receipt.id)
                        throw new Error("Unsaved native changes differ from the committed callback. Save current content before Review.");
                    setStatus(`Saved to server: v${receipt.version} · ${receipt.sha256!.slice(0, 12)}`);
                }
            }
            command.current = null;
            terminalFailure.current = false;
            if (dirtyGeneration.current !== covered)
                throw new Error("Unsaved edits arrived after capture. Save again before Review.");
            if (review) {
                // Capture again while still blocked; changed content in another tab must fail server comparison.
                const fresh = await capture();
                setStatus("Verifying and freezing canonical review…");
                const p = await api.createPreview(s.id, receipt?.id || null, sessionRef.current!.revision, fresh);
                const frozen = await api.getFrozenPreview(s.id);
                updateSession(frozen.session);
                setPreview(p);
                setAccepted(false);
                setConfirmed(false);
                setReviewOpen(true);
                setStatus("Frozen canonical review verified by server.");
            }
        }
        catch (e) {
            // An uncertain Preview response may already have frozen the lease. Recover its exact read-only record.
            if (review) {
                try {
                    const frozen = await api.getFrozenPreview(sessionRef.current!.id);
                    updateSession(frozen.session);
                    setPreview(frozen.preview);
                    setReviewOpen(true);
                    setStatus("Frozen canonical review recovered from server.");
                    return;
                }
                catch { /* No frozen server proof. */ }
            }
            setError((e as Error).message);
            setStatus(`Save / review failed: ${(e as Error).message}`);
        }
        finally {
            unlock();
        }
    };
    saveAction.current = save;
    const changed = (dirty: boolean) => {
        if (!dirty || sessionRef.current?.state !== "Active")
            return;
        dirtyGeneration.current++;
        setStatus("Unsaved changes — awaiting server autosave.");
        stopTimer();
        if (!command.current && !busyRef.current)
            timer.current = setTimeout(() => void saveAction.current(), 8000);
    };
    const confirm = async () => {
        if (!preview || !accepted || !lock())
            return;
        try {
            const signature = JSON.stringify({ preview: preview.id, hash: preview.canonicalHash, revision });
            if (confirmKey.current?.signature !== signature)
                confirmKey.current = { signature, key: generateUuid() };
            const draft = await api.confirmPreview(matterId, preview.id, preview.canonicalHash, false, revision, confirmKey.current.key);
            setRevision(draft.revision);
            setConfirmed(true);
            const frozen = await api.getFrozenPreview(preview.sessionId);
            updateSession(frozen.session);
            setNotice("Canonical text confirmed. Official submission is still required.");
        }
        catch (e) {
            setError((e as Error).message);
        }
        finally {
            unlock();
        }
    };
    const submitKey = useRef<{
        signature: string;
        key: string;
    } | null>(null);
    const submit = async () => {
        if (!preview || !confirmed || !lock())
            return;
        try {
            const payload = { sourceDakId: sourceDak, officePreviewId: preview.id, canonicalHash: preview.canonicalHash };
            const signature = JSON.stringify({ payload, revision });
            if (submitKey.current?.signature !== signature)
                submitKey.current = { signature, key: generateUuid() };
            const response = await fetch(`/api/matters/${matterId}/noting/submit`, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "Idempotency-Key": submitKey.current.key, "If-Match": `"${revision}"` }, body: JSON.stringify(payload) });
            const result = await response.json();
            if (!response.ok)
                throw new Error(result.message || "Official submission rejected.");
            setNotice(`Official Note #${submittedNoteNumber(result)} submitted.`);
            setStatus(`Official Note #${submittedNoteNumber(result)} submitted. Working lease closed.`);
            updateSession({ ...sessionRef.current!, state: "Submitted" });
            setReviewOpen(false);
            setSheet(null);setSheetOpen(false);
            setConfirmed(false);
            setPreview(null);
            await refresh();
        }
        catch (e) {
            setError((e as Error).message);
        }
        finally {
            unlock();
        }
    };
    const received = dak?.transfers.find(t => t.state === "Pending" && t.toUserId === user?.id);
    const receive = async () => {
        if (!dak || !received || !lock())
            return;
        try {
            if (received.includesPhysicalOriginal)
                throw new Error("Physical-original receipt requires confirmation in the Dak workspace.");
            if (receiveCommand.current?.transfer !== received.id)
                receiveCommand.current = { key: generateUuid(), revision: dak.revision, transfer: received.id };
            const c = receiveCommand.current;
            const response = await fetch(`/api/dak/${dak.id}/transfers/${received.id}/receive`, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json", "Idempotency-Key": c.key }, body: JSON.stringify({ expectedRevision: c.revision, physicalReceiptConfirmed: false }) });
            if (!response.ok) {
                const b = await response.json();
                throw new Error(b.message || "Dak receipt rejected.");
            }
            receiveCommand.current = null;
            setNotice("Dak received. Custody updated by the server.");
            await refresh();
        }
        catch (e) {
            setError((e as Error).message);
        }
        finally {
            unlock();
        }
    };
    const physicalSheet = async () => {
        if (!lock())
            return;
        try {
            const result = await api.createGreenSheet(matterId, confirmed ? preview?.id || null : null);
            setSheet(result);
            setSheetOpen(true);
        }
        catch (e) {
            setError((e as Error).message);
        }
        finally {
            unlock();
        }
    };
    const adjust = (clientX: number) => { const r = panels.current?.getBoundingClientRect(); if (r)
        setDivider(Math.max(30, Math.min(75, (clientX - r.left) * 100 / r.width))); };
    const document = workspace?.documents.find(d => d.id === documentId);
    const frozen = session?.state !== "Active";
    const canonicalReview = reviewOpen && preview && <div className="v4-review-surface"><section className="v4-review-card" role="region" aria-label="Canonical Review"><h2>Canonical Review — current note only</h2><p>Editor is frozen. Check the exact text that will enter the immutable official record.</p><pre>{preview.canonicalText}</pre><small>Canonical SHA-256: {preview.canonicalHash}</small><label><input type="checkbox" checked={accepted} onChange={e => setAccepted(e.target.checked)} disabled={confirmed}/> I have reviewed this exact canonical text.</label><div className="v4-dialog-actions"><button onClick={() => setReviewOpen(false)}>Close review</button>{!confirmed ? <button disabled={!accepted || busy} onClick={() => void confirm()}>Confirm canonical text</button> : <><button disabled={busy || !workspace?.capabilities.canSubmit} onClick={() => void submit()}>Submit official Note</button><button disabled={busy || !workspace?.capabilities.canSend || !sourceDak} onClick={() => setSendOpen(true)}>Submit & Send / Return</button></>}</div></section></div>;
    return <main className="v4-workspace">
    <header className="v4-header"><Link to={`/matters/${matterId}`}>← Matter</Link><div><h1>{heading}</h1><span>{dak?.diaryNumber || "Controlling Dak"} · Green Legal Noting · {workspace ? workspace.notes.length : "…"} official notes</span></div><div className="v4-officer">{user?.displayName}<small>{user?.designation?.name}</small></div><button disabled={busy || !!command.current || status.startsWith("Unsaved")} onClick={() => void logout()}>Sign out</button></header>
    <div className="v4-command-bar"><div role="group" aria-label="Workspace view">{([["noting", "Noting Focus"], ["split", "Split View"], ["document", "Document Focus"]] as const).map(([v, t]) => <button key={v} aria-pressed={mode === v} onClick={() => setMode(v)}>{t}</button>)}</div>
      {linkedDaks.length > 1 && <label>Dak <select aria-label="Controlling Dak" value={selectedDakId || ""} disabled={busy || session?.state === "Active"} onChange={e => setSelectedDakId(e.target.value || null)}><option value="">Choose controlling Dak</option>{linkedDaks.map(d => <option key={d.dakId} value={d.dakId}>{d.diaryNumber}</option>)}</select></label>}
      {received && <button disabled={busy} onClick={() => void receive()}>Receive Dak</button>}
      <button disabled={busy || !workspace?.capabilities.canEditDraft} onClick={() => void open()}>{session ? "Reopen server draft" : "Open Green Noting"}</button>
      <button disabled={busy || !ready || frozen || (!!command.current && !terminalFailure.current)} onClick={() => void save()}>Save</button>
      {command.current && <button disabled={busy} onClick={() => void save(false, true)}>Retry save</button>}
      <button disabled={busy || !session || !["Active","Frozen","Reviewed"].includes(session.state) || (!ready && !frozen) || !!command.current} onClick={() => void save(true)}>Canonical Review</button>
      <button disabled={busy || !workspace?.notes.length} onClick={() => setHistoryOpen(true)}>Official history</button>
      <button disabled={busy || (!workspace?.notes.length && !confirmed)} onClick={() => void physicalSheet()}>Legal PDF / Print</button>
    </div>
    <div className="v4-save-line"><span role="status" aria-live="polite">{status}</span><small>Server autosave · Browser draft caching disabled · Unsaved typing is lost on restart</small></div>
    {error && <div className="v4-error" role="alert">{error}<button onClick={() => setError("")} aria-label="Dismiss error">×</button></div>}
    {notice && <div className="v4-notice" role="status">{notice}<button onClick={() => setNotice("")} aria-label="Dismiss notice">×</button></div>}
    <div className={`v4-panels v4-mode-${mode}`} ref={panels} style={{ gridTemplateColumns: mode === "split" ? `${divider}fr 8px ${100 - divider}fr` : undefined }}>
      <section className="v4-native-panel" aria-label="Continuous Green Noting"><div className="v4-panel-title"><strong>Continuous Green Note Sheet</strong><span>Legal · 45 / 15 mm mirror margins</span>{sheetOpen && <button onClick={() => setSheetOpen(false)}>Back to native editor</button>}</div>
        <div className="v4-native-surface">{session && session.state!=="Submitted" ? <OnlyOfficeNoteFrame sessionId={session.id} isFrozen={frozen} isInputBlocked={blocked} onDocumentReady={() => { setReady(true); setStatus("Native editor ready. Server save has not been requested."); }} onDocumentStateChange={changed} onNativeBridgeReady={b => { bridge.current = b;if(!b)setReady(false); }} onError={message=>{setError(message);setReady(false);setStatus("Editor unavailable. Last server commit retained.");}}/> : <div className="v4-empty"><h2>One official continuous note sheet</h2><p>Open your authorized server draft to write below the immutable submitted notes.</p>{received && <p>Receive the nominated Dak first.</p>}<p>The native editor uses the official Legal margins. An aligned native red guide is unavailable. Accurate screen guides are available on the verified read-only Legal PDF.</p></div>}
        {sheetOpen && sheet && <div className="v4-sheet-surface"><AuthorizedPdfPanel url={sheet.pdfUrl} title={`Legal snapshot · ${new Date(sheet.createdAt).toLocaleString()}`} guides={sheet.screenOnlyGuides}/></div>}{canonicalReview}</div>
      </section>
      <div className="v4-divider" role="separator" aria-label="Resize noting and document panels" aria-orientation="vertical" aria-valuemin={30} aria-valuemax={75} aria-valuenow={Math.round(divider)} tabIndex={0} onKeyDown={e => { if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
        e.preventDefault();
        setDivider(v => Math.max(30, Math.min(75, v + (e.key === "ArrowLeft" ? -2 : 2))));
    } }} onPointerDown={e => { e.currentTarget.setPointerCapture(e.pointerId); adjust(e.clientX); }} onPointerMove={e => { if (e.currentTarget.hasPointerCapture(e.pointerId))
        adjust(e.clientX); }} onPointerUp={e => { if (e.currentTarget.hasPointerCapture(e.pointerId))
        e.currentTarget.releasePointerCapture(e.pointerId); }}/>
      <section className="v4-source-panel" aria-label="Authorized original documents"><div className="v4-panel-title"><label>Original PDF <select aria-label="Original PDF document" value={documentId} onChange={e => setDocumentId(e.target.value)}>{workspace?.documents.filter(d => d.mime === "application/pdf").map(d => <option key={d.id} value={d.id}>{d.displayName || d.filename} · v{d.version}</option>)}</select></label></div>{document ? <AuthorizedPdfPanel key={document.id} url={document.contentUrl} title={document.displayName || document.filename}/> : <div className="v4-empty">{workspace ? "No authorized original PDF is linked to this Matter." : "Loading authorized documents…"}</div>}</section>
    </div>
    {sendOpen && preview && <SendReturnModal matterId={matterId} sourceDakId={sourceDak} officePreviewId={preview.id} canonicalHash={preview.canonicalHash} workingRevision={revision} onClose={() => setSendOpen(false)} onSent={r => { setNotice(`Official Note #${r.noteNumber} submitted and Dak dispatched.`);setStatus(`Official Note #${r.noteNumber} submitted. Dak dispatched.`); updateSession({ ...sessionRef.current!, state: "Submitted" }); setReviewOpen(false);setSheet(null);setSheetOpen(false); setPreview(null); setConfirmed(false); void refresh().catch(e => setError(e.message)); }}/>}
    {historyOpen && <div className="v4-overlay"><section className="v4-dialog" role="dialog" aria-modal="true" aria-label="Immutable official notes"><h2>Immutable official history</h2>{workspace?.notes.map(n => <article key={n.id}><h3>Note #{n.number} — {n.authorName}, {n.designation}</h3><small>{new Date(n.submittedAt).toLocaleString()} · {n.textHash}</small><pre>{n.canonicalText}</pre></article>)}<button onClick={() => setHistoryOpen(false)}>Close history</button></section></div>}
  </main>;
}
