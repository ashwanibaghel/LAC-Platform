import React, { useState, useEffect, useCallback } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { IconChevronLeft, IconChevronRight, IconCheck, IconClose, IconFileText } from "../components/Icons";
import "./award.css";

const api = "/api";

export interface IngestionCandidate {
  id: string;
  sessionId: string;
  candidateType: string;
  sequence: number;
  payloadJson: string;
  status: string;
  sourceLocatorJson?: string | null;
  rawSourceText?: string | null;
  validationIssuesJson?: string | null;
  conflictDetailsJson?: string | null;
  fieldReviewJson?: string | null;
  confidence?: number | null;
  verifiedBy?: string | null;
  verifiedAt?: string | null;
  safeToConfirm?: boolean;
}

export interface IngestionSessionOverview {
  id: string;
  sourceDocumentId?: string | null;
  documentName?: string | null;
  targetAwardId?: string | null;
  awardNumber?: string | null;
  selectedVillageId?: string | null;
  villageName?: string | null;
  totalPages?: number;
  processedPages?: number;
  analysisStatus?: string;
  sections: Array<{
    candidateType: string;
    status: string;
    count: number;
    verified: boolean;
    safeToConfirm: boolean;
  }>;
  pages: Array<{
    page: number;
    count: number;
    exact: number;
  }>;
}

function reviewSectionName(type: string): string {
  const map: Record<string, string> = {
    AwardKhasra: "Khasras",
    AwardCore: "Award Details",
    AwardVillage: "Village",
    Notification: "Notifications",
    PossessionEvent: "Possession",
    CourtCase: "Court / Litigation",
    Claim: "Claims",
    AwardLandClass: "Land Classification",
    AwardValuationRule: "Valuation",
    AwardCompensationRule: "Compensation",
    AwardAreaIssue: "Area Issues",
    AwardSupplementaryMatter: "Supplementary",
    UnmappedAwardFinding: "Narrative Evidence",
  };
  return map[type] || type;
}

function deriveHumanValue(role: string, data: any): string | null {
  if (role === "Khasra") {
    return data.khasraNumber ? String(data.khasraNumber).trim() : null;
  }
  if (role === "Qualifier") {
    return data.qualifier ? String(data.qualifier).trim() : null;
  }
  if (role === "RecordedArea") {
    if (data.recordedAreaBigha == null && data.recordedAreaBiswa == null) return null;
    const bigha = data.recordedAreaBigha ?? 0;
    const biswa = data.recordedAreaBiswa ?? 0;
    const biswansi = data.recordedAreaBiswansi;
    return biswansi != null ? `${bigha}-${biswa}-${biswansi}` : `${bigha}-${biswa}`;
  }
  if (role === "AwardedArea") {
    if (data.awardedAreaBigha == null && data.awardedAreaBiswa == null) return null;
    const bigha = data.awardedAreaBigha ?? 0;
    const biswa = data.awardedAreaBiswa ?? 0;
    const biswansi = data.awardedAreaBiswansi;
    return biswansi != null ? `${bigha}-${biswa}-${biswansi}` : `${bigha}-${biswa}`;
  }
  return null;
}

function getRequiredRoles(candidate: IngestionCandidate, payload: any): string[] {
  const roles: string[] = ["Khasra"];
  let locator: any = {};
  try { locator = JSON.parse(candidate.sourceLocatorJson || "{}"); } catch {}
  const sourceCells = locator.structuredPayload?.sourceCells || locator.sourceCells || {};

  if (sourceCells.recordedArea || payload.recordedAreaBigha != null) roles.push("RecordedArea");
  if (sourceCells.awardedArea || payload.awardedAreaBigha != null) roles.push("AwardedArea");
  if (payload.qualifier) roles.push("Qualifier");

  return roles;
}

function getFieldDecisions(candidate: IngestionCandidate): Array<{ fieldRole: string; decision: string; humanValue?: string }> {
  try {
    return JSON.parse(candidate.fieldReviewJson || "[]");
  } catch {
    return [];
  }
}

