import React, { useState, useEffect } from "react";
import type { SendWorkingNoteCommand } from "./notingTypes";
import { generateUuid } from "./notingOfficeApi";

interface DeskItem {
  id: string;
  name: string;
  code: string;
}

interface DeskMemberItem {
  userId: string;
  displayName: string;
  designation?: string | null;
  isPrimary?: boolean;
}

interface LinkedDakItem {
  dakId: string;
  diaryNumber: string;
  subject: string;
  revision: number;
  hasPhysicalOriginal?: boolean;
  isInCustody?: boolean;
}

interface SendReturnModalProps {
  matterId: string;
  sourceDakId: string | null;
  officePreviewId?: string;
  canonicalHash?: string;
  workingRevision: number;
  onClose: () => void;
  onSent: (result: { noteNumber: number }) => void;
}

export const SendReturnModal: React.FC<SendReturnModalProps> = ({
  matterId,
  sourceDakId: initialDakId,
  officePreviewId,
  canonicalHash,
  workingRevision,
  onClose,
  onSent
}) => {
  const [action, setAction] = useState<"Forwarded" | "Returned">("Forwarded");
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [selectedDeskId, setSelectedDeskId] = useState<string>("");
  const [members, setMembers] = useState<DeskMemberItem[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<string>("");
  const [instructions, setInstructions] = useState<string>("");
  const [remarks, setRemarks] = useState<string>("");
  const [includesPhysical, setIncludesPhysical] = useState<boolean>(false);

  // Controlling Dak Selection
  const [linkedDaks, setLinkedDaks] = useState<LinkedDakItem[]>([]);
  const [selectedDakId, setSelectedDakId] = useState<string>(initialDakId || "");
  const [loadingTargets, setLoadingTargets] = useState<boolean>(true);
  const [submitting, setSubmitting] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  // Load Desks and Linked Daks
  useEffect(() => {
    let active = true;
    setLoadingTargets(true);
    setError(null);

    Promise.all([
      fetch("/api/admin/desks", { credentials: "include" }),
      fetch(`/api/matters/${matterId}/context`, { credentials: "include" })
    ])
      .then(async ([resDesks, resContext]) => {
        let deskList: DeskItem[] = [];
        if (resDesks.ok) {
          const dData = await resDesks.json();
          deskList = Array.isArray(dData) ? dData : dData?.items || [];
        }

        let daksList: LinkedDakItem[] = [];
        if (resContext.ok) {
          const ctxData = await resContext.json();
          if (Array.isArray(ctxData?.linkedDaks)) {
            daksList = ctxData.linkedDaks;
          } else if (Array.isArray(ctxData?.daks)) {
            daksList = ctxData.daks;
          }
        }

        if (active) {
          setDesks(deskList);
          if (deskList.length > 0) {
            setSelectedDeskId(deskList[0].id);
          }
          setLinkedDaks(daksList);
          if (!selectedDakId && daksList.length > 0) {
            setSelectedDakId(daksList[0].dakId);
          }
        }
      })
      .catch((err) => {
        if (active) setError(err.message || "Failed to load forwarding destinations");
      })
      .finally(() => {
        if (active) setLoadingTargets(false);
      });

    return () => {
      active = false;
    };
  }, [matterId]);

  // Load Members when selectedDeskId changes
  useEffect(() => {
    if (!selectedDeskId) {
      setMembers([]);
      setSelectedUserId("");
      return;
    }

    let active = true;
    fetch(`/api/admin/desks/${selectedDeskId}/members`, { credentials: "include" })
      .then(async (res) => (res.ok ? res.json() : []))
      .then((data: DeskMemberItem[]) => {
        if (active) {
          const list = Array.isArray(data) ? data : [];
          setMembers(list);
          const primary = list.find((m) => m.isPrimary) || list[0];
          setSelectedUserId(primary ? primary.userId : "");
        }
      })
      .catch(() => {
        if (active) {
          setMembers([]);
          setSelectedUserId("");
        }
      });

    return () => {
      active = false;
    };
  }, [selectedDeskId]);

  const selectedDak = linkedDaks.find((d) => d.dakId === selectedDakId);
  const dakHasPhysical = Boolean(selectedDak?.hasPhysicalOriginal);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedDeskId) {
      setError("Please select a target desk.");
      return;
    }
    if (!selectedUserId) {
      setError("Please select a target officer.");
      return;
    }
    if (!selectedDakId) {
      setError("Please select a controlling Dak to move.");
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const command: SendWorkingNoteCommand = {
        sourceDakId: selectedDakId,
        expectedDakRevision: selectedDak ? selectedDak.revision : 1,
        toDeskId: selectedDeskId,
        toUserId: selectedUserId,
        action,
        includesPhysicalOriginal: dakHasPhysical ? includesPhysical : false,
        instructions: instructions.trim() || undefined,
        remarks: remarks.trim() || undefined,
        officePreviewId,
        canonicalHash
      };

      const res = await fetch(`/api/matters/${matterId}/noting/send`, {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": generateUuid(),
          "If-Match": `"${workingRevision}"`
        },
        body: JSON.stringify(command)
      });

      if (!res.ok) {
        let errMsg = `Failed to send note and dispatch file (${res.status})`;
        try {
          const b = await res.json();
          errMsg = b.message || b.detail || errMsg;
        } catch {}
        throw new Error(errMsg);
      }

      const result = await res.json();
      onSent(result);
      onClose();
    } catch (err: any) {
      setError(err.message || "Failed to submit and forward note");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
        <div className="noting-modal-header">
          <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
            <span style={{ fontSize: "16px" }}>📤</span>
            <h3>Submit Note & Forward File</h3>
          </div>
          <button className="noting-btn-close" onClick={onClose} type="button">✕</button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="noting-modal-body">
            {/* Movement Action */}
            <div>
              <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "6px" }}>
                Movement Action
              </label>
              <div style={{ display: "flex", gap: "16px" }}>
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

            {/* Multiple Dak selector */}
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
                <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
                  Target Desk <span style={{ color: "#b91c1c" }}>*</span>
                </label>
                <select
                  value={selectedDeskId}
                  onChange={(e) => setSelectedDeskId(e.target.value)}
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
                <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
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
              <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
                Movement Instructions (Optional)
              </label>
              <input
                type="text"
                value={instructions}
                onChange={(e) => setInstructions(e.target.value)}
                placeholder="e.g. Please verify title and prepare draft sanction order"
                style={{ width: "100%", padding: "7px 10px", fontSize: "13px", border: "1px solid #cbd5e1", borderRadius: "4px" }}
              />
            </div>

            <div>
              <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
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

            {/* Physical Original Handling: Checkbox ONLY when paper original exists */}
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
            <button
              className="noting-btn noting-btn-send"
              type="submit"
              disabled={submitting || loadingTargets || !selectedUserId || !selectedDakId}
            >
              {submitting ? "Sending..." : "Submit Note & Send File"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
