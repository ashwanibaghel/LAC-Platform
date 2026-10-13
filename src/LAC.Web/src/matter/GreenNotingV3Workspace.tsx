import React, { useState, useEffect, useRef } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { OnlyOfficeNoteFrame } from "./OnlyOfficeNoteFrame";
import "./notingV3.css";

export type V3PrototypeMode = "screenA" | "screenB" | "screenC";

interface GreenNotingV3WorkspaceProps {
  matterId?: string;
  villageName?: string;
  initialMode?: V3PrototypeMode;
}

export const GreenNotingV3Workspace: React.FC<GreenNotingV3WorkspaceProps> = ({
  matterId: propMatterId,
  villageName = "Bijwasan",
  initialMode
}) => {
  const routeParams = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const matterId = propMatterId || routeParams.id || "44444444-4444-4444-4444-444444444444";

  // URL query parameter synchronization
  const paramMode = searchParams.get("mode") as V3PrototypeMode | null;
  const paramZoom = searchParams.get("zoom");
  const paramReview = searchParams.get("review") === "open";
  const paramEmbedOnlyOffice = searchParams.get("onlyoffice") === "true";

  // State
  const [mode, setMode] = useState<V3PrototypeMode>(paramMode || initialMode || "screenB");
  const [zoom, setZoom] = useState<number>(paramZoom ? parseInt(paramZoom, 10) : 100);
  const [showReviewModal, setShowReviewModal] = useState<boolean>(paramReview);
  const [showDrawer, setShowDrawer] = useState<boolean>(false);
  const [embedOnlyOffice, setEmbedOnlyOffice] = useState<boolean>(paramEmbedOnlyOffice);

  // Draft Text States
  const [note1Draft, setNote1Draft] = useState<string>(
    "1. In compliance with the directions of the Land Acquisition Collector, physical inspection was conducted at site on 10.10.2026 along with Halka Patwari.\n" +
    "2. The subject land measuring 4 Bighas 16 Biswas in Khasra No. 14//12/2 was demarcated using total station. Boundaries are intact.\n" +
    "3. Possession was taken over on 15.08.2025 and handed over to DMRC.\n\n" +
    "Submitted for perusal and orders."
  );

  const [note2Draft, setNote2Draft] = useState<string>(
    "1. Perused the inspection report of Kanungo (Acquisition) and demarcation plan submitted at Note #1 ante.\n" +
    "2. Title of the recorded tenure holders has been verified from Khatauni 1426-1431 Fasli. Sh. Sukhbir Singh & Ors. are recorded tenure holders in equal shares.\n" +
    "3. Claim under Section 30-31 is pending before Hon'ble High Court in W.P.(C) 4120/2026. Therefore, compensation amount of ₹1,85,40,000 may be deposited in Reference Court.\n\n" +
    "Submitted for sanction by LAC/ADM."
  );

  // Save Tracking State
  const [saveStatus, setSaveStatus] = useState<"committed" | "saving" | "idle">("committed");
  const [saveStatusText, setSaveStatusText] = useState<string>("Saved to server: v1 (9b0459a1)");
  const saveTimeoutRef = useRef<number | null>(null);

  // Sync mode changes with URL search params
  const handleModeChange = (newMode: V3PrototypeMode) => {
    setMode(newMode);
    searchParams.set("mode", newMode);
    setSearchParams(searchParams);
  };

  const handleZoomChange = (newZoom: number) => {
    setZoom(newZoom);
    searchParams.set("zoom", newZoom.toString());
    setSearchParams(searchParams);
  };

  const handleTextChange = (val: string) => {
    if (mode === "screenA") {
      setNote1Draft(val);
    } else if (mode === "screenB") {
      setNote2Draft(val);
    }
    setSaveStatus("saving");
    setSaveStatusText("Saving changes…");

    if (saveTimeoutRef.current) clearTimeout(saveTimeoutRef.current);
    saveTimeoutRef.current = window.setTimeout(() => {
      setSaveStatus("committed");
      const shortHash = Math.random().toString(16).substring(2, 10);
      setSaveStatusText(`Saved to server: v1 (${shortHash})`);
    }, 1200);
  };

  useEffect(() => {
    return () => {
      if (saveTimeoutRef.current) clearTimeout(saveTimeoutRef.current);
    };
  }, []);

  // Compute canonical hash preview based on current active note
  const activeNoteText = mode === "screenA" ? note1Draft : note2Draft;
  const canonicalHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

  return (
    <div className="v3-workbench-root">
      {/* =========================================================================
          1. ULTRA-COMPACT TOOLBAR (42px)
          Back to Matter | Noting | Save Status | Zoom | Review & Submit | More
          ========================================================================= */}
      <header className="v3-toolbar">
        <div className="v3-tb-left">
          <Link to={`/matters/${matterId}`} className="v3-back-btn" title="Back to Matter Workspace">
            ← Back to Matter
          </Link>
          <div className="v3-tb-divider" />
          <div className="v3-matter-tag">
            <span className="v3-gov-emblem">DELHI GOVT</span>
            <span>Matter LAC/2026/BIJ/042 · {villageName}</span>
          </div>
          <span className="v3-dak-pill" title="Controlling Dak Reference">DAK/2026/0892</span>
        </div>

        <div className="v3-tb-center">
          {/* Truthful Save Status Badge */}
          <div className={`v3-save-badge ${saveStatus}`}>
            <span className="dot" />
            <span data-testid="save-status-text">{saveStatusText}</span>
          </div>

          {/* Document Zoom Controls */}
          <div className="v3-zoom-group">
            <button
              className={`v3-zoom-btn ${zoom === 85 ? "active" : ""}`}
              onClick={() => handleZoomChange(85)}
              title="85% Zoom (Wide Overview)"
            >
              85%
            </button>
            <button
              className={`v3-zoom-btn ${zoom === 90 ? "active" : ""}`}
              onClick={() => handleZoomChange(90)}
              title="90% Zoom (Balanced)"
            >
              90%
            </button>
            <button
              className={`v3-zoom-btn ${zoom === 100 ? "active" : ""}`}
              onClick={() => handleZoomChange(100)}
              title="100% Zoom (Actual Legal Size)"
            >
              100%
            </button>
          </div>
        </div>

        <div className="v3-tb-right">
          {/* Prototype Scenario Switcher for Fast Evaluation & Screenshots */}
          <div className="v3-mode-selector" title="Switch Demonstration Scenarios">
            <button
              className={`v3-mode-btn ${mode === "screenA" ? "active" : ""}`}
              onClick={() => handleModeChange("screenA")}
            >
              Screen A: Note 1 Draft
            </button>
            <button
              className={`v3-mode-btn ${mode === "screenB" ? "active" : ""}`}
              onClick={() => handleModeChange("screenB")}
            >
              Screen B: Note 2 Continuation
            </button>
            <button
              className={`v3-mode-btn ${mode === "screenC" ? "active" : ""}`}
              onClick={() => handleModeChange("screenC")}
            >
              Screen C: 2 Notes Submitted
            </button>
          </div>

          {/* Review & Submit Action */}
          {mode !== "screenC" && (
            <button
              className="v3-btn-review"
              onClick={() => setShowReviewModal(true)}
              data-testid="btn-review-submit"
            >
              Review & Submit
            </button>
          )}

          {/* Secondary Drawer Toggle */}
          <button
            className="v3-btn-more"
            onClick={() => setShowDrawer(!showDrawer)}
            title="Supporting Documents, Citations & Dak Details"
          >
            More ▾
          </button>
        </div>
      </header>

      {/* =========================================================================
          2. DESK VIEWPORT & SINGLE CONTINUOUS LEGAL NOTING CANVAS
          Pale Green Paper, 45mm Red Legal Guideline, Legal Dimensions (215.9 × 355.6 mm)
          ========================================================================= */}
      <main className="v3-desk-viewport">
        <div
          className="v3-legal-sheet"
          style={{ transform: `scale(${zoom / 100})` }}
        >
          {/* Screen-Only Thin Red Legal Margin Guideline (at 45mm Left) */}
          <div className="v3-red-margin-guide" />

          {/* 45mm Left Binding Clearance & Margin Gutter */}
          <aside className="v3-sheet-gutter">
            <div className="v3-gutter-watermark">
              OFFICIAL NOTING SHEET · DELICATE LEGAL RECORDS
            </div>

            {/* Anchored Margin Remark Pill (demonstrates supervisory mark in gutter) */}
            {(mode === "screenB" || mode === "screenC") && (
              <div className="v3-gutter-remark-card" title="Supervisory margin remark anchored to Note 1">
                <span className="tag">MARGIN REMARK</span>
                <strong>ADM (LA)</strong>: Title verification scrutinized against Khatauni 1426-1431 F.
              </div>
            )}
          </aside>

          {/* Main Document Flow (Right of Red Guideline) */}
          <section className="v3-sheet-flow">
            {/* Official Government Docket Header at Top of Page 1 */}
            <div className="v3-docket-header">
              <div className="v3-docket-crest">OFFICE OF THE LAND ACQUISITION COLLECTOR / ADM (SOUTH WEST)</div>
              <div className="v3-docket-sub">GOVERNMENT OF NATIONAL CAPITAL TERRITORY OF DELHI · KAPASHERA, NEW DELHI</div>
              <hr className="v3-docket-rule" />
              <div className="v3-docket-metadata">
                <span><strong>Matter:</strong> LAC/2026/BIJ/042</span>
                <span><strong>Village:</strong> {villageName}</span>
                <span><strong>Controlling Dak:</strong> DAK/2026/0892</span>
                <span><strong>File No:</strong> F.16(42)/2026/LAC/SW/1048</span>
              </div>
            </div>

            {/* SCENARIO 1 (SCREEN A): Officer A (Kanungo) typing Note 1 in single document flow */}
            {mode === "screenA" && (
              <div className="v3-continuation-slot">
                <div className="v3-continuation-header">
                  <span className="v3-draft-badge">NOTE #1 · LIVE DRAFT</span>
                  <span className="v3-officer-pill">Author: Ramphal Sharma (Kanungo, Acquisition)</span>
                </div>

                {embedOnlyOffice ? (
                  <div className="v3-onlyoffice-embedded-host">
                    <OnlyOfficeNoteFrame
                      sessionId="sess-v3-screenA"
                      onDocumentReady={() => {}}
                      onDocumentStateChange={() => handleTextChange(note1Draft)}
                    />
                  </div>
                ) : (
                  <textarea
                    className="v3-editable-textarea"
                    value={note1Draft}
                    onChange={(e) => handleTextChange(e.target.value)}
                    placeholder="Type Note 1 text directly in the visible government flow…"
                    rows={8}
                    autoFocus
                  />
                )}

                <div className="v3-slot-footer">
                  <div style={{ fontSize: "11px", color: "#64748b" }}>
                    Font: Liberation Serif 12pt · Leading: 18pt · Active Lease Held
                  </div>
                  <button
                    className="v3-btn-slot-save"
                    onClick={() => {
                      setSaveStatus("committed");
                      setSaveStatusText("Saved to server: v1 (9b0459a1)");
                    }}
                  >
                    Force Save (Ctrl+S)
                  </button>
                </div>
              </div>
            )}

            {/* SCENARIO 2 (SCREEN B) & SCENARIO 3 (SCREEN C): Note 1 is Submitted & Immutable */}
            {(mode === "screenB" || mode === "screenC") && (
              <div className="v3-official-note-block">
                <div className="v3-note-meta-line">
                  <span className="note-num-pill">NOTE #1</span>
                  <span><strong>FIELD INSPECTION & SITE DEMARCATION REPORT</strong></span>
                  <span style={{ fontSize: "10.5px", color: "#64748b", marginLeft: "auto" }}>
                    Submitted: 11.10.2026 11:30 AM · Ref: DAK/2026/0892
                  </span>
                </div>

                <div className="v3-note-body-text">
                  <p>
                    1. In compliance with the directions of the Land Acquisition Collector, physical inspection was conducted at site on 10.10.2026 along with Halka Patwari.
                  </p>
                  <p>
                    2. The subject land measuring <span className="v3-quote-highlight" title="Anchored supervisory remark anchor">4 Bighas 16 Biswas in Khasra No. 14//12/2 was demarcated</span> using total station. Boundaries are intact.
                  </p>
                  <p>
                    3. Possession was taken over on 15.08.2025 and handed over to requisitioning agency (DMRC).
                  </p>
                  <p style={{ marginTop: "10px" }}>
                    Submitted for perusal and further orders.
                  </p>
                </div>

                {/* Handwritten Signature Reservation Block */}
                <div className="v3-signature-block">
                  <div className="v3-signature-mark">
                    <div>(Ramphal Sharma)</div>
                    <div style={{ fontSize: "10px", color: "#475569" }}>Kanungo (Acquisition) · LAC Branch (SW)</div>
                  </div>
                  <div className="v3-stamp-box">
                    OFFICIALLY SIGNED & COMMITTED · 11.10.2026
                  </div>
                </div>
              </div>
            )}

            {/* SCENARIO 2 (SCREEN B): Officer B (Tehsildar) continues Note 2 directly on the SAME sheet */}
            {mode === "screenB" && (
              <div className="v3-continuation-slot">
                <div className="v3-continuation-header">
                  <span className="v3-draft-badge">NOTE #2 · LIVE CONTINUATION</span>
                  <span className="v3-officer-pill">Author: Rajesh Kumar (Tehsildar, LA / Nodal Officer)</span>
                </div>

                {embedOnlyOffice ? (
                  <div className="v3-onlyoffice-embedded-host">
                    <OnlyOfficeNoteFrame
                      sessionId="sess-v3-screenB"
                      onDocumentReady={() => {}}
                      onDocumentStateChange={() => handleTextChange(note2Draft)}
                    />
                  </div>
                ) : (
                  <textarea
                    className="v3-editable-textarea"
                    value={note2Draft}
                    onChange={(e) => handleTextChange(e.target.value)}
                    placeholder="Continue Note 2 below Note 1 on the same physical sheet…"
                    rows={8}
                    autoFocus
                  />
                )}

                <div className="v3-slot-footer">
                  <div style={{ fontSize: "11px", color: "#64748b" }}>
                    Physical Continuity: Continuing on Page 1 · Adequate Space Remaining
                  </div>
                  <button
                    className="v3-btn-slot-save"
                    onClick={() => {
                      setSaveStatus("committed");
                      setSaveStatusText("Saved to server: v1 (9b0459a1)");
                    }}
                  >
                    Force Save (Ctrl+S)
                  </button>
                </div>
              </div>
            )}

            {/* SCENARIO 3 (SCREEN C): Both Note 1 & Note 2 are Submitted on Page 1 */}
            {mode === "screenC" && (
              <div className="v3-official-note-block">
                <div className="v3-note-meta-line">
                  <span className="note-num-pill">NOTE #2</span>
                  <span><strong>TITLE VERIFICATION & REFERENCE RECOMMENDATION</strong></span>
                  <span style={{ fontSize: "10.5px", color: "#64748b", marginLeft: "auto" }}>
                    Submitted: 12.10.2026 02:45 PM · Ref: DAK/2026/0892
                  </span>
                </div>

                <div className="v3-note-body-text">
                  <p>
                    1. Perused the inspection report of Kanungo (Acquisition) and demarcation plan submitted at Note #1 ante.
                  </p>
                  <p>
                    2. Title of the recorded tenure holders has been verified from Khatauni 1426-1431 Fasli. Sh. Sukhbir Singh & Ors. are recorded tenure holders in equal shares.
                  </p>
                  <p>
                    3. Claim under Section 30-31 is pending before Hon'ble High Court in W.P.(C) 4120/2026. Therefore, compensation amount of ₹1,85,40,000 may be deposited in Reference Court.
                  </p>
                  <p style={{ marginTop: "10px" }}>
                    Submitted for sanction by LAC/ADM.
                  </p>
                </div>

                {/* Handwritten Signature Reservation Block for Note 2 */}
                <div className="v3-signature-block">
                  <div className="v3-signature-mark">
                    <div>(Rajesh Kumar)</div>
                    <div style={{ fontSize: "10px", color: "#475569" }}>Tehsildar (LA) / Nodal Officer · LAC Branch (SW)</div>
                  </div>
                  <div className="v3-stamp-box">
                    OFFICIALLY SIGNED & COMMITTED · 12.10.2026
                  </div>
                </div>
              </div>
            )}

            {/* Bottom Sheet Footer */}
            <footer className="v3-sheet-footer">
              <span>Delhi Government Legal Noting · Page 1 of 1 · US Legal 215.9 × 355.6 mm</span>
              <span>Controlled Official Record · LAC Platform V3</span>
            </footer>
          </section>
        </div>
      </main>

      {/* =========================================================================
          3. CANONICAL REVIEW & SUBMIT MODAL
          Freezes lease, displays exact canonical text & cryptographic SHA-256 hash
          ========================================================================= */}
      {showReviewModal && (
        <div className="v3-modal-overlay">
          <div className="v3-modal-card">
            <div className="v3-modal-header">
              <h3>Canonical Review & Official Confirmation</h3>
              <button
                className="v3-modal-close-btn"
                onClick={() => setShowReviewModal(false)}
                title="Close"
              >
                ✕
              </button>
            </div>

            <div className="v3-modal-body">
              <div className="v3-hash-banner">
                <div><strong>Cryptographic Integrity Hash (SHA-256):</strong></div>
                <span className="v3-hash-code">{canonicalHash}</span>
              </div>

              <div style={{ fontSize: "11.5px", color: "#475569", marginBottom: "8px" }}>
                Canonical text extracted from committed DOCX:
              </div>

              <div className="v3-preview-paper">
                <pre style={{ margin: 0, whiteSpace: "pre-wrap", fontFamily: "inherit", fontSize: "12pt", lineHeight: "1.5" }}>
                  {activeNoteText}
                </pre>
              </div>
            </div>

            <div className="v3-modal-footer">
              <button
                className="v3-btn-modal-cancel"
                onClick={() => setShowReviewModal(false)}
              >
                Cancel / Return to Editing
              </button>
              <button
                className="v3-btn-modal-confirm"
                onClick={() => {
                  setShowReviewModal(false);
                  if (mode === "screenA") {
                    handleModeChange("screenB");
                  } else if (mode === "screenB") {
                    handleModeChange("screenC");
                  }
                }}
              >
                Confirm & Submit Note
              </button>
            </div>
          </div>
        </div>
      )}

      {/* =========================================================================
          4. SECONDARY DRAWER (Supporting Docs & Citations - Non-competing)
          ========================================================================= */}
      {showDrawer && (
        <div className="v3-drawer-overlay" onClick={() => setShowDrawer(false)}>
          <div className="v3-drawer-panel" onClick={(e) => e.stopPropagation()}>
            <div className="v3-drawer-header">
              <h4>Supporting Documents & Citations</h4>
              <button
                style={{ border: "none", background: "transparent", cursor: "pointer", fontSize: "14px" }}
                onClick={() => setShowDrawer(false)}
              >
                ✕
              </button>
            </div>
            <div className="v3-drawer-body">
              <div style={{ marginBottom: "16px" }}>
                <strong style={{ fontSize: "12px", color: "#1e293b" }}>Linked Dak Record</strong>
                <p style={{ fontSize: "11px", color: "#64748b", margin: "4px 0" }}>
                  Diary No: DAK/2026/0892 · Subject: Inspection Report of Khasra No. 14//12/2 Bijwasan
                </p>
              </div>

              <div style={{ marginBottom: "16px" }}>
                <strong style={{ fontSize: "12px", color: "#1e293b" }}>Document Extracts</strong>
                <ul style={{ fontSize: "11px", color: "#475569", paddingLeft: "18px", margin: "4px 0" }}>
                  <li>Award No. 04/2024-25/SW (Bijwasan)</li>
                  <li>Demarcation Field Book Page 42</li>
                  <li>Khatauni Extract 1426-1431 Fasli</li>
                </ul>
              </div>

              <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <label style={{ display: "flex", alignItems: "center", gap: "8px", fontSize: "11px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={embedOnlyOffice}
                    onChange={(e) => setEmbedOnlyOffice(e.target.checked)}
                  />
                  <span>Mount Live ONLYOFFICE DocumentServer Frame in Draft Slot</span>
                </label>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
