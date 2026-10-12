import React, { useState, useEffect, useRef } from "react";
import { Link, useNavigate } from "react-router-dom";
import type {
  ConflictKind,
  CoreDocumentRole,
  IntakeItem,
  IntakeMatchState,
  ParsedProblemDetails,
} from "./smartCoreIntakeAdapter";
import {
  smartIntake,
  parseProblemDetails,
  guessDocumentRole,
  confidenceLabel,
  roleLabel,
  matchStateLabel,
} from "./smartCoreIntakeAdapter";
import { useAuth } from "../auth/AuthProvider";
import { AwardPdfOwnershipConflictPanel } from "../award/AwardPdfOwnershipConflictPanel";
import { VillageTemporaryProceedingsSection } from "../award/VillageTemporaryProceedingsSection";
import { LandRecordApiError, landRecordResponse, type PdfOwnershipConflict } from "../award/LandRecordApiError";
import "./land.css";

// ─── Prop / data types ───────────────────────────────────────────────────────

interface VillageCoreRecordsProps {
  id: string;
}

interface CoreDocumentEntry {
  documentId: string;
  role: string;
  coreDocumentRole: CoreDocumentRole | null;
  originalFileName: string;
  uploadedAt: string;
  status: string;
  mimeType?: string;
  viewRoute?: string;
  downloadRoute?: string;
  legalDate?: string | null;
}

interface RoleBucket {
  role: CoreDocumentRole;
  count: number;
  available: boolean;
  documents: CoreDocumentEntry[];
}

interface AwardCoreRecord {
  id: string;
  awardNumber: string;
  awardDate: string | null;
  awardType: string | null;
  roles: RoleBucket[];
  /** Legacy flat list — still present for compatibility, but we prefer roles[].documents */
  documents?: CoreDocumentEntry[];
}

// ─── Constants ───────────────────────────────────────────────────────────────

const CORE_ROLES: Array<{
  role: CoreDocumentRole;
  label: string;
  description: string;
}> = [
  {
    role: "Award",
    label: "Award PDF",
    description: "Official award notification & declaration",
  },
  {
    role: "NM",
    label: "Naksha Muntazmin (NM)",
    description: "Field measurement & parcel map schedule",
  },
  {
    role: "StatementA",
    label: "Statement A",
    description: "Compensation assessment & ownership register",
  },
  {
    role: "PossessionProceeding",
    label: "Possession Proceeding",
    description: "Site takeover & possession memorandum",
  },
];

// ─── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(iso?: string | null): string {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString("en-IN", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });
  } catch {
    return iso;
  }
}

function docsForRole(award: AwardCoreRecord, role: CoreDocumentRole): CoreDocumentEntry[] {
  // Prefer nested roles array (new API shape)
  const bucket = award.roles?.find((r) => r.role === role);
  if (bucket && bucket.documents?.length) return bucket.documents;
  // Fallback to flat documents (old API shape / compatibility)
  return (award.documents ?? []).filter((d) => d.coreDocumentRole === role);
}

function matchStateBadgeClass(state: IntakeMatchState): string {
  switch (state) {
    case "MatchedExistingAward": return "intake-badge intake-badge-matched";
    case "ProposedNewAward": return "intake-badge intake-badge-proposed";
    case "NeedsOfficerReview": return "intake-badge intake-badge-review";
  }
}

function useSafeAuth() {
  try {
    return useAuth();
  } catch {
    return {
      user: null,
      loading: false,
      login: async () => {},
      logout: async () => {},
      hasPermission: (_code: string) => false,
      refreshUser: async () => {},
    };
  }
}

export interface ConflictDetection {
  isConflict: boolean;
  conflictKind: ConflictKind;
  isSameAward: boolean;
  conflictingAwardId: string | null;
  conflictingAwardNumber: string | null;
  conflictingRole: CoreDocumentRole | string | null;
  conflictingVillageName: string | null;
  documentId: string | null;
  viewRoute: string | null;
  explanation: string;
}

export function detectDocumentConflict({
  intake,
  uploadError,
  parsedProblem,
  targetAwardId,
  targetAwardNumber,
  awards,
  villageName,
}: {
  intake?: IntakeItem | null;
  uploadError?: string | null;
  parsedProblem?: ParsedProblemDetails | null;
  targetAwardId?: string | null;
  targetAwardNumber?: string | null;
  awards: AwardCoreRecord[];
  villageName: string;
  fileName: string;
}): ConflictDetection {
  if (parsedProblem?.ownershipConflict) {
    const link = parsedProblem.ownershipConflict.existingLinks[0];
    return { isConflict: true, conflictKind: "SourceLinkedToAnotherAward", isSameAward: !!targetAwardId && link?.awardId === targetAwardId,
      conflictingAwardId: link?.awardId || null, conflictingAwardNumber: link?.awardNumber || null, conflictingRole: null,
      conflictingVillageName: link?.villages.map(v => v.name).join(", ") || null, documentId: link?.documentId || null,
      viewRoute: link ? `/api/documents/${link.documentId}/content` : null, explanation: parsedProblem.detail || "Review existing PDF ownership." };
  }
  // NOTE: Client-side filename collision detection has been removed.
  // Distinct PDFs can share the same file name (e.g., Award.pdf).
  // Conflict / duplicate status must be established authoritatively by server responses.

  // 1. Authoritative check: Intake item marked duplicate or confirmed by server
  if (intake && intake.status === "Confirmed" && !parsedProblem?.isConflict) {
    const existingAwardId = intake.confirmedAwardId || intake.matchedAwardId || null;
    const existingAward = existingAwardId ? awards.find((a) => a.id === existingAwardId) : undefined;
    const existingAwardNumber =
      existingAward?.awardNumber ||
      intake.matchedAwardNumber ||
      intake.detectedAwardNumber ||
      null;
    const existingRole = intake.confirmedRole || intake.detectedRole || null;
    const docId = intake.confirmedDocumentId || intake.documentId || null;
    const vRoute = intake.viewRoute || (docId ? `/api/documents/${docId}/content` : null);

    const isSameAward =
      !!targetAwardId &&
      (targetAwardId === existingAwardId);

    const conflictKind: ConflictKind = isSameAward
      ? (intake.status === "Confirmed" ? "AlreadyConfirmedDifferentDecision" : "GenericConflict")
      : "SourceLinkedToAnotherAward";

    let explanation = "";
    if (isSameAward) {
      explanation = `Already uploaded: This document is already attached to Award ${targetAwardNumber || existingAwardNumber || ""}${existingRole ? ` as ${roleLabel(existingRole)}` : ""}.`;
    } else if (existingAwardNumber) {
      explanation = `This PDF is already attached to Award ${existingAwardNumber}${villageName ? `, Village ${villageName}` : ""}.`;
    } else {
      explanation = "This PDF is already linked to another Award. Existing Award could not be identified.";
    }

    return {
      isConflict: true,
      conflictKind,
      isSameAward,
      conflictingAwardId: existingAwardId,
      conflictingAwardNumber: existingAwardNumber,
      conflictingRole: existingRole,
      conflictingVillageName: villageName || null,
      documentId: docId,
      viewRoute: vRoute,
      explanation,
    };
  }

  // 2. Authoritative check: Server returned 409 Conflict / RFC 9110 Problem Details
  const isConflictErr =
    parsedProblem?.isConflict ||
    parsedProblem?.status === 409 ||
    (uploadError &&
      (uploadError.includes("409") ||
        uploadError.toLowerCase().includes("already linked") ||
        uploadError.toLowerCase().includes("already attached") ||
        uploadError.toLowerCase().includes("conflict") ||
        uploadError.toLowerCase().includes("different decision")));

  if (isConflictErr) {
    const conflictKind: ConflictKind =
      parsedProblem?.conflictKind ||
      (uploadError?.toLowerCase().includes("role")
        ? "RoleConflict"
        : uploadError?.toLowerCase().includes("concurrent")
        ? "StagedOrConcurrentIntakeConflict"
        : uploadError?.toLowerCase().includes("different decision")
        ? "AlreadyConfirmedDifferentDecision"
        : "SourceLinkedToAnotherAward");

    // Authoritative award matching from server data only — no filename guessing
    let matchedAward: AwardCoreRecord | undefined;
    if (parsedProblem?.conflictingAwardId) {
      matchedAward = awards.find((a) => a.id === parsedProblem.conflictingAwardId);
    } else if (parsedProblem?.conflictingAwardNumber) {
      matchedAward = awards.find(
        (a) => a.awardNumber.toLowerCase() === parsedProblem.conflictingAwardNumber?.toLowerCase()
      );
    } else if (intake?.matchedAwardId) {
      matchedAward = awards.find((a) => a.id === intake.matchedAwardId);
    }

    const conflictingAwardId =
      matchedAward?.id ||
      parsedProblem?.conflictingAwardId ||
      intake?.matchedAwardId ||
      null;

    const conflictingAwardNumber =
      matchedAward?.awardNumber ||
      parsedProblem?.conflictingAwardNumber ||
      intake?.matchedAwardNumber ||
      null;

    const conflictingVillageName =
      parsedProblem?.conflictingVillageName ||
      (matchedAward ? villageName : null);

    const conflictingRole =
      parsedProblem?.conflictingRole ||
      intake?.detectedRole ||
      null;

    const documentId =
      parsedProblem?.conflictingDocumentId ||
      intake?.documentId ||
      null;

    const viewRoute =
      documentId ? `/api/documents/${documentId}/content` : intake?.viewRoute || null;

    const isSameAward =
      !!targetAwardId &&
      (targetAwardId === conflictingAwardId ||
        (!!targetAwardNumber && !!conflictingAwardNumber && targetAwardNumber === conflictingAwardNumber));

    let explanation = "";
    if (conflictKind === "RoleConflict") {
      explanation = `Role Conflict: A document has already fulfilled the ${roleLabel(conflictingRole || "Award")} role for Award ${targetAwardNumber || conflictingAwardNumber || ""}.`;
    } else if (conflictKind === "AlreadyConfirmedDifferentDecision") {
      explanation = `State Conflict: This document was already confirmed under a different role or decision.`;
    } else if (conflictKind === "StagedOrConcurrentIntakeConflict") {
      explanation = `Concurrent Conflict: A concurrent upload or staged intake is already in progress.`;
    } else if (conflictKind === "AwardIdentityConflict") {
      explanation = `Award Mismatch: The document does not match Award ${targetAwardNumber || ""}.`;
    } else if (isSameAward) {
      explanation = `Already uploaded: This document is already attached to Award ${targetAwardNumber || conflictingAwardNumber || ""}${conflictingRole ? ` as ${roleLabel(conflictingRole)}` : ""}.`;
    } else if (conflictingAwardNumber) {
      explanation = `This PDF is already attached to Award ${conflictingAwardNumber}${conflictingVillageName ? `, Village ${conflictingVillageName}` : ""}.`;
    } else {
      explanation = "This PDF is already linked to another Award. Existing Award could not be identified.";
    }

    return {
      isConflict: true,
      conflictKind,
      isSameAward,
      conflictingAwardId,
      conflictingAwardNumber,
      conflictingRole,
      conflictingVillageName,
      documentId,
      viewRoute,
      explanation,
    };
  }

  return {
    isConflict: false,
    conflictKind: "GenericConflict",
    isSameAward: false,
    conflictingAwardId: null,
    conflictingAwardNumber: null,
    conflictingRole: null,
    conflictingVillageName: null,
    documentId: null,
    viewRoute: null,
    explanation: "",
  };
}

