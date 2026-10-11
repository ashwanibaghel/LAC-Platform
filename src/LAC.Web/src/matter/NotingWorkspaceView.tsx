import React, { useState, useEffect, useRef, useCallback } from "react";
import type {
  MatterNotingWorkspaceData,
  MatterOfficialNoteDto,
  MatterNoteRemarkDto,
  MatterWorkingDraftDto
} from "./notingTypes";
import {
  canonicalizeRichTextDoc,
  createNoteAnchor,
  sanitizeTipTapDoc,
  parseDocFromInput,
  mergeTipTapDocs
} from "./notingTextUtil";
import { AddRemarkModal, ViewRemarksModal } from "./RemarkModal";
import { CiteDocumentModal } from "./CiteDocumentModal";
import { SendReturnModal } from "./SendReturnModal";
import { DocumentRailViewer } from "./DocumentRailViewer";
import { NotingRichEditor, type NotingRichEditorHandle } from "./NotingRichEditor";
import { useAuth } from "../auth/AuthProvider";
import {
  saveSecureLocalDraft,
  loadSecureLocalDraft,
  clearSecureLocalDraft,
  purgeLegacyLocalDrafts,
  type SecureRecoverableDraft
} from "./secureDraftStorage";
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
  let currentUserId = "officer";
  try {
    const auth = useAuth();
    if (auth.user?.id) currentUserId = auth.user.id;
    else if (auth.user?.username) currentUserId = auth.user.username;
  } catch {}

  // Layout mode
  const [focusMode, setFocusMode] = useState<boolean>(false);
  const [focusDrawerOpen, setFocusDrawerOpen] = useState<boolean>(false);

  // Controlling Dak selection:
  // When exactly 1 Dak is linked, default to it.
  // When multiple Daks are linked, REQUIRE explicit selection (use initialSourceDakId if provided, else null).
  const [sourceDakId, setSourceDakId] = useState<string | null>(
    linkedDaks.length === 1 ? linkedDaks[0].dakId : (initialSourceDakId || null)
  );

  // Main Workspace Data
  const [workspaceData, setWorkspaceData] = useState<MatterNotingWorkspaceData | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Active Draft State
  const [draftContentJson, setDraftContentJson] = useState<string>("");
  const [draftCanonicalText, setDraftCanonicalText] = useState<string>("");
  const [draftRevision, setDraftRevision] = useState<number>(0);
  const [draftLoaded, setDraftLoaded] = useState<boolean>(false);
  const [autosaveStatus, setAutosaveStatus] = useState<"saved" | "saving" | "offline" | "error">("saved");
  const [lastSavedAt, setLastSavedAt] = useState<Date | null>(null);
  const [recoverableDraft, setRecoverableDraft] = useState<{
    draft: SecureRecoverableDraft;
    serverRevision: number;
    isNewerOnServer: boolean;
  } | null>(null);

  // Rich editor handle and selection
  const editorRef = useRef<NotingRichEditorHandle>(null);
  const [draftSelection, setDraftSelection] = useState<{
    text: string;
    start: number;
    end: number;
  } | null>(null);
  const draftSelectionRef = useRef<{
    text: string;
    start: number;
    end: number;
  } | null>(null);
  draftSelectionRef.current = draftSelection;

  // References for autosave, debouncing and flushing
  const autosaveTimerRef = useRef<number | null>(null);
  const latestJsonRef = useRef<string>("");
  const latestTextRef = useRef<string>("");
  const savedJsonRef = useRef<string>("");
  const draftRevisionRef = useRef<number>(0);
  const inFlightSavePromiseRef = useRef<Promise<boolean> | null>(null);
  const isSavingRef = useRef<boolean>(false);

  latestJsonRef.current = draftContentJson;
  latestTextRef.current = draftCanonicalText;
  draftRevisionRef.current = draftRevision;

  // Selected document in rail/viewer
  const [activeDocId, setActiveDocId] = useState<string | null>(null);
  const [activePage, setActivePage] = useState<number | null>(null);

  // Remarks State
  const [remarksMap, setRemarksMap] = useState<Record<string, MatterNoteRemarkDto[]>>({});
  const [addRemarkState, setAddRemarkState] = useState<{
    note: MatterOfficialNoteDto;
    anchor: any;
  } | null>(null);
  const [viewRemarksState, setViewRemarksState] = useState<{
    note: MatterOfficialNoteDto;
    remarks: MatterNoteRemarkDto[];
  } | null>(null);

  // Popover state for inline clicked/hovered remark
  const [activePopoverRemark, setActivePopoverRemark] = useState<MatterNoteRemarkDto | null>(null);
  const [popoverCoords, setPopoverCoords] = useState<{ x: number; y: number }>({ x: 0, y: 0 });

  // Floating text selection state for remark creation
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
  const [confirmedSendRevision, setConfirmedSendRevision] = useState<number>(0);
  const [submittingAction, setSubmittingAction] = useState<boolean>(false);

  // 409 Draft revision conflict preservation state
  const [draftConflict, setDraftConflict] = useState<{
    localJson: string;
    localContent: string;
    serverJson?: string;
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
    purgeLegacyLocalDrafts();
    return () => {
      purgeLegacyLocalDrafts();
    };
  }, []);

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

      let serverCanonical = "";
      if (draft.contentJson) {
        try {
          const parsed = JSON.parse(draft.contentJson);
          serverCanonical = canonicalizeRichTextDoc(parsed).trim();
          setDraftContentJson(draft.contentJson);
          setDraftCanonicalText(serverCanonical);
          savedJsonRef.current = draft.contentJson;
        } catch {
          setDraftContentJson("");
          setDraftCanonicalText(draft.canonicalText || "");
          serverCanonical = (draft.canonicalText || "").trim();
          savedJsonRef.current = "";
        }
      } else {
        setDraftContentJson("");
        setDraftCanonicalText("");
        savedJsonRef.current = "";
      }

      // Check for user-scoped secure local draft from offline/crash session
      const localBackup = await loadSecureLocalDraft(currentUserId, matterId);
      if (localBackup && localBackup.canonicalText.trim()) {
        const localCanonical = localBackup.canonicalText.trim();
        if (localCanonical !== serverCanonical) {
          // Un-synced offline edits detected!
          if (rev > localBackup.baseRevision) {
            // Newer server revision exists! Do NOT overwrite!
            setRecoverableDraft({
              draft: localBackup,
              serverRevision: rev,
              isNewerOnServer: true
            });
          } else {
            // Server revision is equal or older; prompt to restore
            setRecoverableDraft({
              draft: localBackup,
              serverRevision: rev,
              isNewerOnServer: false
            });
          }
        } else {
          clearSecureLocalDraft(currentUserId, matterId);
          setRecoverableDraft(null);
        }
      } else {
        setRecoverableDraft(null);
      }

      setDraftLoaded(true);
      setAutosaveStatus("saved");
      setLastSavedAt(new Date());
    } catch {
      setDraftLoaded(true);
    }
  }, [matterId, currentUserId]);

  useEffect(() => {
    loadDraft();
  }, [loadDraft]);

  // 3. Durable Save Execution
  const performSave = useCallback(
    async (jsonToSave: string, textToSave: string, force = false): Promise<boolean> => {
      if (!draftLoaded && !force) return false;
      if (isSavingRef.current) {
        return inFlightSavePromiseRef.current || Promise.resolve(false);
      }

      isSavingRef.current = true;
      setAutosaveStatus("saving");

      const savePromise = (async () => {
        try {
          let cleanJson = jsonToSave;
          try {
            const parsed = JSON.parse(jsonToSave);
            cleanJson = JSON.stringify(sanitizeTipTapDoc(parsed) || { type: "doc", content: [{ type: "paragraph" }] });
          } catch {
            const paragraphs = textToSave.split("\n").filter((l) => l.trim().length > 0);
            cleanJson = JSON.stringify({
              type: "doc",
              content: paragraphs.length > 0
                ? paragraphs.map((t) => ({ type: "paragraph", content: [{ type: "text", text: t }] }))
                : [{ type: "paragraph", content: [{ type: "text", text: textToSave }] }]
            });
          }

          const res = await fetch(`/api/matters/${matterId}/noting/draft`, {
            method: "PUT",
            headers: {
              "Content-Type": "application/json",
              "If-Match": `"${draftRevisionRef.current}"`,
              "Idempotency-Key": getUuid()
            },
            body: JSON.stringify({
              contentJson: cleanJson,
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
              let serverJson = "";
              if (serverRes.ok) {
                const etag = serverRes.headers.get("ETag");
                const sDraft: MatterWorkingDraftDto = await serverRes.json();
                serverRev = etag ? parseInt(etag.replace(/"/g, ""), 10) : (sDraft.revision || serverRev);
                serverJson = sDraft.contentJson || "";
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
                localJson: cleanJson,
                localContent: textToSave,
                serverJson,
                serverContent: serverText,
                serverRevision: serverRev,
                message: conflictBody.message || "Draft changed on server; recover saved text before retrying."
              });
            } catch {
              setDraftConflict({
                localJson: cleanJson,
                localContent: textToSave,
                serverJson: "",
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
          savedJsonRef.current = cleanJson;
          clearSecureLocalDraft(currentUserId, matterId);
          setRecoverableDraft(null);
          setAutosaveStatus("saved");
          setLastSavedAt(new Date());
          return true;
        } catch {
          setAutosaveStatus("offline");
          return false;
        } finally {
          isSavingRef.current = false;
          inFlightSavePromiseRef.current = null;
        }
      })();

      inFlightSavePromiseRef.current = savePromise;
      return savePromise;
    },
    [draftLoaded, matterId, sourceDakId, currentUserId]
  );

  // Debounced input change from Rich Editor
  const handleEditorChange = useCallback(
    (contentJson: string, canonicalText: string) => {
      setDraftContentJson(contentJson);
      setDraftCanonicalText(canonicalText);
      latestJsonRef.current = contentJson;
      latestTextRef.current = canonicalText;

      // Immediately write user-scoped AES-GCM encrypted backup for power-loss recovery
      void saveSecureLocalDraft(currentUserId, matterId, {
        contentJson,
        canonicalText,
        baseRevision: draftRevisionRef.current,
        savedAt: Date.now()
      });

      // Avoid scheduling redundant autosaves if content is already saved on server
      if (contentJson === savedJsonRef.current) {
        setAutosaveStatus("saved");
        clearSecureLocalDraft(currentUserId, matterId);
        return;
      }

      if (typeof navigator !== "undefined" && !navigator.onLine) {
        setAutosaveStatus("offline");
      } else {
        setAutosaveStatus("saving");
      }

      if (autosaveTimerRef.current) {
        window.clearTimeout(autosaveTimerRef.current);
      }
      autosaveTimerRef.current = window.setTimeout(() => {
        if (latestJsonRef.current !== savedJsonRef.current) {
          performSave(latestJsonRef.current, latestTextRef.current);
        } else {
          setAutosaveStatus("saved");
          clearSecureLocalDraft(currentUserId, matterId);
        }
      }, 1500);
    },
    [performSave, currentUserId, matterId]
  );

  // 4. Flush and Verify Draft: Guarantees in-flight and pending autosaves complete before Submit or Send
  const flushAndVerifyDraft = useCallback(async (): Promise<{ success: boolean; revision: number }> => {
    // 1. Cancel pending debounce timer
    if (autosaveTimerRef.current) {
      window.clearTimeout(autosaveTimerRef.current);
      autosaveTimerRef.current = null;
    }

    // 2. Await active in-flight save if any
    if (inFlightSavePromiseRef.current) {
      const inFlightOk = await inFlightSavePromiseRef.current;
      if (!inFlightOk) {
        return { success: false, revision: draftRevisionRef.current };
      }
    }

    // 3. If local content differs from last confirmed saved content, perform save now
    if (latestJsonRef.current !== savedJsonRef.current || latestTextRef.current.trim() && savedJsonRef.current === "") {
      const saveOk = await performSave(latestJsonRef.current, latestTextRef.current, true);
      if (!saveOk) {
        return { success: false, revision: draftRevisionRef.current };
      }
    }

    return { success: true, revision: draftRevisionRef.current };
  }, [performSave]);

  // 5. Submit Working Note (Standalone Local Submission into immutable official chain)
  const handleSubmitNote = async () => {
    if (!draftCanonicalText.trim()) {
      alert("Please enter note text before submitting.");
      return;
    }

    setSubmittingAction(true);
    try {
      const verified = await flushAndVerifyDraft();
      if (!verified.success) {
        alert("Draft could not be verified by the server. Your edits have been preserved in the editor. Please resolve any revision conflicts before submitting.");
        return;
      }

      const res = await fetch(`/api/matters/${matterId}/noting/submit`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "If-Match": `"${verified.revision}"`,
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

      clearSecureLocalDraft(currentUserId, matterId);
      setRecoverableDraft(null);
      setDraftContentJson("");
      setDraftCanonicalText("");
      latestJsonRef.current = "";
      latestTextRef.current = "";
      savedJsonRef.current = "";
      setDraftConflict(null);
      await loadDraft();
      await loadWorkspace();
    } catch (err: any) {
      alert(err.message || "Failed to submit note");
    } finally {
      setSubmittingAction(false);
    }
  };

  // 6. Initiate Send / Return: Flushes and verifies draft first, then opens Send modal
  const handleInitiateSend = async () => {
    if (!workspaceData?.capabilities.canSend) return;

    if (!draftCanonicalText.trim()) {
      alert("Please write your note before sending the file. Sending atomically finalizes and records your note with the file movement.");
      return;
    }

    setSubmittingAction(true);
    try {
      const verified = await flushAndVerifyDraft();
      if (!verified.success) {
        alert("Draft could not be verified by the server. Your edits have been preserved in the editor. Please resolve any revision conflicts before sending.");
        return;
      }

      setConfirmedSendRevision(verified.revision);
      setShowSendModal(true);
    } finally {
      setSubmittingAction(false);
    }
  };

  // 7. Text Selection on Submitted Notes -> Add Remark (Accurate offset mapping even with repeated text)
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

    // Find the enclosing paragraph element with data-start-offset to compute exact index
    const range = sel.getRangeAt(0);
    let pNode: HTMLElement | null = range.startContainer as HTMLElement;
    while (pNode && pNode.getAttribute && !pNode.getAttribute("data-start-offset")) {
      pNode = pNode.parentElement;
    }

    let startIdx = -1;
    if (pNode && pNode.getAttribute("data-start-offset")) {
      const pStart = parseInt(pNode.getAttribute("data-start-offset")!, 10);
      let charOffset = 0;
      const walk = (node: Node): boolean => {
        if (node === range.startContainer) {
          charOffset += range.startOffset;
          return true;
        }
        if (node.nodeType === Node.TEXT_NODE) {
          charOffset += (node.textContent || "").length;
        }
        for (let i = 0; i < node.childNodes.length; i++) {
          if (walk(node.childNodes[i])) return true;
        }
        return false;
      };
      walk(pNode);
      const computedStart = pStart + charOffset;
      if (note.canonicalText.substring(computedStart, computedStart + selectedText.length) === selectedText) {
        startIdx = computedStart;
      }
    }

    if (startIdx === -1 && pNode && pNode.getAttribute("data-start-offset")) {
      const pStart = parseInt(pNode.getAttribute("data-start-offset")!, 10);
      const relativeFound = note.canonicalText.indexOf(selectedText, pStart);
      if (relativeFound >= 0) {
        startIdx = relativeFound;
      }
    }

    if (startIdx === -1) {
      startIdx = note.canonicalText.indexOf(selectedText);
    }

    if (startIdx >= 0) {
      const endIdx = startIdx + selectedText.length;
      createNoteAnchor(note.canonicalText, startIdx, endIdx).then((anchor) => {
        const rect = range.getBoundingClientRect();
        setSelectedRange({
          note,
          anchor,
          coords: { x: rect.left + rect.width / 2, y: rect.top - 36 }
        });
      });
    }
  };

  // 8. Citation click in notes -> Navigate right rail/drawer
  const handleCitationClick = (docId: string, page?: number | null) => {
    setActiveDocId(docId);
    if (page) setActivePage(page);
    if (focusMode) {
      setFocusDrawerOpen(true);
    }
  };

  // 9. Insert citation on active draft text selection (using TipTap ProseMirror selection)
  const handleCiteDraftSelection = async () => {
    const verified = await flushAndVerifyDraft();
    if (!verified.success) {
      alert("Draft could not be verified by the server. Please resolve any revision conflicts before citing.");
      return;
    }
    setDraftRevision(verified.revision);
    draftRevisionRef.current = verified.revision;

    let sel = draftSelectionRef.current || draftSelection;
    if (!sel || !sel.text) {
      const winSel = window.getSelection();
      const selText = winSel ? winSel.toString().trim() : "";
      if (selText && draftCanonicalText) {
        const start = draftCanonicalText.indexOf(selText);
        if (start >= 0) {
          sel = { text: selText, start, end: start + selText.length };
        }
      }
    }

    if (!sel || !sel.text) {
      alert("Please highlight the exact text in your draft to link with a citation.");
      return;
    }

    const anchor = await createNoteAnchor(draftCanonicalText, sel.start, sel.end);
    setCitationAnchor(anchor);
    setShowCitationModal(true);
  };

  // 10. Toggle Remark status (PUT /api/matters/{m}/notes/{n}/remarks/{r})
  const handleToggleRemarkState = async (remark: MatterNoteRemarkDto) => {
    const nextState = remark.state === "Open" ? "Addressed" : "Open";
    try {
      const res = await fetch(`/api/matters/${matterId}/notes/${remark.noteId}/remarks/${remark.id}`, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": getUuid()
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
      setRemarksMap((prev) => {
        const list = prev[remark.noteId] || [];
        return {
          ...prev,
          [remark.noteId]: list.map((r) => (r.id === updated.id ? updated : r))
        };
      });
      setActivePopoverRemark(updated);
    } catch (err: any) {
      alert(err.message || "Remark update error");
    }
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
                <option value="">-- Explicitly Select Dak --</option>
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
                <span className="noting-server-saved-tag" style={{ opacity: 0.85, fontSize: "11px", marginLeft: "4px" }}>
                  (Server saved)
                </span>
                {lastSavedAt && (
                  <span style={{ opacity: 0.75, fontSize: "11px" }}>
                    ({lastSavedAt.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })})
                  </span>
                )}
              </>
            )}
            {autosaveStatus === "saving" && <span>⏳ Pending save…</span>}
            {autosaveStatus === "offline" && <span>📡 Offline (Changes pending server reconnection)</span>}
            {autosaveStatus === "error" && (
              <span onClick={() => performSave(latestJsonRef.current, latestTextRef.current, true)}>
                {draftConflict ? "⚠ Conflict (409) — Unsaved edits preserved" : "✕ Save failed — Click to retry"}
              </span>
            )}
          </div>
          {recoverableDraft && (
            <div
              className="noting-recoverable-badge"
              style={{
                background: "#fef3c7",
                color: "#92400e",
                border: "1px solid #fde68a",
                borderRadius: "12px",
                padding: "2px 8px",
                fontSize: "11px",
                fontWeight: 600,
                display: "inline-flex",
                alignItems: "center",
                gap: "4px"
              }}
              title="Secure recoverable draft available"
            >
              <span>↺ Recoverable Draft</span>
            </div>
          )}
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
            onClick={() => performSave(latestJsonRef.current, latestTextRef.current, true)}
            disabled={!capabilities.canEditDraft || submittingAction}
          >
            Save Draft
          </button>

          {/* Submit Note (Internal Standalone Submission) */}
          <button
            type="button"
            className="noting-btn noting-btn-primary"
            onClick={handleSubmitNote}
            disabled={!capabilities.canSubmit || !draftCanonicalText.trim() || submittingAction}
            title="Submit Note as next official numbered record"
          >
            {submittingAction ? "Submitting..." : `Submit Note ${notes.length + 1}`}
          </button>

          {/* Send / Return File Movement */}
          <button
            type="button"
            className="noting-btn noting-btn-send"
            onClick={handleInitiateSend}
            disabled={!capabilities.canSend}
            title={
              capabilities.nativeRoutingPolicyPending
                ? "Inter-officer file movement requires an active controlling Dak"
                : "Atomically submit Note and move controlling Dak to next officer"
            }
          >
            Send / Return File ➔
          </button>
        </div>
      </div>

      {/* Multiple Linked Daks Explicit Selection Banner */}
      {linkedDaks.length > 1 && !sourceDakId && (
        <div
          className="noting-controlling-dak-prompt"
          style={{
            background: "#fff7ed",
            border: "1px solid #fed7aa",
            padding: "8px 14px",
            margin: "0 16px 8px 16px",
            borderRadius: "6px",
            color: "#9a3412",
            fontSize: "12.5px",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between"
          }}
        >
          <div>
            <strong>⚠ Multiple Linked Daks:</strong> Please select which controlling Dak you are acting under for this noting session.
          </div>
          <select
            id="noting-select-controlling-dak"
            value=""
            onChange={(e) => setSourceDakId(e.target.value || null)}
            style={{
              padding: "4px 8px",
              borderRadius: "4px",
              border: "1px solid #fdba74",
              fontWeight: 600,
              background: "#ffffff",
              color: "#9a3412",
              cursor: "pointer"
            }}
          >
            <option value="">-- Select Controlling Dak --</option>
            {linkedDaks.map((d) => (
              <option key={d.dakId} value={d.dakId}>
                {d.diaryNumber} — {d.subject}
              </option>
            ))}
          </select>
        </div>
      )}

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

                    {/* Note Body Text with precise inline highlighted remarks and anchored citations */}
                    <div className="official-note-body">
                      {(() => {
                        const canonical = (note.canonicalText || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
                        const paragraphs = canonical.split("\n");
                        let currentOffset = 0;

                        // Parse remarks with anchors
                        const parsedRemarks = noteRemarks
                          .map((r) => {
                            try {
                              const anchor = typeof r.anchorJson === "string" ? JSON.parse(r.anchorJson) : (r as any).anchor;
                              return { ...r, parsedAnchor: anchor };
                            } catch {
                              return null;
                            }
                          })
                          .filter((r): r is MatterNoteRemarkDto & { parsedAnchor: any } =>
                            Boolean(r && r.parsedAnchor && typeof r.parsedAnchor.start === "number" && typeof r.parsedAnchor.end === "number")
                          );

                        const getCiteKey = (c: any, idx: number) =>
                          `${c.documentId}-${c.anchor?.start ?? 0}-${c.anchor?.end ?? 0}-${idx}`;
                        const renderedCitationKeys = new Set<string>();

                        const renderedParagraphs = paragraphs.map((para, pIdx) => {
                          const pStart = currentOffset;
                          const pEnd = pStart + para.length;
                          currentOffset = pEnd + 1; // newline delimiter

                          if (!para.trim()) {
                            return <p key={pIdx} data-start-offset={pStart}>&nbsp;</p>;
                          }

                          // Intersecting remarks
                          const intersectingRemarks = parsedRemarks.filter(
                            (r) => r.parsedAnchor.start < pEnd && r.parsedAnchor.end > pStart
                          );

                          // Citations ending inside this paragraph
                          const paraCitations = (note.citations || []).filter((c) => {
                            const cEnd = c.anchor?.end;
                            return typeof cEnd === "number" && cEnd >= pStart && cEnd <= pEnd;
                          });

                          paraCitations.forEach((c, cIdx) => renderedCitationKeys.add(getCiteKey(c, cIdx)));

                          // If no remarks and no citations, render direct paragraph
                          if (intersectingRemarks.length === 0 && paraCitations.length === 0) {
                            return (
                              <p key={pIdx} data-start-offset={pStart}>
                                {para}
                              </p>
                            );
                          }

                          // Collect interval boundaries
                          const L = para.length;
                          const boundarySet = new Set<number>();
                          boundarySet.add(0);
                          boundarySet.add(L);

                          for (const r of intersectingRemarks) {
                            const relStart = Math.max(0, Math.min(L, r.parsedAnchor.start - pStart));
                            const relEnd = Math.max(0, Math.min(L, r.parsedAnchor.end - pStart));
                            boundarySet.add(relStart);
                            boundarySet.add(relEnd);
                          }

                          for (const c of paraCitations) {
                            const relEnd = Math.max(0, Math.min(L, (c.anchor?.end ?? 0) - pStart));
                            boundarySet.add(relEnd);
                          }

                          const boundaries = Array.from(boundarySet).sort((a, b) => a - b);
                          const elements: React.ReactNode[] = [];

                          // Check citations at 0
                          const zeroCites = paraCitations.filter((c) => Math.max(0, Math.min(L, (c.anchor?.end ?? 0) - pStart)) === 0);
                          zeroCites.forEach((c, cIdx) => {
                            const doc = documents.find((d) => d.id === c.documentId);
                            elements.push(
                              <span
                                key={`zero-cite-${getCiteKey(c, cIdx)}`}
                                className="note-citation-pill"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  handleCitationClick(c.documentId, c.page);
                                }}
                                title={`Click to view ${doc?.filename || "document"} at page ${c.page || 1}`}
                              >
                                📎 [Doc: {doc?.filename || "Document"}{c.page ? ` (p.${c.page})` : ""}]
                              </span>
                            );
                          });

                          for (let i = 0; i < boundaries.length - 1; i++) {
                            const segStart = boundaries[i];
                            const segEnd = boundaries[i + 1];
                            if (segEnd <= segStart) continue;

                            const segText = para.substring(segStart, segEnd);
                            const canonSegStart = pStart + segStart;
                            const canonSegEnd = pStart + segEnd;

                            // Find remarks covering this slice
                            const coveringRemarks = intersectingRemarks.filter(
                              (r) => r.parsedAnchor.start <= canonSegStart && r.parsedAnchor.end >= canonSegEnd
                            );

                            if (coveringRemarks.length > 0) {
                              const hasOpen = coveringRemarks.some((r) => r.state === "Open");
                              const isOverlapping = coveringRemarks.length > 1;
                              const primaryRemark = coveringRemarks[0];

                              elements.push(
                                <mark
                                  key={`seg-${pIdx}-${segStart}-${segEnd}`}
                                  className={`noting-remark-highlight ${hasOpen ? "remark-open" : "remark-addressed"} ${isOverlapping ? "remark-overlapping" : ""}`}
                                  data-remark-id={primaryRemark.id}
                                  data-remark-ids={coveringRemarks.map((r) => r.id).join(",")}
                                  title={
                                    isOverlapping
                                      ? `${coveringRemarks.length} Remarks (${coveringRemarks.map((r) => `${r.authorName}: "${r.text}"`).join("; ")})`
                                      : `${primaryRemark.authorName}: "${primaryRemark.text}"`
                                  }
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    if (isOverlapping) {
                                      setViewRemarksState({ note, remarks: coveringRemarks });
                                    } else {
                                      setActivePopoverRemark(primaryRemark);
                                      setPopoverCoords({ x: e.clientX, y: e.clientY });
                                    }
                                  }}
                                  onMouseEnter={(e) => {
                                    if (!isOverlapping) {
                                      setActivePopoverRemark(primaryRemark);
                                      setPopoverCoords({ x: e.clientX, y: e.clientY });
                                    }
                                  }}
                                >
                                  {segText}
                                </mark>
                              );
                            } else {
                              elements.push(
                                <React.Fragment key={`text-${pIdx}-${segStart}-${segEnd}`}>
                                  {segText}
                                </React.Fragment>
                              );
                            }

                            // Citations ending right at this segment end
                            const citesEndingHere = paraCitations.filter(
                              (c) => Math.max(0, Math.min(L, (c.anchor?.end ?? 0) - pStart)) === segEnd
                            );
                            citesEndingHere.forEach((c, cIdx) => {
                              const doc = documents.find((d) => d.id === c.documentId);
                              elements.push(
                                <span
                                  key={`cite-${getCiteKey(c, cIdx)}`}
                                  className="note-citation-pill"
                                  onClick={(e) => {
                                    e.stopPropagation();
                                    handleCitationClick(c.documentId, c.page);
                                  }}
                                  title={`Click to view ${doc?.filename || "document"} at page ${c.page || 1}`}
                                >
                                  📎 [Doc: {doc?.filename || "Document"}{c.page ? ` (p.${c.page})` : ""}]
                                </span>
                              );
                            });
                          }

                          return (
                            <p key={pIdx} data-start-offset={pStart}>
                              {elements}
                            </p>
                          );
                        });

                        // Any unanchored citations rendered at the bottom
                        const unanchoredCitations = (note.citations || []).filter(
                          (c, idx) => !renderedCitationKeys.has(getCiteKey(c, idx))
                        );

                        return (
                          <>
                            {renderedParagraphs}
                            {unanchoredCitations.length > 0 && (
                              <div className="official-note-citations-bar" style={{ marginTop: "8px", paddingTop: "6px", borderTop: "1px dashed #cbd5e1" }}>
                                <span style={{ fontSize: "11px", color: "#64748b", fontWeight: 600, marginRight: "6px" }}>
                                  Linked Documents:
                                </span>
                                {unanchoredCitations.map((c, cIdx) => {
                                  const doc = documents.find((d) => d.id === c.documentId);
                                  return (
                                    <span
                                      key={`unanchored-${getCiteKey(c, cIdx)}`}
                                      className="note-citation-pill"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        handleCitationClick(c.documentId, c.page);
                                      }}
                                    >
                                      📎 [Doc: {doc?.filename || "Document"}{c.page ? ` (p.${c.page})` : ""}]
                                    </span>
                                  );
                                })}
                              </div>
                            )}
                          </>
                        );
                      })()}
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

                {/* Recoverable Offline Draft Banner */}
                {recoverableDraft && !draftConflict && (
                  <div
                    className={`noting-recoverable-draft-banner ${recoverableDraft.isNewerOnServer ? "conflict-warning" : ""}`}
                    style={{
                      background: recoverableDraft.isNewerOnServer ? "#fff7ed" : "#f0fdf4",
                      border: `1px solid ${recoverableDraft.isNewerOnServer ? "#fdba74" : "#bbf7d0"}`,
                      borderRadius: "6px",
                      padding: "10px 12px",
                      margin: "8px 12px 10px 12px",
                      display: "flex",
                      flexDirection: "column",
                      gap: "8px"
                    }}
                  >
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <strong style={{ color: recoverableDraft.isNewerOnServer ? "#9a3412" : "#166534", fontSize: "12.5px" }}>
                        {recoverableDraft.isNewerOnServer
                          ? `⚠ Newer Server Draft Available (Rev #${recoverableDraft.serverRevision})`
                          : "↺ Recoverable Offline Draft Detected"}
                      </strong>
                      <span style={{ fontSize: "11px", color: recoverableDraft.isNewerOnServer ? "#c2410c" : "#15803d", fontWeight: 600 }}>
                        Saved {new Date(recoverableDraft.draft.savedAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                      </span>
                    </div>

                    <p style={{ margin: "0", fontSize: "12px", color: recoverableDraft.isNewerOnServer ? "#7c2d12" : "#14532d", lineHeight: "1.4" }}>
                      {recoverableDraft.isNewerOnServer
                        ? `A newer draft revision exists on the server (Rev #${recoverableDraft.serverRevision}). An un-synced local backup (based on Rev #${recoverableDraft.draft.baseRevision}) was found from your previous offline/interrupted session. To prevent overwriting official records, review your local edits.`
                        : "Unsaved edits were securely recovered from your previous offline or interrupted session. You can restore them to your editor or discard them."}
                    </p>

                    <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", alignItems: "center" }}>
                      {recoverableDraft.isNewerOnServer ? (
                        <>
                          <button
                            type="button"
                            className="noting-btn noting-btn-primary"
                            style={{ fontSize: "11.5px", padding: "3px 8px" }}
                            onClick={async () => {
                              const merged = mergeTipTapDocs(draftContentJson, recoverableDraft.draft.contentJson);
                              const mergedJson = JSON.stringify(merged);
                              const mergedText = canonicalizeRichTextDoc(merged).trim();
                              editorRef.current?.setContent(merged);
                              setDraftContentJson(mergedJson);
                              setDraftCanonicalText(mergedText);
                              latestJsonRef.current = mergedJson;
                              latestTextRef.current = mergedText;
                              setRecoverableDraft(null);
                              clearSecureLocalDraft(currentUserId, matterId);
                              await performSave(mergedJson, mergedText, true);
                            }}
                          >
                            Review & Merge Both
                          </button>
                          <button
                            type="button"
                            className="noting-btn noting-btn-secondary"
                            style={{ fontSize: "11.5px", padding: "3px 8px" }}
                            onClick={() => {
                              setRecoverableDraft(null);
                              clearSecureLocalDraft(currentUserId, matterId);
                            }}
                          >
                            Discard Local Backup
                          </button>
                        </>
                      ) : (
                        <>
                          <button
                            type="button"
                            className="noting-btn noting-btn-primary btn-restore-draft"
                            style={{ fontSize: "11.5px", padding: "3px 8px" }}
                            onClick={async () => {
                              const doc = parseDocFromInput(recoverableDraft.draft.contentJson);
                              editorRef.current?.setContent(doc);
                              setDraftContentJson(recoverableDraft.draft.contentJson);
                              setDraftCanonicalText(recoverableDraft.draft.canonicalText);
                              latestJsonRef.current = recoverableDraft.draft.contentJson;
                              latestTextRef.current = recoverableDraft.draft.canonicalText;
                              setRecoverableDraft(null);
                              clearSecureLocalDraft(currentUserId, matterId);
                              await performSave(recoverableDraft.draft.contentJson, recoverableDraft.draft.canonicalText, true);
                            }}
                          >
                            Restore Recovered Draft
                          </button>
                          <button
                            type="button"
                            className="noting-btn noting-btn-secondary btn-discard-draft"
                            style={{ fontSize: "11.5px", padding: "3px 8px" }}
                            onClick={() => {
                              setRecoverableDraft(null);
                              clearSecureLocalDraft(currentUserId, matterId);
                            }}
                          >
                            Discard
                          </button>
                        </>
                      )}
                    </div>
                  </div>
                )}

                {/* 409 Conflict Resolution Banner */}
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
                          const localJ = draftConflict.localJson;
                          setDraftConflict(null);
                          await performSave(localJ, local, true);
                        }}
                      >
                        Keep My Edits & Overwrite Server
                      </button>
                      <button
                        type="button"
                        className="noting-btn noting-btn-secondary"
                        style={{ fontSize: "11.5px", padding: "3px 8px" }}
                        onClick={async () => {
                          const mergedDoc = mergeTipTapDocs(
                            draftConflict.serverJson || draftConflict.serverContent,
                            draftConflict.localJson || draftConflict.localContent
                          );
                          const mergedJson = JSON.stringify(mergedDoc);
                          const mergedText = canonicalizeRichTextDoc(mergedDoc).trim();

                          draftRevisionRef.current = draftConflict.serverRevision;
                          setDraftRevision(draftConflict.serverRevision);
                          setDraftContentJson(mergedJson);
                          setDraftCanonicalText(mergedText);
                          latestJsonRef.current = mergedJson;
                          latestTextRef.current = mergedText;

                          editorRef.current?.setContent(mergedDoc);

                          setDraftConflict(null);
                          await performSave(mergedJson, mergedText, true);
                        }}
                      >
                        Merge Both Versions
                      </button>
                      <button
                        type="button"
                        className="noting-btn noting-btn-secondary"
                        style={{ fontSize: "11.5px", padding: "3px 8px" }}
                        onClick={() => {
                          const serverDoc = parseDocFromInput(draftConflict.serverJson || draftConflict.serverContent);
                          const serverJson = JSON.stringify(serverDoc);
                          const serverText = canonicalizeRichTextDoc(serverDoc).trim();

                          draftRevisionRef.current = draftConflict.serverRevision;
                          setDraftRevision(draftConflict.serverRevision);
                          setDraftContentJson(serverJson);
                          setDraftCanonicalText(serverText);
                          latestJsonRef.current = serverJson;
                          latestTextRef.current = serverText;
                          savedJsonRef.current = serverJson;

                          editorRef.current?.setContent(serverDoc);

                          setDraftConflict(null);
                          setAutosaveStatus("saved");
                          setLastSavedAt(new Date());
                        }}
                      >
                        Discard My Edits & Accept Server Draft
                      </button>
                    </div>
                  </div>
                )}

                {/* Working Rich Text Editor with Formatting Toolbar */}
                <NotingRichEditor
                  ref={editorRef}
                  initialContent={draftContentJson || draftCanonicalText}
                  onChange={handleEditorChange}
                  onSelectionChange={(selectedText, startCanon, endCanon) => {
                    if (selectedText.trim().length > 0) {
                      setDraftSelection({ text: selectedText.trim(), start: startCanon, end: endCanon });
                    } else {
                      setDraftSelection(null);
                    }
                  }}
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
              onSelectDocument={(docId, page) => {
                setActiveDocId(docId);
                if (page) setActivePage(page);
              }}
              onDocumentUploaded={() => loadWorkspace()}
            />
          </div>
        )}
      </div>

      {/* Floating Remark Popover */}
      {activePopoverRemark && (
        <div
          className="noting-remark-popover"
          style={{
            position: "fixed",
            top: `${Math.min(window.innerHeight - 180, Math.max(70, popoverCoords.y - 40))}px`,
            left: `${Math.min(window.innerWidth - 300, Math.max(20, popoverCoords.x + 15))}px`,
            zIndex: 1000
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <div className="noting-remark-popover-header">
            <div>
              <div className="noting-remark-popover-author">{activePopoverRemark.authorName}</div>
              <div style={{ fontSize: "10.5px", color: "#64748b" }}>{activePopoverRemark.designation}</div>
            </div>
            <span className={`noting-remark-popover-badge ${activePopoverRemark.state.toLowerCase()}`}>
              {activePopoverRemark.state}
            </span>
          </div>

          <div className="noting-remark-popover-text">"{activePopoverRemark.text}"</div>

          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "8px" }}>
            {capabilities.canRemark && (
              <button
                type="button"
                className="noting-btn noting-btn-secondary toggle-remark-state-btn"
                style={{ padding: "2px 8px", fontSize: "11px" }}
                onClick={() => handleToggleRemarkState(activePopoverRemark)}
              >
                {activePopoverRemark.state === "Open" ? "Mark Addressed" : "Reopen Remark"}
              </button>
            )}
            <button
              type="button"
              className="noting-btn noting-btn-secondary"
              style={{ padding: "2px 8px", fontSize: "11px" }}
              onClick={() => setActivePopoverRemark(null)}
            >
              ✕ Close
            </button>
          </div>
        </div>
      )}

      {/* Floating Action Pill on text selection */}
      {selectedRange && capabilities.canRemark && (
        <div
          className="text-selection-pill"
          style={{
            position: "fixed",
            left: `${selectedRange.coords.x}px`,
            top: `${selectedRange.coords.y}px`
          }}
          onClick={() => {
            setAddRemarkState({
              note: selectedRange.note,
              anchor: selectedRange.anchor
            });
            setSelectedRange(null);
          }}
        >
          <span>💬 Add Remark on Quote</span>
        </div>
      )}

      {/* Focus Mode Floating Toggle */}
      {focusMode && (
        <button
          type="button"
          className="focus-mode-drawer-toggle"
          onClick={() => setFocusDrawerOpen(!focusDrawerOpen)}
        >
          <span>📁 {focusDrawerOpen ? "Hide Documents" : "Show Documents"} ({documents.length})</span>
        </button>
      )}

      {/* Focus Mode Overlay Drawer */}
      {focusMode && focusDrawerOpen && (
        <div className="focus-mode-overlay-drawer">
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "10px 14px", borderBottom: "1px solid #e2e8f0" }}>
            <strong style={{ fontSize: "13px" }}>Matter Documents</strong>
            <button className="noting-btn noting-btn-secondary" onClick={() => setFocusDrawerOpen(false)}>✕</button>
          </div>
          <div style={{ flex: 1, overflow: "hidden" }}>
            <DocumentRailViewer
              matterId={matterId}
              matterRevision={workspaceData?.revision}
              documents={documents}
              activeDocumentId={activeDocId}
              activePage={activePage}
              canAnnotate={capabilities.canAnnotate}
              onSelectDocument={(docId, page) => {
                setActiveDocId(docId);
                if (page) setActivePage(page);
              }}
              onDocumentUploaded={() => loadWorkspace()}
            />
          </div>
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
          onCreated={(newRemark: MatterNoteRemarkDto) => {
            setRemarksMap((prev) => ({
              ...prev,
              [addRemarkState.note.id]: [...(prev[addRemarkState.note.id] || []), newRemark]
            }));
            setAddRemarkState(null);
          }}
        />
      )}

      {/* View Remarks List Modal */}
      {viewRemarksState && (
        <ViewRemarksModal
          matterId={matterId}
          noteId={viewRemarksState.note.id}
          noteNumber={viewRemarksState.note.number}
          remarks={viewRemarksState.remarks}
          canManage={capabilities.canRemark}
          onClose={() => setViewRemarksState(null)}
          onUpdated={(updatedRemark: MatterNoteRemarkDto) => {
            setRemarksMap((prev) => ({
              ...prev,
              [viewRemarksState.note.id]: (prev[viewRemarksState.note.id] || []).map((r) =>
                r.id === updatedRemark.id ? updatedRemark : r
              )
            }));
          }}
        />
      )}

      {/* Cite Document Modal */}
      {showCitationModal && citationAnchor && (
        <CiteDocumentModal
          matterId={matterId}
          anchor={citationAnchor}
          draftRevision={draftRevisionRef.current || draftRevision}
          documents={documents}
          onClose={() => setShowCitationModal(false)}
          onCited={(updatedDraft) => {
            setDraftRevision(updatedDraft.revision);
            draftRevisionRef.current = updatedDraft.revision;
            setShowCitationModal(false);
          }}
        />
      )}

      {/* Send / Return File Modal */}
      {showSendModal && (
        <SendReturnModal
          matterId={matterId}
          activeDraftRevision={confirmedSendRevision}
          linkedDaks={linkedDaks}
          defaultControllingDakId={sourceDakId}
          onClose={() => setShowSendModal(false)}
          onSent={() => {
            setShowSendModal(false);
            setDraftContentJson("");
            setDraftCanonicalText("");
            latestJsonRef.current = "";
            latestTextRef.current = "";
            savedJsonRef.current = "";
            loadDraft();
            loadWorkspace();
          }}
        />
      )}
    </div>
  );
};
