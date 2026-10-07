import React, { useState, useEffect } from "react";
import type { DakDetail } from "./types";

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
  onClose: () => void;
  onSuccess: () => void;
}

function getUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}

export const DakMovementModal: React.FC<Props> = ({
  dak,
  mode,
  transferId,
  isInitialMark = false,
  activeTransferIncludesPhysical = false,
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
  const [destinationKind, setDestinationKind] = useState<"Officer" | "RecordRoom">("Officer");
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

  // Active transfer reference
  const effectiveTransferId = transferId || dak.pendingTransferId || undefined;

  // Load active desks and members if in send mode
  useEffect(() => {
    if (isSendMode) {
      fetch(`/api/dak/${dak.id}/movement-targets`, { credentials: "include" })
        .then((res) => {
          if (!res.ok) throw new Error("Failed to load available office desks.");
          return res.json() as Promise<{ desks: TargetDeskOption[] }>;
        })
        .then((data) => {
          const activeDesks = data.desks.filter((d) => d.isActive);
          setDesks(activeDesks);
          if (activeDesks.length > 0) {
            setSelectedDeskId(activeDesks[0].id);
            // Pre-select primary member if available
            const primary = activeDesks[0].members.find((m) => m.isPrimary) || activeDesks[0].members[0];
            if (primary) setSelectedUserId(primary.userId);
          }
        })
        .catch(() => setError("Failed to load available office desks."));
    }
  }, [isSendMode, dak.id]);

  const selectedDesk = desks.find((d) => d.id === selectedDeskId);
  const members = selectedDesk ? selectedDesk.members : [];

  // When desk changes, adjust destinationKind and default user
  const handleDeskChange = (newDeskId: string) => {
    setSelectedDeskId(newDeskId);
    const d = desks.find((item) => item.id === newDeskId);
    if (d?.purpose === "RecordRoom") {
      setDestinationKind("RecordRoom");
    } else {
      setDestinationKind("Officer");
    }
    const primary = d?.members.find((m) => m.isPrimary) || d?.members[0];
    setSelectedUserId(primary ? primary.userId : "");
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const idempotencyKey = getUuid();

      if (isSendMode) {
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
      setLoading(false);
    }
  };

  const getModalTitle = () => {
    if (isSendMode) {
      return isInitialMark ? "Initial Mark to Desk & Officer" : "Send / Forward to Next Officer";
    }
    if (isResolveMode) return "Resolve Dak";
    if (mode === "receive") return "Acknowledge Receipt of Dak";
    if (mode === "pull-back") return "Pull Back Dak from Recipient";
    if (mode === "confirm-return") return "Confirm Physical File Return";
    if (mode === "reopen") return "Reopen Resolved Dak";
    if (mode === "cancel") return "Cancel / Annul Dak";
    return "Dak Action";
  };

  return (
    <div className="modal-backdrop">
      <div className="modal-card">
        <h3>{getModalTitle()}</h3>
        <p className="subtext">
          Diary No: <strong>{dak.diaryNumber}</strong> — {dak.subject}
        </p>

        {error && <div className="state error">{error}</div>}

        <form onSubmit={handleSubmit}>
          {/* MODE: SEND / MARK */}
          {isSendMode && (
            <>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Movement Action</label>
                <select
                  value={action}
                  onChange={(e) => setAction(e.target.value as "Marked" | "Forwarded" | "Returned")}
                  disabled={loading}
                >
                  {isInitialMark ? (
                    <option value="Marked">Mark to Desk & Officer (Initial)</option>
                  ) : (
                    <>
                      <option value="Forwarded">Forward / Send to Next Officer</option>
                      <option value="Returned">Return to Officer</option>
                    </>
                  )}
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Target Office Desk *</label>
                <select
                  value={selectedDeskId}
                  onChange={(e) => handleDeskChange(e.target.value)}
                  disabled={loading}
                  required
                >
                  {desks.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code}) {d.purpose === "RecordRoom" ? "[Record Room]" : ""}
                    </option>
                  ))}
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Select Officer / Recipient *</label>
                <select
                  value={selectedUserId}
                  onChange={(e) => setSelectedUserId(e.target.value)}
                  disabled={loading}
                  required
                >
                  <option value="">-- Select Officer / Recipient --</option>
                  {members.map((m) => (
                    <option key={m.userId} value={m.userId}>
                      {m.displayName} {m.designation ? `(${m.designation})` : ""} {m.isPrimary ? "★ Primary" : ""}
                    </option>
                  ))}
                </select>
              </div>

              {selectedDesk && selectedUserId && (
                <div
                  className="desk-context-note"
                  style={{
                    marginBottom: "14px",
                    padding: "10px 12px",
                    background: "#f1f5f9",
                    borderRadius: "6px",
                    fontSize: "12px",
                    color: "#334155"
                  }}
                >
                  Dispatch will nominate <strong>{members.find((m) => m.userId === selectedUserId)?.displayName}</strong> at{" "}
                  <strong>{selectedDesk.name}</strong>. The Dak will remain In Transit until accepted.
                </div>
              )}

              {/* Physical file transit toggle */}
              <div
                className="physical-transit-choice"
                style={{
                  margin: "14px 0",
                  padding: "12px",
                  background: "#f8fafc",
                  borderRadius: "6px",
                  border: "1px solid #cbd5e1"
                }}
              >
                <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", fontWeight: 600 }}>
                  <input
                    type="checkbox"
                    checked={sendPhysicalFile}
                    onChange={(e) => setSendPhysicalFile(e.target.checked)}
                    disabled={loading}
                    style={{ marginTop: "3px" }}
                  />
                  <div>
                    <div>Physical file also being sent</div>
                    <div style={{ fontSize: "12px", fontWeight: "normal", color: "#64748b", marginTop: "2px" }}>
                      If checked, physical paper custody moves to In Transit and transfers to the recipient upon acknowledgment.
                    </div>
                  </div>
                </label>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Instructions / Purpose for Recipient (Optional)</label>
                <textarea
                  placeholder="Specific actions requested or directives (e.g. 'Please examine and put up report')..."
                  value={instructions}
                  onChange={(e) => setInstructions(e.target.value)}
                  disabled={loading}
                  rows={3}
                  maxLength={1000}
                />
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Official Noting / Remarks (Optional)</label>
                <textarea
                  placeholder="File noting or background remarks accompanying this movement..."
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  disabled={loading}
                  rows={3}
                  maxLength={1000}
                />
              </div>
            </>
          )}

          {/* MODE: RESOLVE */}
          {isResolveMode && (
            <>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Resolution Remarks / Final Noting *</label>
                <textarea
                  placeholder="Detail final action taken or reference to resolution order/note..."
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  disabled={loading}
                  required
                  rows={4}
                  maxLength={1000}
                />
              </div>

              <div
                style={{
                  margin: "14px 0",
                  padding: "12px",
                  background: "#f0fdf4",
                  borderRadius: "6px",
                  border: "1px solid #bbf7d0"
                }}
              >
                <label style={{ display: "flex", alignItems: "center", gap: "10px", cursor: "pointer", fontWeight: 600, color: "#166534" }}>
                  <input
                    type="checkbox"
                    checked={completionAttested}
                    onChange={(e) => setCompletionAttested(e.target.checked)}
                    disabled={loading}
                    required
                  />
                  <span>✓ I attest that all necessary official action on this Dak has been completed.</span>
                </label>
              </div>
            </>
          )}

          {/* MODE: RECEIVE */}
          {mode === "receive" && (
            <div style={{ marginBottom: "14px" }}>
              <p style={{ fontSize: "13px", color: "#334155", lineHeight: 1.5, marginBottom: "12px" }}>
                Confirming receipt will transfer operational custody of this Dak to you. You will become the confirmed active holder.
              </p>

              {activeTransferIncludesPhysical && (
                <div
                  style={{
                    padding: "12px",
                    background: "#fffbeb",
                    borderRadius: "6px",
                    border: "1px solid #fde68a",
                    marginBottom: "12px"
                  }}
                >
                  <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", fontWeight: 600, color: "#92400e" }}>
                    <input
                      type="checkbox"
                      checked={physicalReceiptConfirmed}
                      onChange={(e) => setPhysicalReceiptConfirmed(e.target.checked)}
                      disabled={loading}
                      required
                      style={{ marginTop: "3px" }}
                    />
                    <div>
                      <div>I confirm physical receipt of the paper file original *</div>
                      <div style={{ fontSize: "12px", fontWeight: "normal", color: "#b45309", marginTop: "2px" }}>
                        The sender marked the physical original file along with this digital dispatch.
                      </div>
                    </div>
                  </label>
                </div>
              )}
            </div>
          )}

          {/* MODE: PULL BACK */}
          {mode === "pull-back" && (
            <div className="field-group" style={{ marginBottom: "14px" }}>
              <label>Mandatory Pull-Back Reason *</label>
              <textarea
                placeholder="State the reason why you are pulling back this unreceived dispatch..."
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={loading}
                required
                rows={4}
                maxLength={1000}
              />
            </div>
          )}

          {/* MODE: CONFIRM PHYSICAL RETURN */}
          {mode === "confirm-return" && (
            <div className="field-group" style={{ marginBottom: "14px" }}>
              <label>Physical Recovery & Provenance Note *</label>
              <textarea
                placeholder="Record where and in what condition the physical paper file was recovered and stored..."
                value={provenance}
                onChange={(e) => setProvenance(e.target.value)}
                disabled={loading}
                required
                rows={4}
                maxLength={1000}
              />
            </div>
          )}

          {/* MODE: REOPEN */}
          {mode === "reopen" && (
            <div className="field-group" style={{ marginBottom: "14px" }}>
              <label>Supervisory Reopening Reason *</label>
              <textarea
                placeholder="Detail administrative justification for reopening this resolved Dak..."
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={loading}
                required
                rows={4}
                maxLength={1000}
              />
            </div>
          )}

          {/* MODE: CANCEL */}
          {mode === "cancel" && (
            <div className="field-group" style={{ marginBottom: "14px" }}>
              <label>Reason for Cancellation / Annulment *</label>
              <textarea
                placeholder="State the reason why this inward entry is being cancelled..."
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                disabled={loading}
                required
                rows={4}
                maxLength={1000}
              />
            </div>
          )}

          <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "20px" }}>
            <button type="button" className="secondary-button" onClick={onClose} disabled={loading}>
              Cancel
            </button>
            <button
              type="submit"
              className="primary-button"
              disabled={loading || (isResolveMode && (!remarks.trim() || !completionAttested))}
            >
              {loading ? (
                "Processing..."
              ) : isSendMode ? (
                "Send / Mark Dak"
              ) : isResolveMode ? (
                "Confirm Resolution"
              ) : mode === "receive" ? (
                "Confirm Receipt"
              ) : mode === "pull-back" ? (
                "Confirm Pull Back"
              ) : mode === "confirm-return" ? (
                "Confirm Return"
              ) : mode === "reopen" ? (
                "Confirm Reopen"
              ) : (
                "Confirm Cancellation"
              )}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