// ─── Document Ownership Conflict Modal ───────────────────────────────────────

export interface DocumentConflictModalProps {
  fileName: string;
  conflictKind?: ConflictKind;
  conflictingAwardNumber?: string | null;
  conflictingAwardId?: string | null;
  conflictingRole?: CoreDocumentRole | string | null;
  conflictingVillageName?: string | null;
  villageId: string;
  targetAwardNumber?: string | null;
  targetAwardId?: string | null;
  targetRole?: CoreDocumentRole | string | null;
  documentId?: string | null;
  viewRoute?: string | null;
  onClose: () => void;
  onViewExistingAward: (awardId: string) => void;
  onCancelUpload: () => void;
}

export const DocumentConflictModal: React.FC<DocumentConflictModalProps> = ({
  fileName,
  conflictKind = "SourceLinkedToAnotherAward",
  conflictingAwardNumber,
  conflictingAwardId,
  conflictingRole,
  conflictingVillageName,
  targetAwardNumber,
  targetRole,
  onClose,
  onViewExistingAward,
  onCancelUpload,
}) => {
  const { hasPermission } = useSafeAuth();
  // Permission strictly requires Award.Edit — Award.CoreDocument.Upload is insufficient
  const canEdit = hasPermission("Award.Edit");
  const [showReassign, setShowReassign] = useState(false);
  const [showMultiPolicy, setShowMultiPolicy] = useState(false);
  const [reassignReason, setReassignReason] = useState("");

  const isRoleConflict = conflictKind === "RoleConflict";
  const isStateConflict = conflictKind === "AlreadyConfirmedDifferentDecision";
  const isConcurrentConflict = conflictKind === "StagedOrConcurrentIntakeConflict";
  const isIdentityConflict = conflictKind === "AwardIdentityConflict";

  const titleText = isRoleConflict
    ? "Document Role Conflict"
    : isStateConflict
    ? "Document State Conflict"
    : isConcurrentConflict
    ? "Concurrent Intake Conflict"
    : isIdentityConflict
    ? "Award Identity Mismatch"
    : "Document Ownership Conflict";

  const subtitleText = isRoleConflict
    ? "Statutory role slot is already occupied for this Award"
    : isStateConflict
    ? "Conflicting confirmation decision already recorded on this record"
    : isConcurrentConflict
    ? "Concurrent upload or staged processing currently in progress"
    : isIdentityConflict
    ? "Document identifier conflicts with target Award identity"
    : "Registered record collision detected in acquisition repository";

  return (
    <div className="doc-conflict-modal-overlay" onClick={onClose}>
      <div className="doc-conflict-modal" onClick={(e) => e.stopPropagation()}>
        <div
          className="village-modal-header"
          style={{ borderBottom: "1px solid #e2e8f0", padding: "14px 18px" }}
        >
          <div>
            <h3 className="village-modal-title" style={{ fontSize: "16px", color: "#0f172a" }}>
              {titleText}
            </h3>
            <p className="village-modal-subtitle" style={{ fontSize: "12.5px" }}>
              {subtitleText}
            </p>
          </div>
          <button className="village-modal-close" onClick={onClose}>
            &times;
          </button>
        </div>

        {/* Clear explanation banner */}
        <div className="doc-conflict-banner">
          <div className="doc-conflict-banner-title">
            {isRoleConflict ? (
              <>⚠️ Role Conflict: A document has already fulfilled the {roleLabel(targetRole || conflictingRole || null)} role for Award {targetAwardNumber || conflictingAwardNumber || "this award"}.</>
            ) : isStateConflict ? (
              <>⚠️ Document Already Confirmed: This document was already confirmed under a different role or decision.</>
            ) : isConcurrentConflict ? (
              <>⚠️ Concurrent Upload in Progress: A staged intake is currently active for this Award.</>
            ) : isIdentityConflict ? (
              <>⚠️ Award Mismatch: Document identified for {conflictingAwardNumber ? `Award ${conflictingAwardNumber}` : "another award"}, not Award {targetAwardNumber || "selected"}.</>
            ) : conflictingAwardNumber ? (
              <>⚠️ This PDF is already attached to Award {conflictingAwardNumber}{conflictingVillageName ? `, Village ${conflictingVillageName}` : ""}.</>
            ) : (
              <>⚠️ This PDF is already linked to another Award. Existing Award could not be identified.</>
            )}
          </div>
          <p className="doc-conflict-banner-text">
            {isRoleConflict ? (
              "Under statutory acquisition procedures, each role slot (Award, NM, Statement A, Possession Proceeding) can only be occupied by one primary instrument. Replace or remove the existing document before attaching a new one."
            ) : isStateConflict ? (
              "This document has already been confirmed under an alternate role or decision. Conflicting duplicate decisions cannot be applied simultaneously."
            ) : isConcurrentConflict ? (
              "Another intake session is currently staging or processing documents for this Award. Please wait or refresh the workspace."
            ) : (
              `This PDF has an identical cryptographic hash (SHA-256) to an active statutory document registered under ${conflictingAwardNumber ? `Award ${conflictingAwardNumber}` : "another Award"}. To protect statutory land acquisition integrity and prevent accidental misattribution, direct duplicate linking is blocked by backend policy.`
            )}
          </p>
        </div>

        {/* Conflict Details Grid */}
        <div className="doc-conflict-details">
          <div className="doc-conflict-detail-cell">
            <div className="doc-conflict-detail-label">Source PDF</div>
            <div className="doc-conflict-detail-val">📄 {fileName}</div>
          </div>
          <div className="doc-conflict-detail-cell">
            <div className="doc-conflict-detail-label">Current Ownership</div>
            <div className="doc-conflict-detail-val">
              {conflictingAwardNumber ? (
                <>Award {conflictingAwardNumber}{conflictingRole ? ` (${roleLabel(conflictingRole)})` : ""}</>
              ) : (
                <span style={{ color: "#64748b", fontStyle: "italic" }}>Existing Award could not be identified</span>
              )}
            </div>
          </div>
          <div className="doc-conflict-detail-cell">
            <div className="doc-conflict-detail-label">Target / Selected</div>
            <div className="doc-conflict-detail-val">
              Award {targetAwardNumber || "Not selected"}{targetRole ? ` (${roleLabel(targetRole)})` : ""}
            </div>
          </div>
          <div className="doc-conflict-detail-cell">
            <div className="doc-conflict-detail-label">Jurisdiction</div>
            <div className="doc-conflict-detail-val">
              {conflictingVillageName ? `Village ${conflictingVillageName}` : "—"}
            </div>
          </div>
        </div>

        {/* Multi-Award policy explanation */}
        {showMultiPolicy && (
          <div className="doc-conflict-policy-note">
            <strong>Backend Policy Restriction:</strong> Under Codex's published Smart Core Intake API contract, core acquisition documents (Award, NM, Statement A, Possession) strictly enforce single-Award exclusivity. A document registered to {conflictingAwardNumber ? `Award ${conflictingAwardNumber}` : "another Award"} cannot be linked to multiple Awards concurrently. Client-side workarounds to bypass this constraint are prohibited.
          </div>
        )}

        {/* Reassign Panel */}
        {showReassign && (
          <div className="doc-conflict-reassign-panel">
            <div className="doc-conflict-reassign-title">
              Request Correct Link / Document Reassignment
            </div>
            {!canEdit ? (
              <div className="doc-conflict-perm-error">
                ⛔ Permission required: Award.Edit. Contact your administrator to request reallocation.
              </div>
            ) : (
              <>
                <div className="doc-conflict-unavailable-notice">
                  <strong>Administrative Action:</strong> Reassignment requires administrative correction — not yet available.
                  <p style={{ margin: "4px 0 0 0", fontSize: "12px", color: "#64748b" }}>
                    The Codex Smart Core Intake API does not currently provide an authorized reassignment-request endpoint. Reassignment cannot be simulated locally. Direct client-side bypass is prohibited by backend policy. Please coordinate administrative correction with the Land Records Authority.
                  </p>
                </div>
                <p className="doc-conflict-reassign-desc">
                  Reassigning this document moves canonical acquisition provenance from {conflictingAwardNumber ? `Award ${conflictingAwardNumber}` : "existing Award"} to Award {targetAwardNumber || "selected"}. Under backend statutory governance, an explicit justification is required once the administrative workflow is active.
                </p>
                <label className="doc-conflict-reason-label">
                  Statutory Justification / Reassignment Reason *
                </label>
                <textarea
                  className="doc-conflict-reason-input"
                  rows={3}
                  placeholder="State statutory reason for reassignment (e.g., misattributed during batch upload, confirmed revenue record for Award...)"
                  value={reassignReason}
                  onChange={(e) => setReassignReason(e.target.value)}
                  disabled={true}
                />
                <div style={{ display: "flex", gap: "8px" }}>
                  <button
                    className="village-btn village-btn-primary"
                    disabled={true}
                    title="Reassignment requires administrative correction — not yet available"
                  >
                    Submit Reassignment Request (Unavailable)
                  </button>
                  <button
                    className="village-btn village-btn-outline"
                    onClick={() => setShowReassign(false)}
                  >
                    Back
                  </button>
                </div>
              </>
            )}
          </div>
        )}

        {/* Authorized Actions Bar */}
        <div className="doc-conflict-actions-bar">
          <button
            className="village-btn village-btn-outline"
            disabled={!conflictingAwardId}
            title={!conflictingAwardId ? "Existing Award ID is unavailable" : undefined}
            onClick={() => {
              if (conflictingAwardId) {
                onClose();
                onViewExistingAward(conflictingAwardId);
              }
            }}
          >
            👁️ View Existing Award {!conflictingAwardId && <span style={{ fontSize: "11px", color: "#64748b", marginLeft: "4px" }}>(ID unavailable)</span>}
          </button>
          <button
            className="village-btn village-btn-outline"
            onClick={onCancelUpload}
          >
            ✖ Cancel Upload
          </button>
          <button
            className="village-btn village-btn-primary"
            onClick={() => {
              setShowReassign((v) => !v);
              setShowMultiPolicy(false);
            }}
          >
            🔄 Request Correct Link / Reassign
          </button>
          <button
            className="village-btn village-btn-outline"
            onClick={() => {
              setShowMultiPolicy((v) => !v);
              setShowReassign(false);
            }}
          >
            🔗 Link to Multiple Awards
          </button>
        </div>
      </div>
    </div>
  );
};

