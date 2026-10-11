import React, { useState, useEffect, useRef, useCallback } from "react";
import type {
  MatterNotingWorkspaceData,
  MatterOfficialNoteDto,
  MatterNoteRemarkDto,
  MatterWorkingDraftDto
} from "./notingTypes";
import { canonicalizeRichTextDoc, createNoteAnchor, sanitizeTipTapDoc } from "./notingTextUtil";
import { AddRemarkModal, ViewRemarksModal } from "./RemarkModal";
import { CiteDocumentModal } from "./CiteDocumentModal";
import { SendReturnModal } from "./SendReturnModal";
import { DocumentRailViewer } from "./DocumentRailViewer";
import "./noting.css";

interface NotingWorkspaceViewProps {
  matterId: string;
  initialSourceDakId?: string | null;
  linkedDaks?: Array<{
    dakId: string;
    diaryNumber: string;
    subject: string;
    revision: number;
    hasPhysicalOriginal?: boolean;
  }>;
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

export const NotingWorkspaceView: React.FC<NotingWorkspaceViewProps> = ({
  matterId,
  initialSourceDakId = null,
  linkedDaks = []
}) => {
  // Layout mode
  const [focusMode, setFocusMode] = useState<boolean>(false);
  const [focusDrawerOpen, setFocusDrawerOpen] = useState<boolean>(false);

  // Controlling Dak selection
  const [sourceDakId, setSourceDakId] = useState<string | null>(
    initialSourceDakId || (linkedDaks.length > 0 ? linkedDaks[0].dakId : null)
  );

  // Main Workspace Data
  const [workspaceData, setWorkspaceData] = useState<MatterNotingWorkspaceData | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Active Draft State
  const [draftContent, setDraftContent] = useState<string>("");
  const [draftRevision, setDraftRevision] = useState<number>(0);
  const [draftLoaded, setDraftLoaded] = useState<boolean>(false);
  const [autosaveStatus, setAutosaveStatus] = useState<"saved" | "saving" | "offline" | "error">("saved");
  const [lastSavedAt, setLastSavedAt] = useState<Date | null>(null);

  // Autosave timer & pending flush ref
  const autosaveTimerRef = useRef<number | null>(null);
  const latestContentRef = useRef<string>("");
  const draftRevisionRef = useRef<number>(0);
  latestContentRef.current = draftContent;
  draftRevisionRef.current = draftRevision;

  // Selected document in rail/viewer
  const [activeDocId, setActiveDocId] = useState<string | null>(null);
  const [activePage, setActivePage] = useState<number | null>(null);

  // Remarks & Modals
  const [remarksMap, setRemarksMap] = useState<Record<string, MatterNoteRemarkDto[]>>({});
  const [addRemarkState, setAddRemarkState] = useState<{
    note: MatterOfficialNoteDto;
    anchor: any;
  } | null>(null);
  const [viewRemarksState, setViewRemarksState] = useState<{
    note: MatterOfficialNoteDto;
    remarks: MatterNoteRemarkDto[];
  } | null>(null);

  // Floating text selection state
  const [selectedRange, setSelectedRange] = useState<{
    note: MatterOfficialNoteDto;
    anchor: any;
    coords: { x: number; y: number };
  } | null>(null);

  // Citation modal state
  const [showCitationModal, setShowCitationModal] = useState<boolean>(false);
  const [citationAnchor, setCitationAnchor] = useState<any | null>(null);

  // Send / Return movement modal
  const [showSendModal, setShowSendModal] = useState<boolean>(false);

  // 409 Draft revision conflict preservation state
  const [draftConflict, setDraftConflict] = useState<{
    localContent: string;
    serverContent: string;
    serverRevision: number;
    message?: string;
  } | null>(null);

  // 1. Fetch Noting Workspace Data
  const loadWorkspace = useCallback(async () => {
    try {
      const url = sourceDakId
        ? `/api/matters/${matterId}/noting?sourceDakId=${sourceDakId}`
        : `/api/matters/${matterId}/noting`;

      const res = await fetch(url, { credentials: "include" });
      if (!res.ok) {
        throw new Error(`Failed to load workspace (${res.status})`);
      }
      const data: MatterNotingWorkspaceData = await res.json();
      setWorkspaceData(data);
      if (data.documents.length > 0 && !activeDocId) {
        setActiveDocId(data.documents[0].id);
      }

      // Fetch remarks for all official notes
      for (const note of data.notes) {
        fetch(`/api/matters/${matterId}/notes/${note.id}/remarks`, { credentials: "include" })
          .then(async (r) => (r.ok ? r.json() : []))
          .then((remList) => {
            setRemarksMap((prev) => ({ ...prev, [note.id]: remList }));
          })
          .catch(() => {});
      }
    } catch (err: any) {
      setError(err.message || "Could not load noting workspace");
    } finally {
      setLoading(false);
    }
  }, [matterId, sourceDakId, activeDocId]);

  useEffect(() => {
    loadWorkspace();
  }, [loadWorkspace]);

  // 2. Fetch Active Draft
  const loadDraft = useCallback(async () => {
    try {
      const res = await fetch(`/api/matters/${matterId}/noting/draft`, { credentials: "include" });
      if (!res.ok) return;

      const etag = res.headers.get("ETag");
      const revFromEtag = etag ? parseInt(etag.replace(/"/g, ""), 10) : 0;

      const draft: MatterWorkingDraftDto = await res.json();
      const rev = typeof draft.revision === "number" ? draft.revision : revFromEtag;
      setDraftRevision(rev);
      draftRevisionRef.current = rev;

      if (draft.contentJson) {
        try {
          const parsed = JSON.parse(draft.contentJson);
          const canonical = canonicalizeRichTextDoc(parsed);
          setDraftContent(canonical.trim());
        } catch {
          setDraftContent(draft.canonicalText || "");
        }
      } else {
        setDraftContent("");
      }
      setDraftLoaded(true);
      setAutosaveStatus("saved");
      setLastSavedAt(new Date());
    } catch {
      setDraftLoaded(true);
    }
  }, [matterId]);

  useEffect(() => {
    loadDraft();
  }, [loadDraft]);

  // 3. Durable Autosave Engine
  const performSave = useCallback(
    async (textToSave: string, force = false): Promise<boolean> => {
      if (!draftLoaded && !force) return false;
      setAutosaveStatus("saving");

      // Construct ProseMirror / TipTap doc
      const paragraphs = textToSave
        .split("\n")
        .filter((line) => line.trim().length > 0)
        .map((p) => ({
          type: "paragraph",
          content: [{ type: "text", text: p }]
        }));

      const docObj = {
        type: "doc",
        content: paragraphs.length > 0 ? paragraphs : [{ type: "paragraph", content: [{ type: "text", text: textToSave }] }]
      };

      const cleanDoc = sanitizeTipTapDoc(docObj);
      const jsonStr = JSON.stringify(cleanDoc);

      try {
        const res = await fetch(`/api/matters/${matterId}/noting/draft`, {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
            "If-Match": `"${draftRevisionRef.current}"`,
            "Idempotency-Key": getUuid()
          },
          body: JSON.stringify({
            contentJson: jsonStr,
            sourceDakId: sourceDakId || null
          })
        });

        if (res.status === 409) {
          setAutosaveStatus("error");
          try {
            const conflictBody = await res.json().catch(() => ({}));
            const serverRes = await fetch(`/api/matters/${matterId}/noting/draft`, { credentials: "include" });
            let serverRev = draftRevisionRef.current + 1;
            let serverText = "";
            if (serverRes.ok) {
              const etag = serverRes.headers.get("ETag");
              const sDraft: MatterWorkingDraftDto = await serverRes.json();
              serverRev = etag ? parseInt(etag.replace(/"/g, ""), 10) : (sDraft.revision || serverRev);
              if (sDraft.contentJson) {
                try {
                  serverText = canonicalizeRichTextDoc(JSON.parse(sDraft.contentJson)).trim();
                } catch {
                  serverText = sDraft.canonicalText || "";
                }
              } else {
                serverText = sDraft.canonicalText || "";
              }
            }
            setDraftConflict({
              localContent: textToSave,
              serverContent: serverText,
              serverRevision: serverRev,
              message: conflictBody.message || "Draft changed on server; recover saved text before retrying."
            });
          } catch {
            setDraftConflict({
              localContent: textToSave,
              serverContent: "",
              serverRevision: draftRevisionRef.current + 1,
              message: "Draft changed on server; recover saved text before retrying."
            });
          }
          return false;
        }

        if (!res.ok) {
          setAutosaveStatus("error");
          return false;
        }

        const etag = res.headers.get("ETag");
        const updatedDraft: MatterWorkingDraftDto = await res.json();
        const nextRev = etag ? parseInt(etag.replace(/"/g, ""), 10) : updatedDraft.revision;

        setDraftRevision(nextRev);
        draftRevisionRef.current = nextRev;
        setAutosaveStatus("saved");
        setLastSavedAt(new Date());
        return true;
      } catch {
        setAutosaveStatus("offline");
        return false;
      }
    },
    [draftLoaded, matterId, sourceDakId, loadDraft]
  );

  // Debounced input change
  const handleDraftChange = (e: React.ChangeEvent<HTMLTextAreaElement>) => {
    const val = e.target.value;
    setDraftContent(val);
    latestContentRef.current = val;
    setAutosaveStatus("saving");

    if (autosaveTimerRef.current) {
      window.clearTimeout(autosaveTimerRef.current);
    }
    autosaveTimerRef.current = window.setTimeout(() => {
      performSave(val);
    }, 1500);
  };

  // 4. Submit Working Note (Lock into immutable official chain)
  const handleSubmitNote = async () => {
    if (!draftContent.trim()) {
      alert("Please enter note text before submitting.");
      return;
    }

    // Flush pending autosave first
    const saved = await performSave(latestContentRef.current, true);
    if (!saved) {
      const proceed = window.confirm("Draft could not be verified by server. Attempt submit anyway?");
      if (!proceed) return;
    }

    try {
      const res = await fetch(`/api/matters/${matterId}/noting/submit`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "If-Match": `"${draftRevisionRef.current}"`,
          "Idempotency-Key": getUuid()
        },
        body: JSON.stringify({
          sourceDakId: sourceDakId || null
        })
      });

      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Submit note failed (${res.status})`);
      }

      setDraftContent("");
      latestContentRef.current = "";
      await loadDraft();
      await loadWorkspace();
    } catch (err: any) {
      alert(err.message || "Failed to submit note");
    }
  };

  // 5. Text Selection on Submitted Notes -> Add Remark
  const handleMouseUpOnNote = (_e: React.MouseEvent, note: MatterOfficialNoteDto) => {
    const sel = window.getSelection();
    if (!sel || sel.isCollapsed || sel.rangeCount === 0) {
      setSelectedRange(null);
      return;
    }

    const selectedText = sel.toString().trim();
    if (selectedText.length < 2) {
      setSelectedRange(null);
      return;
    }

    // Find position in canonicalText
    const startIdx = note.canonicalText.indexOf(selectedText);
    if (startIdx === -1) {
      setSelectedRange(null);
      return;
    }
    const endIdx = startIdx + selectedText.length;

    createNoteAnchor(note.canonicalText, startIdx, endIdx).then((anchor) => {
      const rect = sel.getRangeAt(0).getBoundingClientRect();
      setSelectedRange({
        note,
        anchor,
        coords: { x: rect.left + rect.width / 2, y: rect.top - 36 }
      });
    });
  };

  // 6. Citation click in notes -> Navigate right rail/drawer
  const handleCitationClick = (docId: string, page?: number | null) => {
    setActiveDocId(docId);
    if (page) setActivePage(page);
    if (focusMode) {
      setFocusDrawerOpen(true);
    }
  };

  // 7. Insert citation on active draft text selection
  const handleCiteDraftSelection = () => {
    const textarea = document.getElementById("active-draft-textarea") as HTMLTextAreaElement | null;
    if (!textarea) return;

    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const selected = textarea.value.substring(start, end).trim();

    if (!selected) {
      alert("Please highlight the exact text in your draft to link with a citation.");
      return;
    }

    createNoteAnchor(textarea.value, start, end).then((anchor) => {
      setCitationAnchor(anchor);
      setShowCitationModal(true);
    });
  };

  if (loading) {
    return (
      <div className="noting-workspace-root" style={{ alignItems: "center", justifyContent: "center" }}>
        <div style={{ fontSize: "14px", color: "#64748b" }}>Loading continuous green noting chain…</div>
      </div>
    );
  }

  if (error || !workspaceData) {
    return (
      <div className="noting-workspace-root" style={{ padding: "24px" }}>
        <div style={{ color: "#b91c1c", fontSize: "14px", background: "#fee2e2", padding: "12px", borderRadius: "6px" }}>
          <strong>Error loading workspace:</strong> {error}
        </div>
      </div>
    );
  }

  const { capabilities, notes, documents } = workspaceData;

  return (
    <div className={`noting-workspace-root ${focusMode ? "focus-mode" : ""}`}>
      {/* Top Bar */}
      <div className="noting-top-bar">
        <div className="noting-top-left">
          {/* Controlling Dak Badge / Disambiguator */}
          {linkedDaks.length > 1 ? (
            <div className="noting-badge-source-dak">
              <span>Dak:</span>
              <select
                value={sourceDakId || ""}
                onChange={(e) => setSourceDakId(e.target.value || null)}
                title="Select Controlling Active Dak"
              >
                {linkedDaks.map((d) => (
                  <option key={d.dakId} value={d.dakId}>
                    {d.diaryNumber} — {d.subject.slice(0, 30)}...
                  </option>
                ))}
              </select>
            </div>
          ) : linkedDaks.length === 1 ? (
            <div className="noting-badge-source-dak">
              <span>Dak: {linkedDaks[0].diaryNumber}</span>
            </div>
          ) : (
            <div className="noting-badge-native">
              <span>Village Native (No Dak)</span>
            </div>
          )}

          {/* Autosave Indicator */}
          <div className={`noting-autosave-indicator ${autosaveStatus}`}>
            {autosaveStatus === "saved" && (
              <>
                <span>✓ All changes saved</span>
                {lastSavedAt && (
                  <span style={{ opacity: 0.75, fontSize: "11px" }}>
                    ({lastSavedAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })})
                  </span>
                )}
              </>
            )}
            {autosaveStatus === "saving" && <span>⏳ Saving draft…</span>}
            {autosaveStatus === "offline" && <span>⚠ Offline — retrying</span>}
            {autosaveStatus === "error" && (
              <span onClick={() => performSave(latestContentRef.current, true)}>
                {draftConflict ? "⚠ Conflict (409) — Unsaved edits preserved" : "✕ Save failed — Click to retry"}
              </span>
            )}
          </div>
        </div>

        {/* Right Top Actions */}
        <div className="noting-top-actions">
          {/* Focus Toggle */}
          <button
            type="button"
            className="noting-btn noting-btn-secondary"
            onClick={() => {
              setFocusMode(!focusMode);
              setFocusDrawerOpen(false);
            }}
          >
            {focusMode ? "◫ Restore Split View" : "⛶ Expand to Focus"}
          </button>

          {/* Manual Save */}
          <button
            type="button"
            className="noting-btn noting-btn-secondary"
            onClick={() => performSave(latestContentRef.current, true)}
            disabled={!capabilities.canEditDraft}
          >
            Save Draft
          </button>

          {/* Submit Note (Internal) */}
          <button
            type="button"
            className="noting-btn noting-btn-primary"
            onClick={handleSubmitNote}
            disabled={!capabilities.canSubmit || !draftContent.trim()}
            title="Submit Note as next official numbered record"
          >
            Submit Note {notes.length + 1}
          </button>

          {/* Send / Return File Movement */}
          <button
            type="button"
            className="noting-btn noting-btn-send"
            onClick={() => setShowSendModal(true)}
            disabled={!capabilities.canSend}
            title={
              capabilities.nativeRoutingPolicyPending
                ? "Inter-officer file movement requires an active controlling Dak"
                : "Submit Note and move controlling Dak to next officer"
            }
          >
            Send / Return File ➔
          </button>
        </div>
      </div>

      {/* Native Policy Pending Notice */}
      {capabilities.nativeRoutingPolicyPending && (
        <div className="noting-policy-warning-banner">
          <span>ℹ</span>
          <span>
            <strong>Native Routing Policy Pending:</strong> Inter-officer file movement requires an active controlling Dak. This native draft can be saved and submitted internally into the official chain.
          </span>
        </div>
      )}

      {/* Split Main Body */}
      <div className="noting-main-split">
        {/* Left Column: Official Notes Chain + Active Draft */}
        <div className="noting-left-column">
          <div className="noting-chain-container">
            {/* Historical Official Notes */}
            {notes.length === 0 ? (
              <div style={{ textAlign: "center", padding: "20px", color: "#64748b", fontStyle: "italic", fontSize: "13px" }}>
                No official submitted notes yet. Compose Note 1 below.
              </div>
            ) : (
              notes.map((note) => {
                const noteRemarks = remarksMap[note.id] || [];
                const openRemarksCount = noteRemarks.filter((r) => r.state === "Open").length;

                return (
                  <div
                    key={note.id}
                    className="official-note-card"
                    onMouseUp={(e) => handleMouseUpOnNote(e, note)}
                  >
                    <div className="official-note-header">
                      <div className="official-note-title">
                        <span className="official-note-number-badge">NOTE {note.number}</span>
                        <span className="official-note-author">{note.authorName}</span>
                        <span className="official-note-designation" style={{ fontSize: "12px", color: "#166534", fontWeight: 500 }}>
                          ({note.designation})
                        </span>
                      </div>

                      <div className="official-note-meta">
                        {note.deskName && <span className="official-note-desk">{note.deskName}</span>}
                        <span className="official-note-timestamp">
                          {new Date(note.submittedAt).toLocaleString(undefined, {
                            day: "numeric",
                            month: "short",
                            year: "numeric",
                            hour: "2-digit",
                            minute: "2-digit"
                          })}
                        </span>

                        {noteRemarks.length > 0 && (
                          <button
                            type="button"
                            className="noting-btn noting-btn-secondary view-remarks-btn"
                            style={{ padding: "1px 6px", fontSize: "11px" }}
                            onClick={() => setViewRemarksState({ note, remarks: noteRemarks })}
                          >
                            Remarks ({noteRemarks.length})
                            {openRemarksCount > 0 && (
                              <span style={{ color: "#d97706", fontWeight: 700, marginLeft: "4px" }}>
                                ({openRemarksCount} Open)
                              </span>
                            )}
                          </button>
                        )}

                        {capabilities.canRemark && (
                          <button
                            type="button"
                            className="noting-btn noting-btn-secondary add-remark-btn"
                            style={{ padding: "1px 6px", fontSize: "11px" }}
                            onClick={async () => {
                              const firstWord = note.canonicalText.trim().split(/\s+/)[0] || "Note";
                              const startIdx = Math.max(0, note.canonicalText.indexOf(firstWord));
                              const endIdx = startIdx + firstWord.length;
                              const anchor = await createNoteAnchor(note.canonicalText, startIdx, endIdx);
                              setAddRemarkState({ note, anchor });
                            }}
                          >
                            + Add Remark
                          </button>
                        )}
                      </div>
                    </div>

                    {/* Note Body Text with Citations */}
                    <div className="official-note-body">
                      {note.canonicalText.split("\n").map((para, idx) => {
                        if (!para.trim()) return null;
                        return (
                          <p key={idx}>
                            {para}
                            {/* Render citations matching this note */}
                            {note.citations.map((c, cIdx) => (
                              <span
                                key={cIdx}
                                className="note-citation-pill"
                                onClick={() => handleCitationClick(c.documentId, c.page)}
                                title={`Click to view page ${c.page || 1}`}
                              >
                                📎 [Doc: {documents.find((d) => d.id === c.documentId)?.filename || "File"}
                                {c.page ? ` (p.${c.page})` : ""}]
                              </span>
                            ))}
                          </p>
                        );
                      })}
                    </div>
                  </div>
                );
              })
            )}

            {/* Active Unsent Draft Card */}
            {capabilities.canEditDraft ? (
              <div className="active-draft-card">
                <div className="active-draft-header">
                  <div className="active-draft-title">
                    <span>✍ ACTIVE DRAFT (Note {notes.length + 1})</span>
                    <span style={{ fontSize: "11px", color: "#64748b", fontWeight: 500 }}>
                      — Personal working draft (unsent)
                    </span>
                  </div>

                  <button
                    type="button"
                    className="noting-btn noting-btn-secondary"
                    style={{ padding: "3px 8px", fontSize: "11.5px" }}
                    onClick={handleCiteDraftSelection}
                    title="Highlight text in draft and cite an authorized Matter document"
                  >
                    + Cite Document
                  </button>
                </div>

                {draftConflict && (
                  <div
                    className="noting-conflict-card"
                    style={{
                      background: "#fff1f2",
                      border: "1px solid #fecdd3",
                      borderRadius: "6px",
                      padding: "10px 12px",
                      margin: "8px 12px 10px 12px"
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                      <strong style={{ color: "#9f1239", fontSize: "12.5px" }}>
                        ⚠ Draft Revision Conflict (409) — Unsaved Edits Preserved
                      </strong>
                      <span style={{ fontSize: "11px", color: "#881337", fontWeight: 600 }}>
                        Server Revision #{draftConflict.serverRevision}
                      </span>
                    </div>
                    <p style={{ margin: "0 0 8px 0", fontSize: "12px", color: "#4c0519", lineHeight: "1.4" }}>
                      {draftConflict.message || "Draft changed on server; recover saved text before retrying."} Your local draft has been kept safe in the editor.
                    </p>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px", marginBottom: "10px" }}>
                      <div style={{ background: "#ffffff", border: "1px solid #fca5a5", borderRadius: "4px", padding: "8px" }}>
                        <div style={{ fontSize: "11px", fontWeight: 700, color: "#991b1b", marginBottom: "4px" }}>Your Local Edits</div>
                        <div style={{ fontSize: "12px", color: "#334155", maxHeight: "80px", overflowY: "auto", whiteSpace: "pre-wrap" }}>
                          {draftConflict.localContent || "(empty)"}
                        </div>
                      </div>
                      <div style={{ background: "#ffffff", border: "1px solid #cbd5e1", borderRadius: "4px", padding: "8px" }}>
                        <div style={{ fontSize: "11px", fontWeight: 700, color: "#475569", marginBottom: "4px" }}>Server Version</div>
                        <div style={{ fontSize: "12px", color: "#334155", maxHeight: "80px", overflowY: "auto", whiteSpace: "pre-wrap" }}>
                          {draftConflict.serverContent || "(empty)"}
                        </div>
                      </div>
                    </div>

                    <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                      <button
                        type="button"
                        className="noting-btn noting-btn-primary"
                        style={{ fontSize: "11.5px", padding: "3px 8px" }}
                        onClick={async () => {
                          draftRevisionRef.current = draftConflict.serverRevision;
                          setDraftRevision(draftConflict.serverRevision);
                          const local = draftConflict.localContent;
                          setDraftConflict(null);
                          await performSave(local, true);
                        }}
                      >
                        Keep My Edits & Overwrite Server
                      </button>
                      <button
                        type="button"
                        className="noting-btn noting-btn-secondary"
                        style={{ fontSize: "11.5px", padding: "3px 8px" }}
                        onClick={() => {
                          const merged = draftConflict.serverContent ? `${draftConflict.serverContent}\n\n${draftConflict.localContent}` : draftConflict.localContent;
                          draftRevisionRef.current = draftConflict.serverRevision;
                          setDraftRevision(draftConflict.serverRevision);
                          setDraftContent(merged);
                          latestContentRef.current = merged;
                          setDraftConflict(null);
                          void performSave(merged, true);
                        }}
                      >
                        Merge Both Versions
                      </button>
                      <button
                        type="button"
                        className="noting-btn noting-btn-secondary"
                        style={{ fontSize: "11.5px", padding: "3px 8px" }}
                        onClick={() => {
                          draftRevisionRef.current = draftConflict.serverRevision;
                          setDraftRevision(draftConflict.serverRevision);
                          setDraftContent(draftConflict.serverContent);
                          latestContentRef.current = draftConflict.serverContent;
                          setDraftConflict(null);
                          setAutosaveStatus("saved");
                        }}
                      >
                        Discard My Edits & Accept Server Draft
                      </button>
                    </div>
                  </div>
                )}

                <div className="active-draft-toolbar">
                  <span style={{ fontSize: "11px", color: "#64748b", marginRight: "6px" }}>Toolbar:</span>
                  <button type="button" className="active-draft-btn-tool" onClick={() => document.execCommand("bold")}>B</button>
                  <button type="button" className="active-draft-btn-tool" onClick={() => document.execCommand("italic")}>I</button>
                  <button type="button" className="active-draft-btn-tool" onClick={() => document.execCommand("underline")}>U</button>
                  <span style={{ color: "#cbd5e1" }}>|</span>
                  <button type="button" className="active-draft-btn-tool" onClick={() => document.execCommand("insertUnorderedList")}>• List</button>
                  <button type="button" className="active-draft-btn-tool" onClick={() => document.execCommand("insertOrderedList")}>1. List</button>
                </div>

                <textarea
                  id="active-draft-textarea"
                  className="active-draft-editor-box"
                  style={{ width: "100%", resize: "vertical" }}
                  rows={6}
                  placeholder="Enter official observations, legal references, and recommendations for this note..."
                  value={draftContent}
                  onChange={handleDraftChange}
                />
              </div>
            ) : (
              <div style={{ background: "#f1f5f9", border: "1px solid #cbd5e1", borderRadius: "6px", padding: "14px", textAlign: "center", color: "#475569", fontSize: "13px" }}>
                🔒 Noting draft is read-only. You must be the confirmed working holder to add official notes.
              </div>
            )}
          </div>
        </div>

        {/* Right Column: Matter Document Rail & Viewer (Split Mode) */}
        {!focusMode && (
          <div className="noting-right-column">
            <DocumentRailViewer
              matterId={matterId}
              matterRevision={workspaceData?.revision}
              documents={documents}
              activeDocumentId={activeDocId}
              activePage={activePage}
              canAnnotate={capabilities.canAnnotate}
              onSelectDocument={(id, page) => {
                setActiveDocId(id);
                if (page) setActivePage(page);
              }}
              onDocumentUploaded={loadWorkspace}
            />
          </div>
        )}
      </div>

      {/* Focus Mode Floating Document Drawer Toggle */}
      {focusMode && !focusDrawerOpen && (
        <button
          type="button"
          className="focus-mode-drawer-toggle"
          onClick={() => setFocusDrawerOpen(true)}
        >
          📂 Documents ({documents.length})
        </button>
      )}

      {/* Focus Mode Slide-In Overlay Drawer */}
      {focusMode && focusDrawerOpen && (
        <div className="focus-mode-overlay-drawer">
          <div style={{ padding: "8px 12px", background: "#f8fafc", borderBottom: "1px solid #e2e8f0", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ fontSize: "12px", fontWeight: 700, color: "#0f172a" }}>Matter Documents Drawer</span>
            <button className="noting-btn noting-btn-secondary" style={{ padding: "2px 6px" }} onClick={() => setFocusDrawerOpen(false)}>✕</button>
          </div>
          <div style={{ flex: 1, overflow: "hidden" }}>
            <DocumentRailViewer
              matterId={matterId}
              matterRevision={workspaceData?.revision}
              documents={documents}
              activeDocumentId={activeDocId}
              activePage={activePage}
              canAnnotate={capabilities.canAnnotate}
              onSelectDocument={(id, page) => {
                setActiveDocId(id);
                if (page) setActivePage(page);
              }}
              onDocumentUploaded={loadWorkspace}
            />
          </div>
        </div>
      )}

      {/* Floating Add Remark Action Pill on Text Selection */}
      {selectedRange && capabilities.canRemark && (
        <div
          className="text-selection-pill"
          style={{ top: `${selectedRange.coords.y}px`, left: `${selectedRange.coords.x}px` }}
          onClick={() => {
            setAddRemarkState({ note: selectedRange.note, anchor: selectedRange.anchor });
            setSelectedRange(null);
          }}
        >
          💬 Add Remark
        </div>
      )}

      {/* Add Remark Modal */}
      {addRemarkState && (
        <AddRemarkModal
          matterId={matterId}
          noteId={addRemarkState.note.id}
          noteNumber={addRemarkState.note.number}
          anchor={addRemarkState.anchor}
          onClose={() => setAddRemarkState(null)}
          onCreated={(rem) => {
            setRemarksMap((prev) => ({
              ...prev,
              [rem.noteId]: [...(prev[rem.noteId] || []), rem]
            }));
          }}
        />
      )}

      {/* View Remarks Modal */}
      {viewRemarksState && (
        <ViewRemarksModal
          matterId={matterId}
          noteId={viewRemarksState.note.id}
          noteNumber={viewRemarksState.note.number}
          remarks={viewRemarksState.remarks}
          canManage={capabilities.canRemark}
          onClose={() => setViewRemarksState(null)}
          onUpdated={(updated) => {
            setRemarksMap((prev) => ({
              ...prev,
              [updated.noteId]: (prev[updated.noteId] || []).map((r) =>
                r.id === updated.id ? updated : r
              )
            }));
            setViewRemarksState((prev) =>
              prev
                ? {
                    ...prev,
                    remarks: prev.remarks.map((r) => (r.id === updated.id ? updated : r))
                  }
                : null
            );
          }}
        />
      )}

      {/* Cite Document Modal */}
      {showCitationModal && citationAnchor && (
        <CiteDocumentModal
          matterId={matterId}
          anchor={citationAnchor}
          draftRevision={draftRevision}
          documents={documents}
          onClose={() => {
            setShowCitationModal(false);
            setCitationAnchor(null);
          }}
          onCited={(updatedDraft) => {
            setDraftRevision(updatedDraft.revision);
            draftRevisionRef.current = updatedDraft.revision;
            setShowCitationModal(false);
            setCitationAnchor(null);
            loadWorkspace();
          }}
        />
      )}

      {/* Send / Return File Modal */}
      {showSendModal && (
        <SendReturnModal
          matterId={matterId}
          activeDraftRevision={draftRevision}
          linkedDaks={linkedDaks}
          defaultControllingDakId={sourceDakId}
          onClose={() => setShowSendModal(false)}
          onSent={() => {
            setShowSendModal(false);
            setDraftContent("");
            latestContentRef.current = "";
            loadDraft();
            loadWorkspace();
          }}
        />
      )}
    </div>
  );
};
