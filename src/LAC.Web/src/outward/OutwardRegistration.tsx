import React, { useState, useEffect } from "react";
import { useNavigate, Link, useSearchParams } from "react-router-dom";
import type { OutwardRegistrationContext } from "./types";
import "./outward.css";

export const OutwardRegistration: React.FC = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const dakIdParam = searchParams.get("dakId") || "";
  const matterIdParam = searchParams.get("matterId") || "";

  const [outwardNumber, setOutwardNumber] = useState("");
  const [outwardDate, setOutwardDate] = useState(() => new Date().toISOString().split("T")[0]);
  const [issuingDeskId, setIssuingDeskId] = useState("");
  const [workstreamId, setWorkstreamId] = useState("");
  const [officeReferenceNumber, setOfficeReferenceNumber] = useState("");
  const [subject, setSubject] = useState("");
  const [recipientName, setRecipientName] = useState("");
  const [recipientDesignation, setRecipientDesignation] = useState("");
  const [recipientDepartment, setRecipientDepartment] = useState("");
  const [recipientAddress, setRecipientAddress] = useState("");
  const [recipientEmail, setRecipientEmail] = useState("");
  const [recipientPhone, setRecipientPhone] = useState("");
  const [remarks, setRemarks] = useState("");
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const [context, setContext] = useState<OutwardRegistrationContext | null>(null);
  const [dakSummary, setDakSummary] = useState<{ diaryNumber: string; subject: string; senderName: string } | null>(null);
  const [matterSummary, setMatterSummary] = useState<{ matterNumber: string; title: string } | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [loadingContext, setLoadingContext] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    // Load registration context (active desks and workstreams)
    fetch("/api/outward/context", { credentials: "include" })
      .then((r) => {
        if (!r.ok) throw new Error("Failed to load outward registration context.");
        return r.json() as Promise<OutwardRegistrationContext>;
      })
      .then((data) => {
        setContext(data);
        if (data.desks.length > 0) {
          const primary = data.desks.find((d) => d.isPrimary) || data.desks[0];
          setIssuingDeskId(primary.id);
        }
        if (data.workstreams.length === 1) {
          setWorkstreamId(data.workstreams[0].id);
        }
      })
      .catch((err: unknown) => {
        setError(err instanceof Error ? err.message : "Failed to load desk context.");
      })
      .finally(() => setLoadingContext(false));
  }, []);

  // If dakIdParam provided, fetch Dak details to prefill fields
  useEffect(() => {
    if (!dakIdParam) return;
    fetch(`/api/dak/${dakIdParam}`, { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then((dak) => {
        if (dak) {
          setDakSummary({
            diaryNumber: dak.diaryNumber,
            subject: dak.subject,
            senderName: dak.senderName,
          });
          if (!subject) {
            setSubject(`Reply to Dak No. ${dak.diaryNumber}: ${dak.subject}`);
          }
          if (!recipientName) {
            setRecipientName(dak.senderName);
          }
          if (!recipientDesignation && dak.senderDesignation) {
            setRecipientDesignation(dak.senderDesignation);
          }
          if (!recipientDepartment && dak.senderDepartment) {
            setRecipientDepartment(dak.senderDepartment);
          }
          if (!recipientAddress && dak.senderAddress) {
            setRecipientAddress(dak.senderAddress);
          }
          if (!officeReferenceNumber && dak.senderReferenceNumber) {
            setOfficeReferenceNumber(dak.senderReferenceNumber);
          }
          if (dak.workstreamId) {
            setWorkstreamId(dak.workstreamId);
          }
        }
      })
      .catch(() => {});
  }, [dakIdParam]);

  // If matterIdParam provided, fetch Matter details
  useEffect(() => {
    if (!matterIdParam) return;
    fetch(`/api/matters/${matterIdParam}`, { credentials: "include" })
      .then((r) => (r.ok ? r.json() : null))
      .then((matter) => {
        if (matter) {
          setMatterSummary({
            matterNumber: matter.matterNumber,
            title: matter.title,
          });
        }
      })
      .catch(() => {});
  }, [matterIdParam]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);

    if (!outwardNumber.trim()) {
      setError("Outward Number is mandatory.");
      return;
    }
    if (!outwardDate) {
      setError("Outward Date is mandatory.");
      return;
    }
    if (!issuingDeskId) {
      setError("An Issuing Desk must be selected from your active memberships.");
      return;
    }
    if (!subject.trim()) {
      setError("Subject is mandatory.");
      return;
    }
    if (!recipientName.trim()) {
      setError("Recipient Name is mandatory.");
      return;
    }

    setSubmitting(true);

    try {
      const formData = new FormData();
      formData.append("outwardNumber", outwardNumber.trim());
      formData.append("outwardDate", outwardDate);
      formData.append("issuingDeskId", issuingDeskId);
      if (workstreamId) formData.append("workstreamId", workstreamId);
      if (officeReferenceNumber.trim()) formData.append("officeReferenceNumber", officeReferenceNumber.trim());
      formData.append("subject", subject.trim());
      formData.append("recipientName", recipientName.trim());
      if (recipientDesignation.trim()) formData.append("recipientDesignation", recipientDesignation.trim());
      if (recipientDepartment.trim()) formData.append("recipientDepartment", recipientDepartment.trim());
      if (recipientAddress.trim()) formData.append("recipientAddress", recipientAddress.trim());
      if (recipientEmail.trim()) formData.append("recipientEmail", recipientEmail.trim());
      if (recipientPhone.trim()) formData.append("recipientPhone", recipientPhone.trim());
      if (remarks.trim()) formData.append("remarks", remarks.trim());
      if (matterIdParam) formData.append("matterId", matterIdParam);
      if (selectedFile) formData.append("file", selectedFile);

      const res = await fetch("/api/outward", {
        method: "POST",
        credentials: "include",
        body: formData,
      });

      if (!res.ok) {
        const data = await res.json().catch(() => null);
        throw new Error(data?.detail || data?.message || "Failed to register Outward dispatch.");
      }

      const result = (await res.json()) as { id: string };

      // If dakId was provided, link it immediately as primary reply
      if (dakIdParam) {
        try {
          await fetch(`/api/outward/${result.id}/dak-links`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            credentials: "include",
            body: JSON.stringify({
              dakId: dakIdParam,
              relationshipType: "PrimaryReply",
              isPrimary: true,
              remarks: "Primary reply linked at registration",
            }),
          });
        } catch {
          // Failure to auto-link dak does not invalidate outward registration
        }
      }

      navigate(`/outward/${result.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to register Outward record.");
      setSubmitting(false);
    }
  };

  return (
    <div className="outward-register-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <Link to="/outward">Outward / Dispatch</Link> <i>/</i> <span>Register Outward</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow">Dispatch & Issue</div>
          <h1>Register Outward Communication</h1>
          <p>Register official outgoing government letters, notices, replies, and compliance reports.</p>
        </div>
      </div>

      {error && <div className="state error">{error}</div>}

      {dakSummary && (
        <div className="dak-attention-banner" style={{ background: "#eff6ff", borderColor: "#3b82f6", color: "#1e40af" }}>
          <div>
            <strong>🔗 Replying to Inward Dak: {dakSummary.diaryNumber}</strong>
            <div style={{ fontSize: "12px", marginTop: "4px" }}>
              Subject: {dakSummary.subject} | From: {dakSummary.senderName}
            </div>
            <div style={{ fontSize: "11px", color: "#2563eb", marginTop: "2px" }}>
              This Outward communication will automatically be linked as the Primary Reply upon creation.
            </div>
          </div>
        </div>
      )}

      {matterSummary && (
        <div className="dak-attention-banner" style={{ background: "#f0fdf4", borderColor: "#22c55e", color: "#166534" }}>
          <div>
            <strong>🏛 Linked to Legal Matter: {matterSummary.matterNumber}</strong>
            <div style={{ fontSize: "12px", marginTop: "4px" }}>
              Title: {matterSummary.title}
            </div>
          </div>
        </div>
      )}

      {context && context.desks.length === 0 && !loadingContext && (
        <div className="dak-attention-banner" style={{ background: "#fef2f2", borderColor: "#ef4444", color: "#991b1b" }}>
          <div>
            <strong>Action Prohibited: No Active Desk Membership</strong>
            <div style={{ fontSize: "12px", marginTop: "4px" }}>
              You are not a member of any active office desk. All outward communications must originate from an active desk where you hold active membership.
            </div>
          </div>
        </div>
      )}

      <form className="lr-form" onSubmit={handleSubmit}>
        <fieldset>
          <legend>1. Dispatch Identification & Originating Desk</legend>
          <div className="field-grid">
            <label>
              Outward Number *
              <input
                type="text"
                required
                placeholder="e.g. LAC/OUT/2026/001"
                value={outwardNumber}
                onChange={(e) => setOutwardNumber(e.target.value)}
              />
            </label>

            <label>
              Outward Date *
              <input
                type="date"
                required
                value={outwardDate}
                onChange={(e) => setOutwardDate(e.target.value)}
              />
            </label>

            <label>
              Issuing Office Desk *
              <select
                required
                value={issuingDeskId}
                onChange={(e) => setIssuingDeskId(e.target.value)}
                disabled={!context || context.desks.length === 0}
              >
                <option value="">-- Select Active Desk --</option>
                {context?.desks.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name} ({d.code}) {d.isPrimary ? "• Primary" : ""}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Workstream (Optional)
              <select
                value={workstreamId}
                onChange={(e) => setWorkstreamId(e.target.value)}
              >
                <option value="">-- Select Workstream (Optional) --</option>
                {context?.workstreams.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name} ({w.code})
                  </option>
                ))}
              </select>
            </label>

            <label>
              Office Reference / File Number
              <input
                type="text"
                placeholder="e.g. 10/LAC/LA/2026"
                value={officeReferenceNumber}
                onChange={(e) => setOfficeReferenceNumber(e.target.value)}
              />
            </label>
          </div>
        </fieldset>

        <fieldset>
          <legend>2. Recipient Information & Subject</legend>
          <div className="field-grid">
            <label style={{ gridColumn: "1 / -1" }}>
              Subject / Matter Description *
              <input
                type="text"
                required
                placeholder="Official subject of communication"
                value={subject}
                onChange={(e) => setSubject(e.target.value)}
              />
            </label>

            <label>
              Recipient Name / Entity *
              <input
                type="text"
                required
                placeholder="e.g. Principal Secretary / Collector / Ram Kumar"
                value={recipientName}
                onChange={(e) => setRecipientName(e.target.value)}
              />
            </label>

            <label>
              Recipient Designation
              <input
                type="text"
                placeholder="e.g. Special Secretary / ADM (LA)"
                value={recipientDesignation}
                onChange={(e) => setRecipientDesignation(e.target.value)}
              />
            </label>

            <label>
              Recipient Department / Office
              <input
                type="text"
                placeholder="e.g. Department of Revenue / High Court"
                value={recipientDepartment}
                onChange={(e) => setRecipientDepartment(e.target.value)}
              />
            </label>

            <label>
              Recipient Email (Optional)
              <input
                type="email"
                placeholder="e.g. officer@delhi.gov.in"
                value={recipientEmail}
                onChange={(e) => setRecipientEmail(e.target.value)}
              />
            </label>

            <label>
              Recipient Phone (Optional)
              <input
                type="tel"
                placeholder="e.g. +91 9876543210"
                value={recipientPhone}
                onChange={(e) => setRecipientPhone(e.target.value)}
              />
            </label>

            <label style={{ gridColumn: "1 / -1" }}>
              Postal / Dispatch Address
              <textarea
                rows={2}
                placeholder="Full postal delivery address"
                value={recipientAddress}
                onChange={(e) => setRecipientAddress(e.target.value)}
              />
            </label>
          </div>
        </fieldset>

        <fieldset>
          <legend>3. Outward Document & Official Remarks</legend>
          <div className="field-grid">
            <label style={{ gridColumn: "1 / -1" }}>
              Main Outward Document (PDF or Signed Scan)
              <input
                type="file"
                accept=".pdf,.png,.jpg,.jpeg,.doc,.docx"
                onChange={(e) => {
                  if (e.target.files && e.target.files.length > 0) {
                    setSelectedFile(e.target.files[0]);
                  } else {
                    setSelectedFile(null);
                  }
                }}
              />
              <span className="field-help" style={{ fontSize: "11px", color: "#64748b", marginTop: "4px", display: "block" }}>
                Accepted formats: PDF, PNG, JPEG. Document can also be added or replaced before final dispatch.
              </span>
            </label>

            <label style={{ gridColumn: "1 / -1" }}>
              Internal Remarks / Instructions
              <textarea
                rows={2}
                placeholder="Notes for dispatch clerk or internal office records"
                value={remarks}
                onChange={(e) => setRemarks(e.target.value)}
              />
            </label>
          </div>
        </fieldset>

        <div className="form-actions" style={{ display: "flex", gap: "10px", marginTop: "20px" }}>
          <button
            type="submit"
            className="primary-button"
            disabled={submitting || (context ? context.desks.length === 0 : false)}
          >
            {submitting ? "Registering Outward..." : "Complete Registration"}
          </button>
          <button
            type="button"
            className="secondary-button"
            onClick={() => navigate(dakIdParam ? `/dak/${dakIdParam}` : "/outward")}
            disabled={submitting}
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
};
