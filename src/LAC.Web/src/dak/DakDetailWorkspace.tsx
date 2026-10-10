import React, { useState, useEffect, useCallback, useRef } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { DakDetail, DakCategory, PhysicalOriginalInfo, DakTransferItem } from "./types";
import { DakTimeline } from "./DakTimeline";
import { DakMovementModal, type MovementModalMode } from "./DakMovementModal";
import { formatDakStatus, isLongPendingReceipt, formatElapsedTime, DAK_PENDING_RECEIPT_ALERT_HOURS } from "./dakConfig";
import "./dak.css";

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
  const { user, hasPermission } = useAuth();

  const currentDakIdRef = useRef<string>(id);
  useEffect(() => {
    currentDakIdRef.current = id;
  }, [id]);

  // Persisted Server State
  const [dak, setDak] = useState<DakDetail | null>(null);
  const [transfers, setTransfers] = useState<DakTransferItem[]>([]);
  const [transfersLoading, setTransfersLoading] = useState(true);
  const [transfersError, setTransfersError] = useState<string | null>(null);
  const [physicalOriginal, setPhysicalOriginal] = useState<PhysicalOriginalInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<"overview" | "documents" | "links" | "timeline">("overview");

  // Modals visibility state
  const [movementModalMode, setMovementModalMode] = useState<MovementModalMode | null>(null);
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
    setTransfers([]);
    setTransfersLoading(true);
    setTransfersError(null);
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
      setTransfersLoading(true);
      setTransfersError(null);

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

      // Load authoritative transfer list for pending delivery and return-pending derivations
      setTransfersLoading(true);
      setTransfersError(null);
      fetch(`/api/dak/${targetId}/transfers`, { signal, credentials: "include" })
        .then(async (r) => {
          if (!r.ok) throw new Error("Transfer details could not be loaded; refresh before acting.");
          return r.json() as Promise<{ items: DakTransferItem[] }>;
        })
        .then((d) => {
          if (!isStale()) {
            setTransfers(d?.items || []);
            setTransfersLoading(false);
          }
        })
        .catch((err: unknown) => {
          if (!isStale()) {
            if (err instanceof Error && err.name === "AbortError") return;
            setTransfers([]);
            setTransfersError("Transfer details could not be loaded; refresh before acting.");
            setTransfersLoading(false);
          }
        });

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
      fetch(`/api/outward?dakId=${targetId}&page=0&pageSize=25`, { signal, credentials: "include" })
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
        setTransfersLoading(false);
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
    if (!dak) return;
    const isPhysicalUnsettled = dak.physicalState === "InTransit" || dak.physicalState === "ReturnPending";
    const terminal = dak.recordStatus !== "Active" || dak.status === "Resolved" || dak.status === "Disposed" || dak.status === "Cancelled";
    if (!hasPermission("Dak.Edit") || terminal || isPhysicalUnsettled) return;

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
    const isPhysicalUnsettled = dak.physicalState === "InTransit" || dak.physicalState === "ReturnPending";
    const terminal = dak.recordStatus !== "Active" || dak.status === "Resolved" || dak.status === "Disposed" || dak.status === "Cancelled";
    if (!hasPermission("Dak.Edit") || terminal || isPhysicalUnsettled) return;
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

  const isResolved = dak.status === "Resolved" || dak.status === "Disposed";
  const isTerminal = dak.recordStatus !== "Active" || isResolved || dak.status === "Cancelled";

  const currentUserId = user?.id || (user as any)?.userId;

  const canMark = hasPermission("Dak.Mark");
  const canMove = hasPermission("Dak.Move");
  const canReceive = hasPermission("Dak.Receive");
  const canPullBack = hasPermission("Dak.PullBack");
  const canResolve = hasPermission("Dak.Resolve"); // Dak.Dispose cannot substitute
  const canReopen = hasPermission("Dak.Reopen");
  // Deriving routing and custody states from authoritative backend contract
  const routingState = dak.routingState;
  const activeTransfer = transfers.find((t) => t.state === "Pending") || null;
  const pulledBackPhysicalTransfer = transfers.find(
    (t) => t.state === "PulledBack" && t.includesPhysicalOriginal && !t.physicalReturnedAt
  ) || null;

  const isInTransit = !isTerminal && (routingState === "InTransit" || Boolean(activeTransfer));
  const isReturnPending = !isTerminal && dak.physicalState === "ReturnPending";
  const isWithHolder = !isTerminal && !isInTransit && !isReturnPending && routingState === "WithHolder";

  const isDeliveryOrRecoveryUnsettled = Boolean(
    routingState === "InTransit" ||
    activeTransfer != null ||
    dak.physicalState === "ReturnPending"
  );
  const canCancel = hasPermission("Dak.Cancel") && !isTerminal && !isDeliveryOrRecoveryUnsettled;
  const canEdit = hasPermission("Dak.Edit") && !isTerminal;
  const isPhysicalUnsettled = dak.physicalState === "InTransit" || dak.physicalState === "ReturnPending";
  const canUpdatePhysicalLocation = Boolean(canEdit && !isTerminal && !isPhysicalUnsettled);
  const canAssignWork = hasPermission("WorkItem.Create") && !isTerminal;

  const isFreshIntake = dak.status === "Registered" && routingState === "Unassigned" && !dak.currentAssignment?.isActive;
  const isDeskOnlyLegacy = routingState === "LegacyUnconfirmed" && Boolean(dak.currentAssignment?.isActive && dak.currentAssignment?.assignedUserId == null);

  // Recipient identification - STRICT ID MATCH ONLY on authoritative activeTransfer
  // pendingReceiverUserId is used for display only; actionable receive strictly requires activeTransfer
  const isCurrentUserRecipient = Boolean(
    !transfersLoading &&
    !transfersError &&
    currentUserId &&
    activeTransfer &&
    activeTransfer.toUserId === currentUserId
  );

  // Sender identification - STRICT ID MATCH ONLY on authoritative activeTransfer
  // Actionable pull back strictly requires activeTransfer
  const isCurrentUserSender = Boolean(
    !transfersLoading &&
    !transfersError &&
    currentUserId &&
    activeTransfer &&
    activeTransfer.senderUserId === currentUserId
  );

  // Confirmed current holder identification - STRICT ID MATCH ONLY
  const isConfirmedCurrentHolder = Boolean(
    currentUserId &&
    routingState === "WithHolder" &&
    dak.currentAssignment?.isActive &&
    (dak.currentAssignment?.isConfirmed === true || dak.currentAssignment?.receivedAt != null) &&
    dak.currentAssignment?.assignedUserId === currentUserId
  );

  // Legacy unconfirmed assignee - STRICT ID MATCH ONLY
  const isLegacyAssignee = Boolean(
    currentUserId &&
    routingState === "LegacyUnconfirmed" &&
    dak.currentAssignment?.isActive &&
    dak.currentAssignment?.assignedUserId === currentUserId
  );

  // Return-pending sender - STRICT ID MATCH ONLY
  const isReturnPendingSender = Boolean(
    !transfersLoading &&
    !transfersError &&
    currentUserId &&
    dak.physicalState === "ReturnPending" &&
    pulledBackPhysicalTransfer &&
    pulledBackPhysicalTransfer.senderUserId === currentUserId
  );

  // Names for display only (never used for permission gating)
  const recipientDisplayName = activeTransfer
    ? (movementDesks.flatMap((d) => d.members).find((m) => m.userId === activeTransfer.toUserId)?.displayName ||
       directoryDesks.find((d) => d.id === activeTransfer.toDeskId)?.name ||
       dak.currentAssignment?.assignedUserDisplayName ||
       "Nominated Recipient")
    : (dak.currentAssignment?.assignedUserDisplayName || "Nominated Recipient");

  const recipientDeskName = activeTransfer
    ? (movementDesks.find((d) => d.id === activeTransfer.toDeskId)?.name ||
       directoryDesks.find((d) => d.id === activeTransfer.toDeskId)?.name ||
       dak.currentAssignment?.deskName ||
       "Target Desk")
    : (dak.currentAssignment?.deskName || "Target Desk");

  const senderDisplayName = activeTransfer
    ? (movementDesks.flatMap((d) => d.members).find((m) => m.userId === activeTransfer.senderUserId)?.displayName ||
       dak.currentAssignment?.assignedByDisplayName ||
       "Sender")
    : (dak.currentAssignment?.assignedByDisplayName || "Sender");

  const handleConfirmCustody = async () => {
    if (!dak) return;
    const targetId = dak.id;
    try {
      const res = await fetch(`/api/dak/${targetId}/custody/confirm`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": getUuid(),
        },
        credentials: "include",
        body: JSON.stringify({ expectedRevision: dak.revision }),
      });
      if (currentDakIdRef.current !== targetId) return;
      if (!res.ok) {
        const d = await res.json().catch(() => null);
        throw new Error(d?.detail || d?.message || "Failed to confirm custody.");
      }
      await loadDakData(targetId);
    } catch (err: unknown) {
      if (currentDakIdRef.current === targetId) {
        alert(err instanceof Error ? err.message : "Error confirming custody.");
      }
    }
  };

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
            <span className={`status-pill status-${dak.status.toLowerCase()}`}>{formatDakStatus(dak.status)}</span>
            {isTerminal && (
              <span className="terminal-badge" title="Historical terminal record; modifications disabled">
                {dak.status === "Cancelled" ? "🔒 Cancelled Record" : "🔒 Resolved Record"}
              </span>
            )}
          </div>
          <h1>{dak.subject}</h1>
          <p className="subtext">
            Received from <strong>{dak.senderName}</strong>
            {dak.senderDepartment ? ` (${dak.senderDepartment})` : ""} on{" "}
            {dak.receivedDate} via {dak.inwardMode}.
            {dak.senderReferenceNumber && <span> • Ref: <strong>{dak.senderReferenceNumber}</strong></span>}
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

      {/* Attention Alert for Long Pending Receipt (>48h configurable) */}
      {!isTerminal && isLongPendingReceipt(dak.currentAssignment?.assignedAt || dak.createdAt) && (
        <div className="hero-attention-alert">
          <span className="alert-icon">⏱️</span>
          <div>
            <strong>Attention: Long Pending Receipt (&gt;{DAK_PENDING_RECEIPT_ALERT_HOURS}h)</strong>
            <div>
              This Dak has been awaiting receipt acknowledgment or desk progression for{" "}
              <strong>{formatElapsedTime(dak.currentAssignment?.assignedAt || dak.createdAt)}</strong>. Expedited officer review recommended.
            </div>
          </div>
        </div>
      )}

      {/* Attention Alert for ReturnPending */}
      {isReturnPending && (
        <div className="hero-attention-alert" style={{ background: "#fef2f2", borderColor: "#fecaca" }}>
          <span className="alert-icon">⚠️</span>
          <div>
            <strong style={{ color: "#991b1b" }}>Physical Paper Recovery Pending</strong>
            <div style={{ color: "#7f1d1d" }}>
              This Dak transfer was pulled back while sending the physical paper original. The paper original must be verified and recovered back to sender custody before new dispatches can be made.
            </div>
          </div>
        </div>
      )}

      {/* Dual Custody Card: Operational Assignment vs Physical Original Location */}
      <div className="dak-custody-card-dual">
        {/* Box A: Current Operational Assignment & Holder */}
        <div className="custody-box operational-box">
          <div className="custody-box-header">
            <span className="custody-box-icon">📋</span>
            <div>
              <span className="custody-box-title">
                {isResolved
                  ? "Historical Custody at Resolution"
                  : isReturnPending
                  ? "Custody Status: Physical Recovery Pending"
                  : isInTransit
                  ? "Current Operational Custody (In Transit)"
                  : "Current Operational Assignment & Holder"}
              </span>
              <span className="custody-box-subtitle">Workflow, Desk & Task Responsibility</span>
            </div>
          </div>

          <div className="custody-box-body">
            {isResolved ? (
              <div className="resolved-custody-card">
                <div style={{ display: "flex", alignItems: "center", gap: "8px", marginBottom: "6px" }}>
                  <span className="badge" style={{ background: "#dcfce7", color: "#166534", fontWeight: 600, padding: "3px 8px", borderRadius: "12px", border: "1px solid #bbf7d0" }}>
                    ✓ Resolved Record
                  </span>
                  <span style={{ fontSize: "12px", color: "#64748b" }}>
                    Settled on {(() => {
                      const dStr = dak.resolvedAt || dak.updatedAt;
                      if (!dStr) return "Record Closure";
                      const d = new Date(dStr);
                      return isNaN(d.getTime()) ? "Record Closure" : d.toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" });
                    })()}
                  </span>
                </div>
                <div className="custody-main-name">
                  {dak.currentAssignment ? `${dak.currentAssignment.deskName} (${dak.currentAssignment.deskCode})` : "Finalized Custody"}
                </div>
                <div className="custody-sub-name" style={{ marginTop: "4px" }}>
                  Last Confirmed Custodian: <strong>{dak.currentAssignment?.assignedUserDisplayName || "Designated Officer"}</strong>
                </div>
                {dak.resolutionRemarks && (
                  <div className="hero-instructions-callout" style={{ marginTop: "10px", borderColor: "#bbf7d0", background: "#f0fdf4" }}>
                    <div className="callout-label" style={{ color: "#166534" }}>Official Resolution Noting:</div>
                    <div className="callout-text" style={{ color: "#14532d" }}>"{dak.resolutionRemarks}"</div>
                  </div>
                )}
              </div>
            ) : isReturnPending ? (
              <div className="return-pending-card">
                <div style={{ display: "flex", alignItems: "center", gap: "8px", marginBottom: "6px" }}>
                  <span className="badge" style={{ background: "#fef2f2", color: "#991b1b", fontWeight: 600, padding: "3px 8px", borderRadius: "12px", border: "1px solid #fecaca" }}>
                    ⚠️ Return Pending
                  </span>
                  <span style={{ fontSize: "12px", color: "#7f1d1d" }}>
                    Physical paper original recovery required
                  </span>
                </div>
                <div className="custody-main-name">
                  {dak.currentAssignment?.deskName || "Originating Desk"}
                </div>
                <div className="custody-sub-name" style={{ marginTop: "4px" }}>
                  Confirmed Holder (Sender): <strong>{senderDisplayName}</strong>
                </div>
                <div className="subtext" style={{ marginTop: "6px", color: "#b91c1c" }}>
                  {pulledBackPhysicalTransfer?.pullBackReason
                    ? `Transfer pulled back: "${pulledBackPhysicalTransfer.pullBackReason}". Physical paper return must be confirmed before further movement.`
                    : "Transfer was pulled back with physical file. File return must be confirmed before further movement."}
                </div>
              </div>
            ) : isInTransit ? (
              <div className="in-transit-custody-card">
                <div className="custody-main-name">
                  {dak.currentAssignment?.deskName || "Dispatching Desk"}
                  <span className="badge badge-warning" style={{ marginLeft: "8px", fontSize: "11px", background: "#fef3c7", color: "#92400e", padding: "2px 8px", borderRadius: "12px", border: "1px solid #fde68a" }}>
                    ⏳ In Transit / Awaiting Receipt
                  </span>
                  {activeTransfer?.includesPhysicalOriginal && (
                    <span className="badge" style={{ marginLeft: "6px", fontSize: "10px", background: "#fee2e2", color: "#991b1b", padding: "2px 6px", borderRadius: "10px", border: "1px solid #fecaca" }}>
                      📁 Physical File In Transit
                    </span>
                  )}
                </div>

                {/* Sender remains last confirmed holder */}
                <div className="custody-sub-name" style={{ marginTop: "6px" }}>
                  Last Confirmed Holder: <strong>{senderDisplayName}</strong>
                </div>

                {/* Pending recipient shown separately */}
                <div className="in-transit-recipient-banner" style={{ marginTop: "10px", padding: "10px 12px", background: "#f8fafc", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                  <div style={{ fontSize: "11px", textTransform: "uppercase", fontWeight: 700, color: "#64748b", letterSpacing: "0.5px" }}>
                    Intended Recipient (Pending Acknowledgment):
                  </div>
                  <div style={{ fontSize: "13px", fontWeight: 600, color: "#0f172a", marginTop: "3px" }}>
                    👤 {recipientDisplayName} <span style={{ fontWeight: 400, color: "#64748b" }}>— {recipientDeskName}</span>
                  </div>
                  <div style={{ fontSize: "12px", color: "#b45309", marginTop: "4px" }}>
                    Dispatched on {new Date(activeTransfer?.sentAt || dak.currentAssignment?.assignedAt || dak.createdAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}
                    {" "}· {formatElapsedTime(activeTransfer?.sentAt || dak.currentAssignment?.assignedAt || dak.createdAt)}
                  </div>
                </div>

                {/* Official Instructions / Remarks from Sender */}
                {(activeTransfer?.remarks || activeTransfer?.instructions || dak.currentAssignment?.instructions) ? (
                  <div className="hero-instructions-callout" style={{ marginTop: "10px" }}>
                    <div className="callout-label">Official Instructions / Remarks from Sender:</div>
                    <div className="callout-text">
                      "{activeTransfer?.instructions || activeTransfer?.remarks || dak.currentAssignment?.instructions}"
                    </div>
                  </div>
                ) : (
                  <div className="hero-instructions-empty" style={{ marginTop: "8px" }}>No specific instructions attached with this dispatch.</div>
                )}
              </div>
            ) : dak.currentAssignment ? (
              <div className="with-holder-custody-card">
                <div className="custody-main-name">
                  {dak.currentAssignment.deskName} ({dak.currentAssignment.deskCode})
                  <span className="badge" style={{ marginLeft: "8px", fontSize: "11px", background: "#f0fdf4", color: "#166534", padding: "2px 8px", borderRadius: "12px", border: "1px solid #bbf7d0" }}>
                    ● Active Custody / In Possession
                  </span>
                </div>
                <div className="custody-sub-name" style={{ marginTop: "4px" }}>
                  Current Officer: <strong>{dak.currentAssignment.assignedUserDisplayName || "General Desk Assignment"}</strong>
                </div>
                <div className="custody-meta" style={{ marginTop: "6px" }}>
                  Marked by <strong>{dak.currentAssignment.assignedByDisplayName}</strong> on{" "}
                  {new Date(dak.currentAssignment.assignedAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}
                  {" "}({formatElapsedTime(dak.currentAssignment.assignedAt)})
                </div>

                {/* Instruction / Noting Callout */}
                {dak.currentAssignment.instructions ? (
                  <div className="hero-instructions-callout" style={{ marginTop: "10px" }}>
                    <div className="callout-label">Official Instructions / Remarks from Sender:</div>
                    <div className="callout-text">"{dak.currentAssignment.instructions}"</div>
                  </div>
                ) : (
                  <div className="hero-instructions-empty" style={{ marginTop: "8px" }}>No specific instructions or remarks attached.</div>
                )}
              </div>
            ) : (
              <div className="unmarked-custody-state">
                <span className="unmarked-badge">⚠️ UNMARKED / Intake Queue</span>
                <span className="subtext" style={{ display: "block", marginTop: "4px" }}>
                  Awaiting initial marking to an office desk or section officer.
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Box B: Physical Original Location */}
        <div className="custody-box physical-box">
          <div className="custody-box-header">
            <span className="custody-box-icon">📁</span>
            <div>
              <span className="custody-box-title">Physical Original Location</span>
              <span className="custody-box-subtitle">Paper Original Document Custody</span>
            </div>
            {canUpdatePhysicalLocation && (
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
            {dak.physicalState === "InTransit" && (
              <div
                className="physical-transit-notice"
                style={{
                  fontSize: "12px",
                  color: "#92400e",
                  background: "#fffbeb",
                  padding: "8px 12px",
                  borderRadius: "6px",
                  marginBottom: "10px",
                  border: "1px solid #fde68a",
                  lineHeight: "1.4"
                }}
              >
                Physical original is in transit. Location will update on acknowledged receipt.
              </div>
            )}
            {dak.physicalState === "ReturnPending" && (
              <div
                className="physical-return-pending-notice"
                style={{
                  fontSize: "12px",
                  color: "#991b1b",
                  background: "#fef2f2",
                  padding: "8px 12px",
                  borderRadius: "6px",
                  marginBottom: "10px",
                  border: "1px solid #fecaca",
                  lineHeight: "1.4"
                }}
              >
                Physical recovery must be confirmed through Confirm Physical Return.
              </div>
            )}
            {physicalOriginal ? (
              <>
                <div className="po-state-row">
                  <span>Paper File Status:</span>
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

      {/* Primary Contextual Actions Bar */}
      {!isTerminal ? (
        <div className="dak-workspace-actions-bar">
          {/* STATE 1: IN TRANSIT */}
          {isInTransit ? (
            <>
              {transfersError ? (
                <span className="transfer-error-notice" style={{ fontSize: "13px", color: "#991b1b", background: "#fef2f2", padding: "6px 12px", borderRadius: "6px", border: "1px solid #fecaca", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                  ⚠️ {transfersError}
                </span>
              ) : transfersLoading ? (
                <span className="transfer-loading-notice" style={{ fontSize: "13px", color: "#64748b", padding: "6px 12px", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                  ⏳ Loading transfer details...
                </span>
              ) : (
                <>
                  {/* Receiver gets RECEIVE */}
                  {isCurrentUserRecipient && canReceive && (
                    <button
                      type="button"
                      className="primary-button"
                      style={{ background: "#059669", borderColor: "#047857" }}
                      onClick={() => setMovementModalMode("receive")}
                    >
                      📥 Receive Dak
                    </button>
                  )}

                  {/* Sender gets PULL BACK until Receive */}
                  {isCurrentUserSender && canPullBack && (
                    <button
                      type="button"
                      className="secondary-button"
                      style={{ color: "#b91c1c", borderColor: "#fca5a5", background: "#fef2f2" }}
                      onClick={() => setMovementModalMode("pull-back")}
                    >
                      ↩ Pull Back Dak
                    </button>
                  )}

                  {/* If third party */}
                  {!isCurrentUserRecipient && !isCurrentUserSender && (
                    <span style={{ fontSize: "13px", color: "#92400e", background: "#fffbeb", padding: "6px 12px", borderRadius: "6px", border: "1px solid #fde68a", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                      ⏳ Awaiting receipt confirmation by <strong>{recipientDisplayName}</strong>
                    </span>
                  )}
                </>
              )}

              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          ) : isReturnPending ? (
            <>
              {transfersError ? (
                <span className="transfer-error-notice" style={{ fontSize: "13px", color: "#991b1b", background: "#fef2f2", padding: "6px 12px", borderRadius: "6px", border: "1px solid #fecaca", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                  ⚠️ {transfersError}
                </span>
              ) : transfersLoading ? (
                <span className="transfer-loading-notice" style={{ fontSize: "13px", color: "#64748b", padding: "6px 12px", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                  ⏳ Loading transfer details...
                </span>
              ) : (
                <>
                  {/* ReturnPending: sender gets Confirm Physical Return */}
                  {isReturnPendingSender && canPullBack && (
                    <button
                      type="button"
                      className="primary-button"
                      style={{ background: "#dc2626", borderColor: "#b91c1c" }}
                      onClick={() => setMovementModalMode("confirm-return")}
                    >
                      📦 Confirm Physical Return
                    </button>
                  )}

                  {!isReturnPendingSender && (
                    <span style={{ fontSize: "13px", color: "#991b1b", background: "#fef2f2", padding: "6px 12px", borderRadius: "6px", border: "1px solid #fecaca", display: "inline-flex", alignItems: "center", gap: "6px" }}>
                      ⚠️ Physical original return awaiting confirmation by sender
                    </span>
                  )}
                </>
              )}

              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          ) : isWithHolder ? (
            <>
              {/* STATE 2: ACTIVE WITH CONFIRMED HOLDER */}
              {isConfirmedCurrentHolder && canMove && (
                <button className="primary-button" onClick={() => setMovementModalMode("send")}>
                  Send / Forward to Next Officer ➔
                </button>
              )}

              {isConfirmedCurrentHolder && canResolve && (
                <button className="secondary-button" onClick={() => setMovementModalMode("resolve")}>
                  ✓ Resolve Dak
                </button>
              )}

              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canAssignWork && (
                <Link
                  to={`/work/new?dakId=${dak.id}`}
                  className="secondary-button"
                >
                  + Assign Work
                </Link>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          ) : (isFreshIntake || isDeskOnlyLegacy) ? (
            <>
              {/* STATE 3: FRESH INTAKE OR DESK-ONLY UNCONFIRMED */}
              {canMark && (
                <button className="primary-button" onClick={() => setMovementModalMode("send")}>
                  Mark to Officer / Desk ➔
                </button>
              )}

              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          ) : (isLegacyAssignee && routingState === "LegacyUnconfirmed") ? (
            <>
              {/* STATE 4: KNOWN LEGACY ASSIGNEE PERSONAL CONFIRMATION */}
              {canReceive && (
                <button
                  type="button"
                  className="primary-button"
                  style={{ background: "#2563eb", borderColor: "#1d4ed8" }}
                  onClick={handleConfirmCustody}
                >
                  Confirm Present-Day Custody
                </button>
              )}

              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          ) : (
            <>
              {canEdit && (
                <button className="secondary-button" onClick={() => setShowEditModal(true)}>
                  Edit Details
                </button>
              )}

              {canCancel && (
                <button className="secondary-button" style={{ color: "#b91c1c", marginLeft: "auto" }} onClick={() => setMovementModalMode("cancel")}>
                  Cancel Entry
                </button>
              )}
            </>
          )}
        </div>
      ) : (
        /* TERMINAL STATE: RESOLVED / CANCELLED */
        <div className="terminal-actions-bar" style={{ display: "flex", gap: "12px", alignItems: "center", padding: "12px 16px", background: "#f8fafc", borderRadius: "8px", border: "1px solid #e2e8f0", marginTop: "16px" }}>
          <div className="terminal-note" style={{ flex: 1 }}>
            <strong style={{ color: "#1e293b" }}>
              {dak.status === "Cancelled" ? "🔒 Dak is Cancelled" : "🔒 Dak is Resolved"}
            </strong>
            <span style={{ display: "block", fontSize: "12px", color: "#64748b", marginTop: "2px" }}>
              {dak.status === "Cancelled"
                ? "This entry was officially cancelled and annulled."
                : "Operational modifications are locked in read-only mode for official record keeping."}
            </span>
          </div>

          {canReopen && dak.status === "Resolved" && dak.recordStatus === "Active" && (
            <button
              type="button"
              className="secondary-button"
              onClick={() => setMovementModalMode("reopen")}
            >
              ↺ Reopen Dak
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
          transferId={movementModalMode === "confirm-return" ? (pulledBackPhysicalTransfer?.id || undefined) : (activeTransfer?.id || undefined)}
          isInitialMark={isFreshIntake || isDeskOnlyLegacy}
          activeTransferIncludesPhysical={activeTransfer?.includesPhysicalOriginal === true}
          physicalSendAvailable={physicalOriginal?.hasPhysicalOriginal === true && physicalOriginal.userId === user?.id
            && !!physicalOriginal.deskId && !!user?.desks.some((d) => d.id === physicalOriginal.deskId)
            && dak.physicalState !== "InTransit" && dak.physicalState !== "ReturnPending"}
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
