import React, { useState, useEffect } from "react";
import { useNavigate, Link } from "react-router-dom";
import type { DakCategory } from "./types";
import "./dak.css";

interface WorkstreamOption {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export const DakRegistration: React.FC = () => {
  const navigate = useNavigate();

  const [diaryNumber, setDiaryNumber] = useState("");
  const [receivedDate, setReceivedDate] = useState(() => new Date().toISOString().split("T")[0]);
  const [subject, setSubject] = useState("");
  const [senderName, setSenderName] = useState("");
  const [senderDesignation, setSenderDesignation] = useState("");
  const [senderDepartment, setSenderDepartment] = useState("");
  const [senderAddress, setSenderAddress] = useState("");
  const [senderReferenceNumber, setSenderReferenceNumber] = useState("");
  const [senderLetterDate, setSenderLetterDate] = useState("");
  const [inwardMode, setInwardMode] = useState("Physical / By Hand");
  const [priority, setPriority] = useState<"Routine" | "Urgent" | "Immediate">("Routine");
  const [dueDate, setDueDate] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [workstreamId, setWorkstreamId] = useState("");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const [categories, setCategories] = useState<DakCategory[]>([]);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
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

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!diaryNumber.trim()) {
      setError("Diary Number is mandatory.");
      return;
    }
    if (!subject.trim()) {
      setError("Subject is mandatory.");
      return;
    }
    if (!senderName.trim()) {
      setError("Sender Name is mandatory.");
      return;
    }

    setSubmitting(true);

    try {
      const formData = new FormData();
      formData.append("diaryNumber", diaryNumber.trim());
      formData.append("receivedDate", receivedDate);
      formData.append("subject", subject.trim());
      formData.append("senderName", senderName.trim());
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
        credentials: "include",
        body: formData,
      });

      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.detail || data?.message || "Failed to register Dak.");
      }

      const result = (await res.json()) as { id: string };
      navigate(`/dak/${result.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to register Dak.");
      setSubmitting(false);
    }
  };

  return (
    <div className="dak-register-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/dak">Dak / Inward</Link> <i>/</i> <span>Register Inward</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow">Central Inward / Reception</div>
          <h1>Register Inward Dak</h1>
          <p>Register incoming official correspondence, representations, court notices, and government references.</p>
        </div>
      </div>

      {error && <div className="state error">{error}</div>}

      <form className="lr-form" onSubmit={handleSubmit}>
        <fieldset>
          <legend>1. Receipt & Identification</legend>
          <div className="field-grid">
            <label>
              Diary / Receipt Number *
              <input
                type="text"
                required
                placeholder="e.g. DAK/2026/00142"
                value={diaryNumber}
                onChange={(e) => setDiaryNumber(e.target.value)}
              />
              <span className="hint">Official diary number assigned by reception / dispatch section.</span>
            </label>

            <label>
              Received Date *
              <input
                type="date"
                required
                value={receivedDate}
                onChange={(e) => setReceivedDate(e.target.value)}
              />
            </label>

            <label>
              Inward Delivery Mode *
              <select value={inwardMode} onChange={(e) => setInwardMode(e.target.value)}>
                <option value="Physical / By Hand">Physical / By Hand</option>
                <option value="Speed Post / Registered Post">Speed Post / Registered Post</option>
                <option value="Courier">Courier</option>
                <option value="Email">Email</option>
                <option value="e-Office / Portal">e-Office / Portal</option>
                <option value="Court Summon / Special Messenger">Court Summon / Special Messenger</option>
              </select>
            </label>

            <label>
              Priority *
              <select value={priority} onChange={(e) => setPriority(e.target.value as "Routine" | "Urgent" | "Immediate")}>
                <option value="Routine">Routine</option>
                <option value="Urgent">Urgent</option>
                <option value="Immediate">Immediate / Top Priority</option>
              </select>
            </label>
          </div>
        </fieldset>

        <fieldset>
          <legend>2. Sender Details & References</legend>
          <div className="field-grid">
            <label>
              Sender Name / Entity *
              <input
                type="text"
                required
                placeholder="e.g. Ramesh Chandra / High Court of Delhi / DDA"
                value={senderName}
                onChange={(e) => setSenderName(e.target.value)}
              />
            </label>

            <label>
              Sender Designation
              <input
                type="text"
                placeholder="e.g. Landowner / Advocate / Deputy Director"
                value={senderDesignation}
                onChange={(e) => setSenderDesignation(e.target.value)}
              />
            </label>

            <label>
              Department / Organization
              <input
                type="text"
                placeholder="e.g. Land & Building Dept / PWD / Delhi Metro"
                value={senderDepartment}
                onChange={(e) => setSenderDepartment(e.target.value)}
              />
            </label>

            <label>
              Sender's Letter Reference Number
              <input
                type="text"
                placeholder="e.g. F.1(23)/2025/L&B/LA/450"
                value={senderReferenceNumber}
                onChange={(e) => setSenderReferenceNumber(e.target.value)}
              />
            </label>

            <label>
              Sender Letter Date
              <input
                type="date"
                value={senderLetterDate}
                onChange={(e) => setSenderLetterDate(e.target.value)}
              />
            </label>

            <label>
              Action Due Date
              <input
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
              <span className="hint">Target date for reply or compliance (if specified).</span>
            </label>

            <label className="span-two">
              Sender Address / Contact
              <textarea
                placeholder="Postal address or contact information of the sender..."
                value={senderAddress}
                onChange={(e) => setSenderAddress(e.target.value)}
                rows={2}
              />
            </label>
          </div>
        </fieldset>

        <fieldset>
          <legend>3. Subject & Classification</legend>
          <div className="field-grid">
            <label className="span-two">
              Subject / Synopsis *
              <input
                type="text"
                required
                placeholder="Brief subject of the communication or representation"
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
              />
            </label>

            <label>
              Dak Category
              <select value={categoryId} onChange={(e) => handleCategoryChange(e.target.value)}>
                <option value="">-- Unclassified --</option>
                {categories.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name} ({c.code})
                  </option>
                ))}
              </select>
            </label>

            <label>
              Assigned Workstream
              <select value={workstreamId} onChange={(e) => setWorkstreamId(e.target.value)}>
                <option value="">-- Functional Workstream (Optional) --</option>
                {workstreams.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name} ({w.code})
                  </option>
                ))}
              </select>
            </label>
          </div>
        </fieldset>

        <fieldset>
          <legend>4. Primary Scanned Document (Optional)</legend>
          <label>
            Upload Inward Document (PDF or Scan)
            <input
              type="file"
              accept=".pdf,image/*"
              onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
            />
            <span className="hint">Scanned copy of the received letter or communication. Additional attachments can be added later.</span>
          </label>
        </fieldset>

        <div className="form-footer">
          <Link to="/dak" className="secondary-button">
            Cancel
          </Link>
          <button type="submit" className="primary-button" disabled={submitting}>
            {submitting ? "Registering Inward Dak..." : "Register Inward Dak"}
          </button>
        </div>
      </form>
    </div>
  );
};
