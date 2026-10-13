import React, { useState, useEffect, useRef, useCallback } from "react";
import { notingOfficeApi, generateUuid } from "./notingOfficeApi";
import { awaitCommittedOfficeSave, submittedNoteNumber } from "./officeSaveBarrier";
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
import { extractAnchorFromDomSelection, type NoteAnchorDto } from "./canonicalTextSelection";

interface GreenNotingWorkspaceProps {
  matterId: string;
  villageName?: string;
  controllingDakNumber?: string | null;
  initialControllingDakId?: string | null;
  linkedDaks?: Array<{ id: string; diaryNumber: string; subject?: string; status?: string }>;
}

type WorkspaceViewMode = "split" | "focus-editor" | "focus-sheet";
type RightPaneViewMode = "pdf" | "canonical-notes";

export const GreenNotingWorkspace: React.FC<GreenNotingWorkspaceProps> = ({
  matterId,
  villageName,
  controllingDakNumber,
  initialControllingDakId,
  linkedDaks = []
}) => {
  // Layout & View Mode
  const [viewMode, setViewMode] = useState<WorkspaceViewMode>("split");
  const [rightPaneView, setRightPaneView] = useState<RightPaneViewMode>("pdf");

  // Controlling Dak Selection
  const [selectedDakId, setSelectedDakId] = useState<string | null>(
    initialControllingDakId || linkedDaks[0]?.id || null
  );

  useEffect(() => {
    if (initialControllingDakId && initialControllingDakId !== selectedDakId) {
      setSelectedDakId(initialControllingDakId);
    } else if (!selectedDakId && linkedDaks.length > 0 && linkedDaks[0]?.id) {
      setSelectedDakId(linkedDaks[0].id);
    }
  }, [initialControllingDakId, linkedDaks, selectedDakId]);

  // Core Workspace & Notes Data
  const [workspaceData, setWorkspaceData] = useState<MatterNotingWorkspaceData | null>(null);
  const [, setWorkingDraft] = useState<MatterWorkingDraftDto | null>(null);
  const [workingRevision, setWorkingRevision] = useState<number>(0);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // ONLYOFFICE Session State
  const [sessionInfo, setSessionInfo] = useState<OfficeSessionInfo | null>(null);
  const [, setSessionRevision] = useState<number>(1);
  const sessionRevisionRef = useRef<number>(1);
  const [sessionError, setSessionError] = useState<string | null>(null);
  const [isStartingSession, setIsStartingSession] = useState<boolean>(false);

  // Edit Generation Tracking & Safe Serialization (Fix 2)
  const dirtyGenerationRef = useRef<number>(0);
  const lastRequestedGenerationRef = useRef<number>(0);
  const lastCommittedGenerationRef = useRef<number>(0);
  const inFlightSavePromiseRef = useRef<Promise<OfficeSaveReceiptDto | null> | null>(null);
  const lastSaveCommandRef = useRef<{ key: string; gen: number; rev: number } | null>(null);

  // Save Receipts & Truthful Status
  const [saveStatus, setSaveStatus] = useState<"idle" | "saving" | "committed" | "failed">("idle");
  const [saveStatusText, setSaveStatusText] = useState<string>("Draft initialised");
  const [lastCommittedReceipt, setLastCommittedReceipt] = useState<OfficeSaveReceiptDto | null>(null);
  const autoSaveTimerRef = useRef<number | null>(null);
  const saveBlockedRef = useRef(false);

  // Continuous Green Sheet PDF State
  const [greenSheet, setGreenSheet] = useState<GreenSheetMetadataDto | null>(null);
  const [loadingSheet, setLoadingSheet] = useState<boolean>(false);

  // Quiesced Preview & Review Flow (Fix 2)
  const [isQuiescing, setIsQuiescing] = useState<boolean>(false);
  const [isInputBlocked, setIsInputBlocked] = useState<boolean>(false);
  const [isFrozen, setIsFrozen] = useState<boolean>(false);
  useEffect(() => { saveBlockedRef.current = isFrozen || isInputBlocked; }, [isFrozen, isInputBlocked]);
  const [preview, setPreview] = useState<MatterOfficePreviewDto | null>(null);
  const [showReviewModal, setShowReviewModal] = useState<boolean>(false);
  const [isConfirmingPreview, setIsConfirmingPreview] = useState<boolean>(false);
  const [citationConflictModal, setCitationConflictModal] = useState<boolean>(false);
  const [isConfirmed, setIsConfirmed] = useState<boolean>(false);

  // Submit / Send State
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [showSendModal, setShowSendModal] = useState<boolean>(false);
  const [submitSuccessNotice, setSubmitSuccessNotice] = useState<string | null>(null);

  // Supervisory Remarks & Canonical Text Selection (Fix 3)
  const [activeRemarkNote, setActiveRemarkNote] = useState<MatterOfficialNoteDto | null>(null);
  const [showViewRemarksModal, setShowViewRemarksModal] = useState<boolean>(false);
  const [showAddRemarkModal, setShowAddRemarkModal] = useState<boolean>(false);
  const [selectedAnchor, setSelectedAnchor] = useState<NoteAnchorDto | null>(null);
  const [selectedNoteForAnchor, setSelectedNoteForAnchor] = useState<MatterOfficialNoteDto | null>(null);
  const [selectionNotice, setSelectionNotice] = useState<string | null>(null);
  const [noteRemarksMap, setNoteRemarksMap] = useState<{ [noteId: string]: MatterNoteRemarkDto[] }>({});

  // 1. Separate: Refresh Official Notes & PDF (Fix 4 - Does NOT wipe or recreate session)
  const loadNotesAndPdf = useCallback(async (dakId: string | null = selectedDakId) => {
    setLoading(true);
    setError(null);
    try {
      const [ws, draftRes] = await Promise.all([
        notingOfficeApi.getNotingWorkspace(matterId, dakId),
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
        setGreenSheet(null);
      } finally {
        setLoadingSheet(false);
      }
    } catch (err: any) {
      setError(err.message || "Failed to load noting workspace");
    } finally {
      setLoading(false);
    }
  }, [matterId, selectedDakId]);

  // 2. Initial Mount: Load Notes & PDF
  useEffect(() => {
    void loadNotesAndPdf(selectedDakId);
    return () => {
      if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
    };
  }, [loadNotesAndPdf, selectedDakId]);

  // 3. Start or Resume an Authorized ONLYOFFICE Session (Fix 4)
  const handleStartOrResumeSession = async (resumeSnapshotId?: string | null) => {
    if (!workspaceData?.capabilities.canEditDraft) return;
    if (inFlightSavePromiseRef.current) { setSessionError("Wait for the pending save before replacing the editor session."); return; }
    setIsStartingSession(true);
    setSessionError(null);

    try {
      const sourceDak = selectedDakId || workspaceData.sourceDakId;
      const snapId = resumeSnapshotId !== undefined ? resumeSnapshotId : (sessionInfo?.latestSnapshotId || null);

      const session = await notingOfficeApi.createOfficeSession(
        matterId,
        sourceDak,
        snapId,
        workingRevision
      );

      dirtyGenerationRef.current = 0;
      lastRequestedGenerationRef.current = 0;
      lastCommittedGenerationRef.current = 0;
      setLastCommittedReceipt(null);
      lastSaveCommandRef.current = null;
      setPreview(null);
      setIsConfirmed(false);
      setSessionInfo(session);
      sessionRevisionRef.current = session.revision;
      setSessionRevision(session.revision);
      setIsFrozen(session.state === "Frozen" || session.state === "Submitted");
      setIsInputBlocked(false);
      setSaveStatus("idle");
      setSaveStatusText("Editor session active");
    } catch (err: any) {
      setSessionError(err.message || "Failed to create or resume ONLYOFFICE session");
    } finally {
      setIsStartingSession(false);
    }
  };

  // Serialize the entire request/callback barrier, not just the command response.
  const triggerSave = useCallback(
    async (_isManual: boolean = false): Promise<OfficeSaveReceiptDto | null> => {
      if (!sessionInfo || sessionInfo.state !== "Active" || isFrozen) return null;
      if (inFlightSavePromiseRef.current) return inFlightSavePromiseRef.current;
      const runSave = async (): Promise<OfficeSaveReceiptDto | null> => {
        setSaveStatus("saving");
        setSaveStatusText("Saving to server…");
        const retry = _isManual && saveStatus === "failed" ? lastSaveCommandRef.current : null;
        const command = retry || {
          key: generateUuid(),
          gen: Math.max(dirtyGenerationRef.current, lastRequestedGenerationRef.current + 1),
          rev: sessionRevisionRef.current,
        };
        const generation = command.gen;
        lastRequestedGenerationRef.current = generation;
        lastSaveCommandRef.current = command;
        try {
          const initial = await notingOfficeApi.requestSave(sessionInfo.id, generation, command.rev, command.key);
          const receipt = await awaitCommittedOfficeSave(initial,
            () => notingOfficeApi.getSaveStatus(sessionInfo.id, initial.id),
            current => {
              sessionRevisionRef.current = current.sessionRevision;
              setSessionRevision(current.sessionRevision);
            });
          lastCommittedGenerationRef.current = receipt.clientGeneration;
          setLastCommittedReceipt(receipt);
          if (dirtyGenerationRef.current > receipt.clientGeneration) {
            setSaveStatus("idle");
            setSaveStatusText("Unsaved edits newer than the last server commit");
          } else {
            setSaveStatus("committed");
            setSaveStatusText(`Saved to server: v${receipt.version} (${receipt.sha256!.substring(0, 8)})`);
          }
          return receipt;
        } catch (err: any) {
          setSaveStatus("failed");
          setSaveStatusText(err.message || "Save failed. Last committed bytes retained.");
          return null;
        } finally {
          inFlightSavePromiseRef.current = null;
        }
      };
      const promise = runSave();
      inFlightSavePromiseRef.current = promise;
      return promise;
    }, [sessionInfo, isFrozen, saveStatus]
  );

  // 6. Handle Document State Change (Debounced Autosave Trigger)
  const handleDocumentStateChange = useCallback(
    (isDirty: boolean) => {
      if (!isDirty || isFrozen || isInputBlocked) return;
      dirtyGenerationRef.current = Math.max(dirtyGenerationRef.current, lastRequestedGenerationRef.current) + 1;
      setSaveStatus("idle");
      setSaveStatusText("Unsaved changes — waiting for server commit");

      if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
      const saveLatest = async () => {
        if (saveBlockedRef.current) return;
        const receipt = await triggerSave(false);
        if (receipt && !saveBlockedRef.current && dirtyGenerationRef.current > lastCommittedGenerationRef.current) {
          if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
          autoSaveTimerRef.current = window.setTimeout(() => void saveLatest(), 2000);
        }
      };
      autoSaveTimerRef.current = window.setTimeout(() => void saveLatest(), 2000);
    },
    [isFrozen, isInputBlocked, triggerSave]
  );

  // 7. Quiesce Input & Generate Canonical Preview Flow (Fix 2)
  const handleStartReview = async () => {
    if (!sessionInfo || sessionInfo.state !== "Active") return;

    // Block typing immediately
    if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
    saveBlockedRef.current = true;
    setIsInputBlocked(true);
    setIsQuiescing(true);
    setSaveStatusText("Quiescing editor & saving latest content…");

    try {
      // Use the returned committed receipt, never React state from before this await.
      let receipt = lastCommittedReceipt;
      if (inFlightSavePromiseRef.current || dirtyGenerationRef.current > lastCommittedGenerationRef.current || !receipt) {
        receipt = await triggerSave(true);
      }
      if (!receipt || receipt.state !== "Committed" || dirtyGenerationRef.current > receipt.clientGeneration) {
        throw new Error("Cannot review until the latest note edits have a verified server commit.");
      }
      const committedSaveId = receipt.id;

      // 2. Generate immutable canonical preview (freezes lease on backend)
      setSaveStatusText("Converting DOCX to verified canonical text…");
      const prev = await notingOfficeApi.createPreview(
        sessionInfo.id,
        committedSaveId,
        sessionRevisionRef.current
      );

      setPreview(prev);
      setIsFrozen(true);
      setIsConfirmed(false);
      setShowReviewModal(true);
      setSessionInfo((prevInfo) => (prevInfo ? { ...prevInfo, state: "Frozen" } : null));
    } catch (err: any) {
      alert(err.message || "Failed to generate canonical preview");
      saveBlockedRef.current = false;
      setIsInputBlocked(false);
    } finally {
      setIsQuiescing(false);
    }
  };

  // 8. Confirm Canonical Preview
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
        setCitationConflictModal(true);
      } else {
        alert(err.message || "Failed to confirm canonical preview");
      }
    } finally {
      setIsConfirmingPreview(false);
    }
  };

  // 9. Official Submit Note
  const handleOfficialSubmit = async () => {
    if (!preview) return;
    setIsSubmitting(true);

    try {
      const sourceDak = selectedDakId || workspaceData?.sourceDakId || null;
      const res = await notingOfficeApi.submitOfficeNote(
        matterId,
        sourceDak,
        preview.id,
        preview.canonicalHash,
        workingRevision
      );

      setShowReviewModal(false);
      setSubmitSuccessNotice(`Official Note #${submittedNoteNumber(res)} submitted successfully.`);

      // Reset editor session and reload notes/PDF
      setSessionInfo(null);
      setIsFrozen(false);
      setIsInputBlocked(false);
      setPreview(null);
      setIsConfirmed(false);
      dirtyGenerationRef.current = 0;
      lastRequestedGenerationRef.current = 0;
      lastCommittedGenerationRef.current = 0;

      await loadNotesAndPdf(selectedDakId);
    } catch (err: any) {
      alert(err.message || "Failed to submit official note");
    } finally {
      setIsSubmitting(false);
    }
  };

  // 10. Remarks Handling (Fix 3 - Exact Text Selection)
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

  const handleNoteTextMouseUp = (e: React.MouseEvent<HTMLDivElement>, note: MatterOfficialNoteDto) => {
    const container = e.currentTarget;
    void extractAnchorFromDomSelection(container, note.canonicalText).then((res) => {
      if (res.success && res.anchor) {
        setSelectedAnchor(res.anchor);
        setSelectedNoteForAnchor(note);
        setSelectionNotice(null);
      } else {
        setSelectedAnchor(null);
        setSelectedNoteForAnchor(null);
      }
    });
  };

  const handleTriggerRemarkModal = () => {
    if (!selectedAnchor || !selectedNoteForAnchor) {
      setSelectionNotice("Please select text in an official note first.");
      return;
    }
    setActiveRemarkNote(selectedNoteForAnchor);
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

          {villageName && (
            <span className="noting-meta-crumb">Village: <strong>{villageName}</strong></span>
          )}

          {/* Controlling Dak Selector */}
          {linkedDaks.length > 1 ? (
            <div className="noting-dak-selector-wrap">
              <span className="noting-meta-crumb">Controlling Dak:</span>
              <select
                className="noting-dak-select"
                value={selectedDakId || ""}
                onChange={(e) => {
                  const newDakId = e.target.value || null;
                  setSelectedDakId(newDakId);
                  void loadNotesAndPdf(newDakId);
                }}
              >
                {linkedDaks.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.diaryNumber} {d.status ? `(${d.status})` : ""}
                  </option>
                ))}
              </select>
            </div>
          ) : controllingDakNumber ? (
            <span className="noting-meta-crumb">
              Controlling Dak: <strong style={{ color: "#0369a1" }}>{controllingDakNumber}</strong>
            </span>
          ) : null}

          <span className="noting-meta-crumb">
            Official Notes: <strong>{workspaceData?.notes.length || 0}</strong>
          </span>
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
        {/* LEFT PANE: Immutable Continuous Physical Green Sheet PDF OR Canonical Notes Surface (Fix 3) */}
        {(viewMode === "split" || viewMode === "focus-sheet") && (
          <div className="green-noting-left-pane">
            <div className="left-pane-header-tabs">
              <button
                type="button"
                className={`pane-tab-btn ${rightPaneView === "pdf" ? "active" : ""}`}
                onClick={() => setRightPaneView("pdf")}
              >
                📄 Continuous Green Sheet PDF
              </button>
              <button
                type="button"
                className={`pane-tab-btn ${rightPaneView === "canonical-notes" ? "active" : ""}`}
                onClick={() => setRightPaneView("canonical-notes")}
              >
                📝 Inspect Official Notes ({workspaceData?.notes.length || 0})
              </button>
            </div>

            {rightPaneView === "pdf" ? (
              <GreenSheetViewer
                matterId={matterId}
                sheetId={greenSheet?.id || null}
                metadata={greenSheet}
                onRefresh={() => void loadNotesAndPdf(selectedDakId)}
                isLoading={loadingSheet}
              />
            ) : (
              <div className="canonical-notes-viewer-surface">
                <div className="canonical-surface-toolbar">
                  <span style={{ fontSize: "12px", color: "#475569" }}>
                    Select any excerpt below to attach an exact supervisory remark.
                  </span>

                  <button
                    type="button"
                    className="noting-btn noting-btn-primary btn-sm"
                    onClick={handleTriggerRemarkModal}
                    disabled={!selectedAnchor || !workspaceData?.capabilities.canRemark}
                    title={selectedAnchor ? `Add remark on "${selectedAnchor.quote.substring(0, 30)}…"` : "Select text first"}
                  >
                    💬 Add Supervisory Remark on Selected Text
                  </button>
                </div>

                {selectionNotice && (
                  <div style={{ background: "#fef3c7", color: "#92400e", padding: "4px 8px", fontSize: "11.5px", borderRadius: "4px" }}>
                    ℹ {selectionNotice}
                  </div>
                )}

                {selectedAnchor && selectedNoteForAnchor && (
                  <div className="active-selection-bar">
                    <span className="selection-label">SELECTED EXCERPT (Note #{selectedNoteForAnchor.number}):</span>
                    <span className="selection-quote">"{selectedAnchor.quote}"</span>
                    <span className="selection-meta">
                      [{selectedAnchor.start}..{selectedAnchor.end}] • SHA: {selectedAnchor.quoteHash.substring(0, 8)}…
                    </span>
                  </div>
                )}

                <div className="canonical-notes-list">
                  {(!workspaceData?.notes || workspaceData.notes.length === 0) ? (
                    <div style={{ padding: "30px", textAlign: "center", color: "#64748b" }}>
                      No official notes have been submitted for this matter yet.
                    </div>
                  ) : (
                    workspaceData.notes.map((note) => (
                      <div key={note.id} className="canonical-note-entry-card" data-note-id={note.id}>
                        <div className="canonical-note-card-header">
                          <div className="note-card-identity">
                            <span className="note-card-badge">NOTE #{note.number}</span>
                            <strong className="note-card-author">{note.authorName}</strong>
                            <span className="note-card-desig">{note.designation}</span>
                          </div>
                          <div className="note-card-actions">
                            <span className="note-card-date">
                              {new Date(note.submittedAt).toLocaleString()}
                            </span>
                            <button
                              type="button"
                              className="noting-btn-text"
                              onClick={() => void handleOpenRemarks(note)}
                            >
                              💬 Remarks ({noteRemarksMap[note.id]?.length ?? 0})
                            </button>
                          </div>
                        </div>

                        <div
                          className="canonical-note-text-body"
                          onMouseUp={(e) => handleNoteTextMouseUp(e, note)}
                        >
                          <pre className="canonical-pre-wrap">{note.canonicalText}</pre>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>
            )}
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
                {sessionInfo && (
                  <span className="editor-lease-tag" title={`Expires at ${new Date(sessionInfo.expiresAt).toLocaleTimeString()}`}>
                    Lease: {sessionInfo.state}
                  </span>
                )}
              </div>
              <div className="editor-card-controls">
                <button
                  type="button"
                  className="btn-save-receipt"
                  onClick={() => void triggerSave(true)}
                  disabled={saveStatus === "saving" || !sessionInfo || sessionInfo.state !== "Active" || isFrozen}
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
                  <p>Loading noting workspace…</p>
                </div>
              ) : sessionInfo ? (
                <OnlyOfficeNoteFrame
                  sessionId={sessionInfo.id}
                  isFrozen={isFrozen || sessionInfo.state === "Frozen" || sessionInfo.state === "Submitted"}
                  isInputBlocked={isInputBlocked}
                  onDocumentStateChange={handleDocumentStateChange}
                  onError={(err) => setSessionError(err)}
                />
              ) : (
                <div className="noting-no-session-pane">
                  <div className="info-icon">ℹ</div>
                  <h4>No Active Editing Lease</h4>
                  <p>{sessionError || "Start or resume an authorized editing session for this matter."}</p>
                  {workspaceData?.capabilities.canEditDraft && (
                    <button
                      type="button"
                      className="noting-btn noting-btn-primary"
                      onClick={() => void handleStartOrResumeSession(null)}
                      disabled={isStartingSession}
                    >
                      {isStartingSession ? "Establishing Lease…" : "Start Note Session"}
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
                onClick={() => {
                  setShowReviewModal(false);
                  setIsInputBlocked(false);
                }}
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
                <div className="confirmed-notice-box">
                  <span>✓ Canonical text confirmed and preview frozen. Ready for official submission.</span>
                </div>
              )}
            </div>

            <div className="noting-modal-footer">
              <button
                type="button"
                className="noting-btn noting-btn-secondary"
                onClick={() => {
                  setShowReviewModal(false);
                  setIsInputBlocked(false);
                }}
              >
                Close Preview
              </button>

              {isConfirmed && (
                <>
                  <button
                    type="button"
                    className="noting-btn noting-btn-secondary"
                    onClick={() => {
                      setShowReviewModal(false);
                      setShowSendModal(true);
                    }}
                    disabled={!workspaceData?.capabilities.canSend}
                  >
                    Send & Forward Matter →
                  </button>
                  <button
                    type="button"
                    className="noting-btn noting-btn-primary"
                    onClick={handleOfficialSubmit}
                    disabled={isSubmitting || !workspaceData?.capabilities.canSubmit}
                  >
                    {isSubmitting ? "Submitting…" : "Submit Note Officially"}
                  </button>
                </>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Citation Conflict 409 Modal */}
      {citationConflictModal && (
        <div className="noting-modal-overlay">
          <div className="noting-modal-card" style={{ maxWidth: "520px" }}>
            <div className="noting-modal-header">
              <h3>Citation Anchor Conflict (409)</h3>
              <button type="button" className="noting-btn-close" onClick={() => setCitationConflictModal(false)}>✕</button>
            </div>
            <div className="noting-modal-body">
              <p style={{ fontSize: "13px", color: "#334155" }}>
                The canonical note text was modified after citations were attached. Submitting without reset would cause misaligned citations.
              </p>
              <div style={{ background: "#fef3c7", border: "1px solid #fde68a", padding: "10px", borderRadius: "4px", fontSize: "12px", color: "#92400e" }}>
                ⚠️ You must deliberately reset document citations to match the edited note content.
              </div>
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
                className="noting-btn noting-btn-primary"
                onClick={() => void handleConfirmPreview(true)}
              >
                Reset Citations & Confirm
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Send & Return Modal */}
      {showSendModal && (
        <SendReturnModal
          matterId={matterId}
          sourceDakId={selectedDakId || workspaceData?.sourceDakId || null}
          officePreviewId={preview?.id || undefined}
          canonicalHash={preview?.canonicalHash || undefined}
          workingRevision={workingRevision}
          onClose={() => setShowSendModal(false)}
          onSent={(res) => {
            setShowSendModal(false);
            setSubmitSuccessNotice(`Matter note #${res.noteNumber} submitted and Dak forwarded successfully.`);
            setSessionInfo(null);
            setIsFrozen(false);
            setIsInputBlocked(false);
            setPreview(null);
            setIsConfirmed(false);
            void loadNotesAndPdf(selectedDakId);
          }}
        />
      )}

      {/* Remarks View Modal */}
      {showViewRemarksModal && activeRemarkNote && (
        <ViewRemarksModal
          matterId={matterId}
          noteId={activeRemarkNote.id}
          noteNumber={activeRemarkNote.number}
          remarks={noteRemarksMap[activeRemarkNote.id] || []}
          canManage={Boolean(workspaceData?.capabilities.canRemark)}
          onClose={() => setShowViewRemarksModal(false)}
          onUpdated={(updated: MatterNoteRemarkDto) => {
            setNoteRemarksMap((prev) => ({
              ...prev,
              [activeRemarkNote.id]: (prev[activeRemarkNote.id] || []).map((r) =>
                r.id === updated.id ? updated : r
              )
            }));
          }}
        />
      )}

      {/* Add Supervisory Remark Modal with exact anchor (Fix 3) */}
      {showAddRemarkModal && activeRemarkNote && selectedAnchor && (
        <AddRemarkModal
          matterId={matterId}
          noteId={activeRemarkNote.id}
          noteNumber={activeRemarkNote.number}
          anchor={selectedAnchor as unknown as NoteAnchor}
          onClose={() => setShowAddRemarkModal(false)}
          onCreated={(newRemark) => {
            setNoteRemarksMap((prev) => ({
              ...prev,
              [activeRemarkNote.id]: [...(prev[activeRemarkNote.id] || []), newRemark]
            }));
            setSelectedAnchor(null);
            setSelectedNoteForAnchor(null);
          }}
        />
      )}
    </div>
  );
};
