import React, { useState, useEffect, useRef } from "react";
import { useNavigate, Link } from "react-router-dom";
import type { DakCategory } from "./types";
import "./dak.css";
import { officeCalendarDate } from "./officeDate.js";
import { createDakRequestId } from "./requestId.js";

interface WorkstreamOption {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export const DakRegistration: React.FC = () => {
  const navigate = useNavigate();
  const diaryInputRef = useRef<HTMLInputElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const submissionInFlight = useRef(false);

  // Form Fields
  const [diaryNumber, setDiaryNumber] = useState("");
  const [receivedDate, setReceivedDate] = useState(() => officeCalendarDate());
  const [inwardMode, setInwardMode] = useState("Physical / By Hand");

  const [senderName, setSenderName] = useState("");
  const [senderReferenceNumber, setSenderReferenceNumber] = useState("");
  const [senderLetterDate, setSenderLetterDate] = useState("");

  const [subject, setSubject] = useState("");

  const [categoryId, setCategoryId] = useState("");
  const [workstreamId, setWorkstreamId] = useState("");
  const [priority, setPriority] = useState<"Routine" | "Urgent" | "Immediate">("Routine");

  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  // Collapsible optional metadata
  const [showMoreDetails, setShowMoreDetails] = useState(false);
  const [senderDesignation, setSenderDesignation] = useState("");
  const [senderDepartment, setSenderDepartment] = useState("");
  const [senderAddress, setSenderAddress] = useState("");
  const [dueDate, setDueDate] = useState("");

  // Idempotency & Lookups state
  const [requestId, setRequestId] = useState(() => createDakRequestId());
  const [categories, setCategories] = useState<DakCategory[]>([]);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  useEffect(() => {
    // Focus Diary Number input on initial load
    diaryInputRef.current?.focus();

    // Load categories & workstreams via operational lookup
    fetch("/api/dak/lookups/registration", { credentials: "include" })
      .then((r) => {
        if (!r.ok) throw new Error("Failed to load categories or workstreams.");
        return r.json() as Promise<{ categories: DakCategory[]; workstreams: WorkstreamOption[] }>;
      })
      .then((data) => {
        setCategories(data.categories.filter((c) => c.isActive));
        setWorkstreams(data.workstreams.filter((w) => w.isActive));
      })
      .catch(() => setError("Failed to load categories or workstreams."));
  }, []);

  const handleCategoryChange = (catId: string) => {
    setCategoryId(catId);
    if (!catId) return;
    const cat = categories.find((c) => c.id === catId);
    if (cat) {
      if (cat.defaultPriority) setPriority(cat.defaultPriority);
      if (cat.defaultWorkstreamId) setWorkstreamId(cat.defaultWorkstreamId);
    }
  };

