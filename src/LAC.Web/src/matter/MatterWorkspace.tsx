// Matter Workspace V2 / Operational Hub Component

import React, { useState, useEffect, useRef } from "react";
import { useParams, Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { MatterDrafts } from "../editor/MatterDraftEditor";
import {
  IconLock,
  IconMoreVertical,
  IconUpload,
  IconLink,
  IconDownload,
  IconClose,
  IconEdit,
  IconArchive,
  IconPlus,
  IconFileText,
  IconFile,
  IconBuilding,
  IconArrowRight,
  IconHistory,
  IconSearch,
  IconFilter
} from "../components/Icons";
import "./matter.css";

interface MatterDocumentExtractProvenance {
  sourceDocumentId: string;
  sourceFileName: string;
  sourceSha256Hash: string | null;
  normalizedSourcePagesText: string;
  itemNumber: string | null;
  khasraReferenceText: string | null;
  contextLabel: string | null;
  extractedAt: string;
  extractedByUserName: string;
}

interface MatterDocumentItem {
  id: string;
  documentId: string;
  documentRole: string | null;
  displayName: string | null;
  originalFileName: string;
  mimeType: string;
  fileSize: number;
  uploadedAt: string;
  extractProvenance?: MatterDocumentExtractProvenance | null;
}

interface EligibleDocumentItem {
  id: string;
  originalFileName: string;
  documentType: string;
  uploadedAt: string;
  source: string;
  isAlreadyLinked?: boolean;
}

interface MatterEventItem {
  id: string;
  sequenceNumber: number;
  action: number;
  actionName: string;
  workstreamId: string | null;
  workstreamName: string | null;
  targetWorkstreamId: string | null;
  targetWorkstreamName: string | null;
  reason: string | null;
  actionAt: string;
  actionByUserId: string;
  actionByUserName: string;
  contextEntityType?: string | null;
  contextEntityId?: string | null;
}

interface MatterDetail {
  id: string;
  villageId: string;
  villageName: string;
  workstreamId: string | null;
  workstreamName: string | null;
  workstreamCode: string | null;
  title: string;
  matterType: string;
  status: string;
  referenceNumber: string | null;
  remarks: string | null;
  khasraReferenceText: string | null;
  revision: number;
  createdAt: string;
  updatedAt: string;
}

interface MatterContextVillage {
  villageId: string;
  name: string;
}

interface MatterContextAward {
  awardId: string;
  awardNumber: string;
  awardDate?: string | null;
  isPrimary: boolean;
}

interface MatterContextKhasra {
  khasraId: string;
  villageId: string;
  displayNumber: string;
}

interface MatterContextCourtCase {
  courtCaseId: string;
  caseNumber: string;
  caseTitle: string;
  courtName: string;
  currentStatus: string;
  operationalNdoh?: string | null;
}

interface MatterContextDak {
  dakId: string;
  diaryNumber: string;
  subject: string;
  status: string;
  receivedDate?: string | null;
}

interface MatterContextWorkItem {
  workItemId: string;
  title: string;
  status: string;
  priority: string;
  dueAt?: string | null;
  responsibleDesk?: { deskId: string; name: string } | null;
  assignedUser?: { userId: string; name: string } | null;
}

interface MatterContextData {
  matter: MatterDetail;
  village: MatterContextVillage | null;
  awards: MatterContextAward[];
  khasras: MatterContextKhasra[];
  courtCases: MatterContextCourtCase[];
  courtContextState: string;
  daks: MatterContextDak[];
  workItems: MatterContextWorkItem[];
  outwards: any[];
  outwardCount: number;
  documentCount: number;
  draftCount: number;
}

interface WorkstreamOption {
  id: string;
  name: string;
  code: string;
}

type WorkspaceTab = "overview" | "work" | "documents" | "drafts" | "outward" | "activity";

export const MatterWorkspace: React.FC<{ MatterOutwardSection: React.ComponentType<{ matterId: string }> }> = ({
  MatterOutwardSection
}) => {
  const { id = "" } = useParams();
  const { hasPermission } = useAuth();

  const [contextData, setContextData] = useState<MatterContextData | null>(null);
  const [documents, setDocuments] = useState<MatterDocumentItem[]>([]);
  const [events, setEvents] = useState<MatterEventItem[]>([]);
  const [eligibleDocs, setEligibleDocs] = useState<EligibleDocumentItem[]>([]);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [refresh, setRefresh] = useState(0);

  // Tab State
  const [activeTab, setActiveTab] = useState<WorkspaceTab>("overview");

  // Overflow Menu State
  const [showOverflow, setShowOverflow] = useState(false);
  const overflowRef = useRef<HTMLDivElement>(null);

  // Modals & Drawers
  const [showEditModal, setShowEditModal] = useState(false);
  const [showReclassifyModal, setShowReclassifyModal] = useState(false);
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showLinkDrawer, setShowLinkDrawer] = useState(false);
  const [showManageLinksDrawer, setShowManageLinksDrawer] = useState(false);

  // Manage Links State
  const [manageLinksTab, setManageLinksTab] = useState<"awards" | "khasras" | "court" | "dak">("awards");
  const [villageAwardsList, setVillageAwardsList] = useState<any[]>([]);
  const [villageKhasrasList, setVillageKhasrasList] = useState<any[]>([]);
  const [courtSearchQuery, setCourtSearchQuery] = useState("");
  const [courtSearchResults, setCourtSearchResults] = useState<any[]>([]);
  const [dakSearchQuery, setDakSearchQuery] = useState("");
  const [dakSearchResults, setDakSearchResults] = useState<any[]>([]);
  const [linkingRecord, setLinkingRecord] = useState(false);
  const [manageLinkError, setManageLinkError] = useState<string | null>(null);

  // Edit Form State (Operational Metadata Only)
  const [editTitle, setEditTitle] = useState("");
  const [editType, setEditType] = useState("Court Case");
  const [editCustomType, setEditCustomType] = useState("");
  const [editRefNo, setEditRefNo] = useState("");
  const [editRemarks, setEditRemarks] = useState("");
  const [editKhasraRef, setEditKhasraRef] = useState("");
  const [editStatus, setEditStatus] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [savingEdit, setSavingEdit] = useState(false);

  // Document Edit & Delete State
  const [editingDoc, setEditingDoc] = useState<MatterDocumentItem | null>(null);
  const [editDocRole, setEditDocRole] = useState("");
  const [editDocDisplayName, setEditDocDisplayName] = useState("");
  const [savingDocEdit, setSavingDocEdit] = useState(false);
  const [docEditError, setDocEditError] = useState<string | null>(null);
  const [deletingDocId, setDeletingDocId] = useState<string | null>(null);

  // Reclassify Form State
  const [reclassifyTargetId, setReclassifyTargetId] = useState("");
  const [reclassifyReason, setReclassifyReason] = useState("");
  const [reclassifyError, setReclassifyError] = useState<string | null>(null);
  const [savingReclassify, setSavingReclassify] = useState(false);

  // Upload Form State
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadRole, setUploadRole] = useState("Application");
  const [uploadDisplayName, setUploadDisplayName] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  // Linking Form State
  const [linkingDocId, setLinkingDocId] = useState<string | null>(null);
  const [linkRole, setLinkRole] = useState("Other");
  const [linkError, setLinkError] = useState<string | null>(null);

  // Drawer Mode: "link" vs "extract"
  const [drawerMode, setDrawerMode] = useState<"link" | "extract">("link");

  // Page Extractor Form State
  const [extractSourceDocId, setExtractSourceDocId] = useState("");
  const [extractPagesText, setExtractPagesText] = useState("");
  const [extractRole, setExtractRole] = useState("Naqsha Mutzamin");
  const [extractItemNumber, setExtractItemNumber] = useState("");
  const [extractKhasraRef, setExtractKhasraRef] = useState("");
  const [extractContextLabel, setExtractContextLabel] = useState("");
  const [extractDisplayName, setExtractDisplayName] = useState("");
  const [extracting, setExtracting] = useState(false);
  const [extractError, setExtractError] = useState<string | null>(null);

  // Close overflow menu on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (overflowRef.current && !overflowRef.current.contains(e.target as Node)) {
        setShowOverflow(false);
      }
    };
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  // Load Matter Context, Documents & Audit Events
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);

    Promise.all([
      fetch(`/api/matters/${id}/context?r=${refresh}`, { credentials: "include" }),
      fetch(`/api/matters/${id}/documents?r=${refresh}`, { credentials: "include" }),
      fetch(`/api/matters/${id}/events?r=${refresh}`, { credentials: "include" })
    ])
      .then(async ([resContext, resDocs, resEvents]) => {
        if (!resContext.ok) {
          if (resContext.status === 403) throw new Error("Access denied: You do not have permission to view this matter.");
          if (resContext.status === 404) throw new Error("Matter not found.");
          throw new Error("Failed to load matter context.");
        }
        const ctx: MatterContextData = await resContext.json();
        let docsData: MatterDocumentItem[] = [];
        if (resDocs.ok) docsData = await resDocs.json();

        let eventsData: MatterEventItem[] = [];
        if (resEvents.ok) {
          const evJson = await resEvents.json();
          eventsData = Array.isArray(evJson) ? evJson : evJson?.items || [];
        }

        if (active) {
          setContextData(ctx);
          setDocuments(docsData);
          setEvents(eventsData);

          const m = ctx.matter;
          setEditTitle(m.title);
          setEditStatus(m.status || "Open");
          setEditRefNo(m.referenceNumber || "");
          setEditRemarks(m.remarks || "");
          setEditKhasraRef(m.khasraReferenceText || "");
          const isStandardType = ["Court Case", "Compensation", "Land Acquisition", "Demarcation", "Possession"].includes(m.matterType);
          setEditType(isStandardType ? m.matterType : "Other");
          setEditCustomType(isStandardType ? "" : m.matterType);
        }
      })
      .catch((err) => {
        if (active) setError(err.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [id, refresh]);

  // Load Workstreams lookup
  useEffect(() => {
    fetch("/api/matters/context", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (data?.workstreams) {
          setWorkstreams(data.workstreams);
          if (data.workstreams.length > 0 && !reclassifyTargetId) {
            setReclassifyTargetId(data.workstreams[0].id);
          }
        }
      })
      .catch(() => {});
  }, [reclassifyTargetId]);

  // Load Eligible Documents when drawer opened
  useEffect(() => {
    if (!showLinkDrawer) return;
    fetch(`/api/matters/${id}/eligible-documents?r=${refresh}`, { credentials: "include" })
      .then((res) => (res.ok ? res.json() : []))
      .then((data) => setEligibleDocs(data))
      .catch(() => setEligibleDocs([]));
  }, [id, showLinkDrawer, refresh]);

  // Load Village Awards & Khasras when Manage Links drawer is opened
  useEffect(() => {
    if (!showManageLinksDrawer || !contextData?.village?.villageId) return;
    const vId = contextData.village.villageId;

    // Fetch Awards
    fetch(`/api/villages/${vId}/core-records`, { credentials: "include" })
      .then((r) => (r.ok ? r.json() : []))
      .then((d) => setVillageAwardsList(Array.isArray(d) ? d : []))
      .catch(() => setVillageAwardsList([]));

    // Fetch Khasras
    fetch(`/api/villages/${vId}/khasras?pageSize=250`, { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then((d) => setVillageKhasrasList(d?.items || (Array.isArray(d) ? d : [])))
      .catch(() => setVillageKhasrasList([]));
  }, [showManageLinksDrawer, contextData?.village?.villageId]);

  // Search Court Cases for Linking
  const handleSearchCourtCases = async (q: string) => {
    setCourtSearchQuery(q);
    if (!q.trim()) {
      setCourtSearchResults([]);
      return;
    }
    try {
      const res = await fetch(`/api/court-cases?search=${encodeURIComponent(q.trim())}&pageSize=10`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setCourtSearchResults(data.items || (Array.isArray(data) ? data : []));
      }
    } catch {
      setCourtSearchResults([]);
    }
  };

  // Search Dak for Linking
  const handleSearchDak = async (q: string) => {
    setDakSearchQuery(q);
    if (!q.trim()) {
      setDakSearchResults([]);
      return;
    }
    try {
      const res = await fetch(`/api/dak?q=${encodeURIComponent(q.trim())}&pageSize=10`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setDakSearchResults(data.items || (Array.isArray(data) ? data : []));
      }
    } catch {
      setDakSearchResults([]);
    }
  };

  // Generic Link Mutation Helper with Revision Conflict Handling (409)
  const executeLinkMutation = async (
    segment: "awards" | "khasras" | "court-cases" | "daks",
    targetId: string,
    method: "PUT" | "DELETE",
    isPrimary?: boolean
  ) => {
    if (!contextData) return;
    const rev = contextData.matter.revision;
    setLinkingRecord(true);
    setManageLinkError(null);

    try {
      let url = `/api/matters/${id}/${segment}/${targetId}`;
      let body: any = null;

      if (method === "PUT") {
        body = JSON.stringify({ expectedRevision: rev, ...(isPrimary !== undefined ? { isPrimary } : {}) });
      } else {
        url += `?expectedRevision=${rev}`;
      }

      const res = await fetch(url, {
        method,
        headers: method === "PUT" ? { "Content-Type": "application/json" } : undefined,
        credentials: "include",
        body: method === "PUT" ? body : undefined
      });

      if (!res.ok) {
        if (res.status === 409) {
          setManageLinkError("Conflict: The matter was updated by another process. Refreshing latest data...");
          setRefresh((r) => r + 1);
          return;
        }
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || `Failed to ${method === "PUT" ? "link" : "unlink"} record.`);
      }

      setRefresh((r) => r + 1);
    } catch (err: any) {
      setManageLinkError(err.message || "Operation failed.");
    } finally {
      setLinkingRecord(false);
    }
  };

  const toggleSelectDoc = (docId: string) => {
    setSelectedDocIds((prev) => (prev.includes(docId) ? prev.filter((x) => x !== docId) : [...prev, docId]));
  };

  // Operational Metadata Update (PUT /api/matters/{id})
  const handleUpdateMetadata = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!contextData) return;

    const finalTitle = editTitle.trim();
    if (!finalTitle) {
      setEditError("Title is required.");
      return;
    }

    const resolvedType = editType === "Other" ? (editCustomType.trim() || "Other") : editType;

    try {
      setSavingEdit(true);
      setEditError(null);
      const res = await fetch(`/api/matters/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          title: finalTitle,
          matterType: resolvedType,
          status: editStatus.trim() || null,
          referenceNumber: editRefNo.trim() || null,
          remarks: editRemarks.trim() || null,
          khasraReferenceText: editKhasraRef.trim() || null,
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        if (res.status === 409) {
          setEditError("Conflict: Matter was updated by another process. Refreshing...");
          setRefresh((r) => r + 1);
          return;
        }
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to update matter details.");
      }

      setShowEditModal(false);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setEditError(err.message || "Failed to update matter.");
    } finally {
      setSavingEdit(false);
    }
  };

  const handleEditDocumentSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingDoc || !contextData) return;

    try {
      setSavingDocEdit(true);
      setDocEditError(null);
      const res = await fetch(`/api/matters/${id}/documents/${editingDoc.documentId}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          role: editDocRole.trim() || null,
          displayName: editDocDisplayName.trim() || null,
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || "Failed to update document details.");
      }

      setEditingDoc(null);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setDocEditError(err.message || "Failed to update document.");
    } finally {
      setSavingDocEdit(false);
    }
  };

  const handleRemoveDocument = async (docId: string) => {
    if (!contextData) return;
    if (!window.confirm("Are you sure you want to remove/unlink this document from the matter?")) return;

    try {
      setDeletingDocId(docId);
      const res = await fetch(`/api/matters/${id}/documents/${docId}/unlink`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        alert(errData?.message || "Failed to remove document.");
        return;
      }

      setRefresh((r) => r + 1);
    } catch {
      alert("Failed to remove document.");
    } finally {
      setDeletingDocId(null);
    }
  };

  const handleReclassify = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!contextData) return;
    if (!reclassifyTargetId) {
      setReclassifyError("Target workstream is required.");
      return;
    }
    if (!reclassifyReason.trim()) {
      setReclassifyError("Reclassification reason is mandatory.");
      return;
    }

    try {
      setSavingReclassify(true);
      setReclassifyError(null);
      const res = await fetch(`/api/matters/${id}/reclassify`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          targetWorkstreamId: reclassifyTargetId,
          reason: reclassifyReason.trim(),
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => null);
        throw new Error(errorData?.message || errorData?.title || "Failed to reclassify matter.");
      }

      setShowReclassifyModal(false);
      setReclassifyReason("");
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setReclassifyError(err.message || "Failed to reclassify.");
    } finally {
      setSavingReclassify(false);
    }
  };

  const handleUploadDocument = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!contextData || !uploadFile) return;

    try {
      setUploading(true);
      setUploadError(null);
      const form = new FormData();
      form.append("file", uploadFile);
      form.append("role", uploadRole);
      if (uploadDisplayName.trim()) form.append("displayName", uploadDisplayName.trim());
      form.append("expectedRevision", contextData.matter.revision.toString());

      const res = await fetch(`/api/matters/${id}/documents`, {
        method: "POST",
        credentials: "include",
        body: form
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || "Failed to upload document.");
      }

      setUploadFile(null);
      setUploadDisplayName("");
      setShowUploadModal(false);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setUploadError(err.message || "Document upload failed.");
    } finally {
      setUploading(false);
    }
  };

  const handleLinkExisting = async (docId: string) => {
    if (!contextData) return;

    try {
      setLinkingDocId(docId);
      setLinkError(null);
      const res = await fetch(`/api/matters/${id}/documents/link`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          documentId: docId,
          role: linkRole,
          displayName: null,
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || "Failed to link document.");
      }

      setShowLinkDrawer(false);
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setLinkError(err.message || "Failed to link document.");
    } finally {
      setLinkingDocId(null);
    }
  };

  const handleExtractPagesSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!contextData || !extractSourceDocId || !extractPagesText.trim()) return;

    try {
      setExtracting(true);
      setExtractError(null);

      const res = await fetch(`/api/matters/${id}/documents/extract-pages`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          sourceDocumentId: extractSourceDocId,
          pageRangeText: extractPagesText.trim(),
          role: extractRole,
          itemNumber: extractItemNumber.trim() || null,
          khasraReferenceText: extractKhasraRef.trim() || null,
          contextLabel: extractContextLabel.trim() || null,
          displayName: extractDisplayName.trim() || null,
          expectedRevision: contextData.matter.revision
        })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        throw new Error(errData?.message || errData?.title || "Failed to extract PDF pages.");
      }

      setShowLinkDrawer(false);
      setExtractSourceDocId("");
      setExtractPagesText("");
      setExtractItemNumber("");
      setExtractKhasraRef("");
      setExtractContextLabel("");
      setExtractDisplayName("");
      setRefresh((r) => r + 1);
    } catch (err: any) {
      setExtractError(err.message || "Failed to extract pages.");
    } finally {
      setExtracting(false);
    }
  };

  const handleExportZip = async () => {
    if (!contextData || selectedDocIds.length === 0) return;
    try {
      const res = await fetch(`/api/matters/${id}/export`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({ documentIds: selectedDocIds })
      });

      if (!res.ok) {
        const errData = await res.json().catch(() => null);
        alert(errData?.message || "Failed to export selected documents.");
        return;
      }

      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `matter-${id.slice(0, 8)}-documents.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      alert("Failed to export documents.");
    }
  };

  if (loading) return <div className="state loading">Loading matter operational workspace...</div>;
  if (error || !contextData) return <div className="state error">{error || "Matter context not found."}</div>;

  const matter = contextData.matter;
  const isArchived = matter.status === "Archived";
  const canAssignWork = !isArchived && hasPermission("WorkItem.Create");
  const canEdit = !isArchived && hasPermission("Matter.Edit");
  const canManageDocs = !isArchived && hasPermission("Matter.Document.Manage");

  const recentEvents = events.slice(0, 5);

  return (
    <div className="matter-workspace-shell">
      {/* Top Identity Block */}
      <div className="matter-identity-card">
        <nav className="matter-breadcrumbs" aria-label="Breadcrumb">
          <Link to="/matters">Matters</Link>
          <span className="matter-breadcrumb-sep">/</span>
          {contextData.village ? (
            <Link to={`/villages/${contextData.village.villageId}`}>{contextData.village.name}</Link>
          ) : (
            <span>Unknown Village</span>
          )}
          <span className="matter-breadcrumb-sep">/</span>
          <span style={{ color: "#0f172a", fontWeight: 600 }}>{matter.title}</span>
        </nav>

        <div className="matter-header-main">
          <div className="matter-header-title-block">
            <h1>{matter.title}</h1>
            <div className="matter-header-meta-row">
              <span className="matter-meta-item">
                <IconBuilding size={14} style={{ color: "#64748b" }} />
                {contextData.village ? (
                  <Link to={`/villages/${contextData.village.villageId}`} style={{ color: "#0369a1", fontWeight: 600, textDecoration: "none" }}>
                    {contextData.village.name}
                  </Link>
                ) : (
                  <span>Unlinked Village</span>
                )}
              </span>
              <span>·</span>
              <span className="matter-meta-item">Type: <strong>{matter.matterType}</strong></span>
              <span>·</span>
              <span className="matter-meta-item">
                Ref: <strong style={{ fontFamily: "monospace" }}>{matter.referenceNumber || "—"}</strong>
              </span>
              <span>·</span>
              <span className="matter-meta-item">
                Workstream: <strong>{matter.workstreamName || "[Unclassified]"}</strong>
              </span>
              <span>·</span>
              <span className={isArchived ? "matter-badge-archived" : "matter-badge-open"}>
                {matter.status}
              </span>
              <span className="matter-revision-tag">Rev {matter.revision}</span>
            </div>
          </div>
        </div>

        {/* Action Controls Toolbar */}
        {!isArchived && (
          <div className="matter-actions-toolbar">
            {canAssignWork && (
              <Link to={`/work/new?matterId=${matter.id}`} className="btn-action-primary">
                <IconPlus size={15} />
                + Assign Work
              </Link>
            )}

            {canEdit && (
              <button
                type="button"
                className="btn-action-secondary"
                onClick={() => {
                  setManageLinkError(null);
                  setShowManageLinksDrawer(true);
                }}
              >
                <IconLink size={14} />
                Manage Linked Records
              </button>
            )}

            {canEdit && (
              <button
                type="button"
                className="btn-action-secondary"
                onClick={() => {
                  setEditTitle(matter.title);
                  setEditStatus(matter.status || "Open");
                  setEditRefNo(matter.referenceNumber || "");
                  setEditRemarks(matter.remarks || "");
                  setEditKhasraRef(matter.khasraReferenceText || "");
                  setEditError(null);
                  setShowEditModal(true);
                }}
              >
                <IconEdit size={14} />
                Edit Metadata
              </button>
            )}

            {canManageDocs && (
              <>
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => {
                    setUploadError(null);
                    setShowUploadModal(true);
                  }}
                >
                  <IconUpload size={14} />
                  + Upload Document
                </button>

                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => {
                    setLinkError(null);
                    setShowLinkDrawer(true);
                  }}
                >
                  <IconFileText size={14} />
                  Extract / Link Doc
                </button>
              </>
            )}

            {/* Overflow Menu button */}
            {canEdit && (
              <div className="matter-overflow-wrap" ref={overflowRef}>
                <button
                  type="button"
                  className="matter-overflow-btn"
                  onClick={() => setShowOverflow((v) => !v)}
                  title="More actions"
                >
                  <IconMoreVertical size={16} />
                </button>

                {showOverflow && (
                  <div className="matter-overflow-menu">
                    <button
                      type="button"
                      className="matter-overflow-item"
                      onClick={() => {
                        setShowOverflow(false);
                        setReclassifyError(null);
                        setShowReclassifyModal(true);
                      }}
                    >
                      <IconBuilding size={14} />
                      Reclassify Workstream
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Internal Workspace Tabs Bar */}
      <div>
        <nav className="matter-tabs-nav" aria-label="Matter Work Surfaces">
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "overview" ? "active" : ""}`}
            onClick={() => setActiveTab("overview")}
          >
            Overview
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "work" ? "active" : ""}`}
            onClick={() => setActiveTab("work")}
          >
            Work
            <span className="matter-tab-count">{contextData.workItems.length}</span>
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "documents" ? "active" : ""}`}
            onClick={() => setActiveTab("documents")}
          >
            Documents
            <span className="matter-tab-count">{contextData.documentCount || documents.length}</span>
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "drafts" ? "active" : ""}`}
            onClick={() => setActiveTab("drafts")}
          >
            Drafts
            <span className="matter-tab-count">{contextData.draftCount}</span>
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "outward" ? "active" : ""}`}
            onClick={() => setActiveTab("outward")}
          >
            Outward
            <span className="matter-tab-count">{contextData.outwardCount}</span>
          </button>
          <button
            type="button"
            className={`matter-tab-btn ${activeTab === "activity" ? "active" : ""}`}
            onClick={() => setActiveTab("activity")}
          >
            Activity
            <span className="matter-tab-count">{events.length}</span>
          </button>
        </nav>

        {/* Tab Body Surfaces */}
        <div className="matter-tab-body">
          {/* TAB 1: OVERVIEW */}
          {activeTab === "overview" && (
            <div className="matter-overview-grid">
              {/* Left Column: Canonical Context Cards */}
              <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
                {/* 1. Canonical Village & Awards Context Card */}
                <div className="matter-card-panel">
                  <h3>
                    <span>Canonical Acquisition Context</span>
                    {canEdit && (
                      <button
                        type="button"
                        className="btn-action-secondary"
                        style={{ padding: "2px 8px", fontSize: "11px" }}
                        onClick={() => {
                          setManageLinksTab("awards");
                          setManageLinkError(null);
                          setShowManageLinksDrawer(true);
                        }}
                      >
                        Manage Links
                      </button>
                    )}
                  </h3>

                  <div className="matter-facts-grid" style={{ marginBottom: 14 }}>
                    <div className="matter-fact-item">
                      <span className="matter-fact-label">Primary Village</span>
                      <span className="matter-fact-value">
                        {contextData.village ? (
                          <Link to={`/villages/${contextData.village.villageId}`} style={{ color: "#0284c7", fontWeight: 600, textDecoration: "none" }}>
                            {contextData.village.name}
                          </Link>
                        ) : (
                          <span style={{ color: "#94a3b8" }}>No Village set</span>
                        )}
                      </span>
                    </div>

                    <div className="matter-fact-item">
                      <span className="matter-fact-label">Linked Awards ({contextData.awards.length})</span>
                      <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginTop: 2 }}>
                        {contextData.awards.length === 0 ? (
                          <span style={{ fontSize: "12px", color: "#94a3b8" }}>No Awards linked</span>
                        ) : (
                          contextData.awards.map((a) => (
                            <Link
                              key={a.awardId}
                              to={`/awards/${a.awardId}`}
                              style={{
                                display: "inline-flex",
                                alignItems: "center",
                                gap: 4,
                                background: a.isPrimary ? "#fef3c7" : "#f1f5f9",
                                border: a.isPrimary ? "1px solid #fde68a" : "1px solid #cbd5e1",
                                color: a.isPrimary ? "#92400e" : "#334155",
                                padding: "2px 8px",
                                borderRadius: "4px",
                                fontSize: "12px",
                                fontWeight: 600,
                                textDecoration: "none"
                              }}
                            >
                              {a.isPrimary && <span title="Primary Award">★</span>}
                              Award {a.awardNumber}
                              {a.isPrimary && <span style={{ fontSize: "10px", textTransform: "uppercase" }}>(Primary)</span>}
                            </Link>
                          ))
                        )}
                      </div>
                    </div>
                  </div>

                  {/* Khasra Canonical Chips & Legacy Reference Text */}
                  <div>
                    <span className="matter-fact-label" style={{ display: "block", marginBottom: 6 }}>
                      Canonical Khasras ({contextData.khasras.length})
                    </span>
                    {contextData.khasras.length === 0 ? (
                      <div style={{ fontSize: "12px", color: "#94a3b8", marginBottom: 6 }}>
                        No canonical Khasras linked.
                      </div>
                    ) : (
                      <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 8 }}>
                        {contextData.khasras.map((k) => (
                          <span
                            key={k.khasraId}
                            style={{
                              background: "#e0f2fe",
                              color: "#0369a1",
                              border: "1px solid #bae6fd",
                              padding: "2px 8px",
                              borderRadius: "4px",
                              fontSize: "12px",
                              fontWeight: 600
                            }}
                          >
                            Khasra #{k.displayNumber}
                          </span>
                        ))}
                      </div>
                    )}

                    {matter.khasraReferenceText && (
                      <div style={{ background: "#f8fafc", border: "1px dashed #cbd5e1", padding: "6px 10px", borderRadius: "6px", fontSize: "12px", color: "#475569", marginTop: 4 }}>
                        <strong>Legacy text reference:</strong> {matter.khasraReferenceText}
                        <span style={{ fontSize: "11px", color: "#94a3b8", display: "block" }}>
                          (Unverified text string from legacy entry)
                        </span>
                      </div>
                    )}
                  </div>
                </div>

                {/* 2. Court Case Context Panel */}
                <div className="matter-card-panel">
                  <h3>
                    <span>Court Case Context</span>
                    {canEdit && (
                      <button
                        type="button"
                        className="btn-action-secondary"
                        style={{ padding: "2px 8px", fontSize: "11px" }}
                        onClick={() => {
                          setManageLinksTab("court");
                          setManageLinkError(null);
                          setShowManageLinksDrawer(true);
                        }}
                      >
                        Manage Court Link
                      </button>
                    )}
                  </h3>

                  {contextData.courtContextState !== "Linked" || contextData.courtCases.length === 0 ? (
                    <div style={{ background: "#f8fafc", border: "1px dashed #cbd5e1", borderRadius: "6px", padding: "16px", textAlgin: "center" as any, display: "flex", flexDirection: "column", alignItems: "center", gap: 8 }}>
                      <span style={{ fontSize: "13px", fontWeight: 600, color: "#64748b" }}>
                        Court case not linked
                      </span>
                      <span style={{ fontSize: "11px", color: "#94a3b8", textAlign: "center" }}>
                        This operational matter has no active link to an authoritative Court Case record.
                      </span>
                      {canEdit && (
                        <button
                          type="button"
                          className="btn-action-primary"
                          style={{ padding: "4px 10px", fontSize: "12px", marginTop: 4 }}
                          onClick={() => {
                            setManageLinksTab("court");
                            setManageLinkError(null);
                            setShowManageLinksDrawer(true);
                          }}
                        >
                          + Link Court Case
                        </button>
                      )}
                    </div>
                  ) : (
                    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
                      {contextData.courtCases.map((c) => (
                        <div
                          key={c.courtCaseId}
                          style={{
                            background: "#f0f9ff",
                            border: "1px solid #bae6fd",
                            borderRadius: "6px",
                            padding: "12px"
                          }}
                        >
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                            <div>
                              <Link
                                to={`/court-cases/${c.courtCaseId}`}
                                style={{ fontSize: "14px", fontWeight: 750, color: "#0369a1", textDecoration: "none" }}
                              >
                                {c.caseNumber}
                              </Link>
                              <div style={{ fontSize: "13px", fontWeight: 600, color: "#0f172a", marginTop: 2 }}>
                                {c.caseTitle}
                              </div>
                              <div style={{ fontSize: "11px", color: "#475569", marginTop: 2 }}>
                                {c.courtName}
                              </div>
                            </div>
                            <span className="matter-badge-open" style={{ fontSize: "11px" }}>
                              {c.currentStatus}
                            </span>
                          </div>

                          <div style={{ display: "flex", gap: 12, marginTop: 10, paddingTop: 8, borderTop: "1px dashed #bae6fd", fontSize: "12px" }}>
                            <div>
                              <span style={{ color: "#64748b", fontSize: "11px", display: "block" }}>
                                Authoritative Next Date (NDOH)
                              </span>
                              <strong style={{ color: "#0369a1" }}>
                                {c.operationalNdoh ? new Date(c.operationalNdoh).toLocaleDateString() : "No NDOH scheduled"}
                              </strong>
                            </div>
                          </div>
                        </div>
                      ))}
                      <span style={{ fontSize: "11px", color: "#64748b", fontStyle: "italic" }}>
                        Court status & NDOH are authoritative from the Court module and read-only in Matter.
                      </span>
                    </div>
                  )}
                </div>

                {/* 3. Dak Items Context Panel */}
                <div className="matter-card-panel">
                  <h3>
                    <span>Linked Dak Communications ({contextData.daks.length})</span>
                    {canEdit && (
                      <button
                        type="button"
                        className="btn-action-secondary"
                        style={{ padding: "2px 8px", fontSize: "11px" }}
                        onClick={() => {
                          setManageLinksTab("dak");
                          setManageLinkError(null);
                          setShowManageLinksDrawer(true);
                        }}
                      >
                        Manage Dak Links
                      </button>
                    )}
                  </h3>

                  {contextData.daks.length === 0 ? (
                    <div style={{ fontSize: "12px", color: "#94a3b8" }}>No Dak receipts or communications linked.</div>
                  ) : (
                    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                      {contextData.daks.map((d) => (
                        <div
                          key={d.dakId}
                          style={{
                            padding: "8px 10px",
                            background: "#f8fafc",
                            border: "1px solid #e2e8f0",
                            borderRadius: "6px",
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center"
                          }}
                        >
                          <div>
                            <div style={{ fontSize: "12px", fontWeight: 700, color: "#0f172a" }}>
                              Diary #{d.diaryNumber}
                            </div>
                            <div style={{ fontSize: "12px", color: "#475569" }}>{d.subject}</div>
                            {d.receivedDate && (
                              <div style={{ fontSize: "11px", color: "#64748b" }}>
                                Received: {new Date(d.receivedDate).toLocaleDateString()}
                              </div>
                            )}
                          </div>
                          <span className="matter-badge-workstream" style={{ fontSize: "11px" }}>
                            {d.status}
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {/* Right Column: Work Items Preview & Remarks */}
              <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
                {/* Active Work Items Preview Card */}
                <div className="matter-card-panel">
                  <h3>
                    <span>Active Work Items ({contextData.workItems.length})</span>
                    {canAssignWork && (
                      <Link to={`/work/new?matterId=${matter.id}`} className="btn-action-primary" style={{ padding: "2px 8px", fontSize: "11px" }}>
                        + Assign Work
                      </Link>
                    )}
                  </h3>

                  {contextData.workItems.length === 0 ? (
                    <div style={{ fontSize: "12px", color: "#94a3b8" }}>No active work items assigned for this matter.</div>
                  ) : (
                    <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                      {contextData.workItems.map((w) => (
                        <div
                          key={w.workItemId}
                          style={{
                            padding: "10px 12px",
                            background: "#f8fafc",
                            border: "1px solid #e2e8f0",
                            borderRadius: "6px"
                          }}
                        >
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                            <div style={{ fontWeight: 650, fontSize: "13px", color: "#0f172a" }}>
                              {w.title}
                            </div>
                            <span
                              style={{
                                padding: "2px 6px",
                                borderRadius: "4px",
                                fontSize: "11px",
                                fontWeight: 700,
                                background: w.priority === "Urgent" ? "#fef2f2" : "#f1f5f9",
                                color: w.priority === "Urgent" ? "#b91c1c" : "#475569"
                              }}
                            >
                              {w.priority}
                            </span>
                          </div>

                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 6, fontSize: "11px", color: "#64748b" }}>
                            <span>
                              Desk: <strong>{w.responsibleDesk?.name || "Unassigned"}</strong>
                              {w.assignedUser ? ` (${w.assignedUser.name})` : ""}
                            </span>
                            <span>Status: <strong>{w.status}</strong></span>
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>

                {/* Remarks & Background Context */}
                <div className="matter-card-panel">
                  <h3>Remarks & Operational Notes</h3>
                  <div className="matter-remarks-box">
                    {matter.remarks || <span style={{ color: "#94a3b8", fontStyle: "italic" }}>No administrative remarks recorded.</span>}
                  </div>

                  <div style={{ marginTop: 20 }}>
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
                      <h4 style={{ margin: 0, fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>
                        Recent Activity Preview
                      </h4>
                      {events.length > 0 && (
                        <button
                          type="button"
                          style={{ border: "none", background: "transparent", color: "#0284c7", fontSize: "12px", fontWeight: 600, cursor: "pointer" }}
                          onClick={() => setActiveTab("activity")}
                        >
                          View full activity ({events.length}) →
                        </button>
                      )}
                    </div>

                    {recentEvents.length === 0 ? (
                      <div style={{ fontSize: "12px", color: "#94a3b8" }}>No activity recorded yet.</div>
                    ) : (
                      <div className="matter-timeline">
                        {recentEvents.map((ev) => (
                          <div key={ev.id} className="matter-timeline-item">
                            <div className="matter-timeline-dot" />
                            <div className="matter-timeline-content">
                              <div className="matter-timeline-header">{ev.actionName}</div>
                              <div className="matter-timeline-meta">
                                By {ev.actionByUserName} · {new Date(ev.actionAt).toLocaleDateString()}
                              </div>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* TAB 2: WORK */}
          {activeTab === "work" && (
            <div>
              <div className="matter-docs-toolbar">
                <div>
                  <h3 style={{ margin: "0 0 2px 0", fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                    Matter Work Items Workspace
                  </h3>
                  <span className="hint">Active tasks, drafting assignments, and operational work items linked to this matter.</span>
                </div>

                {canAssignWork && (
                  <Link to={`/work/new?matterId=${matter.id}`} className="btn-action-primary">
                    <IconPlus size={14} />
                    + Assign Work
                  </Link>
                )}
              </div>

              {contextData.workItems.length === 0 ? (
                <div className="state empty" style={{ padding: "36px" }}>
                  <IconFileText size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
                  <strong>No active work items assigned</strong>
                  <span>Click '+ Assign Work' above to assign a task for this matter.</span>
                </div>
              ) : (
                <table className="matter-directory-table">
                  <thead>
                    <tr>
                      <th style={{ width: "35%" }}>Task / Work Item</th>
                      <th style={{ width: "15%" }}>Priority</th>
                      <th style={{ width: "15%" }}>Status</th>
                      <th style={{ width: "20%" }}>Responsible Desk</th>
                      <th style={{ width: "15%" }}>Due Date</th>
                    </tr>
                  </thead>
                  <tbody>
                    {contextData.workItems.map((w) => (
                      <tr key={w.workItemId} className="matter-table-row">
                        <td>
                          <div style={{ fontWeight: 650, color: "#0f172a" }}>{w.title}</div>
                        </td>
                        <td>
                          <span
                            style={{
                              padding: "2px 8px",
                              borderRadius: "4px",
                              fontSize: "11px",
                              fontWeight: 700,
                              background: w.priority === "Urgent" ? "#fef2f2" : "#f1f5f9",
                              color: w.priority === "Urgent" ? "#b91c1c" : "#334155"
                            }}
                          >
                            {w.priority}
                          </span>
                        </td>
                        <td>
                          <span className="matter-badge-workstream">{w.status}</span>
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", fontWeight: 600, color: "#334155" }}>
                            {w.responsibleDesk?.name || "Unassigned"}
                          </span>
                          {w.assignedUser && (
                            <div style={{ fontSize: "11px", color: "#64748b" }}>{w.assignedUser.name}</div>
                          )}
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", color: "#475569" }}>
                            {w.dueAt ? new Date(w.dueAt).toLocaleDateString() : "—"}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}

          {/* TAB 3: DOCUMENTS */}
          {activeTab === "documents" && (
            <div>
              <div className="matter-docs-toolbar">
                <div>
                  <h3 style={{ margin: "0 0 2px 0", fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                    Matter Documents Workspace
                  </h3>
                  <span className="hint">Direct evidentiary documents uploaded or linked to this matter.</span>
                </div>

                <div className="matter-docs-actions">
                  {selectedDocIds.length > 0 && (
                    <button
                      type="button"
                      className="btn-action-primary"
                      onClick={() => void handleExportZip()}
                    >
                      <IconDownload size={14} />
                      Export Selected ({selectedDocIds.length})
                    </button>
                  )}

                  {canManageDocs && (
                    <>
                      <button
                        type="button"
                        className="btn-action-secondary"
                        onClick={() => {
                          setUploadError(null);
                          setShowUploadModal(true);
                        }}
                      >
                        <IconUpload size={14} />
                        + Upload Document
                      </button>

                      <button
                        type="button"
                        className="btn-action-secondary"
                        onClick={() => {
                          setLinkError(null);
                          setShowLinkDrawer(true);
                        }}
                      >
                        <IconLink size={14} />
                        Extract / Link Doc
                      </button>
                    </>
                  )}
                </div>
              </div>

              {/* Selection Bar */}
              {selectedDocIds.length > 0 && (
                <div className="matter-selection-banner">
                  <span>{selectedDocIds.length} document(s) selected for export.</span>
                  <button
                    type="button"
                    style={{ border: "none", background: "transparent", cursor: "pointer", color: "#0369a1", fontSize: "12px", fontWeight: 600 }}
                    onClick={() => setSelectedDocIds([])}
                  >
                    Deselect all
                  </button>
                </div>
              )}

              {/* Documents Table */}
              {documents.length === 0 ? (
                <div className="state empty" style={{ padding: "36px" }}>
                  <IconFileText size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
                  <strong>No documents linked to this matter</strong>
                  <span>Upload a new document or link an existing record above.</span>
                </div>
              ) : (
                <table className="matter-directory-table">
                  <thead>
                    <tr>
                      <th style={{ width: 36 }}>
                        <input
                          type="checkbox"
                          checked={documents.length > 0 && documents.every((d) => selectedDocIds.includes(d.documentId))}
                          onChange={(e) => {
                            if (e.target.checked) setSelectedDocIds(documents.map((d) => d.documentId));
                            else setSelectedDocIds([]);
                          }}
                        />
                      </th>
                      <th style={{ width: "20%" }}>Role</th>
                      <th style={{ width: "35%" }}>Name / File</th>
                      <th style={{ width: "15%" }}>Size</th>
                      <th style={{ width: "18%" }}>Uploaded</th>
                      <th style={{ width: "12%" }}>Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {documents.map((d) => (
                      <tr key={d.id} className="matter-table-row">
                        <td onClick={(e) => e.stopPropagation()}>
                          <input
                            type="checkbox"
                            checked={selectedDocIds.includes(d.documentId)}
                            onChange={() => toggleSelectDoc(d.documentId)}
                          />
                        </td>
                        <td>
                          <span className="matter-badge-workstream" style={{ background: "#e0f2fe", color: "#0369a1" }}>
                            {d.documentRole || "Document"}
                          </span>
                        </td>
                        <td>
                          <div style={{ fontWeight: 600, color: "#0f172a" }}>
                            {d.displayName || d.originalFileName}
                          </div>
                          {d.displayName && (
                            <div style={{ fontSize: "11px", color: "#64748b" }}>{d.originalFileName}</div>
                          )}
                          {d.extractProvenance && (
                            <div style={{ marginTop: 4, display: "flex", flexWrap: "wrap", gap: 4, fontSize: "11px" }}>
                              <span style={{ background: "#e0f2fe", color: "#0369a1", padding: "2px 6px", borderRadius: "4px", fontWeight: 500 }}>
                                Extracted from: {d.extractProvenance.sourceFileName} · pp. {d.extractProvenance.normalizedSourcePagesText}
                                {d.extractProvenance.itemNumber ? ` · Item #${d.extractProvenance.itemNumber}` : ""}
                              </span>
                            </div>
                          )}
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", color: "#475569" }}>
                            {(d.fileSize / 1024).toFixed(1)} KB
                          </span>
                        </td>
                        <td>
                          <span style={{ fontSize: "12px", color: "#475569" }}>
                            {new Date(d.uploadedAt).toLocaleDateString()}
                          </span>
                        </td>
                        <td>
                          <a
                            href={`/api/matters/${id}/documents/${d.documentId}/content`}
                            target="_blank"
                            rel="noreferrer"
                            className="btn-action-secondary"
                            style={{ padding: "3px 8px", fontSize: "12px", textDecoration: "none" }}
                          >
                            View
                          </a>
                          {canManageDocs && (
                            <>
                              <button
                                type="button"
                                className="btn-action-secondary"
                                style={{ padding: "3px 8px", fontSize: "12px" }}
                                onClick={() => {
                                  setEditingDoc(d);
                                  setEditDocRole(d.documentRole || "Other");
                                  setEditDocDisplayName(d.displayName || d.originalFileName);
                                  setDocEditError(null);
                                }}
                                title="Edit Document Details"
                              >
                                <IconEdit size={12} />
                              </button>
                              <button
                                type="button"
                                className="btn-action-secondary"
                                style={{ padding: "3px 8px", fontSize: "12px", color: "#dc2626" }}
                                disabled={deletingDocId === d.documentId}
                                onClick={() => void handleRemoveDocument(d.documentId)}
                                title="Remove Document from Matter"
                              >
                                <IconClose size={12} />
                              </button>
                            </>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          )}

          {/* TAB 4: DRAFTS */}
          {activeTab === "drafts" && (
            <div>
              <MatterDrafts matterId={id} />
            </div>
          )}

          {/* TAB 5: OUTWARD */}
          {activeTab === "outward" && (
            <div>
              <MatterOutwardSection matterId={id} />
            </div>
          )}

          {/* TAB 6: ACTIVITY */}
          {activeTab === "activity" && (
            <div>
              <div style={{ marginBottom: 16 }}>
                <h3 style={{ margin: "0 0 2px 0", fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                  Matter Audit & Context Timeline
                </h3>
                <span className="hint">Immutable chronological activity history for official compliance auditing.</span>
              </div>

              {events.length === 0 ? (
                <div className="state empty" style={{ padding: "36px" }}>
                  <IconHistory size={32} style={{ color: "#94a3b8", marginBottom: 8 }} />
                  <strong>No audit events recorded</strong>
                </div>
              ) : (
                <div className="matter-timeline" style={{ marginTop: 12 }}>
                  {events.map((ev) => (
                    <div key={ev.id} className="matter-timeline-item">
                      <div className="matter-timeline-dot" />
                      <div className="matter-timeline-content">
                        <div className="matter-timeline-header">
                          <span style={{ color: "#0284c7", fontWeight: 700, marginRight: 6 }}>
                            #{ev.sequenceNumber}
                          </span>
                          {ev.actionName}
                          {ev.contextEntityType && (
                            <span style={{ fontWeight: 600, color: "#0369a1", marginLeft: 6 }}>
                              [{ev.contextEntityType}]
                            </span>
                          )}
                        </div>
                        {ev.reason && <div className="matter-timeline-reason">&ldquo;{ev.reason}&rdquo;</div>}
                        <div className="matter-timeline-meta">
                          Action by <strong>{ev.actionByUserName}</strong> on {new Date(ev.actionAt).toLocaleString()}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* MANAGE LINKED RECORDS DRAWER */}
      {showManageLinksDrawer && (
        <div className="matter-drawer-overlay" onClick={() => setShowManageLinksDrawer(false)}>
          <div className="matter-right-drawer" style={{ width: 500 }} onClick={(e) => e.stopPropagation()}>
            <div className="matter-drawer-header">
              <div>
                <h3 style={{ margin: 0, fontSize: "16px", fontWeight: 700, color: "#0f172a" }}>
                  Manage Linked Records
                </h3>
                <span className="hint">Link or unlink canonical Awards, Khasras, Court Cases, and Dak.</span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowManageLinksDrawer(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {manageLinkError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", fontSize: "13px" }}>
                {manageLinkError}
              </div>
            )}

            {/* Manage Links Sub-Tabs */}
            <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #e2e8f0", paddingBottom: 8 }}>
              {(["awards", "khasras", "court", "dak"] as const).map((t) => (
                <button
                  key={t}
                  type="button"
                  className={`btn-action-secondary ${manageLinksTab === t ? "active" : ""}`}
                  style={{
                    flex: 1,
                    padding: "5px 8px",
                    fontSize: "12px",
                    fontWeight: 600,
                    borderColor: manageLinksTab === t ? "#0284c7" : "#cbd5e1",
                    background: manageLinksTab === t ? "#e0f2fe" : "#ffffff",
                    color: manageLinksTab === t ? "#0369a1" : "#475569"
                  }}
                  onClick={() => {
                    setManageLinksTab(t);
                    setManageLinkError(null);
                  }}
                >
                  {t === "awards" ? "Awards" : t === "khasras" ? "Khasras" : t === "court" ? "Court Case" : "Dak"}
                </button>
              ))}
            </div>

            {/* SUB-TAB 1: AWARDS */}
            {manageLinksTab === "awards" && (
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                <div style={{ fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>Currently Linked Awards</div>
                {contextData.awards.length === 0 ? (
                  <div style={{ fontSize: "12px", color: "#94a3b8" }}>No Awards linked.</div>
                ) : (
                  contextData.awards.map((a) => (
                    <div
                      key={a.awardId}
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        padding: "8px 12px",
                        background: "#f8fafc",
                        border: "1px solid #e2e8f0",
                        borderRadius: "6px"
                      }}
                    >
                      <div>
                        <strong style={{ fontSize: "13px" }}>Award {a.awardNumber}</strong>
                        {a.isPrimary && (
                          <span style={{ marginLeft: 6, fontSize: "11px", color: "#92400e", background: "#fef3c7", padding: "1px 6px", borderRadius: "4px", fontWeight: 700 }}>
                            ★ Primary
                          </span>
                        )}
                      </div>
                      <div style={{ display: "flex", gap: 6 }}>
                        {!a.isPrimary && (
                          <button
                            type="button"
                            className="btn-action-secondary"
                            style={{ padding: "3px 8px", fontSize: "11px" }}
                            disabled={linkingRecord}
                            onClick={() => void executeLinkMutation("awards", a.awardId, "PUT", true)}
                          >
                            Set Primary
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn-action-secondary"
                          style={{ padding: "3px 8px", fontSize: "11px", color: "#dc2626" }}
                          disabled={linkingRecord}
                          onClick={() => void executeLinkMutation("awards", a.awardId, "DELETE")}
                        >
                          Unlink
                        </button>
                      </div>
                    </div>
                  ))
                )}

                <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: 12, marginTop: 8 }}>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Link Available Village Award:
                  </label>
                  <div style={{ display: "flex", gap: 6, marginTop: 6 }}>
                    <select
                      id="select-village-award-to-link"
                      className="matter-filter-select"
                      style={{ flex: 1 }}
                      defaultValue=""
                    >
                      <option value="" disabled>Select Award to Link...</option>
                      {villageAwardsList
                        .filter((va) => !contextData.awards.some((a) => a.awardId === (va.id || va.awardId)))
                        .map((va) => (
                          <option key={va.id || va.awardId} value={va.id || va.awardId}>
                            Award #{va.awardNumber} ({va.awardType || "Standard"})
                          </option>
                        ))}
                    </select>
                    <button
                      type="button"
                      className="btn-action-primary"
                      style={{ padding: "6px 12px", fontSize: "12px" }}
                      disabled={linkingRecord}
                      onClick={() => {
                        const sel = (document.getElementById("select-village-award-to-link") as HTMLSelectElement)?.value;
                        if (sel) void executeLinkMutation("awards", sel, "PUT");
                      }}
                    >
                      Link
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* SUB-TAB 2: KHASRAS */}
            {manageLinksTab === "khasras" && (
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                <div style={{ fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>Currently Linked Khasras</div>
                {contextData.khasras.length === 0 ? (
                  <div style={{ fontSize: "12px", color: "#94a3b8" }}>No Khasras linked.</div>
                ) : (
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                    {contextData.khasras.map((k) => (
                      <span
                        key={k.khasraId}
                        style={{
                          display: "inline-flex",
                          alignItems: "center",
                          gap: 6,
                          background: "#e0f2fe",
                          color: "#0369a1",
                          padding: "4px 8px",
                          borderRadius: "4px",
                          fontSize: "12px",
                          fontWeight: 600
                        }}
                      >
                        Khasra #{k.displayNumber}
                        <button
                          type="button"
                          style={{ border: "none", background: "transparent", cursor: "pointer", color: "#dc2626", padding: 0, fontWeight: 700 }}
                          disabled={linkingRecord}
                          onClick={() => void executeLinkMutation("khasras", k.khasraId, "DELETE")}
                          title="Unlink Khasra"
                        >
                          &times;
                        </button>
                      </span>
                    ))}
                  </div>
                )}

                <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: 12, marginTop: 8 }}>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Link Available Village Khasra:
                  </label>
                  <div style={{ display: "flex", gap: 6, marginTop: 6 }}>
                    <select
                      id="select-village-khasra-to-link"
                      className="matter-filter-select"
                      style={{ flex: 1 }}
                      defaultValue=""
                    >
                      <option value="" disabled>Select Khasra to Link...</option>
                      {villageKhasrasList
                        .filter((vk) => !contextData.khasras.some((k) => k.khasraId === (vk.id || vk.khasraId)))
                        .map((vk) => (
                          <option key={vk.id || vk.khasraId} value={vk.id || vk.khasraId}>
                            Khasra #{vk.displayNumber || vk.khasraNumber} {vk.area ? `(${vk.area})` : ""}
                          </option>
                        ))}
                    </select>
                    <button
                      type="button"
                      className="btn-action-primary"
                      style={{ padding: "6px 12px", fontSize: "12px" }}
                      disabled={linkingRecord}
                      onClick={() => {
                        const sel = (document.getElementById("select-village-khasra-to-link") as HTMLSelectElement)?.value;
                        if (sel) void executeLinkMutation("khasras", sel, "PUT");
                      }}
                    >
                      Link
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* SUB-TAB 3: COURT CASE */}
            {manageLinksTab === "court" && (
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                <div style={{ fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>Currently Linked Court Case</div>
                {contextData.courtCases.length === 0 ? (
                  <div style={{ fontSize: "12px", color: "#94a3b8" }}>No Court Case linked.</div>
                ) : (
                  contextData.courtCases.map((c) => (
                    <div
                      key={c.courtCaseId}
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        padding: "8px 12px",
                        background: "#f0f9ff",
                        border: "1px solid #bae6fd",
                        borderRadius: "6px"
                      }}
                    >
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0369a1" }}>{c.caseNumber}</strong>
                        <div style={{ fontSize: "12px", color: "#0f172a" }}>{c.caseTitle}</div>
                      </div>
                      <button
                        type="button"
                        className="btn-action-secondary"
                        style={{ padding: "3px 8px", fontSize: "11px", color: "#dc2626" }}
                        disabled={linkingRecord}
                        onClick={() => void executeLinkMutation("court-cases", c.courtCaseId, "DELETE")}
                      >
                        Unlink
                      </button>
                    </div>
                  ))
                )}

                <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: 12, marginTop: 8 }}>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Search & Link Authoritative Court Case:
                  </label>
                  <input
                    type="text"
                    className="matter-search-input"
                    placeholder="Search by case number or title..."
                    value={courtSearchQuery}
                    onChange={(e) => void handleSearchCourtCases(e.target.value)}
                    style={{ marginTop: 4 }}
                  />

                  {courtSearchResults.length > 0 && (
                    <div style={{ display: "flex", flexDirection: "column", gap: 6, marginTop: 8, maxHeight: 180, overflowY: "auto" }}>
                      {courtSearchResults.map((cc) => (
                        <div
                          key={cc.id}
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                            padding: "6px 10px",
                            background: "#f8fafc",
                            border: "1px solid #e2e8f0",
                            borderRadius: "4px",
                            fontSize: "12px"
                          }}
                        >
                          <div>
                            <strong>{cc.caseNumber}</strong> · {cc.caseTitle}
                          </div>
                          <button
                            type="button"
                            className="btn-action-secondary"
                            style={{ padding: "2px 8px", fontSize: "11px" }}
                            disabled={linkingRecord}
                            onClick={() => void executeLinkMutation("court-cases", cc.id, "PUT")}
                          >
                            Link
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}

            {/* SUB-TAB 4: DAK */}
            {manageLinksTab === "dak" && (
              <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
                <div style={{ fontSize: "13px", fontWeight: 700, color: "#0f172a" }}>Currently Linked Dak Items</div>
                {contextData.daks.length === 0 ? (
                  <div style={{ fontSize: "12px", color: "#94a3b8" }}>No Dak items linked.</div>
                ) : (
                  contextData.daks.map((d) => (
                    <div
                      key={d.dakId}
                      style={{
                        display: "flex",
                        justifyContent: "space-between",
                        alignItems: "center",
                        padding: "8px 12px",
                        background: "#f8fafc",
                        border: "1px solid #e2e8f0",
                        borderRadius: "6px"
                      }}
                    >
                      <div>
                        <strong style={{ fontSize: "12px" }}>Diary #{d.diaryNumber}</strong>
                        <div style={{ fontSize: "12px", color: "#475569" }}>{d.subject}</div>
                      </div>
                      <button
                        type="button"
                        className="btn-action-secondary"
                        style={{ padding: "3px 8px", fontSize: "11px", color: "#dc2626" }}
                        disabled={linkingRecord}
                        onClick={() => void executeLinkMutation("daks", d.dakId, "DELETE")}
                      >
                        Unlink
                      </button>
                    </div>
                  ))
                )}

                <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: 12, marginTop: 8 }}>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Search & Link Dak Communication:
                  </label>
                  <input
                    type="text"
                    className="matter-search-input"
                    placeholder="Search by diary number or subject..."
                    value={dakSearchQuery}
                    onChange={(e) => void handleSearchDak(e.target.value)}
                    style={{ marginTop: 4 }}
                  />

                  {dakSearchResults.length > 0 && (
                    <div style={{ display: "flex", flexDirection: "column", gap: 6, marginTop: 8, maxHeight: 180, overflowY: "auto" }}>
                      {dakSearchResults.map((dk) => (
                        <div
                          key={dk.id}
                          style={{
                            display: "flex",
                            justifyContent: "space-between",
                            alignItems: "center",
                            padding: "6px 10px",
                            background: "#f8fafc",
                            border: "1px solid #e2e8f0",
                            borderRadius: "4px",
                            fontSize: "12px"
                          }}
                        >
                          <div>
                            <strong>Diary #{dk.diaryNumber}</strong> · {dk.subject}
                          </div>
                          <button
                            type="button"
                            className="btn-action-secondary"
                            style={{ padding: "2px 8px", fontSize: "11px" }}
                            disabled={linkingRecord}
                            onClick={() => void executeLinkMutation("daks", dk.id, "PUT")}
                          >
                            Link
                          </button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Edit Document Details Modal */}
      {editingDoc && (
        <div className="matter-modal-overlay" onClick={() => setEditingDoc(null)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <h3 className="matter-modal-title">Edit Document Details</h3>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setEditingDoc(null)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {docEditError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {docEditError}
              </div>
            )}

            <form onSubmit={handleEditDocumentSubmit}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Document Role *
                  <select value={editDocRole} onChange={(e) => setEditDocRole(e.target.value)}>
                    {["Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                      <option key={r} value={r}>
                        {r}
                      </option>
                    ))}
                  </select>
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Display Name
                  <input
                    type="text"
                    value={editDocDisplayName}
                    onChange={(e) => setEditDocDisplayName(e.target.value)}
                    placeholder="e.g. High Court Order dt 12-05-2026"
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setEditingDoc(null)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={savingDocEdit}
                >
                  {savingDocEdit ? "Saving..." : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Upload Document Modal */}
      {showUploadModal && (
        <div className="matter-modal-overlay" onClick={() => setShowUploadModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <h3 className="matter-modal-title">Upload Matter Document</h3>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowUploadModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {uploadError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {uploadError}
              </div>
            )}

            <form onSubmit={handleUploadDocument}>
              <div className="field-grid">
                <label>
                  Document Role *
                  <select value={uploadRole} onChange={(e) => setUploadRole(e.target.value)}>
                    {["Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                      <option key={r} value={r}>
                        {r}
                      </option>
                    ))}
                  </select>
                </label>

                <label>
                  Display Name (optional)
                  <input
                    type="text"
                    value={uploadDisplayName}
                    onChange={(e) => setUploadDisplayName(e.target.value)}
                    placeholder="e.g. High Court Order dt 12-05-2026"
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Select File *
                  <input
                    type="file"
                    onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowUploadModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={uploading || !uploadFile}
                >
                  {uploading ? "Uploading..." : "Upload Document"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Link Existing Document & Page Extractor Right Drawer */}
      {showLinkDrawer && (
        <div className="matter-drawer-overlay" onClick={() => setShowLinkDrawer(false)}>
          <div className="matter-right-drawer" onClick={(e) => e.stopPropagation()}>
            <div className="matter-drawer-header">
              <div>
                <h3 style={{ margin: 0, fontSize: "16px", fontWeight: 700, color: "#0f172a" }}>
                  {drawerMode === "link" ? "Link Existing Document" : "Extract Specific Pages into PDF"}
                </h3>
                <span className="hint">
                  {drawerMode === "link"
                    ? "Select eligible document from Award or Land Record families."
                    : "Extract lightweight non-contiguous pages (e.g. 9, 16) with Item # provenance."}
                </span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowLinkDrawer(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {/* Mode Switcher Tabs */}
            <div style={{ display: "flex", gap: 8, padding: "0 0 12px 0", borderBottom: "1px solid #e2e8f0", marginBottom: 12 }}>
              <button
                type="button"
                className={`btn-action-secondary ${drawerMode === "link" ? "active" : ""}`}
                style={{
                  flex: 1,
                  padding: "6px 12px",
                  fontSize: "13px",
                  fontWeight: 600,
                  borderColor: drawerMode === "link" ? "#0284c7" : "#cbd5e1",
                  background: drawerMode === "link" ? "#e0f2fe" : "#ffffff",
                  color: drawerMode === "link" ? "#0369a1" : "#475569"
                }}
                onClick={() => {
                  setDrawerMode("link");
                  setLinkError(null);
                }}
              >
                <IconLink size={13} style={{ marginRight: 6 }} />
                Link Full Document
              </button>

              <button
                type="button"
                className={`btn-action-secondary ${drawerMode === "extract" ? "active" : ""}`}
                style={{
                  flex: 1,
                  padding: "6px 12px",
                  fontSize: "13px",
                  fontWeight: 600,
                  borderColor: drawerMode === "extract" ? "#0284c7" : "#cbd5e1",
                  background: drawerMode === "extract" ? "#e0f2fe" : "#ffffff",
                  color: drawerMode === "extract" ? "#0369a1" : "#475569"
                }}
                onClick={() => {
                  setDrawerMode("extract");
                  setExtractError(null);
                  if (eligibleDocs.length > 0 && !extractSourceDocId) {
                    setExtractSourceDocId(eligibleDocs[0].id);
                  }
                }}
              >
                <IconFileText size={13} style={{ marginRight: 6 }} />
                Extract Specific Pages
              </button>
            </div>

            {drawerMode === "link" ? (
              <>
                {linkError && (
                  <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", fontSize: "13px" }}>
                    {linkError}
                  </div>
                )}

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Target Role for Matter Link:
                  </label>
                  <select
                    value={linkRole}
                    onChange={(e) => setLinkRole(e.target.value)}
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                  >
                    {["Naqsha Mutzamin", "Statement A", "Application", "Court Order", "ADM Letter", "Joint Declaration", "Khatoni", "Demarcation", "Correspondence", "Other"].map((r) => (
                      <option key={r} value={r}>
                        {r}
                      </option>
                    ))}
                  </select>
                </div>

                <div style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: 8 }}>
                  {(() => {
                    const linkableCandidates = eligibleDocs.filter((doc) => !doc.isAlreadyLinked && doc.source !== "Matter Document");
                    if (linkableCandidates.length === 0) {
                      return (
                        <div className="state empty" style={{ padding: "24px" }}>
                          <span>No unlinked Award or Land Record documents available for linking.</span>
                        </div>
                      );
                    }
                    return linkableCandidates.map((doc) => (
                      <div
                        key={doc.id}
                        style={{
                          display: "flex",
                          justifyContent: "space-between",
                          alignItems: "center",
                          padding: "10px 12px",
                          background: "#f8fafc",
                          border: "1px solid #e2e8f0",
                          borderRadius: "6px"
                        }}
                      >
                        <div style={{ minWidth: 0, flex: 1, paddingRight: 8 }}>
                          <div style={{ fontWeight: 600, fontSize: "13px", color: "#0f172a", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                            {doc.originalFileName}
                          </div>
                          <div style={{ fontSize: "11px", color: "#64748b" }}>
                            {doc.source} · {new Date(doc.uploadedAt).toLocaleDateString()}
                          </div>
                        </div>
                        <button
                          type="button"
                          className="btn-action-secondary"
                          style={{ padding: "4px 10px", fontSize: "12px" }}
                          disabled={linkingDocId === doc.id}
                          onClick={() => void handleLinkExisting(doc.id)}
                        >
                          {linkingDocId === doc.id ? "Linking..." : "Attach"}
                        </button>
                      </div>
                    ));
                  })()}
                </div>
              </>
            ) : (
              <form onSubmit={handleExtractPagesSubmit} style={{ display: "flex", flexDirection: "column", gap: 12, flex: 1, overflowY: "auto" }}>
                {extractError && (
                  <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", fontSize: "13px" }}>
                    {extractError}
                  </div>
                )}

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Source Master PDF Document *
                  </label>
                  <select
                    value={extractSourceDocId}
                    onChange={(e) => setExtractSourceDocId(e.target.value)}
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                    required
                  >
                    <option value="">-- Select Source Document --</option>
                    {eligibleDocs.map((doc) => (
                      <option key={doc.id} value={doc.id}>
                        {doc.originalFileName} ({doc.source})
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Page Ranges / Numbers *
                  </label>
                  <input
                    type="text"
                    value={extractPagesText}
                    onChange={(e) => setExtractPagesText(e.target.value)}
                    placeholder="e.g. 9, 16 or 9-11, 16"
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                    required
                  />
                  <span style={{ fontSize: "11px", color: "#64748b", marginTop: 2, display: "block" }}>
                    Supports non-contiguous pages like 9, 16 or range combinations 9-11, 16.
                  </span>
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Extracted Document Role *
                  </label>
                  <select
                    value={extractRole}
                    onChange={(e) => setExtractRole(e.target.value)}
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                    required
                  >
                    {["Naqsha Mutzamin", "Statement A", "Award", "Khatoni", "Demarcation", "Application", "Court Order", "ADM Letter", "Other"].map((r) => (
                      <option key={r} value={r}>
                        {r}
                      </option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Item Number (e.g. Item #42 for owner correlation)
                  </label>
                  <input
                    type="text"
                    value={extractItemNumber}
                    onChange={(e) => setExtractItemNumber(e.target.value)}
                    placeholder="e.g. 42 or 42-A"
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                  />
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Khasra Reference Text
                  </label>
                  <input
                    type="text"
                    value={extractKhasraRef}
                    onChange={(e) => setExtractKhasraRef(e.target.value)}
                    placeholder="e.g. 12/1, 18/4"
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                  />
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Context Label / Extract Notes
                  </label>
                  <input
                    type="text"
                    value={extractContextLabel}
                    onChange={(e) => setExtractContextLabel(e.target.value)}
                    placeholder="e.g. Rameshwar Singh Khasra Extracts"
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                  />
                </div>

                <div>
                  <label className="form-label" style={{ fontWeight: 600, fontSize: "13px" }}>
                    Display Name Override (Optional)
                  </label>
                  <input
                    type="text"
                    value={extractDisplayName}
                    onChange={(e) => setExtractDisplayName(e.target.value)}
                    placeholder="e.g. Item #42 - NM Extract (pp. 9, 16)"
                    className="matter-filter-select"
                    style={{ width: "100%", marginTop: 4 }}
                  />
                </div>

                {/* Live Extraction Preview Card */}
                <div style={{ background: "#f0f9ff", border: "1px dashed #0284c7", borderRadius: "6px", padding: "10px 12px", marginTop: 4 }}>
                  <div style={{ fontSize: "11px", fontWeight: 700, color: "#0369a1", textTransform: "uppercase", letterSpacing: "0.5px" }}>
                    Extraction Provenance Live Preview
                  </div>
                  <div style={{ fontSize: "13px", fontWeight: 600, color: "#0f172a", marginTop: 4 }}>
                    {extractDisplayName.trim() ||
                      `${extractItemNumber.trim() ? `Item #${extractItemNumber.trim()} · ` : ""}${extractRole} Extract${extractKhasraRef.trim() ? ` (${extractKhasraRef.trim()})` : ""}`}
                  </div>
                  <div style={{ fontSize: "11px", color: "#0284c7", marginTop: 2 }}>
                    Source: {eligibleDocs.find((d) => d.id === extractSourceDocId)?.originalFileName || "Select Source"} · Pages: {extractPagesText.trim() || "N/A"}
                  </div>
                </div>

                <div className="matter-modal-actions" style={{ marginTop: "auto", paddingTop: 12 }}>
                  <button
                    type="button"
                    className="btn-action-secondary"
                    onClick={() => setShowLinkDrawer(false)}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="btn-action-primary"
                    disabled={extracting || !extractSourceDocId || !extractPagesText.trim()}
                  >
                    {extracting ? "Extracting Pages..." : "Extract & Attach to Matter"}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}

      {/* Edit Metadata Modal */}
      {showEditModal && (
        <div className="matter-modal-overlay" onClick={() => setShowEditModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <h3 className="matter-modal-title">Edit Operational Metadata</h3>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowEditModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {editError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {editError}
              </div>
            )}

            <form onSubmit={handleUpdateMetadata}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Operational Title *
                  <input
                    type="text"
                    value={editTitle}
                    onChange={(e) => setEditTitle(e.target.value)}
                    required
                  />
                </label>

                <label>
                  Matter Type *
                  <select value={editType} onChange={(e) => setEditType(e.target.value)}>
                    {["Court Case", "Compensation", "Land Acquisition", "Demarcation", "Possession", "Other"].map((t) => (
                      <option key={t} value={t}>{t}</option>
                    ))}
                  </select>
                </label>

                {editType === "Other" ? (
                  <label>
                    Specify Custom Type *
                    <input
                      type="text"
                      value={editCustomType}
                      onChange={(e) => setEditCustomType(e.target.value)}
                      placeholder="e.g. Arbitration"
                      required
                    />
                  </label>
                ) : (
                  <label>
                    Reference Number
                    <input
                      type="text"
                      value={editRefNo}
                      onChange={(e) => setEditRefNo(e.target.value)}
                      placeholder="e.g. Ref-2026-001"
                    />
                  </label>
                )}

                <label style={{ gridColumn: "span 2" }}>
                  Status
                  <input
                    type="text"
                    value={editStatus}
                    onChange={(e) => setEditStatus(e.target.value)}
                    placeholder="e.g. Open, Pending, Disposed"
                  />
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Legacy Khasra Text Reference
                  <input
                    type="text"
                    value={editKhasraRef}
                    onChange={(e) => setEditKhasraRef(e.target.value)}
                    placeholder="e.g. 12/1, 14/2"
                  />
                  <span style={{ fontSize: "11px", color: "#64748b" }}>
                    Text only; use Manage Linked Records for canonical Khasra chips.
                  </span>
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Remarks / Operational Notes
                  <textarea
                    value={editRemarks}
                    onChange={(e) => setEditRemarks(e.target.value)}
                    rows={3}
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowEditModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={savingEdit || !editTitle.trim()}
                >
                  {savingEdit ? "Saving..." : "Save Metadata"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reclassify Workstream Modal */}
      {showReclassifyModal && (
        <div className="matter-modal-overlay" onClick={() => setShowReclassifyModal(false)}>
          <div className="matter-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="matter-modal-header">
              <div>
                <h3 className="matter-modal-title">Reclassify Workstream</h3>
                <span className="hint">Alters official workstream classification and administrative ownership.</span>
              </div>
              <button
                type="button"
                style={{ border: "none", background: "transparent", cursor: "pointer", color: "#64748b" }}
                onClick={() => setShowReclassifyModal(false)}
              >
                <IconClose size={18} />
              </button>
            </div>

            {reclassifyError && (
              <div style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", marginBottom: 14, fontSize: "13px" }}>
                {reclassifyError}
              </div>
            )}

            <form onSubmit={handleReclassify}>
              <div className="field-grid">
                <label style={{ gridColumn: "span 2" }}>
                  Target Workstream *
                  <select
                    value={reclassifyTargetId}
                    onChange={(e) => setReclassifyTargetId(e.target.value)}
                    required
                  >
                    {workstreams.map((w) => (
                      <option key={w.id} value={w.id}>
                        {w.name} ({w.code})
                      </option>
                    ))}
                  </select>
                </label>

                <label style={{ gridColumn: "span 2" }}>
                  Mandatory Reclassification Reason *
                  <textarea
                    value={reclassifyReason}
                    onChange={(e) => setReclassifyReason(e.target.value)}
                    placeholder="Provide official administrative justification for reclassification..."
                    rows={3}
                    required
                  />
                </label>
              </div>

              <div className="matter-modal-actions">
                <button
                  type="button"
                  className="btn-action-secondary"
                  onClick={() => setShowReclassifyModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="btn-action-primary"
                  disabled={savingReclassify || !reclassifyReason.trim()}
                >
                  {savingReclassify ? "Reclassifying..." : "Confirm Reclassification"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