// ─── Main component ───────────────────────────────────────────────────────────

export const VillageCoreRecordsWorkspace: React.FC<VillageCoreRecordsProps> = ({ id }) => {
  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<AwardCoreRecord[]>([]);
  const [villageName, setVillageName] = useState<string>("");
  const [highlightedAwardId, setHighlightedAwardId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal state
  const [showUploadModal, setShowUploadModal] = useState(false);
  const [showAddAwardModal, setShowAddAwardModal] = useState(false);
  const [quickAttach, setQuickAttach] = useState<{
    awardId: string;
    awardNumber: string;
    role: CoreDocumentRole;
  } | null>(null);

  // Manual Award creation
  const [manualAward, setManualAward] = useState({
    awardNumber: "",
    awardDate: "",
    awardType: "Regular",
  });
  const [creatingAward, setCreatingAward] = useState(false);
  const [awardError, setAwardError] = useState("");

  const navigate = useNavigate();

  const handleViewExistingAward = (targetAwardId: string) => {
    setShowUploadModal(false);
    setQuickAttach(null);
    const existingAward = records.find((a) => a.id === targetAwardId);
    if (existingAward) {
      setHighlightedAwardId(targetAwardId);
      setTimeout(() => {
        const el = document.getElementById(`award-${targetAwardId}`);
        if (el) {
          el.scrollIntoView({ behavior: "smooth", block: "center" });
        }
      }, 100);
    } else {
      // Award is outside currently loaded village records: navigate to /awards/{targetAwardId}
      navigate(`/awards/${targetAwardId}`);
    }
  };

  useEffect(() => {
    let active = true;
    setLoading(true);

    fetch(`/api/villages/${id}`, { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then((vData) => {
        if (active && vData?.name) setVillageName(vData.name);
      })
      .catch(() => {});

    fetch(`/api/villages/${id}/core-records?r=${refresh}`, {
      credentials: "include",
    })
      .then((r) => {
        if (!r.ok) throw new Error(`Failed to load core records (${r.status})`);
        return r.json();
      })
      .then((data) => {
        if (active) {
          setRecords(Array.isArray(data) ? data : []);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err.message || "Failed to load core records.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [id, refresh]);

  const handleCreateAward = async () => {
    if (!manualAward.awardNumber.trim()) {
      setAwardError("Award number is required.");
      return;
    }
    setCreatingAward(true);
    setAwardError("");
    try {
      const res = await fetch(`/api/villages/${id}/awards`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          awardNumber: manualAward.awardNumber.trim(),
          awardDate: manualAward.awardDate || null,
          awardType: manualAward.awardType || null,
          remarks: null,
        }),
        credentials: "include",
      });
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || "Could not add award.");
      }
      setManualAward({ awardNumber: "", awardDate: "", awardType: "Regular" });
      setShowAddAwardModal(false);
      setRefresh((x) => x + 1);
    } catch (e: any) {
      setAwardError(e.message || "Error creating award.");
    } finally {
      setCreatingAward(false);
    }
  };

  return (
    <div className="village-core-workspace" style={{ marginTop: "12px" }}>
      {/* Header */}
      <div className="village-core-header">
        <div>
          <h2 className="village-core-title">Award Core Records &amp; Documents</h2>
          <p className="village-core-subtitle">
            Statutory acquisition instruments: Award PDFs, Naksha Muntazmins (NM),
            Statement A registers, and Possession proceedings.
          </p>
        </div>
        <div className="village-core-actions">
          <button
            className="village-btn village-btn-outline"
            onClick={() => {
              setAwardError("");
              setShowAddAwardModal(true);
            }}
          >
            + Add Award Manually
          </button>
          <button
            className="village-btn village-btn-primary"
            onClick={() => setShowUploadModal(true)}
          >
            + Upload Core Documents
          </button>
        </div>
      </div>

      {/* Records list */}
      {loading ? (
        <div className="land-loading-container" style={{ padding: "40px", textAlign: "center" }}>
          <div className="land-spinner" />
          <p style={{ marginTop: "12px", color: "#64748b", fontSize: "14px" }}>
            Loading core records &amp; document sets…
          </p>
        </div>
      ) : error ? (
        <div className="land-error-banner">
          <span>⚠️</span>
          <div>{error}</div>
        </div>
      ) : records.length === 0 ? (
        <div className="village-empty-state">
          <p className="village-empty-title">No Awards Registered Yet</p>
          <p className="village-empty-desc">
            Use "Upload Core Documents" to ingest acquisition PDFs — the system will
            detect Award numbers and document types automatically. Or add an Award manually.
          </p>
        </div>
      ) : (
        <div className="village-award-cards-list">
          {records.map((award) => (
            <AwardCoreCard
              key={award.id}
              award={award}
              isHighlighted={highlightedAwardId === award.id}
              onAttach={(awardId, awardNumber, role) =>
                setQuickAttach({ awardId, awardNumber, role })
              }
            />
          ))}
        </div>
      )}

      {/* Smart Intake Upload Modal */}
      {showUploadModal && (
        <SmartCoreUploadModal
          villageId={id}
          villageName={villageName}
          awards={records}
          onClose={() => setShowUploadModal(false)}
          onViewExistingAward={handleViewExistingAward}
          onSuccess={() => {
            setShowUploadModal(false);
            setRefresh((x) => x + 1);
          }}
        />
      )}

      {/* Add Award Manually Modal */}
      {showAddAwardModal && (
        <div className="modal-backdrop" onClick={() => setShowAddAwardModal(false)}>
          <div
            className="modal-card"
            style={{ maxWidth: "500px" }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="village-modal-header">
              <h3 className="village-modal-title">Add Award Manually</h3>
              <button
                className="village-modal-close"
                onClick={() => setShowAddAwardModal(false)}
              >
                &times;
              </button>
            </div>

            {awardError && (
              <div className="land-error-banner" style={{ marginBottom: "14px" }}>
                <span>⚠️</span>
                <div>{awardError}</div>
              </div>
            )}

            <div style={{ display: "grid", gap: "14px", marginTop: "12px" }}>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Number *</label>
                <input
                  type="text"
                  placeholder="e.g. 30/2002-03"
                  value={manualAward.awardNumber}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardNumber: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                  autoFocus
                />
              </div>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Date</label>
                <input
                  type="date"
                  value={manualAward.awardDate}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardDate: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                />
              </div>
              <div className="form-group">
                <label style={{ fontWeight: 600, fontSize: "13px" }}>Award Type</label>
                <select
                  value={manualAward.awardType}
                  onChange={(e) =>
                    setManualAward({ ...manualAward, awardType: e.target.value })
                  }
                  style={{ width: "100%", padding: "8px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                >
                  <option value="Regular">Regular</option>
                  <option value="Supplementary">Supplementary</option>
                  <option value="Special">Special</option>
                </select>
              </div>
            </div>

            <div className="village-modal-footer">
              <button
                className="village-btn village-btn-outline"
                onClick={() => setShowAddAwardModal(false)}
                disabled={creatingAward}
              >
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleCreateAward()}
                disabled={creatingAward || !manualAward.awardNumber.trim()}
              >
                {creatingAward ? "Saving…" : "Save Award"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Explicit Award/role attachment uses the common V2 canonical path. */}
      {quickAttach && (
        <QuickAttachModal
          villageId={id}
          villageName={villageName}
          awardId={quickAttach.awardId}
          awardNumber={quickAttach.awardNumber}
          role={quickAttach.role}
          awards={records}
          onClose={() => setQuickAttach(null)}
          onViewExistingAward={handleViewExistingAward}
          onSuccess={() => {
            setQuickAttach(null);
            setRefresh((x) => x + 1);
          }}
        />
      )}
      {/* Preserved Anti temporary UI; separate from canonical Award registration. */}
      <VillageTemporaryProceedingsSection villageId={id} villageName={villageName}
        linkedAwards={records.map(r => ({ id: r.id, awardNumber: r.awardNumber }))}
        onRefresh={() => setRefresh(x => x + 1)} />
    </div>
  );
};

// ─── Award Core Card ──────────────────────────────────────────────────────────

interface AwardCoreCardProps {
  award: AwardCoreRecord;
  isHighlighted?: boolean;
  onAttach: (awardId: string, awardNumber: string, role: CoreDocumentRole) => void;
}

const AwardCoreCard: React.FC<AwardCoreCardProps> = ({ award, isHighlighted, onAttach }) => {
  return (
    <div
      id={`award-${award.id}`}
      className={`village-award-card ${isHighlighted ? "village-award-card-highlighted" : ""}`}
    >
      <div className="village-award-card-header">
        <div className="village-award-title-group">
          <Link to={`/awards/${award.id}`} className="village-award-card-num">
            Award {award.awardNumber}
          </Link>
          <div className="village-award-meta-row">
            <span className="village-award-meta-item">📅 {formatDate(award.awardDate)}</span>
            <span className="village-award-meta-sep">·</span>
            <span className="village-award-meta-item">{award.awardType || "Acquisition Award"}</span>
          </div>
        </div>
        <div className="village-award-card-cta">
          <Link to={`/awards/${award.id}`} className="village-open-award-link">
            Open Award Workspace &rarr;
          </Link>
        </div>
      </div>

      <div className="village-core-docs-grid">
        {CORE_ROLES.map(({ role, label, description }) => {
          const docs = docsForRole(award, role);
          const hasDocs = docs.length > 0;

          return (
            <div key={role} className={`village-doc-slot ${hasDocs ? "slot-filled" : "slot-empty"}`}>
              <div className="village-doc-slot-header">
                <span className="village-doc-role-name">{label}</span>
                {hasDocs ? (
                  <span className="village-doc-badge badge-uploaded">Uploaded</span>
                ) : (
                  <span className="village-doc-badge badge-missing">Missing</span>
                )}
              </div>

              <p className="village-doc-slot-desc">{description}</p>

              <div className="village-doc-slot-content">
                {hasDocs ? (
                  <div className="village-doc-file-info">
                    {docs.map((doc) => (
                      <div key={doc.documentId} className="village-doc-file-entry">
                        <div className="village-doc-filename" title={doc.originalFileName}>
                          📄 {doc.originalFileName}
                        </div>
                        <div className="village-doc-date">
                          Attached {formatDate(doc.uploadedAt)}
                          {doc.legalDate && <span> · Legal date {formatDate(doc.legalDate)}</span>}
                        </div>
                        <div style={{ marginTop: "6px" }}>
                          <a
                            href={doc.viewRoute || `/api/documents/${doc.documentId}/content`}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="village-doc-view-btn"
                          >
                            View PDF ↗
                          </a>
                        </div>
                      </div>
                    ))}
                    <button className="village-doc-attach-btn" onClick={() => onAttach(award.id, award.awardNumber, role)}>+ Attach another</button>
                  </div>
                ) : (
                  <div className="village-doc-missing-box">
                    <span className="village-doc-missing-text">— Not uploaded</span>
                    <button
                      className="village-doc-attach-btn"
                      onClick={() => onAttach(award.id, award.awardNumber, role)}
                    >
                      + Attach
                    </button>
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

// ─── Smart Core Upload Modal ──────────────────────────────────────────────────

type UploadPhase = "select" | "uploading" | "review" | "done";

interface QueuedFile {
  id: string;
  file: File;
  /** Intake result after upload, or null if still pending / errored */
  intake: IntakeItem | null;
  uploadError: string | null;
  parsedProblem?: ParsedProblemDetails | null;
}

interface SmartCoreUploadModalProps {
  villageId: string;
  villageName?: string;
  awards: AwardCoreRecord[];
  onClose: () => void;
  onSuccess: () => void;
  onViewExistingAward?: (awardId: string) => void;
}

const SmartCoreUploadModal: React.FC<SmartCoreUploadModalProps> = ({
  villageId,
  villageName = "",
  awards,
  onClose,
  onSuccess,
  onViewExistingAward,
}) => {
  const [phase, setPhase] = useState<UploadPhase>("select");
  const [files, setFiles] = useState<File[]>([]);
  const [isDragging, setIsDragging] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const [queuedItems, setQueuedItems] = useState<QueuedFile[]>([]);
  const [confirming, setConfirming] = useState<Record<string, boolean>>({});
  const [activeConflict, setActiveConflict] = useState<
    (ConflictDetection & { fileName: string; targetRole: CoreDocumentRole; queuedId: string; targetAwardId?: string | null }) | null
  >(null);
  const [ownership, setOwnership] = useState<PdfOwnershipConflict | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const addFiles = (incoming: FileList | File[]) => {
    const pdfs: File[] = [];
    for (let i = 0; i < incoming.length; i++) {
      const f = incoming[i];
      if (f.type === "application/pdf" || f.name.toLowerCase().endsWith(".pdf")) {
        pdfs.push(f);
      }
    }
    if (pdfs.length === 0) return;
    setFiles((prev) => {
      const existing = new Set(prev.map((f) => f.name + f.size));
      return [...prev, ...pdfs.filter((f) => !existing.has(f.name + f.size))];
    });
    setUploadError("");
  };

  const removeFile = (idx: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== idx));
  };

  const handleUpload = async () => {
    if (files.length === 0) return;
    setPhase("uploading");
    setUploadError("");

    const result = await smartIntake.uploadBatch(villageId, files);
    if (!result.ok) {
      if (result.parsedProblem?.isConflict) {
        const items: QueuedFile[] = files.map((f, i) => ({
          id: `item-${i}-${f.name}`,
          file: f,
          intake: null,
          uploadError: result.error,
          parsedProblem: result.parsedProblem,
        }));
        setQueuedItems(items);
        setPhase("review");
        return;
      }
      setUploadError(result.error);
      setPhase("select");
      return;
    }

    const items: QueuedFile[] = result.result.items.map((item, i) => {
      const parsedProblem = item.error
        ? parseProblemDetails(JSON.stringify({ message: item.error, code: item.code, conflicts: item.conflicts }), item.errorStatus || 400)
        : null;
      return {
        id: `item-${i}-${item.fileName}`,
        file: files[i] ?? new File([], item.fileName),
        intake: item.intake,
        uploadError: item.error,
        parsedProblem,
      };
    });

    setQueuedItems(items);
    setPhase("review");
  };

  // How many intakes still need officer action
  const pendingCount = queuedItems.filter(
    (q) => q.intake && q.intake.status !== "Confirmed"
  ).length;

  const handleConfirmExisting = async (
    q: QueuedFile,
    documentRole: CoreDocumentRole,
    awardId: string,
    legalDate?: string
  ) => {
    if (!q.intake) return;
    setConfirming((prev) => ({ ...prev, [q.id]: true }));
    const res = await smartIntake.confirmExistingAward(q.intake.intakeId, {
      documentRole,
      awardId,
      legalDate: legalDate || null,
      expectedRevision: q.intake.revision,
    });
    setConfirming((prev) => ({ ...prev, [q.id]: false }));
    if (res.ok) {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id ? { ...item, intake: res.intake, uploadError: null, parsedProblem: null } : item
        )
      );
    } else {
      const conflict = detectDocumentConflict({
        intake: q.intake,
        uploadError: res.error,
        parsedProblem: res.parsedProblem,
        targetAwardId: awardId,
        targetAwardNumber: awards.find((a) => a.id === awardId)?.awardNumber,
        awards,
        villageName,
        fileName: q.file.name,
      });
      if (res.parsedProblem?.ownershipConflict) { setOwnership(res.parsedProblem.ownershipConflict); }
      else if (conflict.isConflict && !conflict.isSameAward) {
        setActiveConflict({
          ...conflict,
          fileName: q.file.name,
          targetRole: documentRole,
          queuedId: q.id,
          targetAwardId: res.parsedProblem?.ownershipConflict?.selectedAwardId || null,
        });
      }
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id
            ? { ...item, uploadError: res.error, parsedProblem: res.parsedProblem }
            : item
        )
      );
    }
  };

  const handleConfirmNewAward = async (
    q: QueuedFile,
    documentRole: CoreDocumentRole,
    awardNumber: string,
    awardDate: string | null,
    awardType: string | null
  ) => {
    if (!q.intake) return;
    setConfirming((prev) => ({ ...prev, [q.id]: true }));
    const res = await smartIntake.confirmNewAward(q.intake.intakeId, {
      documentRole,
      createAward: {
        awardNumber,
        awardDate: awardDate || null,
        awardType: awardType || null,
        confirmed: true,
      },
    });
    setConfirming((prev) => ({ ...prev, [q.id]: false }));
    if (res.ok) {
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id ? { ...item, intake: res.intake, uploadError: null, parsedProblem: null } : item
        )
      );
    } else {
      const conflict = detectDocumentConflict({
        intake: q.intake,
        uploadError: res.error,
        parsedProblem: res.parsedProblem,
        targetAwardNumber: awardNumber,
        awards,
        villageName,
        fileName: q.file.name,
      });
      if (res.parsedProblem?.ownershipConflict) { setOwnership(res.parsedProblem.ownershipConflict); }
      else if (conflict.isConflict && !conflict.isSameAward) {
        setActiveConflict({
          ...conflict,
          fileName: q.file.name,
          targetRole: documentRole,
          queuedId: q.id,
          targetAwardId: res.parsedProblem?.ownershipConflict?.selectedAwardId || null,
        });
      }
      setQueuedItems((prev) =>
        prev.map((item) =>
          item.id === q.id
            ? { ...item, uploadError: res.error, parsedProblem: res.parsedProblem }
            : item
        )
      );
    }
  };

  const handleDoneAll = () => {
    onSuccess();
  };

  return (
    <div className="modal-backdrop" onClick={phase === "select" ? onClose : undefined}>
      <div
        className="modal-card intake-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="village-modal-header">
          <div>
            <h3 className="village-modal-title">Upload Core Acquisition Documents</h3>
            <p className="village-modal-subtitle">
              {phase === "select" && "Drop PDFs — the system detects Award numbers and document types automatically."}
              {phase === "uploading" && "Uploading and analysing…"}
              {phase === "review" && `Review detection results and confirm each document. ${pendingCount > 0 ? `${pendingCount} pending.` : "All confirmed!"}`}
              {phase === "done" && "All documents confirmed and linked."}
            </p>
          </div>
          <button className="village-modal-close" onClick={onClose}>
            &times;
          </button>
        </div>

        {uploadError && (
          <div className="land-error-banner" style={{ margin: "10px 0" }}>
            <span>⚠️</span>
            <div>{uploadError}</div>
          </div>
        )}

        {/* ── Phase: SELECT ── */}
        {phase === "select" && (
          <>
            <div
              className={`village-dropzone ${isDragging ? "dragging" : ""}`}
              onDragOver={(e) => { e.preventDefault(); setIsDragging(true); }}
              onDragLeave={() => setIsDragging(false)}
              onDrop={(e) => { e.preventDefault(); setIsDragging(false); if (e.dataTransfer?.files) addFiles(e.dataTransfer.files); }}
              onClick={() => fileInputRef.current?.click()}
            >
              <input
                ref={fileInputRef}
                type="file"
                accept="application/pdf,.pdf"
                multiple
                style={{ display: "none" }}
                onChange={(e) => { if (e.target.files) addFiles(e.target.files); }}
              />
              <div className="village-dropzone-icon">📥</div>
              <div className="village-dropzone-text">
                <strong>Drag and drop PDF files here</strong>, or{" "}
                <span style={{ color: "#2563eb", textDecoration: "underline" }}>browse from computer</span>
              </div>
              <div className="village-dropzone-hint">
                Up to 20 PDFs · 50 MB each · Award, NM, Statement A, Possession Proceeding
              </div>
            </div>

            {files.length > 0 && (
              <div className="village-queue-list" style={{ marginTop: "12px" }}>
                <div className="village-queue-heading">
                  <span>Selected ({files.length})</span>
                </div>
                {files.map((f, i) => (
                  <div key={`${f.name}-${i}`} className="village-queue-item">
                    <div className="village-queue-file-desc">
                      <span className="village-queue-filename">📄 {f.name}</span>
                      <span className="village-queue-filesize">
                        ({(f.size / 1024).toFixed(0)} KB)
                      </span>
                      <span className="intake-role-hint">
                        Likely: {roleLabel(guessDocumentRole(f.name))}
                      </span>
                    </div>
                    <button
                      className="village-queue-remove-btn"
                      onClick={() => removeFile(i)}
                      title="Remove"
                    >
                      &times;
                    </button>
                  </div>
                ))}
              </div>
            )}

            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose}>
                Cancel
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={() => void handleUpload()}
                disabled={files.length === 0}
              >
                Analyse {files.length > 0 ? `${files.length} ` : ""}Document{files.length === 1 ? "" : "s"}
              </button>
            </div>
          </>
        )}

        {/* ── Phase: UPLOADING ── */}
        {phase === "uploading" && (
          <div style={{ padding: "40px", textAlign: "center" }}>
            <div className="land-spinner" style={{ margin: "0 auto" }} />
            <p style={{ marginTop: "16px", color: "#64748b", fontSize: "14px" }}>
              Uploading {files.length} file{files.length === 1 ? "" : "s"} and running document classifier…
            </p>
          </div>
        )}

        {/* ── Phase: REVIEW ── */}
        {phase === "review" && (
          <>
            <div className="intake-review-list">
              {queuedItems.map((q) => (
                <IntakeReviewCard
                  key={q.id}
                  item={q}
                  awards={awards}
                  villageName={villageName}
                  isConfirming={!!confirming[q.id]}
                  onConfirmExisting={(role, awardId, date) =>
                    void handleConfirmExisting(q, role, awardId, date)
                  }
                  onConfirmNewAward={(role, num, date, type) =>
                    void handleConfirmNewAward(q, role, num, date, type)
                  }
                  onOpenConflict={(conflict, item) =>
                    setActiveConflict({
                      ...conflict,
                      fileName: item.file.name,
                      targetRole: (item.intake?.detectedRole as CoreDocumentRole) || "Award",
                      queuedId: item.id,
                    })
                  }
                  onRemoveItem={(itemId) =>
                    setQueuedItems((prev) => prev.filter((it) => it.id !== itemId))
                  }
                />
              ))}
            </div>

            <div className="village-modal-footer">
              <button className="village-btn village-btn-outline" onClick={onClose}>
                Close
              </button>
              <button
                className="village-btn village-btn-primary"
                onClick={handleDoneAll}
                disabled={pendingCount > 0}
              >
                {pendingCount > 0
                  ? `${pendingCount} Pending Confirmation`
                  : "Done — Refresh Records"}
              </button>
            </div>
          </>
        )}

        {ownership && <div role="alert"><AwardPdfOwnershipConflictPanel conflict={ownership} onResolved={() => { setOwnership(null); setUploadError("Ownership reviewed. Confirm the staged attachment again to complete intake."); }} /></div>}
        {activeConflict && (
          <DocumentConflictModal
            fileName={activeConflict.fileName}
            conflictKind={activeConflict.conflictKind}
            conflictingAwardNumber={activeConflict.conflictingAwardNumber}
            conflictingAwardId={activeConflict.conflictingAwardId}
            conflictingRole={activeConflict.conflictingRole}
            conflictingVillageName={activeConflict.conflictingVillageName || villageName}
            villageId={villageId}
            targetAwardNumber={
              activeConflict.targetAwardId
                ? awards.find((a) => a.id === activeConflict.targetAwardId)?.awardNumber || ""
                : ""
            }
            targetAwardId={activeConflict.targetAwardId}
            targetRole={activeConflict.targetRole}
            documentId={activeConflict.documentId}
            viewRoute={activeConflict.viewRoute}
            onClose={() => setActiveConflict(null)}
            onViewExistingAward={(existingAwardId) => {
              setActiveConflict(null);
              onClose();
              onViewExistingAward?.(existingAwardId);
            }}
            onCancelUpload={() => {
              const idToRemove = activeConflict.queuedId;
              setActiveConflict(null);
              setQueuedItems((prev) => prev.filter((it) => it.id !== idToRemove));
            }}
          />
        )}
      </div>
    </div>
  );
};

// ─── Per-file Intake Review Card ─────────────────────────────────────────────

interface IntakeReviewCardProps {
  item: QueuedFile;
  awards: AwardCoreRecord[];
  villageName?: string;
  isConfirming: boolean;
  onConfirmExisting: (role: CoreDocumentRole, awardId: string, legalDate?: string) => void;
  onConfirmNewAward: (
    role: CoreDocumentRole,
    awardNumber: string,
    awardDate: string | null,
    awardType: string | null
  ) => void;
  onOpenConflict?: (conflict: ConflictDetection, item: QueuedFile) => void;
  onRemoveItem?: (itemId: string) => void;
}

const IntakeReviewCard: React.FC<IntakeReviewCardProps> = ({
  item,
  awards,
  villageName = "",
  isConfirming,
  onConfirmExisting,
  onConfirmNewAward,
  onOpenConflict,
  onRemoveItem,
}) => {
  const { intake, uploadError, parsedProblem } = item;
  const [showEvidence, setShowEvidence] = useState(false);
  const [legalDate, setLegalDate] = useState("");

  // Editable officer overrides (pre-filled from detection)
  const [selectedRole, setSelectedRole] = useState<CoreDocumentRole>(
    (intake?.detectedRole as CoreDocumentRole) === "Unknown"
      ? "Award"
      : (intake?.detectedRole as CoreDocumentRole) ?? "Award"
  );
  const [selectedAwardId, setSelectedAwardId] = useState<string>(
    intake?.matchedAwardId ?? awards[0]?.id ?? ""
  );

  // New Award form (for ProposedNewAward)
  const [newAwardNumber, setNewAwardNumber] = useState(
    intake?.detectedAwardNumber ?? ""
  );
  const [newAwardDate, setNewAwardDate] = useState(
    intake?.detectedAwardDate ?? ""
  );
  const [newAwardType, setNewAwardType] = useState(
    intake?.detectedAwardType ?? ""
  );
  const [showNewAwardForm, setShowNewAwardForm] = useState(false);

  const conflict = detectDocumentConflict({
    intake,
    uploadError,
    parsedProblem,
    targetAwardId: selectedAwardId,
    targetAwardNumber: awards.find((a) => a.id === selectedAwardId)?.awardNumber,
    awards,
    villageName,
    fileName: item.file.name,
  });

  // Handle same-award match: reserved strictly for server-confirmed duplicate identification (isDuplicate: true)
  if (conflict.isConflict && conflict.isSameAward && intake?.isDuplicate && !uploadError) {
    return (
      <div className="intake-card doc-already-uploaded-card">
        <div className="intake-card-top">
          <span className="intake-card-filename">📄 {item.file.name}</span>
          <span className="intake-badge intake-badge-confirmed">Already uploaded</span>
        </div>
        <div className="doc-already-uploaded-text" style={{ marginTop: "10px" }}>
          {conflict.explanation}
        </div>
        <div className="intake-action-row" style={{ marginTop: "12px", display: "flex", gap: "8px" }}>
          {(conflict.viewRoute || conflict.documentId) && (
            <button
              className="village-btn village-btn-primary"
              onClick={() => {
                const url = conflict.viewRoute || `/api/documents/${conflict.documentId}/content`;
                window.open(url, "_blank");
              }}
            >
              📄 Open Existing Document
            </button>
          )}
          {onRemoveItem && (
            <button
              className="village-btn village-btn-outline"
              onClick={() => onRemoveItem(item.id)}
            >
              Cancel Upload
            </button>
          )}
        </div>
      </div>
    );
  }

  if (uploadError && !intake) {
    if (conflict.isConflict) {
      return (
        <div className="intake-card intake-card-error">
          <div className="intake-card-top">
            <span className="intake-card-filename">📄 {item.file.name}</span>
            <span className="intake-badge intake-badge-conflict">
              {conflict.conflictKind === "RoleConflict"
                ? "Role Conflict"
                : conflict.conflictKind === "AlreadyConfirmedDifferentDecision"
                ? "State Conflict"
                : "Ownership Conflict"}
            </span>
          </div>
          <div className="doc-conflict-card-banner">
            <div style={{ fontWeight: 600, color: "#991b1b" }}>
              ⚠️ {conflict.explanation}
            </div>
            <p className="doc-conflict-card-note">
              {conflict.conflictKind === "RoleConflict"
                ? "Statutory role slot is already occupied for this Award."
                : conflict.conflictKind === "AlreadyConfirmedDifferentDecision"
                ? "Conflicting decision already recorded. Cannot apply duplicate decision."
                : "Ownership collision detected. Direct duplicate linking is blocked by backend policy."}
            </p>
            <div className="doc-conflict-card-actions">
              <button
                className="village-btn village-btn-primary"
                onClick={() => onOpenConflict?.(conflict, item)}
              >
                Resolve Conflict &amp; Options
              </button>
              {onRemoveItem && (
                <button
                  className="village-btn village-btn-outline"
                  onClick={() => onRemoveItem(item.id)}
                >
                  Cancel Upload
                </button>
              )}
            </div>
          </div>
        </div>
      );
    }
    return (
      <div className="intake-card intake-card-error">
        <div className="intake-card-filename">📄 {item.file.name}</div>
        <div className="intake-error-msg">⚠️ {uploadError}</div>
      </div>
    );
  }

  if (!intake) {
    return (
      <div className="intake-card intake-card-error">
        <div className="intake-card-filename">📄 {item.file.name}</div>
        <div className="intake-error-msg">⚠️ No classification result.</div>
      </div>
    );
  }

  const isConfirmed = intake.status === "Confirmed";
  const conf = confidenceLabel(intake.confidence);

  if (isConfirmed) {
    return (
      <div className="intake-card intake-card-confirmed">
        <div className="intake-card-top">
          <span className="intake-card-filename">📄 {intake.fileName}</span>
          <span className="intake-badge intake-badge-confirmed">✓ Confirmed</span>
        </div>
        <div className="intake-confirmed-summary">
          <span className="intake-confirmed-role">{roleLabel(intake.confirmedRole)}</span>
          {intake.confirmedAwardId && (
            <span className="intake-confirmed-award">
              → Award {awards.find((a) => a.id === intake.confirmedAwardId)?.awardNumber ?? "linked"}
            </span>
          )}
        </div>
        {intake.isDuplicate && (
          <div className="intake-duplicate-note">ℹ️ Exact duplicate — original document reused.</div>
        )}
      </div>
    );
  }

  return (
    <div className={`intake-card ${conflict.isConflict || uploadError ? "intake-card-error" : "intake-card-pending"}`}>
      {/* Top row */}
      <div className="intake-card-top">
        <span className="intake-card-filename">📄 {intake.fileName}</span>
        {conflict.isConflict ? (
          <span className="intake-badge intake-badge-conflict">
            {conflict.conflictKind === "RoleConflict"
              ? "Role Conflict"
              : conflict.conflictKind === "AlreadyConfirmedDifferentDecision"
              ? "State Conflict"
              : "Ownership Conflict"}
          </span>
        ) : (
          <span className={matchStateBadgeClass(intake.matchState)}>
            {matchStateLabel(intake.matchState)}
          </span>
        )}
      </div>

      {/* Detection summary */}
      <div className="intake-detection-row">
        <div className="intake-detection-item">
          <span className="intake-det-label">Detected type</span>
          <span className="intake-det-value">{roleLabel(intake.detectedRole)}</span>
        </div>
        {intake.detectedAwardNumber && (
          <div className="intake-detection-item">
            <span className="intake-det-label">Award no.</span>
            <span className="intake-det-value">{intake.detectedAwardNumber}</span>
          </div>
        )}
        {intake.detectedAwardDate && (
          <div className="intake-detection-item">
            <span className="intake-det-label">Award date</span>
            <span className="intake-det-value">{formatDate(intake.detectedAwardDate)}</span>
          </div>
        )}
        {selectedRole === "PossessionProceeding" && <label>Actual Possession Proceedings date (required)<input type="date" value={legalDate} onChange={e => setLegalDate(e.target.value)} required /></label>}
        {/* Do not display "Low confidence" as the explanation for an ownership conflict */}
        {conflict.isConflict ? (
          <div className="intake-detection-item">
            <span className="intake-det-label">Status</span>
            <span className="intake-badge intake-badge-conflict">
              {conflict.conflictKind === "RoleConflict"
                ? "Role Conflict"
                : conflict.conflictKind === "AlreadyConfirmedDifferentDecision"
                ? "State Conflict"
                : "Ownership Conflict"}
            </span>
          </div>
        ) : (
          <div className="intake-detection-item">
            <span className="intake-det-label">Confidence</span>
            <span className={`intake-det-value intake-conf-${conf.level}`}>{conf.text}</span>
          </div>
        )}
      </div>

      {/* Evidence accordion */}
      {intake.evidence && intake.evidence.length > 0 && (
        <div className="intake-evidence-section">
          <button
            className="intake-evidence-toggle"
            onClick={() => setShowEvidence((v) => !v)}
          >
            {showEvidence ? "▾" : "▸"} Evidence ({intake.evidence.length})
          </button>
          {showEvidence && (
            <div className="intake-evidence-list">
              {intake.evidence.map((ev, i) => (
                <div key={i} className="intake-evidence-item">
                  <span className="intake-ev-field">{ev.field}</span>
                  <span className="intake-ev-page">p.{ev.pageNumber}</span>
                  <span className="intake-ev-text">"{ev.sourceText}"</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Ownership conflict banner */}
      {conflict.isConflict && (
        <div className="doc-conflict-card-banner">
          <div style={{ fontWeight: 600, color: "#991b1b" }}>
            ⚠️ {conflict.explanation}
          </div>
          <p className="doc-conflict-card-note">
            {conflict.conflictKind === "RoleConflict"
              ? "This source already has different attachment metadata or a different role. Review the existing link."
              : conflict.conflictKind === "AlreadyConfirmedDifferentDecision"
              ? "Conflicting decision already recorded. Cannot apply duplicate decision."
              : `Ownership collision detected. This document cannot be linked directly without resolving ownership with ${conflict.conflictingAwardNumber ? `Award ${conflict.conflictingAwardNumber}` : "the registered record"}.`}
          </p>
          <div className="doc-conflict-card-actions">
            <button
              className="village-btn village-btn-primary"
              onClick={() => onOpenConflict?.(conflict, item)}
            >
              Resolve Conflict &amp; Options
            </button>
            {onRemoveItem && (
              <button
                className="village-btn village-btn-outline"
                onClick={() => onRemoveItem(item.id)}
              >
                Cancel Upload
              </button>
            )}
          </div>
        </div>
      )}

      {uploadError && !conflict.isConflict && (
        <div className="intake-error-msg">⚠️ {uploadError}</div>
      )}

      {/* Officer confirmation controls */}
      <div className="intake-confirm-section">
        {/* Role override */}
        <div className="intake-field-row">
          <label className="intake-field-label">Document type</label>
          <select
            className="intake-select"
            value={selectedRole}
            onChange={(e) => setSelectedRole(e.target.value as CoreDocumentRole)}
            disabled={isConfirming}
          >
            <option value="Award">Award PDF</option>
            <option value="NM">Naksha Muntazmin (NM)</option>
            <option value="StatementA">Statement A</option>
            <option value="PossessionProceeding">Possession Proceeding</option>
          </select>
        </div>

        {/* MatchedExistingAward or NeedsOfficerReview → award picker + confirm */}
        {(intake.matchState === "MatchedExistingAward" ||
          intake.matchState === "NeedsOfficerReview") && (
          <>
            <div className="intake-field-row">
              <label className="intake-field-label">Link to Award</label>
              <select
                className="intake-select"
                value={selectedAwardId}
                onChange={(e) => setSelectedAwardId(e.target.value)}
                disabled={isConfirming}
              >
                {awards.map((a) => (
                  <option key={a.id} value={a.id}>
                    Award {a.awardNumber}
                    {a.awardDate ? ` · ${formatDate(a.awardDate)}` : ""}
                  </option>
                ))}
              </select>
            </div>
            <div className="intake-action-row">
              <button
                className="village-btn village-btn-primary"
                disabled={isConfirming || !selectedAwardId || selectedRole === "PossessionProceeding" && !legalDate}
                onClick={() =>
                  onConfirmExisting(selectedRole, selectedAwardId, legalDate)
                }
              >
                {isConfirming ? "Confirming…" : "Confirm & Link"}
              </button>
            </div>
          </>
        )}

        {/* ProposedNewAward → review form + confirm */}
        {intake.matchState === "ProposedNewAward" && (
          <>
            {!showNewAwardForm ? (
              <div className="intake-action-row">
                <div className="intake-proposed-note">
                  No matching Award found for this village. Review the detected details and confirm creation of a new Award.
                </div>
                <div style={{ display: "flex", gap: "8px" }}>
                  {awards.length > 0 && (
                    <>
                      <select
                        className="intake-select"
                        value={selectedAwardId}
                        onChange={(e) => setSelectedAwardId(e.target.value)}
                        disabled={isConfirming}
                        style={{ flex: 1 }}
                      >
                        {awards.map((a) => (
                          <option key={a.id} value={a.id}>
                            Award {a.awardNumber}
                          </option>
                        ))}
                      </select>
                      <button
                        className="village-btn village-btn-outline"
                        disabled={isConfirming || !selectedAwardId || selectedRole === "PossessionProceeding" && !legalDate}
                        onClick={() => onConfirmExisting(selectedRole, selectedAwardId, legalDate)}
                      >
                        {isConfirming ? "Confirming…" : "Link Existing"}
                      </button>
                    </>
                  )}
                  <button
                    className="village-btn village-btn-primary"
                    onClick={() => setShowNewAwardForm(true)}
                    disabled={isConfirming}
                  >
                    Review &amp; Create Award
                  </button>
                </div>
              </div>
            ) : (
              <div className="intake-new-award-form">
                <div className="intake-new-award-title">Create New Award</div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Number *</label>
                  <input
                    className="intake-input"
                    type="text"
                    value={newAwardNumber}
                    onChange={(e) => setNewAwardNumber(e.target.value)}
                    placeholder="e.g. 30/2002-03"
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Date</label>
                  <input
                    className="intake-input"
                    type="date"
                    value={newAwardDate}
                    onChange={(e) => setNewAwardDate(e.target.value)}
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-field-row">
                  <label className="intake-field-label">Award Type</label>
                  <input
                    className="intake-input"
                    type="text"
                    value={newAwardType}
                    onChange={(e) => setNewAwardType(e.target.value)}
                    placeholder="e.g. Main, Supplementary"
                    disabled={isConfirming}
                  />
                </div>
                <div className="intake-action-row">
                  <button
                    className="village-btn village-btn-outline"
                    onClick={() => setShowNewAwardForm(false)}
                    disabled={isConfirming}
                  >
                    Back
                  </button>
                  <button
                    className="village-btn village-btn-primary"
                    disabled={isConfirming || !newAwardNumber.trim()}
                    onClick={() =>
                      onConfirmNewAward(
                        selectedRole,
                        newAwardNumber.trim(),
                        newAwardDate || null,
                        newAwardType || null
                      )
                    }
                  >
                    {isConfirming ? "Creating…" : "Confirm & Create Award"}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
};

// ─── Quick Attach Modal (now routes through Smart Intake) ─────────────────────

interface QuickAttachModalProps {
  villageId: string;
  villageName?: string;
  awardId: string;
  awardNumber: string;
  role: CoreDocumentRole;
  awards?: AwardCoreRecord[];
  onClose: () => void;
  onSuccess: () => void;
  onViewExistingAward?: (awardId: string) => void;
}

const QuickAttachModal: React.FC<QuickAttachModalProps> = ({ villageId, awardId, awardNumber, role, onClose, onSuccess }) => {
  const [file, setFile] = useState<File | null>(null);
  const [mode, setMode] = useState("Direct");
  const [legalDate, setLegalDate] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<LandRecordApiError | null>(null);
  const [result, setResult] = useState<{ documentId: string; attachmentStatus: string } | null>(null);
  const upload = async () => {
    if (!file) return;
    setBusy(true); setError(null);
    try {
      const body = new FormData(); body.append("file", file);
      const query = new URLSearchParams({ role, villageId, mode });
      if (legalDate) query.set("legalDate", legalDate);
      setResult(await landRecordResponse(await fetch(`/api/awards/${awardId}/core-documents?${query}`, { method: "POST", credentials: "include", body })));
    } catch (e) { setError(e instanceof LandRecordApiError ? e : new LandRecordApiError("Upload could not complete. Retry the same file.")); }
    finally { setBusy(false); }
  };
  return <div className="village-modal-backdrop"><div className="village-modal" role="dialog" aria-label="Attach core PDF">
    <div className="village-modal-header"><h3>Attach {roleLabel(role)} to Award {awardNumber}</h3><button onClick={onClose}>Close</button></div>
    <label>PDF file<input type="file" accept=".pdf,application/pdf" onChange={e => setFile(e.target.files?.[0] || null)} /></label>
    <label>{role === "PossessionProceeding" ? "Actual Possession Proceedings date (required)" : "Legal date (optional)"}<input type="date" value={legalDate} onChange={e => setLegalDate(e.target.value)} required={role === "PossessionProceeding"} /></label>
    {role === "Award" && <label>Upload mode<select value={mode} onChange={e => setMode(e.target.value)}><option value="Direct">Direct Upload</option><option value="UploadAndAnalyze">Upload &amp; Analyze</option></select></label>}
    <p>{mode === "Direct" ? "Stores and attaches the selected source. Analysis remains an explicit later step." : "Stores and attaches the source, then queues Award analysis for review."}</p>
    {error?.code === "PDF_OWNERSHIP_CONFLICT" && error.conflicts ? <AwardPdfOwnershipConflictPanel conflict={error.conflicts} onResolved={r => { setError(null); setResult({ documentId: r.documentId, attachmentStatus: "Ownership reviewed" }); }} /> : error && <p role="alert">{error.message}</p>}
    {result ? <div role="status"><p>{result.attachmentStatus}</p><a href={`/api/documents/${result.documentId}/content`} target="_blank" rel="noreferrer">View source PDF</a><button onClick={onSuccess}>Done — Refresh Records</button></div> : <button className="village-btn village-btn-primary" disabled={busy || !file || role === "PossessionProceeding" && !legalDate} onClick={() => void upload()}>{busy ? "Uploading…" : mode === "Direct" ? "Direct Upload" : "Upload & Analyze"}</button>}
  </div></div>;
};
