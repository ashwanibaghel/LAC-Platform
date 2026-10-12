import { useState, useRef } from "react";
import { landRecordResponse } from "./LandRecordApiError";

interface VillageTemporaryProceedingsSectionProps {
  villageId: string;
  villageName?: string;
  linkedAwards?: { id: string; awardNumber: string }[];
  onRefresh?: () => void;
}

export function VillageTemporaryProceedingsSection({
  villageId,
  villageName: _villageName,
  linkedAwards = [],
  onRefresh,
}: VillageTemporaryProceedingsSectionProps) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [documentType, setDocumentType] = useState<"TemporaryOffer" | "TemporaryProceedings" | "TemporaryOrder">("TemporaryOffer");
  const [actRegime, setActRegime] = useState("LAA1894");
  const [legalSection, setLegalSection] = useState("35(1)");
  const [purpose, setPurpose] = useState("");
  const [durationMonths, setDurationMonths] = useState("");
  const [legalDate, setLegalDate] = useState("");
  const [commencementDate, setCommencementDate] = useState("");
  const [expiryDate, setExpiryDate] = useState("");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [compensationReference, setCompensationReference] = useState("");
  const [remarks, setRemarks] = useState("");
  const [selectedAwardId, setSelectedAwardId] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState("");
  const [successMessage, setSuccessMessage] = useState("");

  const mutationIdRef = useRef<string>(crypto.randomUUID());

  const handleOpenDrawer = () => {
    setDocumentType("TemporaryOffer");
    setActRegime("LAA1894");
    setLegalSection("35(1)");
    setPurpose("");
    setDurationMonths("");
    setLegalDate("");
    setCommencementDate("");
    setExpiryDate("");
    setReferenceNumber("");
    setCompensationReference("");
    setRemarks("");
    setSelectedAwardId("");
    setFile(null);
    setErrorMessage("");
    setDrawerOpen(true);
    mutationIdRef.current = crypto.randomUUID();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!file) {
      setErrorMessage("Please choose a PDF document for this proceeding.");
      return;
    }
    if (!purpose.trim()) {
      setErrorMessage("Purpose of temporary occupation is required.");
      return;
    }

    setBusy(true);
    setErrorMessage("");

    const form = new FormData();
    form.append("file", file);

    const query = new URLSearchParams({
      mutationId: mutationIdRef.current,
      documentType,
      actRegime,
      legalSection: legalSection.trim(),
      purpose: purpose.trim(),
    });

    if (durationMonths) query.set("durationMonths", durationMonths);
    if (legalDate) query.set("legalDate", legalDate);
    if (commencementDate) query.set("commencementDate", commencementDate);
    if (expiryDate) query.set("expiryDate", expiryDate);
    if (referenceNumber.trim()) query.set("referenceNumber", referenceNumber.trim());
    if (compensationReference.trim()) query.set("compensationReference", compensationReference.trim());
    if (remarks.trim()) query.set("remarks", remarks.trim());
    if (selectedAwardId) query.set("awardId", selectedAwardId);

    try {
      const res = await fetch(`/api/villages/${villageId}/temporary-acquisition-proceedings?${query}`, {
        method: "POST",
        credentials: "include",
        body: form,
      });

      const result = await landRecordResponse<{ proceedingId: string; documentId: string; duplicate: boolean }>(res);
      setSuccessMessage(
        result.duplicate
          ? "Proceeding already registered. Existing record and source reused."
          : `Section 35 ${documentType} successfully recorded.`
      );
      setDrawerOpen(false);
      onRefresh?.();
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : "Failed to record temporary acquisition proceeding.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="village-card temporary-proceedings-section" style={{ marginTop: "20px" }}>
      <div className="village-card-header" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div>
          <h3 className="village-card-title">Section 35 Temporary Occupation Proceedings</h3>
          <p className="village-card-subtitle">
            Temporary land occupation & compensation offers under Section 35 — strictly distinct from final Award acquisitions.
          </p>
        </div>
        <button type="button" className="secondary-button" onClick={handleOpenDrawer}>
          + Record Section 35 Proceeding
        </button>
      </div>

      {successMessage && (
        <div role="status" style={{ backgroundColor: "#f0fdf4", border: "1px solid #bbf7d0", color: "#166534", padding: "10px 14px", borderRadius: "6px", margin: "12px 16px", fontSize: "13px" }}>
          ✓ {successMessage}
        </div>
      )}

      {errorMessage && (
        <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", color: "#991b1b", padding: "10px 14px", borderRadius: "6px", margin: "12px 16px", fontSize: "13px" }}>
          ⚠️ {errorMessage}
        </div>
      )}

      <div style={{ padding: "16px", backgroundColor: "#f8fafc", borderTop: "1px solid #e2e8f0" }}>
        <div style={{ display: "flex", alignItems: "center", gap: "12px", fontSize: "13px", color: "#475569" }}>
          <span>ℹ️</span>
          <span>
            Section 35 proceedings do not create Award declarations or change permanent ownership. All temporary milestones appear chronologically in the Village Acquisition Timeline.
          </span>
        </div>
      </div>

      {drawerOpen && (
        <aside className="workflow-drawer" aria-label="Record Section 35 Proceeding" role="dialog" aria-modal="true">
          <section className="workspace-panel" style={{ maxWidth: "600px", width: "100%", margin: "0 auto" }}>
            <div className="panel-title" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "18px" }}>Record Section 35 Proceeding</h3>
              <button type="button" className="quiet-button" onClick={() => setDrawerOpen(false)}>
                ✕ Close
              </button>
            </div>

            <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: "12px", marginTop: "12px" }}>
              <div style={{ backgroundColor: "#eff6ff", border: "1px solid #bfdbfe", padding: "8px 12px", borderRadius: "6px", fontSize: "12px", color: "#1e40af" }}>
                <strong>Legal Rule:</strong> Section 35 Offer/Order does not manufacture an Award number or invent commencement/expiry dates.
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                <label>
                  Proceeding Type *
                  <select value={documentType} onChange={(e) => setDocumentType(e.target.value as any)}>
                    <option value="TemporaryOffer">Temporary Offer (Compensation Offer)</option>
                    <option value="TemporaryProceedings">Temporary Proceedings (Occupation Minutes)</option>
                    <option value="TemporaryOrder">Temporary Order (Collector Order)</option>
                  </select>
                </label>

                <label>
                  Legal Section *
                  <input
                    type="text"
                    value={legalSection}
                    onChange={(e) => setLegalSection(e.target.value)}
                    placeholder="e.g. 35(1), 35(2)"
                    required
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                <label>
                  Act Regime *
                  <select value={actRegime} onChange={(e) => setActRegime(e.target.value)}>
                    <option value="LAA1894">Land Acquisition Act 1894</option>
                    <option value="RFCTLARR2013">RFCTLARR Act 2013</option>
                  </select>
                </label>

                <label>
                  Duration (Months)
                  <input
                    type="number"
                    min="1"
                    max="36"
                    value={durationMonths}
                    onChange={(e) => setDurationMonths(e.target.value)}
                    placeholder="e.g. 12 or 24"
                  />
                </label>
              </div>

              <label>
                Purpose of Temporary Occupation *
                <input
                  type="text"
                  value={purpose}
                  onChange={(e) => setPurpose(e.target.value)}
                  placeholder="e.g. Construction of temporary drain / bypass"
                  required
                />
              </label>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                <label>
                  Reference Number
                  <input
                    type="text"
                    value={referenceNumber}
                    onChange={(e) => setReferenceNumber(e.target.value)}
                    placeholder="e.g. Offer No. 35/2004"
                  />
                </label>

                <label>
                  Proceeding / Legal Date
                  <input
                    type="date"
                    value={legalDate}
                    onChange={(e) => setLegalDate(e.target.value)}
                  />
                </label>
              </div>

              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                <label>
                  Commencement Date
                  <input
                    type="date"
                    value={commencementDate}
                    onChange={(e) => setCommencementDate(e.target.value)}
                  />
                </label>

                <label>
                  Expiry Date
                  <input
                    type="date"
                    value={expiryDate}
                    onChange={(e) => setExpiryDate(e.target.value)}
                  />
                </label>
              </div>

              <label>
                Compensation Reference / Terms
                <input
                  type="text"
                  value={compensationReference}
                  onChange={(e) => setCompensationReference(e.target.value)}
                  placeholder="e.g. Compensation assessed at Rs. 15,000/bigha/year"
                />
              </label>

              {linkedAwards.length > 0 && (
                <label>
                  Associated Award (Optional)
                  <select value={selectedAwardId} onChange={(e) => setSelectedAwardId(e.target.value)}>
                    <option value="">(None - Independent Section 35 Proceeding)</option>
                    {linkedAwards.map((a) => (
                      <option key={a.id} value={a.id}>
                        Award {a.awardNumber}
                      </option>
                    ))}
                  </select>
                </label>
              )}

              <label>
                Remarks
                <textarea
                  rows={2}
                  value={remarks}
                  onChange={(e) => setRemarks(e.target.value)}
                  placeholder="Optional remarks"
                />
              </label>

              <label>
                Source PDF File *
                <input
                  type="file"
                  accept="application/pdf,.pdf"
                  onChange={(e) => setFile(e.target.files?.[0] || null)}
                  required
                />
              </label>

              {errorMessage && (
                <div role="alert" style={{ backgroundColor: "#fef2f2", border: "1px solid #fecaca", color: "#991b1b", padding: "10px 14px", borderRadius: "6px", fontSize: "13px" }}>
                  {errorMessage}
                </div>
              )}

              <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px", borderTop: "1px solid #e2e8f0", paddingTop: "12px" }}>
                <button type="button" className="quiet-button" onClick={() => setDrawerOpen(false)} disabled={busy}>
                  Cancel
                </button>
                <button type="submit" disabled={busy || !file || !purpose.trim()}>
                  {busy ? "Recording…" : "Record Section 35 Proceeding"}
                </button>
              </div>
            </form>
          </section>
        </aside>
      )}
    </section>
  );
}
