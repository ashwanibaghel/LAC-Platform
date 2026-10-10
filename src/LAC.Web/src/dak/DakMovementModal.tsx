import React, { useState, useEffect, useRef, useId } from "react";
import type { DakDetail } from "./types";
import { createDakRequestId } from "./requestId.js";
import "./movement.css";

interface DeskMemberOption {
  userId: string;
  displayName: string;
  designation?: string | null;
  isPrimary: boolean;
}

interface TargetDeskOption {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  purpose?: string;
  members: DeskMemberOption[];
}

export type MovementModalMode =
  | "move"
  | "send"
  | "dispose"
  | "resolve"
  | "cancel"
  | "receive"
  | "pull-back"
  | "confirm-return"
  | "reopen";

interface Props {
  dak: DakDetail;
  mode: MovementModalMode;
  transferId?: string;
  isInitialMark?: boolean;
  activeTransferIncludesPhysical?: boolean;
  physicalSendAvailable?: boolean;
  onClose: () => void;
  onSuccess: () => void;
}

export const DakMovementModal: React.FC<Props> = ({
  dak,
  mode,
  transferId,
  isInitialMark = false,
  activeTransferIncludesPhysical = false,
  physicalSendAvailable = false,
  onClose,
  onSuccess
}) => {
  const isSendMode = mode === "move" || mode === "send";
  const isResolveMode = mode === "dispose" || mode === "resolve";

  // Move / Send fields
  const [action, setAction] = useState<"Marked" | "Forwarded" | "Returned">(
    isInitialMark ? "Marked" : "Forwarded"
  );
  const [desks, setDesks] = useState<TargetDeskOption[]>([]);
  const [selectedDeskId, setSelectedDeskId] = useState<string>("");
  const [selectedUserId, setSelectedUserId] = useState<string>("");
  const [deskSearch, setDeskSearch] = useState("");
  const [targetsState, setTargetsState] = useState<"loading" | "ready" | "unauthorized" | "error">("loading");
  const [targetsError, setTargetsError] = useState("");
  const [lookupAttempt, setLookupAttempt] = useState(0);
  const [instructions, setInstructions] = useState<string>("");
  const [sendPhysicalFile, setSendPhysicalFile] = useState(false);

  // Common / Reason / Remarks fields
  const [remarks, setRemarks] = useState<string>("");
  const [reason, setReason] = useState<string>("");
  const [provenance, setProvenance] = useState<string>("");
  const [completionAttested, setCompletionAttested] = useState(false);
  const [physicalReceiptConfirmed, setPhysicalReceiptConfirmed] = useState(false);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const submittingRef = useRef(false);
  const dialogRef = useRef<HTMLDivElement>(null);
  const requestRef = useRef<{ signature: string; key: string } | null>(null);
  const fieldPrefix = useId();

  useEffect(() => {
    const previouslyFocused = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    dialogRef.current?.focus();
    return () => { document.body.style.overflow = previousOverflow; previouslyFocused?.focus(); };
  }, []);

  useEffect(() => { if (error) dialogRef.current?.querySelector(".dak-movement-body")?.scrollTo({ top: 0 }); }, [error]);

  // Active transfer reference
  const effectiveTransferId = transferId || dak.pendingTransferId || undefined;

  // Load active desks and members if in send mode
  useEffect(() => {
    const controller = new AbortController();
    setSelectedDeskId(""); setSelectedUserId(""); setDesks([]);
    if (isSendMode) {
      setTargetsState("loading"); setTargetsError("");
      fetch(`/api/dak/${dak.id}/movement-targets`, { credentials: "include", signal: controller.signal })
        .then(async (res) => {
          if (res.status === 401 || res.status === 403) {
            setTargetsState("unauthorized");
            throw new Error(res.status === 401 ? "Your session has expired. Sign in again to continue." : "You are not authorized to mark or send this Dak. Monitoring access does not grant custody.");
          }
          if (!res.ok) throw new Error("Office destinations could not be loaded. Retry or contact the office administrator.");
          return res.json() as Promise<{ desks: TargetDeskOption[] }>;
        })
        .then((data) => {
          if (controller.signal.aborted) return;
          if (!Array.isArray(data.desks) || data.desks.some((d) => !Array.isArray(d.members))) throw new Error("Office destinations returned an invalid response. Please retry.");
          setDesks(data.desks.filter((d) => d.isActive));
          setTargetsState("ready");
        })
        .catch((err: Error) => {
          if (controller.signal.aborted) return;
          setTargetsError(err.message);
          setTargetsState((current) => current === "unauthorized" ? current : "error");
        });
    }
    return () => controller.abort();
  }, [isSendMode, dak.id, lookupAttempt]);

  const selectedDesk = desks.find((d) => d.id === selectedDeskId);
  const members = selectedDesk ? selectedDesk.members : [];
  const selectedMember = members.find((m) => m.userId === selectedUserId);
  const destinationKind = selectedDesk?.purpose === "RecordRoom" ? "RecordRoom" : "Officer";
  const filteredDesks = desks.filter((d) => `${d.name} ${d.code}`.toLowerCase().includes(deskSearch.trim().toLowerCase()));

  // When desk changes, adjust destinationKind and default user
  const handleDeskChange = (newDeskId: string) => {
    setSelectedDeskId(newDeskId);
    setSelectedUserId("");
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (submittingRef.current) return;
    submittingRef.current = true;
    setError(null);
    setLoading(true);

    try {
      const signature = JSON.stringify({ mode, action, selectedDeskId, selectedUserId, destinationKind, sendPhysicalFile, instructions, remarks, reason, provenance, completionAttested, physicalReceiptConfirmed, effectiveTransferId, revision: dak.revision });
      if (requestRef.current?.signature !== signature) requestRef.current = { signature, key: createDakRequestId() };
      const idempotencyKey = requestRef.current.key;

      if (isSendMode) {
        if (targetsState !== "ready" || !selectedDesk || !selectedMember) throw new Error("Choose an available office desk and one eligible recipient before sending.");
        if (sendPhysicalFile && !physicalSendAvailable) throw new Error("Confirm the physical original is in your custody before sending it.");
        if (!selectedDeskId) {
          setError("Please select a target office desk.");
          setLoading(false);
          return;
        }
        if (!selectedUserId) {
          setError("Target officer is required. A Dak must be dispatched to exactly one recipient officer.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/transfers`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            action,
            toDeskId: selectedDeskId,
            toUserId: selectedUserId,
            destinationKind,
            includesPhysicalOriginal: sendPhysicalFile,
            expectedRevision: dak.revision,
            remarks: remarks.trim() || null,
            instructions: instructions.trim() || null,
            remarksKind: "Text",
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          if (res.status === 409) {
            throw new Error("This Dak was modified elsewhere. Please refresh before proceeding.");
          }
          throw new Error(data?.detail || data?.message || "Failed to dispatch Dak.");
        }
      } else if (isResolveMode) {
        if (!remarks.trim()) {
          setError("Resolution remarks / noting is mandatory.");
          setLoading(false);
          return;
        }
        if (!completionAttested) {
          setError("You must attest that all necessary official action on this Dak is complete.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/resolve`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            completionAttested: true,
            remarks: remarks.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          if (res.status === 409) {
            throw new Error("This Dak was modified elsewhere. Please refresh before proceeding.");
          }
          throw new Error(data?.detail || data?.message || "Failed to resolve Dak.");
        }
      } else if (mode === "receive") {
        if (!effectiveTransferId) {
          throw new Error("No pending transfer ID found to receive.");
        }
        if (activeTransferIncludesPhysical && !physicalReceiptConfirmed) {
          setError("Physical receipt confirmation is required because the physical file was included.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/transfers/${effectiveTransferId}/receive`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            expectedRevision: dak.revision,
            physicalReceiptConfirmed: Boolean(activeTransferIncludesPhysical && physicalReceiptConfirmed),
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          throw new Error(data?.detail || data?.message || "Failed to acknowledge receipt of Dak.");
        }
      } else if (mode === "pull-back") {
        if (!effectiveTransferId) {
          throw new Error("No pending transfer ID found to pull back.");
        }
        if (!reason.trim()) {
          setError("Mandatory pull-back reason is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/transfers/${effectiveTransferId}/pull-back`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            reason: reason.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          throw new Error(data?.detail || data?.message || "Failed to pull back Dak.");
        }
      } else if (mode === "confirm-return") {
        if (!effectiveTransferId) {
          throw new Error("No transfer found to confirm return.");
        }
        if (!provenance.trim()) {
          setError("Mandatory physical return provenance note is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/transfers/${effectiveTransferId}/confirm-return`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            provenance: provenance.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          throw new Error(data?.detail || data?.message || "Failed to confirm physical return.");
        }
      } else if (mode === "reopen") {
        if (!reason.trim()) {
          setError("Mandatory reopening reason is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/reopen`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Idempotency-Key": idempotencyKey,
          },
          credentials: "include",
          body: JSON.stringify({
            reason: reason.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          throw new Error(data?.detail || data?.message || "Failed to reopen Dak.");
        }
      } else if (mode === "cancel") {
        if (!reason.trim()) {
          setError("Mandatory cancellation reason is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/cancel`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "If-Match": `"${dak.revision}"`,
          },
          credentials: "include",
          body: JSON.stringify({
            reason: reason.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          throw new Error(data?.detail || data?.message || "Failed to cancel Dak.");
        }
      }

      onSuccess();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "An unexpected error occurred.");
    } finally {
      submittingRef.current = false;
      setLoading(false);
    }
  };

  const getModalTitle = () => {
    if (isSendMode) {
      return isInitialMark ? "Initial Mark to Desk & Officer" : action === "Returned" ? "Return to Officer" : "Send / Forward to Next Officer";
    }
    if (isResolveMode) return "Resolve Dak";
    if (mode === "receive") return "Acknowledge Receipt of Dak";
    if (mode === "pull-back") return "Pull Back Dak from Recipient";
    if (mode === "confirm-return") return "Confirm Physical File Return";
    if (mode === "reopen") return "Reopen Resolved Dak";
    if (mode === "cancel") return "Cancel / Annul Dak";
    return "Dak Action";
  };

  const submitDisabled = loading || (isSendMode && (targetsState !== "ready" || !selectedDesk || !selectedMember || (sendPhysicalFile && !physicalSendAvailable)))
    || (isResolveMode && (!remarks.trim() || !completionAttested))
    || (mode === "receive" && (!effectiveTransferId || (activeTransferIncludesPhysical && !physicalReceiptConfirmed)))
    || ((mode === "pull-back" || mode === "reopen" || mode === "cancel") && !reason.trim())
    || (mode === "confirm-return" && !provenance.trim());
  const closeDialog = () => { if (!submittingRef.current) onClose(); };
  const handleDialogKey = (event: React.KeyboardEvent) => {
    if (event.key === "Escape") { event.stopPropagation(); event.preventDefault(); closeDialog(); }
    if (event.key !== "Tab") return;
    const focusable = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>("button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href]") || []);
    const first = focusable[0], last = focusable[focusable.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === dialogRef.current)) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && (document.activeElement === last || document.activeElement === dialogRef.current)) { event.preventDefault(); first?.focus(); }
  };
  const fieldId = (name: string) => `${fieldPrefix}-${name}`;

  return (
    <div className="modal-backdrop dak-movement-backdrop">
      <div className="modal-card dak-movement-dialog" role="dialog" aria-modal="true" aria-labelledby={fieldId("title")}
        aria-describedby={fieldId("context")} tabIndex={-1} ref={dialogRef} onKeyDown={handleDialogKey}>
        <header className="dak-movement-header">
          <div className="dak-movement-heading">
            <span className="dak-movement-emblem" aria-hidden="true">↗</span>
            <div><span className="dak-movement-eyebrow">INWARD DAK · OFFICIAL MOVEMENT</span><h2 id={fieldId("title")}>{getModalTitle()}</h2></div>
          </div>
          <button type="button" className="dak-movement-close" aria-label="Close movement dialog" onClick={closeDialog} disabled={loading}>×</button>
          <div className="dak-movement-context" id={fieldId("context")}>
            <strong className="dak-movement-diary">{dak.diaryNumber}</strong><span className="dak-movement-subject" title={dak.subject}>{dak.subject}</span>
            <span className="dak-movement-sender">From {dak.senderName}{dak.senderDepartment ? ` · ${dak.senderDepartment}` : ""}</span>
          </div>
        </header>
        <form onSubmit={handleSubmit} className="dak-movement-form">
          <div className="dak-movement-body" aria-busy={loading}>
            {error && <div className="dak-movement-message error" role="alert">{error}</div>}
            {isSendMode && <>
              <section className="dak-movement-section">
                <div className="dak-movement-section-heading"><span>01</span><h3>Destination & recipient</h3><small>One confirmed holder at a time</small></div>
                <label className="dak-movement-field" htmlFor={fieldId("action")}>Movement Action
                  <select id={fieldId("action")} aria-label="Movement Action" value={action} onChange={(e) => setAction(e.target.value as typeof action)} disabled={loading}>
                    {isInitialMark ? <option value="Marked">Mark to Desk & Officer (Initial)</option> : <><option value="Forwarded">Forward / Send to Next Officer</option><option value="Returned">Return to Officer</option></>}
                  </select>
                </label>
                {targetsState === "loading" && <div className="dak-movement-message" role="status">Loading configured desks and eligible officers…</div>}
                {(targetsState === "unauthorized" || targetsState === "error") && <div className="dak-movement-message error" role="alert"><strong>{targetsState === "unauthorized" ? "Movement access unavailable" : "Destinations could not be loaded"}</strong><p>{targetsError}</p>{targetsState === "error" && <button type="button" className="secondary-button" onClick={() => setLookupAttempt((n) => n + 1)}>Retry destinations</button>}</div>}
                {targetsState === "ready" && desks.length === 0 && <div className="dak-movement-message warning" role="status"><strong>No active Office Desks configured</strong><p>Ask the office administrator to configure an Office Desk and assign an eligible officer with Dak.Receive access and active work responsibility. {isInitialMark ? "This Dak remains Unmarked until it is sent." : "No dispatch is created; current custody stays unchanged."}</p></div>}
                <div className="dak-movement-grid">
                  <div>
                    <label className="dak-movement-field" htmlFor={fieldId("search")}>Search Office Desks
                      <input id={fieldId("search")} type="search" placeholder="Search by desk name or code" value={deskSearch} disabled={loading || targetsState !== "ready" || desks.length === 0}
                        onKeyDown={(e) => { if (e.key === "Enter") e.preventDefault(); }} onChange={(e) => { setDeskSearch(e.target.value); handleDeskChange(""); }} />
                    </label>
                    <label className="dak-movement-field" htmlFor={fieldId("desk")}>Target Office Desk *
                      <select id={fieldId("desk")} aria-label="Target Office Desk *" value={selectedDeskId} onChange={(e) => handleDeskChange(e.target.value)} disabled={loading || targetsState !== "ready" || filteredDesks.length === 0} required>
                        <option value="">{targetsState === "loading" ? "Loading desks…" : desks.length === 0 ? "No active desks available" : filteredDesks.length === 0 ? "No matching desk" : "Choose an Office Desk"}</option>
                        {filteredDesks.map((d) => <option key={d.id} value={d.id}>{d.name} ({d.code}){d.purpose === "RecordRoom" ? " · Record Room" : ""} · {d.members.length} eligible</option>)}
                      </select>
                    </label>
                    {targetsState === "ready" && desks.length > 0 && filteredDesks.length === 0 && <p className="dak-movement-hint" role="status">No desk matches your search. Clear or change the search.</p>}
                  </div>
                  <div>
                    <label className="dak-movement-field" htmlFor={fieldId("recipient")}>Select Officer / Recipient *
                      <select id={fieldId("recipient")} aria-label="Select Officer / Recipient *" value={selectedUserId} onChange={(e) => setSelectedUserId(e.target.value)} disabled={loading || !selectedDesk || members.length === 0} required>
                        <option value="">{!selectedDesk ? "Choose a desk first" : members.length === 0 ? "No eligible officers at this desk" : "Choose a recipient officer"}</option>
                        {members.map((m) => <option key={m.userId} value={m.userId}>{m.displayName}{m.designation ? ` (${m.designation})` : ""}{m.isPrimary ? " · Primary seat" : ""}</option>)}
                      </select>
                    </label>
                    <p className="dak-movement-hint">Only active members authorized to receive Dak at the selected desk are listed.</p>
                    {selectedDesk && members.length === 0 && <div className="dak-movement-message warning" role="status"><strong>No eligible recipient at {selectedDesk.name}</strong><p>The office administrator must assign an active member with receipt access and valid work responsibility. Choose another desk to continue.</p></div>}
                    {selectedDesk?.purpose === "RecordRoom" && <span className="dak-movement-room">Record Room · caretaker acceptance required</span>}
                  </div>
                </div>
              </section>
              <section className="dak-movement-section">
                <div className="dak-movement-section-heading"><span>02</span><h3>Physical original</h3></div>
                <label className={`dak-movement-choice ${sendPhysicalFile ? "selected" : ""}`}>
                  <input type="checkbox" checked={sendPhysicalFile} onChange={(e) => setSendPhysicalFile(e.target.checked)} disabled={loading || !physicalSendAvailable} />
                  <span><strong>Physical file also being sent</strong><small>Paper is In Transit when sent. Physical custody transfers only when the recipient confirms actual receipt.</small></span>
                </label>
                <p className="dak-movement-hint">{physicalSendAvailable ? "Leave unchecked to send the Dak digitally while retaining the recorded paper location." : "To send paper, first record the physical original in your custody at your active desk. Digital dispatch keeps the paper location unchanged."}</p>
              </section>
              <section className="dak-movement-section">
                <div className="dak-movement-section-heading"><span>03</span><h3>Instructions & official noting</h3><small>Optional</small></div>
                <div className="dak-movement-grid">
                  <label className="dak-movement-field" htmlFor={fieldId("instructions")}>Instructions / Purpose for Recipient (Optional)
                    <textarea id={fieldId("instructions")} placeholder="Specific actions requested or directives (e.g. 'Please examine and put up report')..." value={instructions} onChange={(e) => setInstructions(e.target.value)} disabled={loading} rows={3} maxLength={1000} />
                  </label>
                  <label className="dak-movement-field" htmlFor={fieldId("remarks")}>Official Noting / Remarks (Optional)
                    <textarea id={fieldId("remarks")} placeholder="File noting or background remarks accompanying this movement..." value={remarks} onChange={(e) => setRemarks(e.target.value)} disabled={loading} rows={3} maxLength={1000} />
                  </label>
                </div>
              </section>
            </>}
            {!isSendMode && <section className="dak-movement-section">
              <div className="dak-movement-section-heading"><span>✓</span><h3>Confirm official action</h3></div>
              {isResolveMode && <>
                <label className="dak-movement-field">Resolution Remarks / Final Noting *<textarea placeholder="Detail final action taken or reference to resolution order/note..." value={remarks} onChange={(e) => setRemarks(e.target.value)} disabled={loading} required rows={4} maxLength={1000} /></label>
                <label className="dak-movement-choice"><input type="checkbox" checked={completionAttested} onChange={(e) => setCompletionAttested(e.target.checked)} disabled={loading} required /><span>✓ I attest that all necessary official action on this Dak has been completed.</span></label>
              </>}
              {mode === "receive" && <>
                <div className="dak-movement-message"><strong>Receive and become the confirmed holder</strong><p>Confirming receipt transfers operational custody to you. A dispatch alone does not transfer custody.</p></div>
                {activeTransferIncludesPhysical ? <label className="dak-movement-choice warning"><input type="checkbox" checked={physicalReceiptConfirmed} onChange={(e) => setPhysicalReceiptConfirmed(e.target.checked)} disabled={loading} required /><span><strong>I confirm physical receipt of the paper file original *</strong><small>Confirm only after the physical original has actually reached you.</small></span></label> : <p className="dak-movement-hint">This dispatch is digital only. The recorded physical paper location stays unchanged.</p>}
              </>}
              {mode === "pull-back" && <><div className="dak-movement-message warning"><strong>Available only before recipient acceptance</strong><p>If paper has left you, pull-back leaves it Return Pending until you confirm actual recovery.</p></div><label className="dak-movement-field">Mandatory Pull-Back Reason *<textarea placeholder="State the reason why you are pulling back this unreceived dispatch..." value={reason} onChange={(e) => setReason(e.target.value)} disabled={loading} required rows={4} maxLength={1000} /></label></>}
              {mode === "confirm-return" && <><p className="dak-movement-hint">Confirm recovery only after the original paper file is physically back in your custody.</p><label className="dak-movement-field">Physical Recovery & Provenance Note *<textarea placeholder="Record where and in what condition the physical paper file was recovered and stored..." value={provenance} onChange={(e) => setProvenance(e.target.value)} disabled={loading} required rows={4} maxLength={1000} /></label></>}
              {mode === "reopen" && <label className="dak-movement-field">Supervisory Reopening Reason *<textarea placeholder="Detail administrative justification for reopening this resolved Dak..." value={reason} onChange={(e) => setReason(e.target.value)} disabled={loading} required rows={4} maxLength={1000} /></label>}
              {mode === "cancel" && <label className="dak-movement-field">Reason for Cancellation / Annulment *<textarea placeholder="State the reason why this inward entry is being cancelled..." value={reason} onChange={(e) => setReason(e.target.value)} disabled={loading} required rows={4} maxLength={1000} /></label>}
            </section>}
          </div>
          <footer className="modal-actions dak-movement-footer">
            <div className="dak-movement-confirmation" aria-live="polite">
              {isSendMode ? selectedDesk && selectedMember ? <><strong>{action === "Returned" ? "Return to" : "Send to"} {selectedMember.displayName} · {selectedDesk.name}</strong><span>{sendPhysicalFile ? "Digital Dak + physical original · both await receipt" : "Digital Dak only · paper location unchanged"}</span></> : <><strong>Select a desk and recipient to continue</strong><span>No transfer is created until you confirm.</span></> : <><strong>{getModalTitle()}</strong><span>{mode === "receive" && activeTransferIncludesPhysical ? "Digital and physical receipt require your acknowledgment." : "This action is recorded in the immutable movement history."}</span></>}
            </div>
            <div className="dak-movement-buttons">
              <button type="button" className="secondary-button" onClick={closeDialog} disabled={loading}>Cancel</button>
              <button type="submit" className="primary-button" disabled={submitDisabled}>{loading ? "Processing..." : isSendMode ? "Send / Mark Dak" : isResolveMode ? "Confirm Resolution" : mode === "receive" ? "Confirm Receipt" : mode === "pull-back" ? "Confirm Pull Back" : mode === "confirm-return" ? "Confirm Return" : mode === "reopen" ? "Confirm Reopen" : "Confirm Cancellation"}</button>
            </div>
          </footer>
        </form>
      </div>
    </div>
  );
};
