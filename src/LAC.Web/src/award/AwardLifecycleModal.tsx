import { useState, useRef } from "react";
import { landRecordResponse, LandRecordApiError } from "./LandRecordApiError";
import type { AwardLifecycleInput, AwardMutationResult, RecordStatus } from "./landRecordsStaffTypes";

interface AwardLifecycleModalProps {
  awardId: string;
  awardNumber: string;
  currentStatus: RecordStatus;
  currentArchiveKind?: "Archive" | "Void" | null;
  revision: number;
  onClose: () => void;
  onSaved: () => void;
}

export function AwardLifecycleModal({
  awardId,
  awardNumber,
  currentStatus,
  currentArchiveKind,
  revision,
  onClose,
  onSaved,
}: AwardLifecycleModalProps) {
  const isArchived = currentStatus === "Archived";
  const [action, setAction] = useState<"Archive" | "Void" | "Restore">(
    isArchived ? "Restore" : "Archive"
  );
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");
  const mutationIdRef = useRef<string>(crypto.randomUUID());

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!reason.trim()) {
      setErrorMessage("Please enter an official reason for this lifecycle action.");
      return;
    }

    setBusy(true);
    setErrorMessage("");

    try {
      const input: AwardLifecycleInput = {
        mutationId: mutationIdRef.current,
        expectedRevision: revision,
        action,
        reason: reason.trim(),
      };

      const res = await fetch(`/api/awards/${awardId}/lifecycle`, {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(input),
      });

      await landRecordResponse<AwardMutationResult>(res);
      onSaved();
    } catch (err) {
      if (err instanceof LandRecordApiError && err.code === "RevisionConflict") {
        setErrorMessage("Revision conflict: This Award has been updated by another officer. Please reload.");
      } else {
        setErrorMessage(err instanceof Error ? err.message : "Lifecycle transition failed.");
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <aside className="workflow-drawer" aria-label="Award Lifecycle Management" role="dialog" aria-modal="true">
      <section className="workspace-panel" style={{ maxWidth: "560px", width: "100%", margin: "0 auto" }}>
        <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <div>
            <h3 style={{ margin: 0, fontSize: "18px", color: "#0f172a" }}>
              {isArchived ? "Restore Award" : "Award Lifecycle Management"}
            </h3>
            <span style={{ fontSize: "12px", color: "#64748b" }}>
              Award {awardNumber} · Current Status: <strong>{currentStatus}</strong>
              {currentArchiveKind ? ` (${currentArchiveKind})` : ""} · Revision {revision}
            </span>
          </div>
          <button type="button" className="quiet-button" onClick={onClose}>
            ✕ Close
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "16px", marginTop: "14px" }}>
          {!isArchived ? (
            <div>
              <label style={{ display: "block", marginBottom: "6px", fontWeight: 600, fontSize: "13px" }}>
                Select Lifecycle Action *
              </label>
              <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
                <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="radio"
                    name="lifecycleAction"
                    value="Archive"
                    checked={action === "Archive"}
                    onChange={() => setAction("Archive")}
                  />
                  <div>
                    <strong>Archive Award</strong>
                    <div style={{ color: "#64748b", fontSize: "12px" }}>
                      Mark record as administratively archived. Dependent Khasras, documents and history are preserved.
                    </div>
                  </div>
                </label>
                <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", fontSize: "13px", cursor: "pointer" }}>
                  <input
                    type="radio"
                    name="lifecycleAction"
                    value="Void"
                    checked={action === "Void"}
                    onChange={() => setAction("Void")}
                  />
                  <div>
                    <strong>Void Duplicate / Mistaken Entry</strong>
                    <div style={{ color: "#64748b", fontSize: "12px" }}>
                      Mark as voided registration. No destructive delete; existing dependent links remain auditable.
                    </div>
                  </div>
                </label>
              </div>
            </div>
          ) : (
            <div style={{ backgroundColor: "#eff6ff", border: "1px solid #bfdbfe", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#1e40af" }}>
              <strong>Restore Award:</strong> This will return Award {awardNumber} to Active status without rewriting existing history.
            </div>
          )}

          <div>
            <label style={{ display: "block", marginBottom: "6px", fontWeight: 600, fontSize: "13px" }}>
              Officer Reason *
            </label>
            <textarea
              rows={3}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Mandatory administrative justification for this lifecycle transition"
              required
              style={{ width: "100%", padding: "8px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
            />
          </div>

          <div style={{ backgroundColor: "#f8fafc", border: "1px solid #e2e8f0", borderRadius: "6px", padding: "10px 12px", fontSize: "12px", color: "#475569" }}>
            ℹ️ <strong>Audit Guarantee:</strong> Lifecycle operations are audited with your identity, revision token, and reason.
            Ingestion into archived records is blocked.
          </div>

          {errorMessage && (
            <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", borderRadius: "6px", padding: "10px 14px", fontSize: "13px", color: "#b91c1c" }}>
              {errorMessage}
            </div>
          )}

          <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
            <button type="button" className="quiet-button" onClick={onClose} disabled={busy}>
              Cancel
            </button>
            <button
              type="submit"
              disabled={busy || !reason.trim()}
              style={{
                backgroundColor: action === "Restore" ? "#16a34a" : "#dc2626",
                color: "#ffffff",
              }}
            >
              {busy ? "Processing…" : action === "Restore" ? "Restore Award" : `Confirm ${action}`}
            </button>
          </div>
        </form>
      </section>
    </aside>
  );
}
