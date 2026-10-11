import React, { useState, useEffect } from "react";
import type { MatterOfficialNoteDto } from "./notingTypes";

interface LinkedDakItem {
  dakId: string;
  diaryNumber: string;
  subject: string;
  revision: number;
  hasPhysicalOriginal?: boolean;
}

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
  members: DeskMemberOption[];
}

interface SendReturnModalProps {
  matterId: string;
  activeDraftRevision: number;
  linkedDaks: LinkedDakItem[];
  defaultControllingDakId?: string | null;
  onClose: () => void;
  onSent: (result: { note: MatterOfficialNoteDto; dak: any }) => void;
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

export const SendReturnModal: React.FC<SendReturnModalProps> = ({
  matterId,
  activeDraftRevision,
  linkedDaks,
  defaultControllingDakId,
  onClose,
  onSent
}) => {
  const [action, setAction] = useState<"Forwarded" | "Returned">("Forwarded");
  const [selectedDakId, setSelectedDakId] = useState<string>(
    defaultControllingDakId || linkedDaks[0]?.dakId || ""
  );

  const [desks, setDesks] = useState<TargetDeskOption[]>([]);
  const [selectedDeskId, setSelectedDeskId] = useState<string>("");
  const [selectedUserId, setSelectedUserId] = useState<string>("");
  const [instructions, setInstructions] = useState<string>("");
  const [remarks, setRemarks] = useState<string>("");
  const [includesPhysical, setIncludesPhysical] = useState<boolean>(false);
  const [dakHasPhysical, setDakHasPhysical] = useState<boolean>(false);

  const [loadingTargets, setLoadingTargets] = useState<boolean>(true);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const selectedDak = linkedDaks.find((d) => d.dakId === selectedDakId);

  // Fetch physical original status for the selected Dak
  useEffect(() => {
    if (!selectedDakId) {
      setDakHasPhysical(false);
      return;
    }
    fetch(`/api/dak/${selectedDakId}/physical-original`, { credentials: "include" })
      .then(async (res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (data && data.hasPhysicalOriginal === true) {
          setDakHasPhysical(true);
        } else {
          setDakHasPhysical(false);
        }
      })
      .catch(() => {
        setDakHasPhysical(Boolean(selectedDak?.hasPhysicalOriginal));
      });
  }, [selectedDakId, selectedDak?.hasPhysicalOriginal]);

  // Load destination desks/officers from active controlling Dak
  useEffect(() => {
    if (!selectedDakId) return;
    setLoadingTargets(true);
    setError(null);

    fetch(`/api/dak/${selectedDakId}/movement-targets`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) {
          throw new Error("Could not load eligible destinations for this Dak.");
        }
        return res.json() as Promise<{ desks: TargetDeskOption[] }>;
      })
      .then((data) => {
        if (Array.isArray(data.desks)) {
          const activeDesks = data.desks.filter((d) => d.isActive);
          setDesks(activeDesks);
          if (activeDesks.length > 0) {
            setSelectedDeskId(activeDesks[0].id);
            const firstPrimary = activeDesks[0].members.find((m) => m.isPrimary) || activeDesks[0].members[0];
            setSelectedUserId(firstPrimary ? firstPrimary.userId : "");
          }
        }
      })
      .catch((err: any) => {
        setError(err.message || "Failed to load movement targets");
      })
      .finally(() => {
        setLoadingTargets(false);
      });
  }, [selectedDakId]);

  const selectedDesk = desks.find((d) => d.id === selectedDeskId);
  const members = selectedDesk ? selectedDesk.members : [];

  const handleDeskChange = (deskId: string) => {
    setSelectedDeskId(deskId);
    const desk = desks.find((d) => d.id === deskId);
    const primary = desk?.members.find((m) => m.isPrimary) || desk?.members[0];
    setSelectedUserId(primary ? primary.userId : "");
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedDakId) {
      setError("Please select the controlling active Dak to move.");
      return;
    }
    if (!selectedDeskId) {
      setError("Please select the recipient Desk.");
      return;
    }
    if (!selectedUserId) {
      setError("Please select the recipient Officer.");
      return;
    }

    if (!selectedDak) {
      setError("Selected Dak could not be resolved.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const payload = {
        sourceDakId: selectedDakId,
        expectedDakRevision: selectedDak.revision,
        toDeskId: selectedDeskId,
        toUserId: selectedUserId,
        action,
        includesPhysicalOriginal: includesPhysical,
        instructions: instructions.trim() || undefined,
        remarks: remarks.trim() || undefined
      };

      const res = await fetch(`/api/matters/${matterId}/noting/send`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "If-Match": `"${activeDraftRevision}"`,
          "Idempotency-Key": getUuid()
        },
        body: JSON.stringify(payload)
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Send failed with status ${res.status}`);
      }

      const result = await res.json();
      onSent(result);
      onClose();
    } catch (err: any) {
      setError(err.message || "Failed to send file");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
        <div className="noting-modal-header">
          <h3>Send / Return File (Note & Movement)</h3>
          <button className="noting-btn noting-btn-secondary" onClick={onClose} type="button">✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          <div className="noting-modal-body">
            {/* Movement Action Type */}
            <div>
              <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "6px" }}>
                Movement Action
              </label>
              <div style={{ display: "flex", gap: "12px" }}>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                  <input
                    type="radio"
                    name="action"
                    value="Forwarded"
                    checked={action === "Forwarded"}
                    onChange={() => setAction("Forwarded")}
                  />
                  <span>Forward to Next Officer / Branch</span>
                </label>
                <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                  <input
                    type="radio"
                    name="action"
                    value="Returned"
                    checked={action === "Returned"}
                    onChange={() => setAction("Returned")}
                  />
                  <span>Return to Previous Desk / Initiator</span>
                </label>
              </div>
            </div>

            {/* Multiple Dak selector (Non-negotiable edge-case rule) */}
            {linkedDaks.length > 1 ? (
              <div style={{ background: "#f8fafc", border: "1px solid #cbd5e1", padding: "10px", borderRadius: "6px" }}>
                <label style={{ fontSize: "12px", fontWeight: 700, color: "#0f172a", display: "block", marginBottom: "4px" }}>
                  Controlling Active Dak <span style={{ color: "#b91c1c" }}>*</span>
                </label>
                <div style={{ fontSize: "11px", color: "#64748b", marginBottom: "6px" }}>
                  This Matter links multiple Daks. Select which controlling Dak will be moved with this submission. Other linked Daks remain in their current custody.
                </div>
                <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                  {linkedDaks.map((d) => (
                    <label key={d.dakId} style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "12.5px", cursor: "pointer" }}>
                      <input
                        type="radio"
                        name="controllingDak"
                        value={d.dakId}
                        checked={selectedDakId === d.dakId}
                        onChange={() => setSelectedDakId(d.dakId)}
                      />
                      <span><strong>{d.diaryNumber}</strong> — {d.subject} (rev {d.revision})</span>
                    </label>
                  ))}
                </div>
              </div>
            ) : selectedDak ? (
              <div style={{ fontSize: "12px", color: "#475569", background: "#f1f5f9", padding: "6px 10px", borderRadius: "4px" }}>
                Active Controlling Dak: <strong>{selectedDak.diaryNumber}</strong> — {selectedDak.subject}
              </div>
            ) : null}

            {/* Recipient Desk & Officer */}
            <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
              <div>
                <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                  Target Desk <span style={{ color: "#b91c1c" }}>*</span>
                </label>
                <select
                  value={selectedDeskId}
                  onChange={(e) => handleDeskChange(e.target.value)}
                  disabled={loadingTargets || desks.length === 0}
                  style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", background: "#ffffff" }}
                >
                  {desks.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                  Target Officer <span style={{ color: "#b91c1c" }}>*</span>
                </label>
                <select
                  value={selectedUserId}
                  onChange={(e) => setSelectedUserId(e.target.value)}
                  disabled={loadingTargets || members.length === 0}
                  style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", background: "#ffffff" }}
                >
                  {members.map((m) => (
                    <option key={m.userId} value={m.userId}>
                      {m.displayName} {m.designation ? `(${m.designation})` : ""} {m.isPrimary ? "★ Primary" : ""}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {/* Instructions & Remarks */}
            <div>
              <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                Movement Instructions (Optional)
              </label>
              <input
                type="text"
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
                placeholder="e.g. Please examine valuation report and verify title"
                style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
              />
            </div>

            <div>
              <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155", display: "block", marginBottom: "4px" }}>
                Movement Remarks (Optional)
              </label>
              <textarea
                value={remarks}
                onChange={(e) => setRemarks(e.target.value)}
                placeholder="Optional movement ledger remarks..."
                rows={2}
                style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px", resize: "vertical" }}
              />
            </div>

            {/* Physical Original Handling: Checkbox ONLY when paper original exists, otherwise clear digital-only indicator */}
            {dakHasPhysical ? (
              <div style={{ background: "#fff7ed", border: "1px solid #ffedd5", padding: "8px 10px", borderRadius: "4px" }}>
                <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "12.5px", color: "#9a3412", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={includesPhysical}
                    onChange={(e) => setIncludesPhysical(e.target.checked)}
                  />
                  <span>Simultaneously hand over physical paper original file (Physical dispatch)</span>
                </label>
              </div>
            ) : (
              <div style={{ background: "#f8fafc", border: "1px solid #e2e8f0", padding: "6px 10px", borderRadius: "4px", fontSize: "12px", color: "#64748b" }}>
                ℹ Digital-only file movement. No physical paper original is attached to this Dak.
              </div>
            )}

            {error && (
              <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
                {error}
              </div>
            )}
          </div>
          <div className="noting-modal-footer">
            <button className="noting-btn noting-btn-secondary" type="button" onClick={onClose} disabled={submitting}>
              Cancel
            </button>
            <button className="noting-btn noting-btn-send" type="submit" disabled={submitting || loadingTargets || !selectedUserId}>
              {submitting ? "Sending..." : "Submit Note & Send File"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
