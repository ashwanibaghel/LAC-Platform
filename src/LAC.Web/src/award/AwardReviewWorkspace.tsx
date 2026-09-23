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

  // Candidates & Selection
  const [candidates, setCandidates] = useState<IngestionCandidate[]>([]);
  const [candidateIndex, setCandidateIndex] = useState<number>(0);

  // Reviewer & State
  const [reviewer, setReviewer] = useState<string>(
    () => sessionStorage.getItem("lac.reviewOfficer") || ""
  );
  const [viewMode, setViewMode] = useState<"crop" | "pdf">("crop");
  const [activeRole, setActiveRole] = useState<string>("khasra");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string>("");

  // Editable Form Payload
  const [formData, setFormData] = useState<any>({});

  const activeCandidate = candidates[candidateIndex] || null;

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
    query.set("page", "0");
    query.set("pageSize", "100");
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
          setCandidateIndex(0);
        }
      })
      .catch((e) => {
        if (active) console.warn("Candidates load error:", e);
      });

    return () => { active = false; };
  }, [sessionId, bucket, category, sourcePageFilter, refresh]);

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
        const khasraVal = `${formData.khasraNumber || ""}${formData.qualifier ? " " + formData.qualifier : ""}`;
        await fetch(`${api}/award-ingestion-candidates/${activeCandidate.id}/verify-award-khasra-field`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            verifiedBy: reviewer.trim(),
            fieldRole: activeRole,
            humanValue: action === "Confirm" ? khasraVal : null,
            decision: action,
          }),
          credentials: "include",
        });
      } else {
        await fetch(`${api}/award-ingestion-candidates/${activeCandidate.id}/verify`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            verifiedBy: reviewer.trim(),
            correctedPayloadJson: JSON.stringify(formData),
            action,
          }),
          credentials: "include",
        });
      }

      // Auto-advance
      if (candidateIndex < candidates.length - 1) {
        setCandidateIndex((prev) => prev + 1);
      } else {
        setRefresh((r) => r + 1);
      }
    } catch (e: any) {
      setMessage(e?.message || "Verification failed.");
    } finally {
      setBusy(false);
    }
  }, [activeCandidate, reviewer, activeRole, formData, candidateIndex, candidates.length]);

  // Keyboard Navigation
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement;
      if (target && ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName)) return;

      if (e.key === "ArrowRight") {
        e.preventDefault();
        setCandidateIndex((i) => Math.min(candidates.length - 1, i + 1));
      } else if (e.key === "ArrowLeft") {
        e.preventDefault();
        setCandidateIndex((i) => Math.max(0, i - 1));
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
  }, [candidates.length, handleVerifyAction]);

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
    ? `${api}/award-ingestion-candidates/${activeCandidate.id}/source-crop?fieldRole=${encodeURIComponent(activeRole)}`
    : undefined;
  const pdfUrl = overview.sourceDocumentId
    ? `${api}/documents/${overview.sourceDocumentId}/content#page=${sourcePage}`
    : undefined;

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
                Crop Focus
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
              Finding {candidates.length > 0 ? candidateIndex + 1 : 0} of {candidates.length}
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
                  <>
                    <label>
                      Khasra Number:
                      <input
                        type="text"
                        value={formData.khasraNumber || ""}
                        onChange={(e) => setFormData({ ...formData, khasraNumber: e.target.value })}
                        onFocus={() => setActiveRole("khasra")}
                      />
                    </label>
                    <label>
                      Qualifier (e.g. min):
                      <input
                        type="text"
                        value={formData.qualifier || ""}
                        onChange={(e) => setFormData({ ...formData, qualifier: e.target.value })}
                        onFocus={() => setActiveRole("qualifier")}
                      />
                    </label>
                    <label>
                      Recorded Bigha:
                      <input
                        type="number"
                        value={formData.recordedAreaBigha ?? ""}
                        onChange={(e) => setFormData({ ...formData, recordedAreaBigha: e.target.value === "" ? null : Number(e.target.value) })}
                        onFocus={() => setActiveRole("recordedArea")}
                      />
                    </label>
                    <label>
                      Awarded Bigha:
                      <input
                        type="number"
                        value={formData.awardedAreaBigha ?? ""}
                        onChange={(e) => setFormData({ ...formData, awardedAreaBigha: e.target.value === "" ? null : Number(e.target.value) })}
                        onFocus={() => setActiveRole("awardedArea")}
                      />
                    </label>
                  </>
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

              {message && <div style={{ fontSize: "12px", color: "#dc2626" }}>{message}</div>}
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
                disabled={candidateIndex <= 0}
                onClick={() => setCandidateIndex((i) => Math.max(0, i - 1))}
              >
                <IconChevronLeft size={16} /> Previous
              </button>
              <button
                type="button"
                className="action-btn btn-quiet"
                disabled={candidateIndex >= candidates.length - 1}
                onClick={() => setCandidateIndex((i) => Math.min(candidates.length - 1, i + 1))}
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
                Uncertain
              </button>
              <button
                type="button"
                className="action-btn btn-reject"
                disabled={busy || !activeCandidate}
                onClick={() => handleVerifyAction("Skip")}
              >
                Skip / Reject
              </button>
              <button
                type="button"
                className="action-btn btn-confirm"
                disabled={busy || !activeCandidate}
                onClick={() => handleVerifyAction("Confirm")}
              >
                <IconCheck size={16} /> Confirm & Next
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
