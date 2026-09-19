import React, { useState, useEffect } from "react";
import type { DakDetail } from "./types";

interface DeskOption {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

interface DeskMemberOption {
  userId: string;
  username: string;
  displayName: string;
  isPrimary: boolean;
  isActive: boolean;
}

interface Props {
  dak: DakDetail;
  mode: "move" | "dispose" | "cancel";
  onClose: () => void;
  onSuccess: () => void;
}

export const DakMovementModal: React.FC<Props> = ({ dak, mode, onClose, onSuccess }) => {
  const isCurrentlyAssigned = dak.currentAssignment !== undefined && dak.currentAssignment !== null;

  // Move fields
  const [action, setAction] = useState<"Marked" | "Forwarded" | "Returned">(
    isCurrentlyAssigned ? "Forwarded" : "Marked"
  );
  const [desks, setDesks] = useState<DeskOption[]>([]);
  const [selectedDeskId, setSelectedDeskId] = useState<string>("");
  const [members, setMembers] = useState<DeskMemberOption[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<string>("");
  const [instructions, setInstructions] = useState<string>("");

  // Dispose / Cancel / Common fields
  const [remarks, setRemarks] = useState<string>("");
  const [reason, setReason] = useState<string>("");

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Load active desks if in move mode
  useEffect(() => {
    if (mode === "move") {
      fetch("/api/admin/desks", { credentials: "include" })
        .then((res) => res.json() as Promise<DeskOption[]>)
        .then((data) => {
          const activeDesks = data.filter((d) => d.isActive);
          setDesks(activeDesks);
          if (activeDesks.length > 0) {
            setSelectedDeskId(activeDesks[0].id);
          }
        })
        .catch(() => setError("Failed to load available office desks."));
    }
  }, [mode]);

  // Load members when desk changes
  useEffect(() => {
    if (mode === "move" && selectedDeskId) {
      setSelectedUserId("");
      fetch(`/api/admin/desks/${selectedDeskId}/members`, { credentials: "include" })
        .then((res) => res.json() as Promise<DeskMemberOption[]>)
        .then((data) => {
          setMembers(data.filter((m) => m.isActive));
        })
        .catch(() => setMembers([]));
    }
  }, [mode, selectedDeskId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    setLoading(true);

    try {
      if (mode === "move") {
        if (!selectedDeskId) {
          setError("Please select a target office desk.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/move`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            action,
            toDeskId: selectedDeskId,
            toUserId: selectedUserId || null,
            remarks: remarks.trim() || null,
            instructions: instructions.trim() || null,
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          if (res.status === 409) {
            throw new Error("This Dak was modified elsewhere. Please refresh before proceeding.");
          }
          throw new Error(data?.detail || data?.message || "Failed to move Dak.");
        }
      } else if (mode === "dispose") {
        if (!remarks.trim()) {
          setError("Disposal remarks/noting is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/dispose`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            remarks: remarks.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          if (res.status === 409) {
            throw new Error("This Dak was modified elsewhere. Please refresh before proceeding.");
          }
          throw new Error(data?.detail || data?.message || "Failed to dispose Dak.");
        }
      } else if (mode === "cancel") {
        if (!reason.trim()) {
          setError("Cancellation reason is required.");
          setLoading(false);
          return;
        }

        const res = await fetch(`/api/dak/${dak.id}/cancel`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            reason: reason.trim(),
            expectedRevision: dak.revision,
          }),
        });

        if (!res.ok) {
          const data = await res.json().catch(() => null);
          if (res.status === 409) {
            throw new Error("This Dak was modified elsewhere. Please refresh before proceeding.");
          }
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

  return (
    <div className="modal-backdrop">
      <div className="modal-card">
        <h3>
          {mode === "move" && (isCurrentlyAssigned ? "Forward / Return Dak" : "Mark Dak to Desk")}
          {mode === "dispose" && "Dispose / Close Dak"}
          {mode === "cancel" && "Cancel / Annul Dak"}
        </h3>
        <p className="subtext">
          Diary No: <strong>{dak.diaryNumber}</strong> — {dak.subject}
        </p>

        {error && <div className="state error">{error}</div>}

        <form onSubmit={handleSubmit}>
          {mode === "move" && (
            <>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Movement Action</label>
                <select
                  value={action}
                  onChange={(e) => setAction(e.target.value as "Marked" | "Forwarded" | "Returned")}
                  disabled={loading}
                >
                  {!isCurrentlyAssigned && <option value="Marked">Mark to Desk (Initial)</option>}
                  {isCurrentlyAssigned && (
                    <>
                      <option value="Forwarded">Forward to Desk</option>
                      <option value="Returned">Return to Desk</option>
                    </>
                  )}
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Target Office Desk *</label>
                <select
                  value={selectedDeskId}
                  onChange={(e) => setSelectedDeskId(e.target.value)}
                  disabled={loading}
                  required
                >
                  {desks.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Earmark to Officer / Member (Optional)</label>
                <select
                  value={selectedUserId}
                  onChange={(e) => setSelectedUserId(e.target.value)}
                  disabled={loading}
                >
                  <option value="">-- General Desk Assignment (No specific officer) --</option>
                  {members.map((m) => (
                    <option key={m.userId} value={m.userId}>
                      {m.displayName} {m.isPrimary ? "★ (Primary)" : ""}
                    </option>
                  ))}
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Instructions for Recipient</label>
                <input
                  type="text"
                  placeholder="e.g. Please examine Khatauni records and put up note"
                  value={instructions}
                  onChange={(e) => setInstructions(e.target.value)}
                  disabled={loading}
                />
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Remarks / Official Noting</label>
                <textarea
                  placeholder="Enter noting or movement remarks..."
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  disabled={loading}
                  rows={3}
                />
              </div>
            </>
          )}

          {mode === "dispose" && (
            <div className="field-group" style={{ marginBottom: "14px" }}>
              <label>Disposal Remarks / Final Noting *</label>
              <textarea
                placeholder="Detail final action taken or reference to disposal communication..."
                value={remarks}
                onChange={(e) => setRemarks(e.target.value)}
                disabled={loading}
                required
                rows={4}
              />
            </div>
          )}

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
              />
            </div>
          )}

          <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "20px" }}>
            <button type="button" className="secondary-button" onClick={onClose} disabled={loading}>
              Cancel
            </button>
            <button type="submit" className="primary-button" disabled={loading}>
              {loading ? "Processing..." : mode === "move" ? "Confirm Movement" : mode === "dispose" ? "Confirm Disposal" : "Confirm Cancellation"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