  const clearSelectedFile = () => {
    setSelectedFile(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  const handleRegister = async (action: "next" | "open") => {
    if (submissionInFlight.current) return;
    setError(null);
    setSuccessBanner(null);

    const trimmedDiary = diaryNumber.trim();
    if (!trimmedDiary) {
      setError("Diary Number is mandatory.");
      diaryInputRef.current?.focus();
      return;
    }
    const trimmedSender = senderName.trim();
    if (!trimmedSender) {
      setError("Sender Name is mandatory.");
      return;
    }
    const trimmedSubject = subject.trim();
    if (!trimmedSubject) {
      setError("Subject is mandatory.");
      return;
    }

    submissionInFlight.current = true;
    setSubmitting(true);

    try {
      const formData = new FormData();
      formData.append("diaryNumber", trimmedDiary);
      formData.append("receivedDate", receivedDate);
      formData.append("subject", trimmedSubject);
      formData.append("senderName", trimmedSender);
      if (senderDesignation.trim()) formData.append("senderDesignation", senderDesignation.trim());
      if (senderDepartment.trim()) formData.append("senderDepartment", senderDepartment.trim());
      if (senderAddress.trim()) formData.append("senderAddress", senderAddress.trim());
      if (senderReferenceNumber.trim()) formData.append("senderReferenceNumber", senderReferenceNumber.trim());
      if (senderLetterDate) formData.append("senderLetterDate", senderLetterDate);
      formData.append("inwardMode", inwardMode);
      formData.append("priority", priority);
      if (dueDate) formData.append("dueDate", dueDate);
      if (categoryId) formData.append("categoryId", categoryId);
      if (workstreamId) formData.append("workstreamId", workstreamId);
      if (selectedFile) formData.append("file", selectedFile);

      const res = await fetch("/api/dak", {
        method: "POST",
        headers: { "Idempotency-Key": requestId },
        credentials: "include",
        body: formData,
      });

      if (!res.ok) {
        const data = await res.json().catch(() => null);
        const errMsg = data?.detail || data?.message || "Failed to register Dak.";
        throw new Error(errMsg);
      }

      const result = (await res.json()) as { id: string; diaryNumber: string };

      if (action === "open") {
        navigate(`/dak/${result.id}`);
        return;
      }

      // Action === 'next'
      setSuccessBanner(`Dak #${result.diaryNumber} registered successfully.`);

      // Reset form fields for the next entry
      setDiaryNumber("");
      setSubject("");
      setSenderName("");
      setSenderReferenceNumber("");
      setSenderLetterDate("");
      setSenderDesignation("");
      setSenderDepartment("");
      setSenderAddress("");
      setDueDate("");
      clearSelectedFile();
      setCategoryId("");
      setWorkstreamId("");
      setPriority("Routine");
      setInwardMode("Physical / By Hand");

      // Generate a fresh stable idempotency key for the next record
      setRequestId(createDakRequestId());

      // Autofocus Diary Number for immediate fast entry
      diaryInputRef.current?.focus();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to register Dak.");
    } finally {
      submissionInFlight.current = false;
      setSubmitting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    // Prevent accidental implicit form submission on Enter inside text inputs, but allow normal Enter on buttons/selects
    if (e.key === "Enter" && (e.target as HTMLElement).tagName === "INPUT") {
      e.preventDefault();
    }
  };

  return (
    <div className="dak-quick-intake-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/dak">Dak / Inward</Link> <i>/</i> <span>New Dak Entry</span>
      </div>

      <div className="intake-header">
        <div>
          <span className="eyebrow">Central Inward Intake</span>
          <h1>New Dak Entry</h1>
        </div>
        <div className="intake-header-meta">
          <Link to="/dak" className="secondary-button">Back to Dak Register</Link>
          <span>Date: <strong>{receivedDate}</strong> (Delhi IST)</span>
        </div>
      </div>

      {error && <div className="state error" role="alert">{error}</div>}
      {successBanner && <div className="state success-banner" role="status">✓ {successBanner}</div>}

      <form className="quick-intake-form" onSubmit={(e) => e.preventDefault()} onKeyDown={handleKeyDown}>
        {/* ROW 1: Receipt / Identification */}
        <div className="intake-row row-3col">
          <label className="field-group">
            <span className="field-label">Diary No. *</span>
            <input
              ref={diaryInputRef}
              type="text"
              required
              autoFocus
              placeholder="e.g. DAK/2026/00142"
              value={diaryNumber}
              onChange={(e) => setDiaryNumber(e.target.value)}
              className="intake-input input-highlight"
            />
          </label>

          <label className="field-group">
            <span className="field-label">Received Date *</span>
            <input
              type="date"
              required
              value={receivedDate}
              onChange={(e) => setReceivedDate(e.target.value)}
              className="intake-input"
            />
          </label>

          <label className="field-group">
            <span className="field-label">Inward Mode *</span>
            <select value={inwardMode} onChange={(e) => setInwardMode(e.target.value)} className="intake-select">
              <option value="Physical / By Hand">Physical / By Hand</option>
              <option value="Speed Post / Registered Post">Speed Post / Registered Post</option>
              <option value="Courier">Courier</option>
              <option value="Email">Email</option>
              <option value="e-Office / Portal">e-Office / Portal</option>
              <option value="Court Summon / Special Messenger">Court Summon / Special Messenger</option>
            </select>
          </label>
        </div>

        {/* ROW 2: Sender Details */}
        <div className="intake-row row-3col">
          <label className="field-group">
            <span className="field-label">From / Sender *</span>
            <input
              type="text"
              required
              placeholder="Sender Name / Entity"
              value={senderName}
              onChange={(e) => setSenderName(e.target.value)}
              className="intake-input"
            />
          </label>

          <label className="field-group">
            <span className="field-label">Sender Ref. No.</span>
            <input
              type="text"
              placeholder="e.g. F.1(23)/2025/L&B"
              value={senderReferenceNumber}
              onChange={(e) => setSenderReferenceNumber(e.target.value)}
              className="intake-input"
            />
          </label>

          <label className="field-group">
            <span className="field-label">Sender Ref. Date</span>
            <input
              type="date"
              value={senderLetterDate}
              onChange={(e) => setSenderLetterDate(e.target.value)}
              className="intake-input"
            />
          </label>
        </div>

        {/* ROW 3: Subject (full width) */}
        <div className="intake-row row-full">
          <label className="field-group">
            <span className="field-label">Subject / Synopsis *</span>
            <input
              type="text"
              required
              placeholder="Subject or title of incoming correspondence..."
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              className="intake-input"
            />
          </label>
        </div>

        {/* ROW 4: Classification */}
        <div className="intake-row row-3col">
          <label className="field-group">
            <span className="field-label">Category</span>
            <select value={categoryId} onChange={(e) => handleCategoryChange(e.target.value)} className="intake-select">
              <option value="">-- Unclassified --</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.name} ({c.code})
                </option>
              ))}
            </select>
          </label>

