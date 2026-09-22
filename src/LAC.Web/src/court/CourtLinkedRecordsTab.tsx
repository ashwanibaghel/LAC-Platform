import React, { useState } from "react";
import { Link } from "react-router-dom";
import type {
  CourtCaseDetailDto,
  CourtCasePartyDto,
  CourtCaseRepresentativeDto,
} from "./types";

interface CourtLinkedRecordsTabProps {
  courtCase: CourtCaseDetailDto;
  onRefresh: () => void;
}

export const CourtLinkedRecordsTab: React.FC<CourtLinkedRecordsTabProps> = ({ courtCase, onRefresh }) => {
  const canLinkAward = courtCase.capabilities?.canLinkAward ?? false;
  const canLinkKhasra = courtCase.capabilities?.canLinkKhasra ?? false;
  const canEdit = courtCase.capabilities?.canEdit ?? false;

  // Sub-modals state
  const [showAwardModal, setShowAwardModal] = useState(false);
  const [awardIdInput, setAwardIdInput] = useState("");

  const [showKhasraModal, setShowKhasraModal] = useState(false);
  const [khasraIdInput, setKhasraIdInput] = useState("");

  const [showPartyModal, setShowPartyModal] = useState(false);
  const [editingParty, setEditingParty] = useState<CourtCasePartyDto | null>(null);
  const [partyDisplayName, setPartyDisplayName] = useState("");
  const [partyRole, setPartyRole] = useState("Petitioner");
  const [fatherOrSpouseName, setFatherOrSpouseName] = useState("");
  const [addressText, setAddressText] = useState("");
  const [partyRemarks, setPartyRemarks] = useState("");
  const [sequence, setSequence] = useState(0);

  const [showRepModal, setShowRepModal] = useState(false);
  const [editingRep, setEditingRep] = useState<CourtCaseRepresentativeDto | null>(null);
  const [repDisplayName, setRepDisplayName] = useState("");
  const [representativeType, setRepresentativeType] = useState("Counsel");
  const [representsRole, setRepresentsRole] = useState("");
  const [contactText, setContactText] = useState("");
  const [repRemarks, setRepRemarks] = useState("");
  const [courtCasePartyId, setCourtCasePartyId] = useState<string | null>(null);

  const [submitting, setSubmitting] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  // Link / Unlink Award
  const handleLinkAward = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/awards`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          awardId: awardIdInput.trim(),
          expectedRevision: courtCase.revision,
        }),
      });
      if (res.ok) {
        setShowAwardModal(false);
        setAwardIdInput("");
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to link award" }));
        setModalError(err.error || "Failed to link award");
      }
    } catch {
      setModalError("Network error linking award");
    } finally {
      setSubmitting(false);
    }
  };

  const handleUnlinkAward = async (awardId: string) => {
    if (!window.confirm("Are you sure you want to unlink this Award from the court case?")) return;
    try {
      const url = courtCase.revision != null
        ? `/api/court-cases/${courtCase.id}/awards/${awardId}?expectedRevision=${courtCase.revision}`
        : `/api/court-cases/${courtCase.id}/awards/${awardId}`;
      const res = await fetch(url, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) onRefresh();
      else alert("Failed to unlink award");
    } catch {
      alert("Network error unlinking award");
    }
  };

  // Link / Unlink Khasra
  const handleLinkKhasra = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    try {
      setSubmitting(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/khasras`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          khasraId: khasraIdInput.trim(),
          expectedRevision: courtCase.revision,
        }),
      });
      if (res.ok) {
        setShowKhasraModal(false);
        setKhasraIdInput("");
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to link khasra" }));
        setModalError(err.error || "Failed to link khasra");
      }
    } catch {
      setModalError("Network error linking khasra");
    } finally {
      setSubmitting(false);
    }
  };

  const handleUnlinkKhasra = async (khasraId: string) => {
    if (!window.confirm("Are you sure you want to unlink this Khasra from the court case?")) return;
    try {
      const url = courtCase.revision != null
        ? `/api/court-cases/${courtCase.id}/khasras/${khasraId}?expectedRevision=${courtCase.revision}`
        : `/api/court-cases/${courtCase.id}/khasras/${khasraId}`;
      const res = await fetch(url, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) onRefresh();
      else alert("Failed to unlink khasra");
    } catch {
      alert("Network error unlinking khasra");
    }
  };

  // Party CRUD
  const openAddPartyModal = () => {
    setEditingParty(null);
    setPartyDisplayName("");
    setPartyRole("Petitioner");
    setFatherOrSpouseName("");
    setAddressText("");
    setPartyRemarks("");
    setSequence(0);
    setModalError(null);
    setShowPartyModal(true);
  };

  const openEditPartyModal = (p: CourtCasePartyDto) => {
    setEditingParty(p);
    setPartyDisplayName(p.displayName);
    setPartyRole(p.role);
    setFatherOrSpouseName(p.fatherOrSpouseName || "");
    setAddressText(p.addressText || "");
    setPartyRemarks(p.remarks || "");
    setSequence(p.sequence || 0);
    setModalError(null);
    setShowPartyModal(true);
  };

  const handleSaveParty = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    try {
      setSubmitting(true);
      const url = editingParty
        ? `/api/court-cases/${courtCase.id}/parties/${editingParty.id}`
        : `/api/court-cases/${courtCase.id}/parties`;
      const method = editingParty ? "PUT" : "POST";

      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          displayName: partyDisplayName.trim(),
          role: partyRole,
          fatherOrSpouseName: fatherOrSpouseName ? fatherOrSpouseName.trim() : null,
          addressText: addressText ? addressText.trim() : null,
          remarks: partyRemarks ? partyRemarks.trim() : null,
          sequence,
          expectedRevision: courtCase.revision,
        }),
      });

      if (res.ok) {
        setShowPartyModal(false);
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to save party" }));
        setModalError(err.error || "Failed to save party");
      }
    } catch {
      setModalError("Network error saving party");
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemoveParty = async (partyId: string) => {
    if (!window.confirm("Remove this party from the case?")) return;
    try {
      const url = courtCase.revision != null
        ? `/api/court-cases/${courtCase.id}/parties/${partyId}?expectedRevision=${courtCase.revision}`
        : `/api/court-cases/${courtCase.id}/parties/${partyId}`;
      const res = await fetch(url, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) onRefresh();
      else alert("Failed to remove party");
    } catch {
      alert("Network error removing party");
    }
  };

  // Representative CRUD
  const openAddRepModal = () => {
    setEditingRep(null);
    setRepDisplayName("");
    setRepresentativeType("Counsel");
    setRepresentsRole("");
    setContactText("");
    setRepRemarks("");
    setCourtCasePartyId(null);
    setModalError(null);
    setShowRepModal(true);
  };

  const openEditRepModal = (r: CourtCaseRepresentativeDto) => {
    setEditingRep(r);
    setRepDisplayName(r.displayName);
    setRepresentativeType(r.representativeType);
    setRepresentsRole(r.representsRole || "");
    setContactText(r.contactText || "");
    setRepRemarks(r.remarks || "");
    setCourtCasePartyId(r.courtCasePartyId || null);
    setModalError(null);
    setShowRepModal(true);
  };

  const handleSaveRep = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    try {
      setSubmitting(true);
      const url = editingRep
        ? `/api/court-cases/${courtCase.id}/representatives/${editingRep.id}`
        : `/api/court-cases/${courtCase.id}/representatives`;
      const method = editingRep ? "PUT" : "POST";

      const res = await fetch(url, {
        method,
        headers: { "Content-Type": "application/json" },
        credentials: "include",
        body: JSON.stringify({
          displayName: repDisplayName.trim(),
          representativeType,
          representsRole: representsRole ? representsRole.trim() : null,
          contactText: contactText ? contactText.trim() : null,
          remarks: repRemarks ? repRemarks.trim() : null,
          courtCasePartyId: courtCasePartyId || null,
          expectedRevision: courtCase.revision,
        }),
      });

      if (res.ok) {
        setShowRepModal(false);
        onRefresh();
      } else {
        const err = await res.json().catch(() => ({ error: "Failed to save counsel" }));
        setModalError(err.error || "Failed to save counsel");
      }
    } catch {
      setModalError("Network error saving counsel");
    } finally {
      setSubmitting(false);
    }
  };

  const handleRemoveRep = async (repId: string) => {
    if (!window.confirm("Remove this representative from the case?")) return;
    try {
      const url = courtCase.revision != null
        ? `/api/court-cases/${courtCase.id}/representatives/${repId}?expectedRevision=${courtCase.revision}`
        : `/api/court-cases/${courtCase.id}/representatives/${repId}`;
      const res = await fetch(url, {
        method: "DELETE",
        credentials: "include",
      });
      if (res.ok) onRefresh();
      else alert("Failed to remove representative");
    } catch {
      alert("Network error removing representative");
    }
  };

  const awards = courtCase.awards ?? [];
  const khasras = courtCase.khasras ?? [];
  const parties = courtCase.parties ?? [];
  const reps = courtCase.representatives ?? [];

  return (
    <div className="court-linked-records-tab" style={{ display: "flex", flexDirection: "column", gap: "28px" }}>
      {/* 1. Linked Awards */}
      <div>
        <div className="court-section-header">
          <h3>Linked Awards {courtCase.awardsCount !== null ? `(${courtCase.awardsCount ?? awards.length})` : ""}</h3>
          {canLinkAward && (
            <button className="secondary-button" onClick={() => { setModalError(null); setAwardIdInput(""); setShowAwardModal(true); }}>
              + Link Award
            </button>
          )}
        </div>

        {courtCase.awardsCount === null ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            You do not have permission to view linked awards.
          </div>
        ) : awards.length === 0 ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            No awards linked to this court case.
          </div>
        ) : (
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))", gap: "12px" }}>
            {awards.map((aw) => (
              <div
                key={aw.awardId}
                style={{
                  background: "#fff",
                  border: "1px solid #e2e8f0",
                  borderRadius: "8px",
                  padding: "16px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                }}
              >
                <div>
                  <Link
                    to={`/awards/${aw.awardId}`}
                    style={{ fontWeight: 700, color: "#2563eb", textDecoration: "none", fontSize: "15px" }}
                  >
                    Award No. {aw.awardNumber}
                  </Link>
                  {aw.villageNames && aw.villageNames.length > 0 && (
                    <div style={{ fontSize: "13px", color: "#475569", marginTop: "4px" }}>
                      Villages: {aw.villageNames.join(", ")}
                    </div>
                  )}
                  {aw.awardDate && (
                    <div style={{ fontSize: "12px", color: "#94a3b8", marginTop: "2px" }}>
                      Dated: {aw.awardDate}
                    </div>
                  )}
                </div>
                {canLinkAward && (
                  <button
                    className="quiet-button"
                    style={{ color: "#dc2626" }}
                    onClick={() => void handleUnlinkAward(aw.awardId)}
                    title="Unlink Award"
                  >
                    ✕
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* 2. Linked Khasras */}
      <div>
        <div className="court-section-header">
          <h3>Linked Khasras {courtCase.khasrasCount !== null ? `(${courtCase.khasrasCount ?? khasras.length})` : ""}</h3>
          {canLinkKhasra && (
            <button className="secondary-button" onClick={() => { setModalError(null); setKhasraIdInput(""); setShowKhasraModal(true); }}>
              + Link Khasra
            </button>
          )}
        </div>

        {courtCase.khasrasCount === null ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            You do not have permission to view linked khasras.
          </div>
        ) : khasras.length === 0 ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            No specific khasras linked to this court case.
          </div>
        ) : (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
            <table className="data-table" style={{ width: "100%", borderCollapse: "collapse" }}>
              <thead>
                <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0", textAlign: "left" }}>
                  <th style={{ padding: "10px 16px" }}>Village</th>
                  <th style={{ padding: "10px 16px" }}>Khasra Number</th>
                  <th style={{ padding: "10px 16px" }}>Area</th>
                  <th style={{ padding: "10px 16px", textAlign: "right" }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {khasras.map((kh) => (
                  <tr key={kh.khasraId} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "10px 16px", color: "#334155" }}>{kh.villageName}</td>
                    <td style={{ padding: "10px 16px" }}>
                      <Link to={`/khasras/${kh.khasraId}`} style={{ fontWeight: 600, color: "#2563eb", textDecoration: "none" }}>
                        {kh.normalizedNumber} {kh.qualifier && `(${kh.qualifier})`}
                      </Link>
                    </td>
                    <td style={{ padding: "10px 16px", color: "#64748b", fontSize: "13px" }}>
                      {kh.recordedArea != null ? `${kh.recordedArea} ${kh.areaUnit || ""}` : "—"}
                    </td>
                    <td style={{ padding: "10px 16px", textAlign: "right" }}>
                      {canLinkKhasra && (
                        <button
                          className="quiet-button"
                          style={{ color: "#dc2626" }}
                          onClick={() => void handleUnlinkKhasra(kh.khasraId)}
                          title="Unlink Khasra"
                        >
                          ✕
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* 3. Parties */}
      <div>
        <div className="court-section-header">
          <h3>Parties to Litigation ({parties.length})</h3>
          {canEdit && (
            <button className="secondary-button" onClick={openAddPartyModal}>
              + Add Party
            </button>
          )}
        </div>

        {parties.length === 0 ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            No parties recorded for this court case.
          </div>
        ) : (
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: "12px" }}>
            {parties.map((p) => (
              <div
                key={p.id}
                style={{
                  background: "#fff",
                  border: "1px solid #e2e8f0",
                  borderRadius: "8px",
                  padding: "16px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                }}
              >
                <div>
                  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                    <span className="court-badge court-badge-ndoh">{p.role}</span>
                  </div>
                  <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a", marginTop: "6px" }}>
                    {p.displayName}
                  </div>
                  {p.fatherOrSpouseName && (
                    <div style={{ fontSize: "13px", color: "#475569", marginTop: "4px" }}>
                      Father/Spouse: {p.fatherOrSpouseName}
                    </div>
                  )}
                  {p.addressText && (
                    <div style={{ fontSize: "12px", color: "#64748b", marginTop: "2px" }}>
                      Address: {p.addressText}
                    </div>
                  )}
                  {p.remarks && (
                    <div style={{ fontSize: "12px", color: "#94a3b8", marginTop: "2px" }}>
                      {p.remarks}
                    </div>
                  )}
                </div>
                {canEdit && (
                  <div style={{ display: "flex", gap: "4px" }}>
                    <button className="quiet-button" onClick={() => openEditPartyModal(p)} title="Edit">
                      ✎
                    </button>
                    <button
                      className="quiet-button"
                      style={{ color: "#dc2626" }}
                      onClick={() => void handleRemoveParty(p.id)}
                      title="Remove"
                    >
                      ✕
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* 4. Representatives / Counsel */}
      <div>
        <div className="court-section-header">
          <h3>Standing Counsel & Representatives ({reps.length})</h3>
          {canEdit && (
            <button className="secondary-button" onClick={openAddRepModal}>
              + Add Counsel
            </button>
          )}
        </div>

        {reps.length === 0 ? (
          <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "20px", color: "#64748b" }}>
            No legal counsel or government representatives assigned.
          </div>
        ) : (
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(300px, 1fr))", gap: "12px" }}>
            {reps.map((r) => (
              <div
                key={r.id}
                style={{
                  background: "#fff",
                  border: "1px solid #e2e8f0",
                  borderRadius: "8px",
                  padding: "16px",
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                }}
              >
                <div>
                  <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                    <span className="court-badge court-badge-ndoh">{r.representativeType}</span>
                  </div>
                  <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a", marginTop: "6px" }}>
                    {r.displayName}
                  </div>
                  {r.representsRole && (
                    <div style={{ fontSize: "13px", color: "#475569", marginTop: "2px" }}>
                      Represents: {r.representsRole}
                    </div>
                  )}
                  {r.contactText && (
                    <div style={{ fontSize: "12px", color: "#64748b", marginTop: "2px" }}>
                      Contact: {r.contactText}
                    </div>
                  )}
                  {r.remarks && (
                    <div style={{ fontSize: "12px", color: "#94a3b8", marginTop: "2px" }}>
                      {r.remarks}
                    </div>
                  )}
                </div>
                {canEdit && (
                  <div style={{ display: "flex", gap: "4px" }}>
                    <button className="quiet-button" onClick={() => openEditRepModal(r)} title="Edit">
                      ✎
                    </button>
                    <button
                      className="quiet-button"
                      style={{ color: "#dc2626" }}
                      onClick={() => void handleRemoveRep(r.id)}
                      title="Remove"
                    >
                      ✕
                    </button>
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Award Modal */}
      {showAwardModal && (
        <div className="court-modal-backdrop" onClick={() => setShowAwardModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Link Award to Court Case</h3>
              <button className="quiet-button" onClick={() => setShowAwardModal(false)}>✕</button>
            </div>
            <form onSubmit={handleLinkAward}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Award GUID *
                  </label>
                  <input
                    type="text"
                    required
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="Enter Award ID"
                    value={awardIdInput}
                    onChange={(e) => setAwardIdInput(e.target.value)}
                  />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowAwardModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Linking..." : "Link Award"}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Khasra Modal */}
      {showKhasraModal && (
        <div className="court-modal-backdrop" onClick={() => setShowKhasraModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>Link Khasra to Court Case</h3>
              <button className="quiet-button" onClick={() => setShowKhasraModal(false)}>✕</button>
            </div>
            <form onSubmit={handleLinkKhasra}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>
                    Khasra GUID *
                  </label>
                  <input
                    type="text"
                    required
                    className="form-input"
                    style={{ width: "100%" }}
                    placeholder="Enter Khasra ID"
                    value={khasraIdInput}
                    onChange={(e) => setKhasraIdInput(e.target.value)}
                  />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowKhasraModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Linking..." : "Link Khasra"}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Party Modal */}
      {showPartyModal && (
        <div className="court-modal-backdrop" onClick={() => setShowPartyModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>{editingParty ? "Edit Party" : "Add Party to Case"}</h3>
              <button className="quiet-button" onClick={() => setShowPartyModal(false)}>✕</button>
            </div>
            <form onSubmit={handleSaveParty}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Party Role</label>
                  <select className="form-input" style={{ width: "100%" }} value={partyRole} onChange={(e) => setPartyRole(e.target.value)}>
                    <option value="Petitioner">Petitioner</option>
                    <option value="Respondent">Respondent</option>
                    <option value="Proforma Respondent">Proforma Respondent</option>
                    <option value="Claimant">Claimant</option>
                    <option value="Applicant">Applicant</option>
                    <option value="Non-Applicant">Non-Applicant</option>
                    <option value="Other">Other</option>
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Display Name *</label>
                  <input type="text" required className="form-input" style={{ width: "100%" }} value={partyDisplayName} onChange={(e) => setPartyDisplayName(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Father / Spouse Name</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={fatherOrSpouseName} onChange={(e) => setFatherOrSpouseName(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Address Text</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={addressText} onChange={(e) => setAddressText(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Remarks</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={partyRemarks} onChange={(e) => setPartyRemarks(e.target.value)} />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowPartyModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Saving..." : "Save Party"}</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Representative Modal */}
      {showRepModal && (
        <div className="court-modal-backdrop" onClick={() => setShowRepModal(false)}>
          <div className="court-modal" onClick={(e) => e.stopPropagation()}>
            <div className="court-modal-header">
              <h3>{editingRep ? "Edit Counsel" : "Add Legal Counsel"}</h3>
              <button className="quiet-button" onClick={() => setShowRepModal(false)}>✕</button>
            </div>
            <form onSubmit={handleSaveRep}>
              <div className="court-modal-body">
                {modalError && <div style={{ color: "#b91c1c", background: "#fef2f2", padding: "10px", borderRadius: "6px" }}>{modalError}</div>}
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Representative Type</label>
                  <select className="form-input" style={{ width: "100%" }} value={representativeType} onChange={(e) => setRepresentativeType(e.target.value)}>
                    <option value="Counsel">Counsel</option>
                    <option value="Standing Counsel">Standing Counsel (Govt)</option>
                    <option value="Additional Standing Counsel">Additional Standing Counsel</option>
                    <option value="Government Pleader">Government Pleader</option>
                    <option value="Advocate on Record">Advocate on Record</option>
                    <option value="Private Counsel">Private Counsel</option>
                    <option value="Representative">Departmental Representative</option>
                  </select>
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Display Name *</label>
                  <input type="text" required className="form-input" style={{ width: "100%" }} value={repDisplayName} onChange={(e) => setRepDisplayName(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Represents Role</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={representsRole} onChange={(e) => setRepresentsRole(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Contact Text</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={contactText} onChange={(e) => setContactText(e.target.value)} />
                </div>
                <div>
                  <label style={{ display: "block", fontSize: "13px", fontWeight: 600, color: "#334155", marginBottom: "4px" }}>Remarks</label>
                  <input type="text" className="form-input" style={{ width: "100%" }} value={repRemarks} onChange={(e) => setRepRemarks(e.target.value)} />
                </div>
              </div>
              <div className="court-modal-footer">
                <button type="button" className="secondary-button" onClick={() => setShowRepModal(false)} disabled={submitting}>Cancel</button>
                <button type="submit" className="primary-button" disabled={submitting}>{submitting ? "Saving..." : "Save Counsel"}</button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
