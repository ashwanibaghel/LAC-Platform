import React, { useState } from "react";
import type { NoteAnchor, MatterNoteRemarkDto } from "./notingTypes";
import { generateUuid } from "./notingOfficeApi";

interface AddRemarkModalProps {
  matterId: string;
  noteId: string;
  noteNumber: number;
  anchor: NoteAnchor;
  onClose: () => void;
  onCreated: (remark: MatterNoteRemarkDto) => void;
}

export const AddRemarkModal: React.FC<AddRemarkModalProps> = ({
  matterId,
  noteId,
  noteNumber,
  anchor,
  onClose,
  onCreated
}) => {
  const [text, setText] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!text.trim()) {
      setError("Please enter remark text.");
      return;
    }
    if (!anchor || anchor.version !== 1 || !anchor.quoteHash || anchor.start < 0 || anchor.end <= anchor.start || !anchor.quote.trim()) {
      setError("Cannot submit remark: invalid anchor. Exact canonical text selection and SHA-256 quote hash are required.");
      return;
    }
    setSubmitting(true);
    setError(null);

    try {
      const res = await fetch(`/api/matters/${matterId}/notes/${noteId}/remarks`, {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": generateUuid()
        },
        body: JSON.stringify({
          anchor,
          text: text.trim()
        })
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Failed to create remark (${res.status})`);
      }

      const created: MatterNoteRemarkDto = await res.json();
      onCreated(created);
      onClose();
    } catch (err: any) {
      setError(err.message || "Failed to add remark");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()}>
        <div className="noting-modal-header">
          <h3>Add Supervisory Remark on Note {noteNumber}</h3>
          <button className="noting-btn-close" onClick={onClose} type="button">✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          <div className="noting-modal-body">
            <div>
              <label style={{ fontSize: "11px", fontWeight: 700, color: "#64748b", textTransform: "uppercase" }}>
                Referenced Note Excerpt
              </label>
              <div
                style={{
                  background: "#fef3c7",
                  border: "1px solid #fde68a",
                  padding: "8px 10px",
                  borderRadius: "4px",
                  fontSize: "12.5px",
                  color: "#92400e",
                  fontStyle: "italic",
                  marginTop: "4px",
                  maxHeight: "80px",
                  overflowY: "auto"
                }}
              >
                "{anchor.quote}"
              </div>
              <div style={{ fontSize: "11px", color: "#64748b", marginTop: "3px" }}>
                Offsets: {anchor.start}..{anchor.end} ({anchor.end - anchor.start} chars) • SHA256: {anchor.quoteHash ? `${anchor.quoteHash.substring(0, 12)}…` : "None"}
              </div>
            </div>

            <div>
              <label style={{ fontSize: "12px", fontWeight: 700, color: "#334155", display: "block", marginBottom: "4px" }}>
                Remark / Query Text
              </label>
              <textarea
                value={text}
                onChange={(e) => setText(e.target.value)}
                placeholder="Enter feedback or instructions for the next officer..."
                rows={4}
                style={{
                  width: "100%",
                  padding: "8px 10px",
                  fontSize: "13px",
                  border: "1px solid #cbd5e1",
                  borderRadius: "4px",
                  outline: "none",
                  resize: "vertical"
                }}
                autoFocus
              />
            </div>

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
            <button className="noting-btn noting-btn-primary" type="submit" disabled={submitting}>
              {submitting ? "Adding..." : "Add Remark"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};

interface ViewRemarksModalProps {
  matterId: string;
  noteId: string;
  noteNumber: number;
  remarks: MatterNoteRemarkDto[];
  canManage: boolean;
  onClose: () => void;
  onUpdated: (updated: MatterNoteRemarkDto) => void;
}

export const ViewRemarksModal: React.FC<ViewRemarksModalProps> = ({
  matterId,
  noteId,
  noteNumber,
  remarks,
  canManage,
  onClose,
  onUpdated
}) => {
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleToggleState = async (remark: MatterNoteRemarkDto) => {
    const nextState = remark.state === "Open" ? "Addressed" : "Open";
    setUpdatingId(remark.id);
    setError(null);

    try {
      const res = await fetch(`/api/matters/${matterId}/notes/${noteId}/remarks/${remark.id}`, {
        method: "PUT",
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": generateUuid()
        },
        body: JSON.stringify({
          state: nextState,
          expectedRevision: remark.revision
        })
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Failed to update remark status (${res.status})`);
      }

      const updated: MatterNoteRemarkDto = await res.json();
      onUpdated(updated);
    } catch (err: any) {
      setError(err.message || "Failed to toggle remark status");
    } finally {
      setUpdatingId(null);
    }
  };

  return (
    <div className="noting-modal-overlay" onClick={onClose}>
      <div className="noting-modal-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
        <div className="noting-modal-header">
          <h3>Remarks on Note {noteNumber} ({remarks.length})</h3>
          <button className="noting-btn-close" onClick={onClose} type="button">✕</button>
        </div>
        <div className="noting-modal-body" style={{ maxHeight: "400px", overflowY: "auto" }}>
          {error && (
            <div style={{ color: "#b91c1c", fontSize: "12px", background: "#fee2e2", padding: "6px 10px", borderRadius: "4px" }}>
              {error}
            </div>
          )}

          {remarks.map((r) => {
            let anchorSnippet = "";
            try {
              const a = JSON.parse(r.anchorJson);
              anchorSnippet = a.quote;
            } catch {}

            return (
              <div
                key={r.id}
                style={{
                  border: "1px solid #e2e8f0",
                  borderRadius: "6px",
                  padding: "10px 12px",
                  background: r.state === "Addressed" ? "#f8fafc" : "#fffbeb"
                }}
              >
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "6px" }}>
                  <div>
                    <span style={{ fontWeight: 700, fontSize: "12.5px", color: "#0f172a" }}>{r.authorName}</span>
                    <span style={{ fontSize: "11px", color: "#64748b", marginLeft: "6px" }}>({r.designation})</span>
                  </div>
                  <span
                    style={{
                      fontSize: "11px",
                      fontWeight: 700,
                      padding: "2px 6px",
                      borderRadius: "4px",
                      background: r.state === "Addressed" ? "#dcfce7" : "#fef3c7",
                      color: r.state === "Addressed" ? "#166534" : "#92400e"
                    }}
                  >
                    {r.state}
                  </span>
                </div>

                {anchorSnippet && (
                  <div
                    style={{
                      fontSize: "11.5px",
                      color: "#64748b",
                      fontStyle: "italic",
                      marginBottom: "6px",
                      borderLeft: "2px solid #cbd5e1",
                      paddingLeft: "6px"
                    }}
                  >
                    "{anchorSnippet}"
                  </div>
                )}

                <div style={{ fontSize: "13px", color: "#1e293b", lineHeight: 1.5, marginBottom: "8px" }}>
                  {r.text}
                </div>

                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", fontSize: "11px", color: "#94a3b8" }}>
                  <span>{new Date(r.createdAt).toLocaleString(undefined, { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" })}</span>
                  {canManage && (
                    <button
                      type="button"
                      className="noting-btn noting-btn-secondary"
                      style={{ padding: "2px 8px", fontSize: "11px" }}
                      disabled={updatingId === r.id}
                      onClick={() => handleToggleState(r)}
                    >
                      {updatingId === r.id ? "Saving..." : r.state === "Open" ? "Mark Addressed" : "Reopen Remark"}
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
        <div className="noting-modal-footer">
          <button className="noting-btn noting-btn-secondary" type="button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
};
