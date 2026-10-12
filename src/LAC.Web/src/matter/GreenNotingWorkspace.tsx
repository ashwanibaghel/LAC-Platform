import React, { useState, useEffect, useRef, useCallback } from "react";
import { notingOfficeApi } from "./notingOfficeApi";
import type {
  MatterNotingWorkspaceData,
  MatterWorkingDraftDto,
  OfficeSessionInfo,
  OfficeSaveReceiptDto,
  MatterOfficePreviewDto,
  GreenSheetMetadataDto,
  MatterOfficialNoteDto,
  MatterNoteRemarkDto,
  NoteAnchor
} from "./notingTypes";
import { GreenSheetViewer } from "./GreenSheetViewer";
import { OnlyOfficeNoteFrame } from "./OnlyOfficeNoteFrame";
import { SendReturnModal } from "./SendReturnModal";
import { AddRemarkModal, ViewRemarksModal } from "./RemarkModal";

interface GreenNotingWorkspaceProps {
  matterId: string;
  villageName?: string;
  controllingDakNumber?: string | null;
}

type WorkspaceViewMode = "split" | "focus-editor" | "focus-sheet";

export const GreenNotingWorkspace: React.FC<GreenNotingWorkspaceProps> = ({
  matterId,
  villageName,
  controllingDakNumber
}) => {
  // Layout & View Mode
  const [viewMode, setViewMode] = useState<WorkspaceViewMode>("split");

  // Core Data
  const [workspaceData, setWorkspaceData] = useState<MatterNotingWorkspaceData | null>(null);
  const [, setWorkingDraft] = useState<MatterWorkingDraftDto | null>(null);
  const [workingRevision, setWorkingRevision] = useState<number>(0);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // ONLYOFFICE Session State
  const [sessionInfo, setSessionInfo] = useState<OfficeSessionInfo | null>(null);
  const [sessionRevision, setSessionRevision] = useState<number>(1);
  const [clientGeneration, setClientGeneration] = useState<number>(1);
  const [sessionError, setSessionError] = useState<string | null>(null);

  // Save Receipts & Truthful Status
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "committed" | "failed">("idle");
  const [saveStatusText, setSaveStatusText] = useState<string>("Draft initialised");
  const [lastCommittedReceipt, setLastCommittedReceipt] = useState<OfficeSaveReceiptDto | null>(null);
  const lastSaveRequestIdRef = useRef<string | null>(null);
  const pollTimerRef = useRef<number | null>(null);
  const autoSaveTimerRef = useRef<number | null>(null);

  // Continuous Green Sheet State
  const [greenSheet, setGreenSheet] = useState<GreenSheetMetadataDto | null>(null);
  const [loadingSheet, setLoadingSheet] = useState<boolean>(false);

  // Quiesced Preview & Review Flow
  const [isQuiescing, setIsQuiescing] = useState<boolean>(false);
  const [preview, setPreview] = useState<MatterOfficePreviewDto | null>(null);
  const [showReviewModal, setShowReviewModal] = useState<boolean>(false);
  const [isConfirmingPreview, setIsConfirmingPreview] = useState<boolean>(false);
  const [citationConflictModal, setCitationConflictModal] = useState<boolean>(false);
  const [isConfirmed, setIsConfirmed] = useState<boolean>(false);

  // Submit / Send State
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [showSendModal, setShowSendModal] = useState<boolean>(false);
  const [submitSuccessNotice, setSubmitSuccessNotice] = useState<string | null>(null);

  // Remarks Modals
  const [activeRemarkNote, setActiveRemarkNote] = useState<MatterOfficialNoteDto | null>(null);
  const [showViewRemarksModal, setShowViewRemarksModal] = useState<boolean>(false);
  const [showAddRemarkModal, setShowAddRemarkModal] = useState<boolean>(false);
  const [pendingRemarkAnchor, setPendingRemarkAnchor] = useState<NoteAnchor | null>(null);
  const [noteRemarksMap, setNoteRemarksMap] = useState<{ [noteId: string]: MatterNoteRemarkDto[] }>({});

  // 1. Initial Load: Workspace, Working Draft, and Green Sheet
  const loadWorkspace = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [ws, draftRes] = await Promise.all([
        notingOfficeApi.getNotingWorkspace(matterId),
        notingOfficeApi.getWorkingDraft(matterId)
      ]);
      setWorkspaceData(ws);
      setWorkingDraft(draftRes.draft);
      setWorkingRevision(draftRes.etagRevision);

      // Load official green sheet
      setLoadingSheet(true);
      try {
        const sheet = await notingOfficeApi.createGreenSheet(matterId, null);
        setGreenSheet(sheet);
      } catch {
        // If no official sheet exists yet, greenSheet remains null (viewer renders blank official template)
        setGreenSheet(null);
      } finally {
        setLoadingSheet(false);
      }

      // If user has edit permissions, initialize ONLYOFFICE session
      if (ws.capabilities.canEditDraft) {
        try {
          const session = await notingOfficeApi.createOfficeSession(
            matterId,
            ws.sourceDakId,
            null,
            draftRes.etagRevision
          );
          setSessionInfo(session);
          setSessionRevision(session.revision);
          setSaveStatusText("Editor ready");
        } catch (sErr: any) {
          setSessionError(sErr.message || "Failed to establish ONLYOFFICE session");
        }
      }
    } catch (err: any) {
      setError(err.message || "Failed to load noting workspace");
    } finally {
      setLoading(false);
    }
  }, [matterId]);

  useEffect(() => {
    void loadWorkspace();
    return () => {
      if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
      if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
    };
  }, [loadWorkspace]);

  // 2. Poll Save Receipt until Committed or Failed
  const pollSaveReceipt = useCallback(
    async (sessionId: string, saveId: string) => {
      try {
        const receipt = await notingOfficeApi.getSaveStatus(sessionId, saveId);
        if (receipt.state === "Pending") {
          // Poll again after 600ms
          pollTimerRef.current = window.setTimeout(() => {
            void pollSaveReceipt(sessionId, saveId);
          }, 600);
        } else if (receipt.state === "Committed") {
          setSaveStatus("committed");
          setLastCommittedReceipt(receipt);
          lastSaveRequestIdRef.current = receipt.id;
          const shortHash = receipt.sha256 ? receipt.sha256.substring(0, 8) : "";
          setSaveStatusText(`Saved to server: v${receipt.version || 1} (${shortHash})`);
          if (receipt.sessionRevision) {
            setSessionRevision(receipt.sessionRevision);
          }
        } else if (receipt.state === "Failed") {
          setSaveStatus("failed");
          const failMsg = receipt.failureCode || "Save failed";
          setSaveStatusText(`Save failed: ${failMsg}. Last committed bytes retained.`);
        }
      } catch (err: any) {
        setSaveStatus("failed");
        setSaveStatusText(`Save status check failed: ${err.message}`);
      }
    },
    []
  );

  // 3. Trigger Server Force-Save
  const triggerSave = useCallback(
    async (_isManual: boolean = false): Promise<string | null> => {
      if (!sessionInfo || sessionInfo.state !== "Active") return null;
      if (saveStatus === "saving") return lastSaveRequestIdRef.current;

      setSaveStatus("saving");
      setSaveStatusText("Saving to server…");

      const nextGen = clientGeneration + 1;
      setClientGeneration(nextGen);

      try {
        const receipt = await notingOfficeApi.requestSave(sessionInfo.id, nextGen, sessionRevision);
        lastSaveRequestIdRef.current = receipt.id;

        if (receipt.state === "Committed") {
          setSaveStatus("committed");
          setLastCommittedReceipt(receipt);
          const shortHash = receipt.sha256 ? receipt.sha256.substring(0, 8) : "";
          setSaveStatusText(`Saved to server: v${receipt.version || 1} (${shortHash})`);
          return receipt.id;
        }

        // State is Pending: poll until committed
        void pollSaveReceipt(sessionInfo.id, receipt.id);
        return receipt.id;
      } catch (err: any) {
        setSaveStatus("failed");
        setSaveStatusText(`Save request failed: ${err.message}`);
        return null;
      }
    },
    [sessionInfo, sessionRevision, clientGeneration, saveStatus, pollSaveReceipt]
  );

  // 4. Handle Document State Change (Debounced Autosave Trigger)
  const handleDocumentStateChange = useCallback(
    (isDirty: boolean) => {
      if (!isDirty) return;
      if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
      // Debounce 2500ms
      autoSaveTimerRef.current = window.setTimeout(() => {
        void triggerSave(false);
      }, 2500);
    },
    [triggerSave]
  );

  // 5. Quiesce Input & Generate Canonical Preview Flow
  const handleStartReview = async () => {
    if (!sessionInfo) return;
    setIsQuiescing(true);
    setSaveStatusText("Quiescing editor & obtaining committed receipt…");

    try {
      // 1. Ensure latest save request is committed
      let committedSaveId = lastCommittedReceipt?.id;
      if (!committedSaveId || saveStatus !== "committed") {
        committedSaveId = await triggerSave(true) || undefined;
        // Wait up to 5 seconds for commit if pending
        let retries = 10;
        while (retries > 0 && saveStatus !== "committed" && committedSaveId) {
          await new Promise((r) => setTimeout(r, 500));
          const receipt = await notingOfficeApi.getSaveStatus(sessionInfo.id, committedSaveId);
          if (receipt.state === "Committed") {
            setLastCommittedReceipt(receipt);
            committedSaveId = receipt.id;
            break;
          }
          retries--;
        }
      }

      if (!committedSaveId) {
        throw new Error("Cannot create preview: No committed snapshot found. Please type note content first.");
      }

      // 2. Generate immutable canonical preview (freezes lease)
      setSaveStatusText("Converting DOCX to verified canonical legal text…");
      const prev = await notingOfficeApi.createPreview(sessionInfo.id, committedSaveId, sessionRevision);
      setPreview(prev);
      setIsConfirmed(false);
      setShowReviewModal(true);
    } catch (err: any) {
      alert(err.message || "Failed to generate canonical preview");
    } finally {
      setIsQuiescing(false);
    }
  };

  // 6. Confirm Canonical Preview
  const handleConfirmPreview = async (resetCitations: boolean = false) => {
    if (!preview) return;
    setIsConfirmingPreview(true);

    try {
      const updatedDraft = await notingOfficeApi.confirmPreview(
        matterId,
        preview.id,
        preview.canonicalHash,
        resetCitations,
        workingRevision
      );
      setWorkingDraft(updatedDraft);
      setWorkingRevision(updatedDraft.revision);
      setIsConfirmed(true);
      setCitationConflictModal(false);

      // Generate working-inclusive Green Sheet preview
      try {
        const previewSheet = await notingOfficeApi.createGreenSheet(matterId, preview.id);
        setGreenSheet(previewSheet);
      } catch {}
    } catch (err: any) {
      if (err.status === 409) {
        // Citation conflict!
        setCitationConflictModal(true);
      } else {
        alert(err.message || "Failed to confirm canonical preview");
      }
    } finally {
      setIsConfirmingPreview(false);
    }
  };

  // 7. Atomic Submit to Official File
  const handleSubmitToOfficialFile = async () => {
    if (!preview) return;
    if (!window.confirm("Submit this note to the official continuous Green Legal file? This action cannot be undone.")) {
      return;
    }

    setIsSubmitting(true);
    try {
      const result = await notingOfficeApi.submitOfficeNote(
        matterId,
        workspaceData?.sourceDakId || null,
        preview.id,
        preview.canonicalHash,
        workingRevision
      );

      setSubmitSuccessNotice(`Note #${result.noteNumber} successfully published to official continuous Green Legal file!`);
      setShowReviewModal(false);
      setPreview(null);
      setIsConfirmed(false);

      // Reload fresh workspace and official green sheet
      await loadWorkspace();
    } catch (err: any) {
      alert(err.message || "Failed to submit note to official file");
    } finally {
      setIsSubmitting(false);
    }
  };

  // 8. Open Remarks for Note
  const handleOpenRemarks = async (note: MatterOfficialNoteDto) => {
    setActiveRemarkNote(note);
    try {
      const res = await fetch(`/api/matters/${matterId}/notes/${note.id}/remarks`, { credentials: "include" });
      if (res.ok) {
        const list: MatterNoteRemarkDto[] = await res.json();
        setNoteRemarksMap((prev) => ({ ...prev, [note.id]: Array.isArray(list) ? list : [] }));
      }
    } catch {}
    setShowViewRemarksModal(true);
  };

  const handleInitiateRemark = (note: MatterOfficialNoteDto) => {
    setActiveRemarkNote(note);
    setPendingRemarkAnchor({
      version: 1,
      start: 0,
      end: Math.min(60, note.canonicalText?.length || 0),
      quote: note.canonicalText?.substring(0, 60) || "Note entry",
      quoteHash: ""
    });
    setShowAddRemarkModal(true);
  };

  return (
    <div className={`green-noting-workspace-root mode-${viewMode}`}>
      {/* Secondary Context Control Bar */}
      <div className="noting-sub-toolbar">
        <div className="noting-sub-toolbar-left">
          <div className="file-indicator-badge">
            <span className="file-badge-icon">📁</span>
            <span className="file-badge-text">OFFICIAL FILE NOTING</span>
          </div>
          {villageName && <span className="noting-meta-crumb">Village: <strong>{villageName}</strong></span>}
          {controllingDakNumber && (
            <span className="noting-meta-crumb">
              Controlling Dak: <strong style={{ color: "#0369a1" }}>{controllingDakNumber}</strong>
            </span>
          )}
          <span className="noting-meta-crumb">
            Official Notes: <strong>{workspaceData?.notes.length || 0}</strong>
          </span>
          {workspaceData && workspaceData.notes.length > 0 && (
            <div style={{ display: "inline-flex", gap: "4px" }}>
              <button
                type="button"
                className="noting-text-btn"
                onClick={() => void handleOpenRemarks(workspaceData.notes[workspaceData.notes.length - 1])}
                title="View remarks on latest official note"
              >
                💬 Remarks
              </button>
              {workspaceData.capabilities.canRemark && (
                <button
                  type="button"
                  className="noting-text-btn"
                  onClick={() => handleInitiateRemark(workspaceData.notes[workspaceData.notes.length - 1])}
                  title="Add remark on latest official note"
                >
                  + Add Remark
                </button>
              )}
            </div>
          )}
        </div>

        {/* Truthful Save Status Banner */}
        <div className="noting-sub-toolbar-center">
          <div className={`noting-save-badge status-${saveStatus}`}>
            {saveStatus === "saving" && <span className="noting-spinner-inline" />}
            {saveStatus === "committed" && <span className="status-dot-green">●</span>}
            {saveStatus === "failed" && <span className="status-dot-red">▲</span>}
            <span className="status-label">{saveStatusText}</span>
          </div>
        </div>

        {/* View Mode Switcher & Primary Action Buttons */}
        <div className="noting-sub-toolbar-right">
          <div className="view-mode-toggle-group">
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "split" ? "active" : ""}`}
              onClick={() => setViewMode("split")}
              title="Split View: Continuous Green Sheet (Left) + Note Editor (Right)"
            >
              ◫ Split
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "focus-editor" ? "active" : ""}`}
              onClick={() => setViewMode("focus-editor")}
              title="Focus Note Editor (Full Width)"
            >
              🗖 Note Editor
            </button>
            <button
              type="button"
              className={`view-toggle-btn ${viewMode === "focus-sheet" ? "active" : ""}`}
              onClick={() => setViewMode("focus-sheet")}
              title="Focus Continuous Green Sheet (Full Width)"
            >
              📄 Green Sheet
            </button>
          </div>

          {sessionInfo && sessionInfo.state === "Active" && (
            <button
              type="button"
              className="noting-btn-review"
              onClick={handleStartReview}
              disabled={isQuiescing || loading}
              title="Quiesce editor, verify canonical text, and prepare submission"
            >
              {isQuiescing ? "Quiescing…" : "Review & Submit Note →"}
            </button>
          )}
        </div>
      </div>

      {error && (
        <div style={{ background: "#fee2e2", color: "#b91c1c", padding: "6px 12px", borderRadius: 4, fontSize: 12 }}>
          ⚠️ {error}
        </div>
      )}

      {submitSuccessNotice && (
        <div className="noting-banner-success">
          <span>✓ {submitSuccessNotice}</span>
          <button type="button" onClick={() => setSubmitSuccessNotice(null)}>✕</button>
        </div>
      )}

      {/* Main Split / Focus Workspace Panes */}
      <div className="green-noting-panes-wrapper">
        {/* LEFT PANE: Immutable Continuous Physical Green Sheet PDF */}
        {(viewMode === "split" || viewMode === "focus-sheet") && (
          <div className="green-noting-left-pane">
            <GreenSheetViewer
              matterId={matterId}
              sheetId={greenSheet?.id || null}
              metadata={greenSheet}
              onRefresh={() => void loadWorkspace()}
              isLoading={loadingSheet}
            />
          </div>
        )}

        {/* RIGHT PANE: Actor's Private Note ONLYOFFICE Editor */}
        {(viewMode === "split" || viewMode === "focus-editor") && (
          <div className="green-noting-right-pane">
            <div className="note-editor-card-header">
              <div className="editor-card-title-block">
                <span className="editor-badge-private">ACTOR PRIVATE DRAFT</span>
                <span className="editor-note-hint">
                  {workspaceData ? `Drafting Note #${workspaceData.notes.length + 1}` : "Drafting Note"}
                </span>
              </div>
              <div className="editor-card-controls">
                <button
                  type="button"
                  className="btn-save-receipt"
                  onClick={() => void triggerSave(true)}
                  disabled={saveStatus === "saving" || !sessionInfo || sessionInfo.state !== "Active"}
                  title="Request immediate force-save receipt from Document Server"
                >
                  Force Save
                </button>
              </div>
            </div>

            <div className="note-editor-container">
              {loading ? (
                <div className="noting-loading-pane">
                  <div className="noting-spinner" />
                  <p>Loading actor note editor…</p>
                </div>
              ) : sessionInfo ? (
                <OnlyOfficeNoteFrame
                  sessionId={sessionInfo.id}
                  isFrozen={sessionInfo.state === "Frozen"}
                  onDocumentStateChange={handleDocumentStateChange}
                  onError={(err) => setSessionError(err)}
                />
              ) : (
                <div className="noting-no-session-pane">
                  <div className="info-icon">ℹ</div>
                  <h4>No Active Editing Lease</h4>
                  <p>{sessionError || "You currently do not have an active editing lease for this Matter."}</p>
                  {workspaceData?.capabilities.canEditDraft && (
                    <button
                      type="button"
                      className="noting-btn noting-btn-primary"
                      onClick={() => void loadWorkspace()}
                    >
                      Start New Editing Lease
                    </button>
                  )}
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {/* Canonical Review & Submit Modal */}
      {showReviewModal && preview && (
        <div className="noting-modal-overlay">
          <div className="noting-modal-card" style={{ maxWidth: "780px" }}>
            <div className="noting-modal-header">
              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                <span style={{ fontSize: "18px" }}>📜</span>
                <div>
                  <h3 style={{ margin: 0 }}>Official Note Canonical Review</h3>
                  <div style={{ fontSize: "11px", color: "#64748b" }}>
                    Verified against OfficeNoteV1 schema and Liberation Serif 12 physical policy
                  </div>
                </div>
              </div>
              <button
                type="button"
                className="noting-btn-close"
                onClick={() => setShowReviewModal(false)}
              >
                ✕
              </button>
            </div>

            <div className="noting-modal-body" style={{ maxHeight: "480px", overflowY: "auto" }}>
              <div className="canonical-hash-banner">
                <span className="hash-label">CANONICAL SHA256:</span>
                <code className="hash-code">{preview.canonicalHash}</code>
              </div>

              <div>
                <label style={{ fontSize: "11px", fontWeight: 700, color: "#475569", textTransform: "uppercase" }}>
                  Verified Canonical Note Text
                </label>
                <div className="canonical-preview-paper">
                  <pre className="canonical-text-content">{preview.canonicalText}</pre>
                </div>
              </div>

              {!isConfirmed ? (
                <div className="confirmation-callout">
                  <p>
                    Please verify the exact legal text above. Confirming will freeze this preview and validate all citations for submission.
                  </p>
                  <button
                    type="button"
                    className="noting-btn noting-btn-primary"
                    onClick={() => void handleConfirmPreview(false)}
                    disabled={isConfirmingPreview}
                  >
                    {isConfirmingPreview ? "Validating & Confirming…" : "Confirm Canonical Preview"}
                  </button>
                </div>
              ) : (
                <div className="confirmed-action-panel">
                  <div className="confirmed-badge">
                    ✓ Canonical preview confirmed! Ready for official file submission or forwarding.
                  </div>
                  <div className="submit-action-buttons">
                    <button
                      type="button"
                      className="noting-btn-submit-file"
                      onClick={() => void handleSubmitToOfficialFile()}
                      disabled={isSubmitting}
                    >
                      {isSubmitting ? "Publishing…" : "✓ Submit to Official File (Keep at Desk)"}
                    </button>
                    <button
                      type="button"
                      className="noting-btn-send-dak"
                      onClick={() => setShowSendModal(true)}
                      disabled={isSubmitting}
                    >
                      📤 Submit & Forward File (Dispatch Dak)
                    </button>
                  </div>
                </div>
              )}
            </div>

            <div className="noting-modal-footer">
              <button
                type="button"
                className="noting-btn noting-btn-secondary"
                onClick={() => setShowReviewModal(false)}
                disabled={isSubmitting || isConfirmingPreview}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Citation Conflict Modal (HTTP 409) */}
      {citationConflictModal && (
        <div className="noting-modal-overlay">
          <div className="noting-modal-card" style={{ maxWidth: "520px" }}>
            <div className="noting-modal-header">
              <h3 style={{ color: "#b91c1c" }}>⚠️ Citation Range Conflict (HTTP 409)</h3>
              <button type="button" className="noting-btn-close" onClick={() => setCitationConflictModal(false)}>✕</button>
            </div>
            <div className="noting-modal-body">
              <p style={{ margin: 0, fontSize: "13px", color: "#334155" }}>
                This note previously had document citations linked to specific text excerpts. The edited note text has changed, so prior citation anchors are invalid.
              </p>
              <p style={{ margin: "10px 0 0 0", fontSize: "12.5px", color: "#64748b" }}>
                Under office policy, citation ranges are never silently shifted. You must deliberately reset citations and re-anchor them if necessary.
              </p>
            </div>
            <div className="noting-modal-footer">
              <button
                type="button"
                className="noting-btn noting-btn-secondary"
                onClick={() => setCitationConflictModal(false)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="noting-btn noting-btn-danger"
                onClick={() => void handleConfirmPreview(true)}
                disabled={isConfirmingPreview}
              >
                {isConfirmingPreview ? "Resetting…" : "Reset Citations & Confirm"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Send / Forward Modal */}
      {showSendModal && preview && (
        <SendReturnModal
          matterId={matterId}
          sourceDakId={workspaceData?.sourceDakId || null}
          officePreviewId={preview.id}
          canonicalHash={preview.canonicalHash}
          workingRevision={workingRevision}
          onClose={() => setShowSendModal(false)}
          onSent={(res) => {
            setSubmitSuccessNotice(`Note #${res.noteNumber} published and file successfully forwarded!`);
            setShowSendModal(false);
            setShowReviewModal(false);
            setPreview(null);
            void loadWorkspace();
          }}
        />
      )}

      {/* Add Supervisory Remark Modal */}
      {showAddRemarkModal && activeRemarkNote && pendingRemarkAnchor && (
        <AddRemarkModal
          matterId={matterId}
          noteId={activeRemarkNote.id}
          noteNumber={activeRemarkNote.number}
          anchor={pendingRemarkAnchor}
          onClose={() => setShowAddRemarkModal(false)}
          onCreated={(r) => {
            setNoteRemarksMap((prev) => ({
              ...prev,
              [activeRemarkNote.id]: [...(prev[activeRemarkNote.id] || []), r]
            }));
            setShowAddRemarkModal(false);
          }}
        />
      )}

      {/* View Remarks Modal */}
      {showViewRemarksModal && activeRemarkNote && (
        <ViewRemarksModal
          matterId={matterId}
          noteId={activeRemarkNote.id}
          noteNumber={activeRemarkNote.number}
          remarks={noteRemarksMap[activeRemarkNote.id] || []}
          canManage={workspaceData?.capabilities.canRemark || false}
          onClose={() => setShowViewRemarksModal(false)}
          onUpdated={(updated) => {
            setNoteRemarksMap((prev) => ({
              ...prev,
              [activeRemarkNote.id]: (prev[activeRemarkNote.id] || []).map((x) => (x.id === updated.id ? updated : x))
            }));
          }}
        />
      )}
    </div>
  );
};