export const AwardReviewWorkspace: React.FC = () => {
  const { id: routeAwardId, sessionId = "" } = useParams();
  const navigate = useNavigate();

  const [refresh, setRefresh] = useState(0);
  const [overview, setOverview] = useState<IngestionSessionOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filter States
  const [bucket, setBucket] = useState<string>("attention");
  const [category, setCategory] = useState<string>("");
  const [sourcePageFilter, setSourcePageFilter] = useState<string>("");

  // Pagination & Candidate Selection
  const [page, setPage] = useState<number>(0);
  const [pageSize, setPageSize] = useState<number>(50);
  const [totalCandidatesCount, setTotalCandidatesCount] = useState<number>(0);
  const [candidates, setCandidates] = useState<IngestionCandidate[]>([]);
  const [candidateIndex, setCandidateIndex] = useState<number>(0);

  // Reviewer & Active Role State
  const [reviewer, setReviewer] = useState<string>(
    () => sessionStorage.getItem("lac.reviewOfficer") || ""
  );
  const [viewMode, setViewMode] = useState<"crop" | "pdf">("crop");
  const [activeRole, setActiveRole] = useState<string>("Khasra"); // "Khasra", "Qualifier", "RecordedArea", "AwardedArea"
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string>("");

  // Commit Modal State
  const [showCommitModal, setShowCommitModal] = useState<boolean>(false);
  const [commitBusy, setCommitBusy] = useState<boolean>(false);
  const [commitError, setCommitError] = useState<string | null>(null);

  // Editable Form Payload
  const [formData, setFormData] = useState<any>({});

  const activeCandidate = candidates[candidateIndex] || null;

  // Reset page when filters change
  useEffect(() => {
    setPage(0);
    setCandidateIndex(0);
  }, [bucket, category, sourcePageFilter]);

  // Load Session Overview
  useEffect(() => {
    if (!sessionId) return;
    let active = true;
    setLoading(true);
    setError(null);

    fetch(`${api}/award-ingestion-sessions/${sessionId}/overview?r=${refresh}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Could not load document review session.");
        return res.json() as Promise<IngestionSessionOverview>;
      })
      .then((data) => {
        if (active) {
          setOverview(data);
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setError(e?.message || "Document review session unavailable.");
          setLoading(false);
        }
      });
    return () => { active = false; };
  }, [sessionId, refresh]);

  // Load Candidates
  useEffect(() => {
    if (!sessionId) return;
    let active = true;
    const query = new URLSearchParams();
    query.set("page", String(page));
    query.set("pageSize", String(pageSize));
    if (bucket) query.set("bucket", bucket);
    if (category) query.set("type", category);
    if (sourcePageFilter) query.set("sourcePage", sourcePageFilter);
    query.set("r", String(refresh));

    fetch(`${api}/award-ingestion-sessions/${sessionId}/candidates?${query.toString()}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Could not load candidates.");
        return res.json();
      })
      .then((resData) => {
        if (active) {
          const items = resData.items || [];
          setCandidates(items);
          setTotalCandidatesCount(resData.totalCount ?? items.length);
          if (candidateIndex >= items.length && items.length > 0) {
            setCandidateIndex(0);
          }
        }
      })
      .catch((e) => {
        if (active) console.warn("Candidates load error:", e);
      });

    return () => { active = false; };
  }, [sessionId, page, pageSize, bucket, category, sourcePageFilter, refresh]);

  // Sync Form Data when Active Candidate Changes
  useEffect(() => {
    if (!activeCandidate) {
      setFormData({});
      return;
    }
    try {
      setFormData(JSON.parse(activeCandidate.payloadJson || "{}"));
    } catch {
      setFormData({});
    }
    setActiveRole("Khasra");
  }, [activeCandidate]);

  // Persist Reviewer Name
  const handleReviewerChange = (name: string) => {
    setReviewer(name);
    sessionStorage.setItem("lac.reviewOfficer", name);
  };

  // Perform Verification Action
  const handleVerifyAction = useCallback(async (action: "Confirm" | "Uncertain" | "Skip" | "Correct") => {
    if (!activeCandidate) return;
    if (!reviewer.trim()) {
      setMessage("Please enter your name as the verifying officer.");
      return;
    }

    try {
      setBusy(true);
      setMessage("");

      if (activeCandidate.candidateType === "AwardKhasra") {
        const humanVal = action === "Confirm" || action === "Correct"
          ? deriveHumanValue(activeRole, formData)
          : null;

        if ((action === "Confirm" || action === "Correct") && !humanVal) {
          setMessage(`Please enter a valid value for ${activeRole}.`);
          setBusy(false);
          return;
        }

        const res = await fetch(`${api}/award-ingestion-candidates/${activeCandidate.id}/verify-award-khasra-field`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            verifiedBy: reviewer.trim(),
            fieldRole: activeRole,
            humanValue: humanVal,
            decision: action,
          }),
          credentials: "include",
        });

        if (!res.ok) {
          const err = await res.json().catch(() => ({}));
          throw new Error(err.detail || err.message || "Field verification failed.");
        }

        // On success, refresh the overview & candidates
        setRefresh((r) => r + 1);

        // Check field completion for current candidate
        const requiredRoles = getRequiredRoles(activeCandidate, formData);
        const existingDecisions = getFieldDecisions(activeCandidate);
        const updatedDecisions = existingDecisions.filter(d => (d.fieldRole || (d as any).FieldRole) !== activeRole);
        updatedDecisions.push({ fieldRole: activeRole, decision: action, humanValue: humanVal || undefined });

        const unconfirmedRole = requiredRoles.find(role => {
          const d = updatedDecisions.find(x => (x.fieldRole || (x as any).FieldRole) === role);
          return !d || (d.decision !== "Confirm" && d.decision !== "Correct");
        });

        if (unconfirmedRole) {
          // Stay on current candidate and move to next unconfirmed field
          setActiveRole(unconfirmedRole);
          setMessage(`Saved decision for ${activeRole}. Next field: ${unconfirmedRole}.`);
        } else {
          // All required fields verified -> advance candidate
          if (candidateIndex < candidates.length - 1) {
            setCandidateIndex((prev) => prev + 1);
            setActiveRole("Khasra");
          } else if ((page + 1) * pageSize < totalCandidatesCount) {
            setPage((prev) => prev + 1);
            setCandidateIndex(0);
            setActiveRole("Khasra");
          } else {
            setMessage("Candidate fully verified! Reached end of current list.");
          }
        }
      } else {
        const res = await fetch(`${api}/award-ingestion-candidates/${activeCandidate.id}/verify`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            verifiedBy: reviewer.trim(),
            correctedPayloadJson: JSON.stringify(formData),
            action,
          }),
          credentials: "include",
        });

        if (!res.ok) {
          const err = await res.json().catch(() => ({}));
          throw new Error(err.detail || err.message || "Finding verification failed.");
        }

        setRefresh((r) => r + 1);
        if (candidateIndex < candidates.length - 1) {
          setCandidateIndex((prev) => prev + 1);
        } else if ((page + 1) * pageSize < totalCandidatesCount) {
          setPage((prev) => prev + 1);
          setCandidateIndex(0);
        }
      }
    } catch (e: any) {
      // NEVER auto-advance on error!
      setMessage(e?.message || "Verification request failed.");
    } finally {
      setBusy(false);
    }
  }, [activeCandidate, reviewer, activeRole, formData, candidateIndex, candidates.length, page, pageSize, totalCandidatesCount]);

  // Handle Commit Verified Action
  const handleCommitVerified = async () => {
    if (!reviewer.trim()) {
      setCommitError("Please enter your name as the verifying officer.");
      return;
    }
    try {
      setCommitBusy(true);
      setCommitError(null);
      const res = await fetch(`${api}/award-ingestion-sessions/${sessionId}/commit-verified`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          verifiedBy: reviewer.trim(),
          expectedCount: verifiedCount,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.detail || err.message || "Failed to commit verified findings.");
      }

      setShowCommitModal(false);
      setMessage(`Successfully committed ${verifiedCount} human-verified finding(s) into official records.`);
      setRefresh((r) => r + 1);
    } catch (e: any) {
      setCommitError(e?.message || "Commit failed.");
    } finally {
      setCommitBusy(false);
    }
  };

  // Keyboard Navigation
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target && ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName)) return;

      if (e.key === "ArrowRight") {
        e.preventDefault();
        if (candidateIndex < candidates.length - 1) {
          setCandidateIndex((i) => i + 1);
        } else if ((page + 1) * pageSize < totalCandidatesCount) {
          setPage((p) => p + 1);
          setCandidateIndex(0);
        }
      } else if (e.key === "ArrowLeft") {
        e.preventDefault();
        if (candidateIndex > 0) {
          setCandidateIndex((i) => i - 1);
        } else if (page > 0) {
          setPage((p) => p - 1);
          setCandidateIndex(pageSize - 1);
        }
      } else if (e.key === "Enter" || e.key.toLowerCase() === "c") {
        e.preventDefault();
        void handleVerifyAction("Confirm");
      } else if (e.key.toLowerCase() === "r" || e.key.toLowerCase() === "s") {
        e.preventDefault();
        void handleVerifyAction("Skip");
      } else if (e.key.toLowerCase() === "u") {
        e.preventDefault();
        void handleVerifyAction("Uncertain");
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [candidates.length, candidateIndex, page, pageSize, totalCandidatesCount, handleVerifyAction]);

  if (loading) return <div className="state loading" style={{ padding: "40px", textAlign: "center" }}>Loading document workstation…</div>;
  if (error || !overview) return <div className="state error" style={{ padding: "40px" }}>{error || "Session not found."}</div>;

  // Extract Summary Counts
  const sections = overview.sections || [];
  const countByStatus = (pred: (s: any) => boolean) => sections.filter(pred).reduce((acc, s) => acc + s.count, 0);

  const exactCount = countByStatus((s) => s.safeToConfirm && !s.verified && s.status === "Ready");
  const verifiedCount = countByStatus((s) => s.verified && s.status === "Ready");
  const committedCount = countByStatus((s) => s.status === "Committed");
  const conflictsCount = countByStatus((s) => ["Conflict", "Ambiguous", "DuplicateInBatch"].includes(s.status));
  const unreadableCount = countByStatus((s) => s.status === "Invalid");
  const attentionCount = countByStatus((s) => !s.safeToConfirm && !s.verified && !["Committed", "Skipped", "Rejected"].includes(s.status));

  // Category Options
  const categories = Array.from(new Set(sections.map((s) => s.candidateType)));

  // Target Award ID for links
  const targetAwardId = overview.targetAwardId || routeAwardId;

  // Source Locator details for active candidate
  let locator: any = {};
  try { locator = JSON.parse(activeCandidate?.sourceLocatorJson || "{}"); } catch {}
  const sourcePage = activeCandidate?.sourceLocatorJson ? (locator.Page || locator.page || 1) : 1;
  const rawOcr = activeCandidate?.rawSourceText || locator.RawOcr || locator.rawOcr || "";
  const cropUrl = activeCandidate
    ? `${api}/award-ingestion-candidates/${activeCandidate.id}/source-crop?fieldRole=${encodeURIComponent(activeRole.toLowerCase())}`
    : undefined;
  const pdfUrl = overview.sourceDocumentId
    ? `${api}/documents/${overview.sourceDocumentId}/content#page=${sourcePage}`
    : undefined;

  const globalCandidateIndex = totalCandidatesCount > 0 ? page * pageSize + candidateIndex + 1 : 0;

  return (
    <div className="award-review-workspace">
      {/* Header Bar */}
      <div className="review-header-bar">
        <div className="review-header-top">
          <div className="review-title-group">
            <h1>{overview.documentName || "Award Document Review Workstation"}</h1>
            <p>
              {overview.awardNumber ? `Award #${overview.awardNumber}` : "Award Context Pending"}
              {overview.villageName ? ` · Village ${overview.villageName}` : ""}
              {overview.totalPages ? ` · ${overview.processedPages || 0}/${overview.totalPages} Pages Processed` : ""}
            </p>
          </div>

          <div className="review-header-actions">
            {verifiedCount > 0 && (
              <button
                type="button"
                className="action-btn btn-confirm"
                style={{ background: "#16a34a", color: "#ffffff", fontWeight: 600 }}
                onClick={() => setShowCommitModal(true)}
              >
                <IconCheck size={16} /> Commit Verified ({verifiedCount})
              </button>
            )}

            <div className="officer-input-pill">
              <label htmlFor="officer-name">Verifying Officer:</label>
              <input
                id="officer-name"
                type="text"
                value={reviewer}
                onChange={(e) => handleReviewerChange(e.target.value)}
                placeholder="Your Name"
              />
            </div>
            {targetAwardId && (
              <Link to={`/awards/${targetAwardId}`} className="action-btn btn-quiet">
                Back to Award Workspace
              </Link>
            )}
          </div>
        </div>

        {/* Summary Strip */}
        <div className="review-summary-strip">
          <button className={`sum-pill pill-attention ${bucket === "attention" ? "active" : ""}`} onClick={() => setBucket("attention")}>
            Need Attention <b>{attentionCount}</b>
          </button>
          <button className={`sum-pill pill-exact ${bucket === "exact" ? "active" : ""}`} onClick={() => setBucket("exact")}>
            Exact Matches <b>{exactCount}</b>
          </button>
          <button className={`sum-pill pill-verified ${bucket === "verified" ? "active" : ""}`} onClick={() => setBucket("verified")}>
            Human Verified <b>{verifiedCount}</b>
          </button>
          <button className={`sum-pill pill-conflict ${bucket === "conflict" ? "active" : ""}`} onClick={() => setBucket("conflict")}>
            Conflicts <b>{conflictsCount}</b>
          </button>
          <button className={`sum-pill ${bucket === "unreadable" ? "active" : ""}`} onClick={() => setBucket("unreadable")}>
            Unreadable <b>{unreadableCount}</b>
          </button>
          <button className={`sum-pill ${bucket === "committed" ? "active" : ""}`} onClick={() => setBucket("committed")}>
            Committed <b>{committedCount}</b>
          </button>
          <button className={`sum-pill ${bucket === "" ? "active" : ""}`} onClick={() => setBucket("")}>
            All Findings
          </button>
        </div>

        {/* Category Filter Chips */}
        <div className="review-filter-chips">
          <button className={`cat-chip ${category === "" ? "active" : ""}`} onClick={() => setCategory("")}>
            All Categories
          </button>
          {categories.map((cat) => (
            <button
              key={cat}
              className={`cat-chip ${category === cat ? "active" : ""}`}
              onClick={() => setCategory(cat)}
            >
              {reviewSectionName(cat)}
            </button>
          ))}
        </div>
      </div>

      {/* Main Split Workstation */}
      <div className="workstation-split-container">
        {/* Left Pane: Evidence Viewer */}
        <div className="left-evidence-pane">
          <div className="evidence-pane-header">
            <div className="evidence-pane-title">
              <IconFileText size={16} />
              <span>Source Document Evidence · Page {sourcePage}</span>
            </div>
            <div className="mode-toggle-group">
              <button
                className={`mode-btn ${viewMode === "crop" ? "active" : ""}`}
                onClick={() => setViewMode("crop")}
              >
                Crop Focus ({activeRole})
              </button>
              <button
                className={`mode-btn ${viewMode === "pdf" ? "active" : ""}`}
                onClick={() => setViewMode("pdf")}
              >
                Full PDF Page
              </button>
            </div>
          </div>

          <div className="evidence-body">
            {viewMode === "crop" ? (
              <>
                <div className="crop-preview-box">
                  {cropUrl ? (
                    <img src={cropUrl} alt={`Source crop for ${activeRole}`} />
                  ) : (
                    <span style={{ fontSize: "12px", color: "#64748b" }}>No image crop available</span>
                  )}
                </div>
                {rawOcr && (
                  <div>
                    <span style={{ fontSize: "11px", fontWeight: 700, color: "#475569", textTransform: "uppercase" }}>
                      Extracted OCR Text
                    </span>
                    <div className="raw-ocr-snippet">{rawOcr}</div>
                  </div>
                )}
              </>
            ) : (
              pdfUrl ? (
                <iframe src={pdfUrl} className="iframe-pdf-wrapper" title={`PDF Page ${sourcePage}`} />
              ) : (
                <div style={{ padding: "20px", color: "#64748b" }}>No document viewer stream available.</div>
              )
            )}
          </div>
        </div>

        {/* Right Pane: Review Card */}
        <div className="right-review-pane">
          <div className="review-pane-header">
            <span className="finding-counter">
              Finding {globalCandidateIndex} of {totalCandidatesCount}
            </span>
            <div className="shortcut-hint">
              <span>Shortcuts:</span>
              <kbd className="shortcut-kbd">Enter</kbd> Confirm
              <kbd className="shortcut-kbd">S</kbd> Skip
              <kbd className="shortcut-kbd">U</kbd> Uncertain
              <kbd className="shortcut-kbd">→</kbd> Next
            </div>
          </div>

          {activeCandidate ? (
            <div className="finding-card-content">
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
                <span className="cat-chip active">{reviewSectionName(activeCandidate.candidateType)}</span>
                <span style={{ fontSize: "12px", fontWeight: 600, color: "#64748b" }}>
                  Status: {activeCandidate.status}
                </span>
              </div>

              {/* Warning Alert if validation issues exist */}
              {activeCandidate.validationIssuesJson && (
                <div className="alert-callout warning">
                  <strong>Verification Alert:</strong>{" "}
                  {JSON.parse(activeCandidate.validationIssuesJson || "[]").join(" ")}
                </div>
              )}

              {/* Form Fields */}
              <div className="finding-form-grid">
                {activeCandidate.candidateType === "AwardKhasra" ? (
                  (() => {
                    const decisions = getFieldDecisions(activeCandidate);
                    const getBadge = (roleName: string) => {
                      const dec = decisions.find((d: any) => (d.fieldRole || d.FieldRole) === roleName);
                      if (!dec) return <span className="field-badge pending">Needs Review</span>;
                      if (dec.decision === "Confirm") return <span className="field-badge confirmed">✓ Confirmed</span>;
                      if (dec.decision === "Correct") return <span className="field-badge corrected">✎ Corrected</span>;
                      if (dec.decision === "Skip") return <span className="field-badge skipped">Skipped</span>;
                      if (dec.decision === "Uncertain") return <span className="field-badge uncertain">Uncertain</span>;
                      return <span className="field-badge pending">{dec.decision}</span>;
                    };

                    return (
                      <>
                        <label className={activeRole === "Khasra" ? "field-label active" : "field-label"}>
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                            <span>Khasra Number:</span>
                            {getBadge("Khasra")}
                          </div>
                          <input
                            type="text"
                            value={formData.khasraNumber || ""}
                            onChange={(e) => setFormData({ ...formData, khasraNumber: e.target.value })}
                            onFocus={() => setActiveRole("Khasra")}
                          />
                        </label>

                        <label className={activeRole === "Qualifier" ? "field-label active" : "field-label"}>
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                            <span>Qualifier (e.g. min):</span>
                            {getBadge("Qualifier")}
                          </div>
                          <input
                            type="text"
                            value={formData.qualifier || ""}
                            onChange={(e) => setFormData({ ...formData, qualifier: e.target.value })}
                            onFocus={() => setActiveRole("Qualifier")}
                          />
                        </label>

                        <label className={activeRole === "RecordedArea" ? "field-label active" : "field-label"}>
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                            <span>Recorded Bigha-Biswa-Biswansi:</span>
                            {getBadge("RecordedArea")}
                          </div>
                          <div style={{ display: "flex", gap: "6px" }}>
                            <input
                              type="number"
                              placeholder="Bigha"
                              value={formData.recordedAreaBigha ?? ""}
                              onChange={(e) => setFormData({ ...formData, recordedAreaBigha: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("RecordedArea")}
                            />
                            <input
                              type="number"
                              placeholder="Biswa"
                              value={formData.recordedAreaBiswa ?? ""}
                              onChange={(e) => setFormData({ ...formData, recordedAreaBiswa: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("RecordedArea")}
                            />
                            <input
                              type="number"
                              placeholder="Biswansi"
                              value={formData.recordedAreaBiswansi ?? ""}
                              onChange={(e) => setFormData({ ...formData, recordedAreaBiswansi: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("RecordedArea")}
                            />
                          </div>
                        </label>

                        <label className={activeRole === "AwardedArea" ? "field-label active" : "field-label"}>
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                            <span>Awarded Bigha-Biswa-Biswansi:</span>
                            {getBadge("AwardedArea")}
                          </div>
                          <div style={{ display: "flex", gap: "6px" }}>
                            <input
                              type="number"
                              placeholder="Bigha"
                              value={formData.awardedAreaBigha ?? ""}
                              onChange={(e) => setFormData({ ...formData, awardedAreaBigha: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("AwardedArea")}
                            />
                            <input
                              type="number"
                              placeholder="Biswa"
                              value={formData.awardedAreaBiswa ?? ""}
                              onChange={(e) => setFormData({ ...formData, awardedAreaBiswa: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("AwardedArea")}
                            />
                            <input
                              type="number"
                              placeholder="Biswansi"
                              value={formData.awardedAreaBiswansi ?? ""}
                              onChange={(e) => setFormData({ ...formData, awardedAreaBiswansi: e.target.value === "" ? null : Number(e.target.value) })}
                              onFocus={() => setActiveRole("AwardedArea")}
                            />
                          </div>
                        </label>
                      </>
                    );
                  })()
                ) : (
                  Object.keys(formData).slice(0, 6).map((key) => (
                    <label key={key}>
                      {key.replace(/([A-Z])/g, " $1")}:
                      <input
                        type="text"
                        value={formData[key] ?? ""}
                        onChange={(e) => setFormData({ ...formData, [key]: e.target.value })}
                      />
                    </label>
                  ))
                )}
              </div>

              {message && <div style={{ fontSize: "12px", color: message.includes("Successfully") || message.includes("Saved") ? "#16a34a" : "#dc2626", marginTop: "8px" }}>{message}</div>}
            </div>
          ) : (
            <div className="finding-card-content" style={{ justifyContent: "center", alignItems: "center", color: "#64748b" }}>
              No finding selected or available in this group.
            </div>
          )}

          {/* Review Pane Footer Actions */}
          <div className="review-pane-footer">
            <div className="action-btn-group">
              <button
                type="button"
                className="action-btn btn-quiet"
                disabled={page === 0 && candidateIndex === 0}
                onClick={() => {
                  if (candidateIndex > 0) {
                    setCandidateIndex((i) => i - 1);
                  } else if (page > 0) {
                    setPage((p) => p - 1);
                    setCandidateIndex(pageSize - 1);
                  }
                }}
              >
                <IconChevronLeft size={16} /> Previous
              </button>
              <span style={{ fontSize: "12px", color: "#64748b" }}>
                Page {page + 1} of {Math.max(1, Math.ceil(totalCandidatesCount / pageSize))}
              </span>
              <button
                type="button"
                className="action-btn btn-quiet"
                disabled={candidateIndex >= candidates.length - 1 && (page + 1) * pageSize >= totalCandidatesCount}
                onClick={() => {
                  if (candidateIndex < candidates.length - 1) {
                    setCandidateIndex((i) => i + 1);
                  } else if ((page + 1) * pageSize < totalCandidatesCount) {
                    setPage((p) => p + 1);
                    setCandidateIndex(0);
                  }
                }}
              >
                Next <IconChevronRight size={16} />
              </button>
            </div>

            <div className="action-btn-group">
              <button
                type="button"
                className="action-btn btn-uncertain"
                disabled={busy || !activeCandidate}
                onClick={() => handleVerifyAction("Uncertain")}
              >
                {activeCandidate?.candidateType === "AwardKhasra" ? "Mark Field Uncertain" : "Mark Uncertain"}
              </button>
              <button
                type="button"
                className="action-btn btn-reject"
                disabled={busy || !activeCandidate}
                onClick={() => handleVerifyAction("Skip")}
              >
                {activeCandidate?.candidateType === "AwardKhasra" ? "Skip Field" : "Skip Finding"}
              </button>
              <button
                type="button"
                className="action-btn btn-confirm"
                disabled={busy || !activeCandidate}
                onClick={() => handleVerifyAction("Confirm")}
              >
                <IconCheck size={16} /> {activeCandidate?.candidateType === "AwardKhasra" ? `Confirm Field (${activeRole})` : "Confirm Finding & Next"}
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Commit Verified Modal */}
      {showCommitModal && (
        <div style={{ position: "fixed", inset: 0, background: "rgba(15,23,42,0.6)", zIndex: 100, display: "flex", alignItems: "center", justifyContent: "center" }}>
          <div style={{ background: "#ffffff", borderRadius: "8px", width: "480px", maxWidth: "90vw", padding: "24px", boxShadow: "0 20px 25px -5px rgba(0,0,0,0.1)" }}>
            <h2 style={{ fontSize: "18px", fontWeight: 700, color: "#0f172a", marginBottom: "8px" }}>
              Commit Human-Verified Findings
            </h2>
            <p style={{ fontSize: "13.5px", color: "#475569", marginBottom: "16px", lineHeight: "1.5" }}>
              Are you sure you want to commit <strong>{verifiedCount}</strong> verified finding(s) into official revenue records for this award?
            </p>

            <div style={{ marginBottom: "16px" }}>
              <label style={{ display: "block", fontSize: "12px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                Verifying Officer Name:
              </label>
              <input
                type="text"
                value={reviewer}
                onChange={(e) => handleReviewerChange(e.target.value)}
                placeholder="Enter Officer Name"
                style={{ width: "100%", padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px" }}
              />
            </div>

            {commitError && (
              <div style={{ fontSize: "12.5px", color: "#dc2626", marginBottom: "12px" }}>
                {commitError}
              </div>
            )}

            <div style={{ display: "flex", justifyContent: "flex-end", gap: "8px", marginTop: "20px" }}>
              <button
                type="button"
                className="action-btn btn-quiet"
                disabled={commitBusy}
                onClick={() => setShowCommitModal(false)}
              >
                Cancel
              </button>
              <button
                type="button"
                className="action-btn btn-confirm"
                style={{ background: "#16a34a", color: "#ffffff" }}
                disabled={commitBusy || !reviewer.trim()}
                onClick={handleCommitVerified}
              >
                {commitBusy ? "Committing…" : `Confirm & Commit (${verifiedCount})`}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