          <label className="field-group">
            <span className="field-label">Workstream</span>
            <select value={workstreamId} onChange={(e) => setWorkstreamId(e.target.value)} className="intake-select">
              <option value="">-- Unassigned --</option>
              {workstreams.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name} ({w.code})
                </option>
              ))}
            </select>
          </label>

          <label className="field-group">
            <span className="field-label">Priority *</span>
            <select value={priority} onChange={(e) => setPriority(e.target.value as "Routine" | "Urgent" | "Immediate")} className="intake-select">
              <option value="Routine">Routine</option>
              <option value="Urgent">Urgent</option>
              <option value="Immediate">Immediate / Top Priority</option>
            </select>
          </label>
        </div>

        {/* ROW 5: Primary Scan / PDF */}
        <div className="intake-row row-full scan-row">
          <label className="field-group scan-group">
            <span className="field-label">Primary Scan / PDF (Optional)</span>
            <div className="file-input-wrap">
              <input
                ref={fileInputRef}
                type="file"
                accept=".pdf,image/*"
                id="primary-scan-file"
                onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
                style={{ display: "none" }}
              />
              <label htmlFor="primary-scan-file" className="file-browse-btn">
                📎 Choose File...
              </label>
              <span className="file-name-display">
                {selectedFile ? selectedFile.name : "No document selected"}
              </span>
              {selectedFile && (
                <button
                  type="button"
                  className="file-remove-btn"
                  onClick={clearSelectedFile}
                  title="Remove document"
                >
                  ✕
                </button>
              )}
            </div>
          </label>
        </div>

        {/* Collapsible Section: More details */}
        <div className="more-details-section">
          <button
            type="button"
            className="more-details-toggle"
            onClick={() => setShowMoreDetails((prev) => !prev)}
          >
            {showMoreDetails ? "▼ Hide additional metadata" : "► More details (Designation, Dept, Address, Due Date)"}
          </button>

          {showMoreDetails && (
            <div className="more-details-content">
              <div className="intake-row row-3col">
                <label className="field-group">
                  <span className="field-label">Sender Designation</span>
                  <input
                    type="text"
                    placeholder="e.g. Advocate / Director / Landowner"
                    value={senderDesignation}
                    onChange={(e) => setSenderDesignation(e.target.value)}
                    className="intake-input"
                  />
                </label>

                <label className="field-group">
                  <span className="field-label">Department / Organization</span>
                  <input
                    type="text"
                    placeholder="e.g. Land & Building Dept / DDA"
                    value={senderDepartment}
                    onChange={(e) => setSenderDepartment(e.target.value)}
                    className="intake-input"
                  />
                </label>

                <label className="field-group">
                  <span className="field-label">Action Due Date</span>
                  <input
                    type="date"
                    value={dueDate}
                    onChange={(e) => setDueDate(e.target.value)}
                    className="intake-input"
                  />
                </label>
              </div>

              <div className="intake-row row-full">
                <label className="field-group">
                  <span className="field-label">Sender Address / Contact</span>
                  <textarea
                    rows={2}
                    placeholder="Postal address or contact details..."
                    value={senderAddress}
                    onChange={(e) => setSenderAddress(e.target.value)}
                    className="intake-textarea"
                  />
                </label>
              </div>
            </div>
          )}
        </div>

        {/* FOOTER ACTIONS */}
        <div className="intake-footer">
          <button
            type="button"
            className="secondary-button"
            onClick={() => navigate("/dak")}
            disabled={submitting}
          >
            Cancel
          </button>
          <div className="footer-right-actions">
            <button
              type="button"
              className="secondary-button btn-open"
              disabled={submitting}
              onClick={() => void handleRegister("open")}
            >
              {submitting ? "Submitting..." : "REGISTER & OPEN"}
            </button>
            <button
              type="button"
              className="primary-button btn-next"
              disabled={submitting}
              onClick={() => void handleRegister("next")}
            >
              {submitting ? "Submitting..." : "REGISTER & NEXT ↵"}
            </button>
          </div>
        </div>
      </form>
    </div>
  );
};
