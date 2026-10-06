import React, { useState, useEffect, useCallback, useRef } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { DakDetail, DakCategory, PhysicalOriginalInfo } from "./types";
import { DakTimeline } from "./DakTimeline";
import { DakMovementModal } from "./DakMovementModal";
import "./dak.css";

interface DeskOption {
  id: string;
  code: string;
  name: string;
}

interface DeskTargetOption {
  id: string;
  code: string;
  name: string;
  members: { userId: string; displayName: string; designation?: string; isPrimary: boolean }[];
}

export const DakDetailWorkspace: React.FC = () => {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const currentDakIdRef = useRef<string>(id);
  useEffect(() => {
    currentDakIdRef.current = id;
  }, [id]);

  // Persisted Server State
  const [dak, setDak] = useState<DakDetail | null>(null);
  const [physicalOriginal, setPhysicalOriginal] = useState<PhysicalOriginalInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "documents" | "links" | "timeline">("overview");

  // Modals visibility state
  const [movementModalMode, setMovementModalMode] = useState<"move" | "dispose" | "cancel" | null>(null);
  const [showEditModal, setShowEditModal] = useState(false);
  const [showAttachModal, setShowAttachModal] = useState(false);
  const [showLinkModal, setShowLinkModal] = useState(false);
  const [showPhysicalModal, setShowPhysicalModal] = useState(false);

  // Edit metadata form state
  const [editSubject, setEditSubject] = useState("");
  const [editSenderName, setEditSenderName] = useState("");
  const [editSenderDesignation, setEditSenderDesignation] = useState("");
  const [editSenderDepartment, setEditSenderDepartment] = useState("");
  const [editSenderAddress, setEditSenderAddress] = useState("");
  const [editSenderRef, setEditSenderRef] = useState("");
  const [editLetterDate, setEditLetterDate] = useState("");
  const [editInwardMode, setEditInwardMode] = useState("");
  const [editPriority, setEditPriority] = useState<"Routine" | "Urgent" | "Immediate">("Routine");
  const [editDueDate, setEditDueDate] = useState("");
  const [editCategoryId, setEditCategoryId] = useState("");
  const [editWorkstreamId, setEditWorkstreamId] = useState("");
  const [categories, setCategories] = useState<DakCategory[]>([]);
  const [workstreams, setWorkstreams] = useState<{ id: string; name: string }[]>([]);

  // Physical original separate MODAL DRAFT state (independent of persisted physicalOriginal card state)
  const [poDraftHasOriginal, setPoDraftHasOriginal] = useState<"unknown" | "yes" | "no">("unknown");
  const [poDraftDeskId, setPoDraftDeskId] = useState("");
  const [poDraftUserId, setPoDraftUserId] = useState("");
  const [poDraftLocationNote, setPoDraftLocationNote] = useState("");
  const [poDraftProvenanceNote, setPoDraftProvenanceNote] = useState("");
  const [savingPO, setSavingPO] = useState(false);

  // Desk lookups: both directory desks (Dak.View) and movement target desks with members (Dak.Move)
  const [directoryDesks, setDirectoryDesks] = useState<DeskOption[]>([]);
  const [movementDesks, setMovementDesks] = useState<DeskTargetOption[]>([]);

  // Add Attachment form state
  const [attachFile, setAttachFile] = useState<File | null>(null);
  const [attachTitle, setAttachTitle] = useState("");
  const [attachType, setAttachType] = useState("Enclosure");
  const [attaching, setAttaching] = useState(false);

  // Add Link form state
  const [linkType, setLinkType] = useState<"Village" | "Award" | "Matter" | "Khasra">("Award");
  const [linkEntityId, setLinkEntityId] = useState("");
  const [linking, setLinking] = useState(false);

  // Outward replies
  const [outwardReplies, setOutwardReplies] = useState<
    { id: string; outwardNumber: string; outwardDate: string; subject: string; status: string; recipientName: string }[]
  >([]);

  // Helper to clear all record-specific state immediately when id changes or reset is needed
  const resetRecordState = useCallback(() => {
    setDak(null);
    setPhysicalOriginal(null);
    setOutwardReplies([]);
    setMovementDesks([]);
    setCategories([]);
    setWorkstreams([]);
    setShowPhysicalModal(false);
    setShowEditModal(false);
    setShowAttachModal(false);
    setShowLinkModal(false);
    setMovementModalMode(null);
    setAttachFile(null);
    setAttachTitle("");
    setAttachType("Enclosure");
    setLinkEntityId("");
    setLinkType("Award");
    setPoDraftHasOriginal("unknown");
    setPoDraftDeskId("");
    setPoDraftUserId("");
    setPoDraftLocationNote("");
    setPoDraftProvenanceNote("");
    setEditSubject("");
    setEditSenderName("");
    setEditSenderDesignation("");
    setEditSenderDepartment("");
    setEditSenderAddress("");
    setEditSenderRef("");
    setEditLetterDate("");
    setEditInwardMode("");
    setEditPriority("Routine");
    setEditDueDate("");
    setEditCategoryId("");
    setEditWorkstreamId("");
    setSavingPO(false);
    setAttaching(false);
    setLinking(false);
  }, []);

  const loadDakData = useCallback(async (targetId: string, signal?: AbortSignal) => {
    if (!targetId) return;

    const isStale = () => currentDakIdRef.current !== targetId || Boolean(signal?.aborted);

    if (isStale()) return;

    try {
      setLoading(true);
      setError(null);

      const res = await fetch(`/api/dak/${targetId}`, { signal, credentials: "include" });
      if (isStale()) return;

      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view this Dak.");
        if (res.status === 404) throw new Error("Dak not found.");
        throw new Error("Failed to load Dak details.");
      }
      const data = (await res.json()) as DakDetail;
      if (isStale()) return;
      setDak(data);

      // Pre-fill edit metadata modal
      setEditSubject(data.subject);
      setEditSenderName(data.senderName);
      setEditSenderDesignation(data.senderDesignation || "");
      setEditSenderDepartment(data.senderDepartment || "");
      setEditSenderAddress(data.senderAddress || "");
      setEditSenderRef(data.senderReferenceNumber || "");
      setEditLetterDate(data.senderLetterDate || "");
      setEditInwardMode(data.inwardMode);
      setEditPriority(data.priority);
      setEditDueDate(data.dueDate || "");
      setEditCategoryId(data.categoryId || "");
      setEditWorkstreamId(data.workstreamId || "");

      // Load Physical Original Data
      fetch(`/api/dak/${targetId}/physical-original`, { signal, credentials: "include" })
        .then((r) => (r.ok ? (r.json() as Promise<PhysicalOriginalInfo>) : null))
        .then((poData) => {
          if (!isStale()) setPhysicalOriginal(poData);
        })
        .catch(() => {
          if (!isStale()) setPhysicalOriginal(null);
        });

      // Load outward replies
      fetch(`/api/outward?dakId=${targetId}`, { signal, credentials: "include" })
        .then((r) => (r.ok ? (r.json() as Promise<{ items: { id: string; outwardNumber: string; outwardDate: string; subject: string; status: string; recipientName: string }[] }>) : null))
        .then((d) => {
          if (!isStale()) setOutwardReplies(d?.items || []);
        })
        .catch(() => {
          if (!isStale()) setOutwardReplies([]);
        });

      // Load edit lookups
      fetch(`/api/dak/${targetId}/lookups/edit`, { signal, credentials: "include" })
        .then((r) => (r.ok ? (r.json() as Promise<{ categories: DakCategory[]; workstreams: { id: string; name: string }[] }>) : null))
        .then((d) => {
          if (!isStale()) {
            if (d) {
              setCategories(d.categories.filter((c) => c.isActive));
              setWorkstreams(d.workstreams);
            } else {
              setCategories([]);
              setWorkstreams([]);
            }
          }
        })
        .catch(() => {
          if (!isStale()) {
            setCategories([]);
            setWorkstreams([]);
          }
        });

      // Optionally load enriched movement targets with member officers (Dak.Move)
      fetch(`/api/dak/${targetId}/movement-targets`, { signal, credentials: "include" })
        .then((r) => (r.ok ? (r.json() as Promise<{ desks: DeskTargetOption[] }>) : null))
        .then((d) => {
          if (!isStale()) setMovementDesks(d?.desks || []);
        })
        .catch(() => {
          if (!isStale()) setMovementDesks([]);
        });
    } catch (err: unknown) {
      if (!isStale()) {
        setError(err instanceof Error ? err.message : "Failed to load Dak.");
      }
    } finally {
      if (!isStale()) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    if (!id) return;

    // Immediately clear all record-specific state on id change
    resetRecordState();

    const controller = new AbortController();
    void loadDakData(id, controller.signal);

    // Also load directory desks (global lookup)
    fetch("/api/dak/lookups/directory", { signal: controller.signal, credentials: "include" })
      .then((r) => (r.ok ? (r.json() as Promise<{ desks: DeskOption[] }>) : null))
      .then((data) => {
        if (!controller.signal.aborted && data) setDirectoryDesks(data.desks);
      })
      .catch(() => {});

    return () => {
      controller.abort();
    };
  }, [id, resetRecordState, loadDakData]);

  // Combine desk choices: movementDesks if available, otherwise directoryDesks mapped to DeskTargetOption
  const combinedDesks: DeskTargetOption[] = movementDesks.length > 0
    ? movementDesks
    : directoryDesks.map((d) => ({ id: d.id, code: d.code, name: d.name, members: [] }));

  const openPhysicalModal = () => {
    // Initialize draft state variables ONLY from current persisted physicalOriginal
    if (physicalOriginal) {
      setPoDraftHasOriginal(physicalOriginal.hasPhysicalOriginal === true ? "yes" : physicalOriginal.hasPhysicalOriginal === false ? "no" : "unknown");
      setPoDraftDeskId(physicalOriginal.deskId || "");
      setPoDraftUserId(physicalOriginal.userId || "");
      setPoDraftLocationNote(physicalOriginal.locationNote || "");
      setPoDraftProvenanceNote(physicalOriginal.provenanceNote || "");
    } else {
      setPoDraftHasOriginal("unknown");
      setPoDraftDeskId("");
      setPoDraftUserId("");
      setPoDraftLocationNote("");
      setPoDraftProvenanceNote("");
    }
    setShowPhysicalModal(true);
  };

  const handleSaveMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak) return;
    const targetId = dak.id;
    try {
      const res = await fetch(`/api/dak/${targetId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json", "If-Match": `"${dak.revision}"` },
        credentials: "include",
        body: JSON.stringify({
          subject: editSubject.trim(),
          senderName: editSenderName.trim(),
          senderDesignation: editSenderDesignation.trim() || null,
          senderDepartment: editSenderDepartment.trim() || null,
          senderAddress: editSenderAddress.trim() || null,
          senderReferenceNumber: editSenderRef.trim() || null,
          senderLetterDate: editLetterDate || null,
          inwardMode: editInwardMode.trim(),
          priority: editPriority,
          dueDate: editDueDate || null,
          categoryId: editCategoryId || null,
          workstreamId: editWorkstreamId || null,
          expectedRevision: dak.revision,
        }),
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to update metadata.");
      }

      setShowEditModal(false);
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error saving metadata.");
      }
    }
  };

  const handleSavePhysicalOriginal = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak) return;
    const targetId = dak.id;

    if (!poDraftProvenanceNote.trim()) {
      alert("A provenance / observation note is mandatory when updating physical original custody.");
      return;
    }

    const hasPO = poDraftHasOriginal === "yes" ? true : poDraftHasOriginal === "no" ? false : null;

    if (hasPO !== true && (poDraftDeskId || poDraftUserId || poDraftLocationNote.trim())) {
      alert("A location can only be recorded when physical original existence is confirmed (Yes).");
      return;
    }

    try {
      setSavingPO(true);
      const res = await fetch(`/api/dak/${targetId}/physical-original`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          hasPhysicalOriginal: hasPO,
          deskId: hasPO === true && poDraftDeskId ? poDraftDeskId : null,
          userId: hasPO === true && poDraftUserId ? poDraftUserId : null,
          locationNote: hasPO === true && poDraftLocationNote.trim() ? poDraftLocationNote.trim() : null,
          provenanceNote: poDraftProvenanceNote.trim(),
          expectedRevision: physicalOriginal?.revision ?? dak.revision,
        }),
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to update Physical Original information.");
      }

      setShowPhysicalModal(false);
      // Canonical reload: refresh both Dak details and Physical Original to update dak.revision
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error saving Physical Original information.");
      }
    } finally {
      if (currentDakIdRef.current === targetId) {
        setSavingPO(false);
      }
    }
  };

  const handleAddAttachment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak || !attachFile) return;
    const targetId = dak.id;
    try {
      setAttaching(true);
      const fd = new FormData();
      fd.append("file", attachFile);
      fd.append("title", attachTitle.trim() || attachFile.name);
      fd.append("attachmentType", attachType);

      const res = await fetch(`/api/dak/${targetId}/attachments`, {
        method: "POST",
        headers: { "If-Match": `"${dak.revision}"` },
        credentials: "include",
        body: fd,
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to upload attachment.");
      }

      setAttachFile(null);
      setAttachTitle("");
      setShowAttachModal(false);
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error uploading attachment.");
      }
    } finally {
      if (currentDakIdRef.current === targetId) {
        setAttaching(false);
      }
    }
  };

  const handleDeleteAttachment = async (attachmentId: string) => {
    if (!dak || !confirm("Are you sure you want to remove this attachment?")) return;
    const targetId = dak.id;
    try {
      const res = await fetch(`/api/dak/${targetId}/attachments/${attachmentId}`, {
        method: "DELETE",
        headers: { "If-Match": `"${dak.revision}"` },
        credentials: "include",
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) throw new Error("Failed to delete attachment.");
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error deleting attachment.");
      }
    }
  };

  const handleAddLink = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dak || !linkEntityId.trim()) return;
    const targetId = dak.id;
    try {
      setLinking(true);
      const endpointType = linkType.toLowerCase() + "s";
      const res = await fetch(`/api/dak/${targetId}/links/${endpointType}`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "If-Match": `"${dak.revision}"` },
        credentials: "include",
        body: JSON.stringify({ entityId: linkEntityId.trim() }),
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to link entity.");
      }

      setLinkEntityId("");
      setShowLinkModal(false);
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error creating link.");
      }
    } finally {
      if (currentDakIdRef.current === targetId) {
        setLinking(false);
      }
    }
  };

  const handleDeleteLink = async (linkTypeParam: string, linkId: string) => {
    if (!dak || !confirm("Remove this association?")) return;
    const targetId = dak.id;
    try {
      const endpointType = linkTypeParam.toLowerCase() + "s";
      const res = await fetch(`/api/dak/${targetId}/links/${endpointType}/${linkId}`, {
        method: "DELETE",
        headers: { "If-Match": `"${dak.revision}"` },
        credentials: "include",
      });

      if (currentDakIdRef.current !== targetId) return;

      if (!res.ok) throw new Error("Failed to remove link.");
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error removing link.");
      }
    }
  };

  if (loading || (dak && dak.id !== id)) return <div className="state"><strong>Loading Dak record...</strong></div>;
  if (error || !dak) return <div className="state error"><strong>Error:</strong> {error || "Dak not found."}</div>;

  const isTerminal = dak.recordStatus !== "Active" || dak.status === "Disposed" || dak.status === "Cancelled";
  const canAssignWork = hasPermission("WorkItem.Create") && !isTerminal;
  const canMove = hasPermission("Dak.Move") && !isTerminal;
  const canDispose = hasPermission("Dak.Dispose") && !isTerminal;
  const canCancel = hasPermission("Dak.Cancel") && !isTerminal;
  const canEdit = hasPermission("Dak.Edit") && !isTerminal;

  // Selected desk in physical modal draft
  const poDraftSelectedDesk = combinedDesks.find((d) => d.id === poDraftDeskId);

  // Derive card display labels STRICTLY from persisted physicalOriginal object (never from modal draft)
  const poCardState = physicalOriginal?.hasPhysicalOriginal === true
    ? "yes"
    : physicalOriginal?.hasPhysicalOriginal === false
      ? "no"
      : "unknown";

  const poCardDeskName = physicalOriginal?.deskId
    ? combinedDesks.find((d) => d.id === physicalOriginal.deskId)?.name || null
    : null;

  const poCardOfficerName = physicalOriginal?.userId
    ? combinedDesks.flatMap((d) => d.members).find((m) => m.userId === physicalOriginal.userId)?.displayName || null
    : null;

  return (
    <div className="dak-detail-workspace">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/dak">Dak / Inward</Link> <i>/</i> <span>{dak.diaryNumber}</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow" style={{ display: "flex", gap: "10px", alignItems: "center" }}>
            <span>Inward Dak #{dak.diaryNumber}</span>
            <span className={`priority-pill priority-${dak.priority.toLowerCase()}`}>{dak.priority}</span>
            <span className={`status-pill status-${dak.status.toLowerCase()}`}>{dak.status}</span>
            {isTerminal && <span className="terminal-badge" title="Historical terminal record; modifications disabled">🔒 Read-Only Record</span>}
          </div>
          <h1>{dak.subject}</h1>
          <p className="subtext">
            Received from <strong>{dak.senderName}</strong>
            {dak.senderDepartment ? ` (${dak.senderDepartment})` : ""} on{" "}
            {dak.receivedDate} via {dak.inwardMode}.
          </p>
        </div>
      </div>

      {/* Custody Warning Banner */}
      {dak.currentAssignment?.needsAttention && (
        <div className="dak-attention-banner">
          <span style={{ fontSize: "20px" }}>⚠️</span>
          <div>
            <strong>Operational Assignment Alert: Attention Required</strong>
            <div>
              The assigned office desk (<em>{dak.currentAssignment.deskName}</em>) is inactive or the assigned user is no longer an active eligible member of that desk. Immediate reassignment is advised.
            </div>
          </div>
        </div>
      )}

      {/* Dual Custody Card: Operational Assignment vs Physical Original Location */}
      <div className="dak-custody-card-dual">
        {/* Box A: Current Operational Assignment */}
        <div className="custody-box operational-box">
          <div className="custody-box-header">
            <span className="custody-box-icon">📋</span>
            <div>
              <span className="custody-box-title">Current Operational Assignment</span>
              <span className="custody-box-subtitle">Workflow & Task Responsibility</span>
            </div>
          </div>

          <div className="custody-box-body">
            {dak.currentAssignment ? (
              <>
                <div className="custody-main-name">
                  {dak.currentAssignment.deskName} ({dak.currentAssignment.deskCode})
                </div>
                <div className="custody-sub-name">
                  Officer: <strong>{dak.currentAssignment.assignedUserDisplayName || "General Desk Assignment"}</strong>
                </div>
                <div className="custody-meta">
                  Marked by {dak.currentAssignment.assignedByDisplayName} on{" "}
                  {new Date(dak.currentAssignment.assignedAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}
                </div>
              </>
            ) : (
              <div className="unmarked-custody-state">
                <span className="unmarked-badge">⚠️ UNMARKED / Intake Queue</span>
                <span className="subtext" style={{ display: "block", marginTop: "4px" }}>
                  Awaiting initial marking to an office desk.
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Box B: Physical Original Location (Rendered STRICTLY from persisted physicalOriginal) */}
        <div className="custody-box physical-box">
          <div className="custody-box-header">
            <span className="custody-box-icon">📁</span>
            <div>
              <span className="custody-box-title">Physical Original Location</span>
              <span className="custody-box-subtitle">Paper Original Document Custody</span>
            </div>
            {canEdit && (
              <button
                className="secondary-button btn-xs"
                style={{ marginLeft: "auto" }}
                onClick={openPhysicalModal}
              >
                Update Physical Location
              </button>
            )}
          </div>

          <div className="custody-box-body">
            {physicalOriginal ? (
              <>
                <div className="po-state-row">
                  <span>State:</span>
                  <span className={`po-state-badge po-state-${poCardState}`}>
                    {poCardState === "yes" ? "✓ Yes (Paper Original Confirmed)" : poCardState === "no" ? "✕ No (Digital Only)" : "❓ Unknown"}
                  </span>
                </div>

                {poCardState === "yes" && (
                  <div className="po-details-grid">
                    {poCardDeskName && (
                      <div>
                        <span className="po-meta-label">Desk:</span> <strong>{poCardDeskName}</strong>
                      </div>
                    )}
                    {poCardOfficerName && (
                      <div>
                        <span className="po-meta-label">Custodian Officer:</span> <strong>{poCardOfficerName}</strong>
                      </div>
                    )}
                    {physicalOriginal.locationNote && (
                      <div>
                        <span className="po-meta-label">Location Note:</span> {physicalOriginal.locationNote}
                      </div>
                    )}
                    {physicalOriginal.provenanceNote && (
                      <div>
                        <span className="po-meta-label">Provenance Note:</span> <em>{physicalOriginal.provenanceNote}</em>
                      </div>
                    )}
                  </div>
                )}
              </>
            ) : (
              <div className="subtext">Physical original custody not recorded yet.</div>
            )}
          </div>
        </div>
      </div>

      {/* Primary Footer Actions Bar */}
      {!isTerminal && (
        <div className="dak-workspace-actions-bar">
          {canAssignWork && (
            <Link
              to={`/work/new?dakId=${dak.id}`}
              className="primary-button"
              style={{ background: "#0284c7" }}
            >
              + Assign Work
            </Link>
          )}

          {canMove && (
            <button className="primary-button" onClick={() => setMovementModalMode("move")}>
              {dak.currentAssignment ? "Forward / Return ➔" : "Mark to Desk ➔"}
            </button>
          )}

          {canEdit && (
            <button className="secondary-button" onClick={() => setShowEditModal(true)}>
              Edit Details
            </button>
          )}

          {canDispose && (
            <button className="secondary-button" onClick={() => setMovementModalMode("dispose")}>
              Dispose Dak
            </button>
          )}

          {canCancel && (
            <button className="secondary-button" style={{ color: "#b91c1c" }} onClick={() => setMovementModalMode("cancel")}>
              Cancel Entry
            </button>
          )}
        </div>
      )}

      {/* Tabs */}
      <div className="workspace-tabs-bar">
        <button
          className={`tab-btn ${activeTab === "overview" ? "active" : ""}`}
          onClick={() => setActiveTab("overview")}
        >
          Overview & Details
        </button>
        <button
          className={`tab-btn ${activeTab === "documents" ? "active" : ""}`}
          onClick={() => setActiveTab("documents")}
        >
          Documents & Attachments ({(dak.attachments?.length || 0) + (dak.mainDocumentId ? 1 : 0)})
        </button>
        <button
          className={`tab-btn ${activeTab === "links" ? "active" : ""}`}
          onClick={() => setActiveTab("links")}
        >
          Linked Context ({(dak.villageLinks?.length || 0) + (dak.awardLinks?.length || 0) + (dak.matterLinks?.length || 0) + (dak.khasraLinks?.length || 0)})
        </button>
        <button
          className={`tab-btn ${activeTab === "timeline" ? "active" : ""}`}
          onClick={() => setActiveTab("timeline")}
        >
          Movement Timeline
        </button>
      </div>

      {/* TAB 1: OVERVIEW */}
      {activeTab === "overview" && (
        <div className="dak-grid-2col">
          <div className="info-section">
            <h2>Correspondence Metadata</h2>
            <dl className="meta-dl">
              <div>
                <dt>Diary Number</dt>
                <dd><strong>{dak.diaryNumber}</strong></dd>
              </div>
              <div>
                <dt>Received Date</dt>
                <dd>{dak.receivedDate}</dd>
              </div>
              <div>
                <dt>Inward Mode</dt>
                <dd>{dak.inwardMode}</dd>
              </div>
              <div>
                <dt>Priority</dt>
                <dd><span className={`priority-pill priority-${dak.priority.toLowerCase()}`}>{dak.priority}</span></dd>
              </div>
              <div>
                <dt>Action Due Date</dt>
                <dd>{dak.dueDate || "None specified"}</dd>
              </div>
              <div>
                <dt>Category</dt>
                <dd>{dak.categoryName || "Unclassified"}</dd>
              </div>
              <div>
                <dt>Functional Workstream</dt>
                <dd>{dak.workstreamName || "General / Unassigned"}</dd>
              </div>
              <div>
                <dt>Status</dt>
                <dd><span className={`status-pill status-${dak.status.toLowerCase()}`}>{dak.status}</span></dd>
              </div>
              <div>
                <dt>Revision</dt>
                <dd>Rev #{dak.revision}</dd>
              </div>
            </dl>
          </div>

          <div className="info-section">
            <h2>Sender Details & References</h2>
            <dl className="meta-dl">
              <div>
                <dt>Sender Name</dt>
                <dd><strong>{dak.senderName}</strong></dd>
              </div>
              <div>
                <dt>Designation</dt>
                <dd>{dak.senderDesignation || "—"}</dd>
              </div>
              <div>
                <dt>Department / Entity</dt>
                <dd>{dak.senderDepartment || "—"}</dd>
              </div>
              <div>
                <dt>Letter Ref. No.</dt>
                <dd>{dak.senderReferenceNumber || "—"}</dd>
              </div>
              <div>
                <dt>Letter Date</dt>
                <dd>{dak.senderLetterDate || "—"}</dd>
              </div>
              <div>
                <dt>Sender Address</dt>
                <dd>{dak.senderAddress || "—"}</dd>
              </div>
              <div>
                <dt>Registered By</dt>
                <dd>{dak.createdBy || "System"}</dd>
              </div>
              <div>
                <dt>Registered At</dt>
                <dd>{new Date(dak.createdAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}</dd>
              </div>
            </dl>
          </div>
        </div>
      )}

      {/* TAB 2: DOCUMENTS */}
      {activeTab === "documents" && (
        <div>
          <div className="section-heading">
            <div>
              <h2>Scanned Correspondence & Supporting Documents</h2>
              <span>Primary inward scan and attached exhibits or annexures.</span>
            </div>
            {canEdit && (
              <button className="primary-button" onClick={() => setShowAttachModal(true)}>
                + Add Attachment
              </button>
            )}
          </div>

          {dak.mainDocumentId ? (
            <div className="workspace-panel" style={{ marginBottom: "20px" }}>
              <div className="panel-title">
                <h3>Primary Inward Scan</h3>
                <a
                  className="primary-button"
                  href={`/api/dak/${dak.id}/content`}
                  target="_blank"
                  rel="noreferrer"
                >
                  View / Download Document
                </a>
              </div>
              <p>File: <strong>{dak.mainDocumentFileName || "Main Scan Document"}</strong></p>
            </div>
          ) : (
            <div className="state">No primary inward scan was uploaded during registration.</div>
          )}

          <h3>Additional Enclosures & Annexures</h3>
          {dak.attachments.length === 0 ? (
            <p className="subtext">No additional attachments uploaded.</p>
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>#</th>
                    <th>Document Title</th>
                    <th>Type</th>
                    <th>Original File</th>
                    <th>Uploaded Date</th>
                    <th>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {dak.attachments.map((a) => (
                    <tr key={a.id}>
                      <td>{a.sequenceOrder}</td>
                      <td><strong>{a.title}</strong></td>
                      <td>{a.attachmentType}</td>
                      <td>{a.originalFileName}</td>
                      <td>{new Date(a.createdAt).toLocaleDateString("en-IN", { dateStyle: "medium" })}</td>
                      <td>
                        <div style={{ display: "flex", gap: "8px" }}>
                          <a
                            className="text-action"
                            href={`/api/dak/${dak.id}/attachments/${a.id}/content`}
                            target="_blank"
                            rel="noreferrer"
                          >
                            Download
                          </a>
                          {canEdit && (
                            <button
                              className="text-action"
                              style={{ color: "#ef4444" }}
                              onClick={() => void handleDeleteAttachment(a.id)}
                            >
                              Remove
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

      {/* TAB 3: LINKED CONTEXT */}
      {activeTab === "links" && (
        <div>
          <div className="section-heading">
            <div>
              <h2>Domain Cross-References</h2>
              <span>Link this correspondence to specific Villages, Awards, Matters, or Khasras.</span>
            </div>
            {canEdit && (
              <button className="primary-button" onClick={() => setShowLinkModal(true)}>
                + Add Cross-Reference
              </button>
            )}
          </div>

          <div className="dak-grid-2col">
            <div className="info-section">
              <h3>Linked Awards ({dak.awardLinks.length})</h3>
              {dak.awardLinks.length === 0 ? (
                <p className="subtext">No awards linked.</p>
              ) : (
                dak.awardLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    {l.canOpen && l.entityId ? (
                      <Link to={`/awards/${l.entityId}`}>Award: {l.displayName}</Link>
                    ) : (
                      <span className="restricted-badge">🔒 Restricted record</span>
                    )}
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("awards", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}

              <h3 style={{ marginTop: "20px" }}>Linked Villages ({dak.villageLinks.length})</h3>
              {dak.villageLinks.length === 0 ? (
                <p className="subtext">No villages linked.</p>
              ) : (
                dak.villageLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    {l.canOpen && l.entityId ? (
                      <Link to={`/villages/${l.entityId}`}>Village: {l.displayName}</Link>
                    ) : (
                      <span className="restricted-badge">🔒 Restricted record</span>
                    )}
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("villages", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}
            </div>

            <div className="info-section">
              <h3>Linked Matters ({dak.matterLinks.length})</h3>
              {dak.matterLinks.length === 0 ? (
                <p className="subtext">No matters linked.</p>
              ) : (
                dak.matterLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    {l.canOpen && l.entityId ? (
                      <Link to={`/matters/${l.entityId}`}>Matter: {l.displayName}</Link>
                    ) : (
                      <span className="restricted-badge">🔒 Restricted record</span>
                    )}
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("matters", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}

              <h3 style={{ marginTop: "20px" }}>Linked Khasras ({dak.khasraLinks.length})</h3>
              {dak.khasraLinks.length === 0 ? (
                <p className="subtext">No khasras linked.</p>
              ) : (
                dak.khasraLinks.map((l) => (
                  <span key={l.linkId} className="dak-link-badge">
                    {l.canOpen && l.entityId ? (
                      <Link to={`/khasras/${l.entityId}`}>Khasra: {l.displayName}</Link>
                    ) : (
                      <span className="restricted-badge">🔒 Restricted record</span>
                    )}
                    {canEdit && (
                      <button className="dak-link-remove" onClick={() => void handleDeleteLink("khasras", l.linkId)}>
                        ✕
                      </button>
                    )}
                  </span>
                ))
              )}
            </div>
          </div>

          {/* Outward Dispatches / Replies */}
          <div style={{ marginTop: "24px", paddingTop: "20px", borderTop: "1px solid #e2e8f0" }}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
              <div>
                <h3 style={{ margin: 0, fontSize: "16px", color: "#1e3a58" }}>
                  Outward Replies & Dispatches ({outwardReplies.length})
                </h3>
                <span style={{ fontSize: "12px", color: "#64748b" }}>
                  Official outward letters issued in response to or referencing this Dak.
                </span>
              </div>
              {hasPermission("Outward.Create") && !isTerminal && (
                <button
                  className="primary-button"
                  style={{ fontSize: "12px", padding: "4px 10px" }}
                  onClick={() => navigate(`/outward/new?dakId=${dak.id}`)}
                >
                  + Draft Outward Reply
                </button>
              )}
            </div>

            {outwardReplies.length === 0 ? (
              <p className="subtext">No outward replies have been issued for this Dak yet.</p>
            ) : (
              <div className="table-responsive">
                <table className="data-table">
                  <thead>
                    <tr>
                      <th>Outward No.</th>
                      <th>Subject</th>
                      <th>Recipient</th>
                      <th>Date</th>
                      <th>Status</th>
                      <th style={{ textAlign: "right" }}>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {outwardReplies.map((r) => (
                      <tr key={r.id}>
                        <td>
                          <Link to={`/outward/${r.id}`} style={{ fontWeight: 600, color: "#1e609e" }}>
                            {r.outwardNumber}
                          </Link>
                        </td>
                        <td>{r.subject}</td>
                        <td>{r.recipientName}</td>
                        <td>{r.outwardDate}</td>
                        <td>
                          <span className={`outward-pill outward-pill-${r.status.toLowerCase()}`}>
                            {r.status}
                          </span>
                        </td>
                        <td style={{ textAlign: "right" }}>
                          <button
                            className="secondary-button"
                            style={{ fontSize: "11px", padding: "2px 8px" }}
                            onClick={() => navigate(`/outward/${r.id}`)}
                          >
                            Open
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      )}

      {/* TAB 4: MOVEMENT TIMELINE */}
      {activeTab === "timeline" && (
        <div>
          <h2>Official Movement & Noting History</h2>
          <p className="subtext">Immutable audit trail of all custody transfers, officer assignments, notings, and instructions.</p>
          <DakTimeline dakId={dak.id} />
        </div>
      )}

      {/* Movement Modal */}
      {movementModalMode && (
        <DakMovementModal
          dak={dak}
          mode={movementModalMode}
          onClose={() => setMovementModalMode(null)}
          onSuccess={() => {
            if (!dak) return;
            const targetId = dak.id;
            if (currentDakIdRef.current === targetId) {
              setMovementModalMode(null);
              void loadDakData(targetId);
            }
          }}
        />
      )}

      {/* Edit Metadata Modal */}
      {showEditModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "720px" }}>
            <h3>Edit Dak Classification & Details</h3>
            <form onSubmit={handleSaveMetadata}>
              <div className="field-grid" style={{ marginBottom: "14px" }}>
                <label className="span-two">
                  Subject *
                  <input
                    type="text"
                    required
                    value={editSubject}
                    onChange={(e) => setEditSubject(e.target.value)}
                  />
                </label>
                <label>
                  Sender Name *
                  <input
                    type="text"
                    required
                    value={editSenderName}
                    onChange={(e) => setEditSenderName(e.target.value)}
                  />
                </label>
                <label>
                  Sender Designation
                  <input
                    type="text"
                    value={editSenderDesignation}
                    onChange={(e) => setEditSenderDesignation(e.target.value)}
                  />
                </label>
                <label>
                  Department / Entity
                  <input
                    type="text"
                    value={editSenderDepartment}
                    onChange={(e) => setEditSenderDepartment(e.target.value)}
                  />
                </label>
                <label>
                  Sender Reference No.
                  <input
                    type="text"
                    value={editSenderRef}
                    onChange={(e) => setEditSenderRef(e.target.value)}
                  />
                </label>
                <label>
                  Letter Date
                  <input
                    type="date"
                    value={editLetterDate}
                    onChange={(e) => setEditLetterDate(e.target.value)}
                  />
                </label>
                <label>
                  Inward Mode
                  <input
                    type="text"
                    value={editInwardMode}
                    onChange={(e) => setEditInwardMode(e.target.value)}
                  />
                </label>
                <label>
                  Priority
                  <select
                    value={editPriority}
                    onChange={(e) => setEditPriority(e.target.value as "Routine" | "Urgent" | "Immediate")}
                  >
                    <option value="Routine">Routine</option>
                    <option value="Urgent">Urgent</option>
                    <option value="Immediate">Immediate</option>
                  </select>
                </label>
                <label>
                  Action Due Date
                  <input
                    type="date"
                    value={editDueDate}
                    onChange={(e) => setEditDueDate(e.target.value)}
                  />
                </label>
                <label>
                  Category
                  <select value={editCategoryId} onChange={(e) => setEditCategoryId(e.target.value)}>
                    <option value="">-- Unclassified --</option>
                    {categories.map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </select>
                </label>
                <label>
                  Workstream
                  <select value={editWorkstreamId} onChange={(e) => setEditWorkstreamId(e.target.value)}>
                    <option value="">-- Unassigned --</option>
                    {workstreams.map((w) => (
                      <option key={w.id} value={w.id}>{w.name}</option>
                    ))}
                  </select>
                </label>
                <label className="span-two">
                  Sender Address
                  <textarea
                    value={editSenderAddress}
                    onChange={(e) => setEditSenderAddress(e.target.value)}
                    rows={2}
                  />
                </label>
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowEditModal(false)}>
                  Cancel
                </button>
                <button type="submit" className="primary-button">
                  Save Changes
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit Physical Original Custody Modal */}
      {showPhysicalModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "600px" }}>
            <h3>Update Physical Original Custody</h3>
            <form onSubmit={handleSavePhysicalOriginal}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Physical Paper Original Received / Present? *</label>
                <select
                  value={poDraftHasOriginal}
                  onChange={(e) => {
                    const val = e.target.value as "unknown" | "yes" | "no";
                    setPoDraftHasOriginal(val);
                    if (val !== "yes") {
                      setPoDraftDeskId("");
                      setPoDraftUserId("");
                      setPoDraftLocationNote("");
                    }
                  }}
                  className="intake-select"
                >
                  <option value="unknown">Unknown / Unconfirmed</option>
                  <option value="yes">Yes (Physical paper original exists)</option>
                  <option value="no">No (Digital only / no paper copy)</option>
                </select>
              </div>

              {poDraftHasOriginal === "yes" && (
                <>
                  <div className="field-group" style={{ marginBottom: "14px" }}>
                    <label>Physical Location Desk</label>
                    <select
                      value={poDraftDeskId}
                      onChange={(e) => {
                        setPoDraftDeskId(e.target.value);
                        setPoDraftUserId("");
                      }}
                      className="intake-select"
                    >
                      <option value="">-- Select Office Desk --</option>
                      {combinedDesks.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name} ({d.code})
                        </option>
                      ))}
                    </select>
                  </div>

                  {poDraftDeskId && poDraftSelectedDesk?.members.length ? (
                    <div className="field-group" style={{ marginBottom: "14px" }}>
                      <label>Physical Custodian Officer (at selected desk)</label>
                      <select
                        value={poDraftUserId}
                        onChange={(e) => setPoDraftUserId(e.target.value)}
                        className="intake-select"
                      >
                        <option value="">-- Unassigned / General Desk Storage --</option>
                        {poDraftSelectedDesk.members.map((m) => (
                          <option key={m.userId} value={m.userId}>
                            {m.displayName} {m.designation ? `(${m.designation})` : ""} {m.isPrimary ? "[Primary]" : ""}
                          </option>
                        ))}
                      </select>
                    </div>
                  ) : null}

                  <div className="field-group" style={{ marginBottom: "14px" }}>
                    <label>Physical Location Note</label>
                    <input
                      type="text"
                      placeholder="e.g. Almirah #3, Shelf B, File Cover 45-A"
                      value={poDraftLocationNote}
                      onChange={(e) => setPoDraftLocationNote(e.target.value)}
                      className="intake-input"
                    />
                  </div>
                </>
              )}

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Provenance / Audit Observation Note *</label>
                <textarea
                  required
                  rows={3}
                  placeholder="Record observation note regarding physical paper status or transfer details..."
                  value={poDraftProvenanceNote}
                  onChange={(e) => setPoDraftProvenanceNote(e.target.value)}
                  className="intake-textarea"
                />
                <span className="hint">Mandatory note explaining physical paper verification or location update.</span>
              </div>

              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowPhysicalModal(false)}
                  disabled={savingPO}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={savingPO}>
                  {savingPO ? "Saving..." : "Save Physical Custody"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Attachment Modal */}
      {showAttachModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Add Attachment</h3>
            <form onSubmit={handleAddAttachment}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>File *</label>
                <input
                  type="file"
                  required
                  onChange={(e) => setAttachFile(e.target.files?.[0] || null)}
                />
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Attachment Title</label>
                <input
                  type="text"
                  placeholder="e.g. High Court Notice / Site Inspection Report"
                  value={attachTitle}
                  onChange={(e) => setAttachTitle(e.target.value)}
                />
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Attachment Type</label>
                <select value={attachType} onChange={(e) => setAttachType(e.target.value)}>
                  <option value="Enclosure">Enclosure</option>
                  <option value="Annexure">Annexure</option>
                  <option value="CourtNotice">Court Notice</option>
                  <option value="InspectionReport">Inspection Report</option>
                  <option value="Other">Other Supporting Document</option>
                </select>
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowAttachModal(false)} disabled={attaching}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={attaching || !attachFile}>
                  {attaching ? "Uploading..." : "Upload Attachment"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Add Link Modal */}
      {showLinkModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Link Domain Entity</h3>
            <form onSubmit={handleAddLink}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Target Entity Type</label>
                <select
                  value={linkType}
                  onChange={(e) => setLinkType(e.target.value as "Village" | "Award" | "Matter" | "Khasra")}
                >
                  <option value="Award">Award</option>
                  <option value="Matter">Matter</option>
                  <option value="Village">Village</option>
                  <option value="Khasra">Khasra</option>
                </select>
              </div>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Entity ID (UUID) *</label>
                <input
                  type="text"
                  required
                  placeholder="Enter Entity UUID"
                  value={linkEntityId}
                  onChange={(e) => setLinkEntityId(e.target.value)}
                />
              </div>
              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowLinkModal(false)} disabled={linking}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={linking || !linkEntityId.trim()}>
                  {linking ? "Linking..." : "Create Link"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
