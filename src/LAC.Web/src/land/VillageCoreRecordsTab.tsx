import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { IconPlus, IconChevronRight } from "../components/Icons";

const api = "/api";

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

const CORE_ROLES = [
  { key: "Award", label: "Award PDF" },
  { key: "NM", label: "NM / ENM Register" },
  { key: "StatementA", label: "Statement-A" },
  { key: "PossessionProceeding", label: "Possession Proceedings" }
] as const;

export const VillageCoreRecordsTab: React.FC<{ id: string }> = ({ id }) => {
  const [refresh, setRefresh] = useState(0);
  const [records, setRecords] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal States
  const [addAwardModalOpen, setAddAwardModalOpen] = useState(false);
  const [newAward, setNewAward] = useState({ awardNumber: "", awardDate: "", awardType: "" });

  const [uploadModalOpen, setUploadModalOpen] = useState(false);
  const [uploadTarget, setUploadTarget] = useState<{ awardId: string; awardNumber: string; role: string } | null>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);

  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  // Fetch core records
  useEffect(() => {
    let active = true;
    setLoading(true);
    fetch(`${api}/villages/${id}/core-records?r=${refresh}`, { credentials: "include" })
      .then(async (r) => {
        if (!r.ok) throw new Error("Could not load core records.");
        return r.json();
      })
      .then((d) => {
        if (active) {
          setRecords(d);
          setLoading(false);
        }
      })
      .catch((e) => {
        if (active) {
          setError(e?.message || "Failed to load core records.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [id, refresh]);

  // Handle Add Award
  const handleCreateAward = async () => {
    if (!newAward.awardNumber.trim()) return;
    try {
      setBusy(true);
      setMessage("");
      const res = await fetch(`${api}/villages/${id}/awards`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...newAward,
          awardDate: newAward.awardDate || null,
          remarks: null
        }),
        credentials: "include"
      });
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.title || "Could not add Award.");
      }
      setNewAward({ awardNumber: "", awardDate: "", awardType: "" });
      setAddAwardModalOpen(false);
      setRefresh((x) => x + 1);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not add Award.");
    } finally {
      setBusy(false);
    }
  };

  // Handle Upload Core Document
  const handleUploadDocument = async () => {
    if (!uploadTarget || !selectedFile) return;
    try {
      setBusy(true);
      setMessage("");
      const form = new FormData();
      form.append("file", selectedFile);
      const res = await fetch(
        `${api}/awards/${uploadTarget.awardId}/core-documents?role=${encodeURIComponent(uploadTarget.role)}`,
        { method: "POST", body: form, credentials: "include" }
      );
      if (!res.ok) {
        const errText = await res.text().catch(() => "");
        throw new Error(errText || "Could not upload document.");
      }
      setUploadModalOpen(false);
      setUploadTarget(null);
      setSelectedFile(null);
      setRefresh((x) => x + 1);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : "Could not upload core document.");
    } finally {
      setBusy(false);
    }
  };

  const openUploadModal = (awardId: string, awardNumber: string, role: string) => {
    setUploadTarget({ awardId, awardNumber, role });
    setSelectedFile(null);
    setMessage("");
    setUploadModalOpen(true);
  };

  if (loading) return <div className="state loading">Loading core records…</div>;
  if (error) return <div className="state error"><strong>Unable to load core records.</strong><span>{error}</span></div>;

  return (
    <div className="village-core-records-tab flex flex-col gap-5">
      {/* Header & Add Action */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-base font-bold text-slate-900 m-0">Core Records Matrix</h2>
          <span className="text-xs text-slate-500">
            Award-level core documents linked to this village. Reused automatically by all related Matters.
          </span>
        </div>

        <button
          className="home-action-btn home-action-btn-primary"
          onClick={() => {
            setMessage("");
            setAddAwardModalOpen(true);
          }}
        >
          <IconPlus size={15} />
          <span>Add Award</span>
        </button>
      </div>

      {message && <div className="form-message role-alert" role="alert">{message}</div>}

      {/* Matrix Table */}
      {records.length === 0 ? (
        <div className="state empty">
          <strong>No Awards linked yet</strong>
          <span>Click "+ Add Award" to create an Award entry for this village.</span>
        </div>
      ) : (
        <div className="core-matrix-wrap">
          <table className="core-matrix-table">
            <thead>
              <tr>
                <th>Award Number</th>
                <th>Award Date</th>
                <th>Award Type</th>
                <th>Award PDF</th>
                <th>NM / ENM</th>
                <th>Statement-A</th>
                <th>Possession</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {records.map((award: any) => {
                const getRoleCount = (roleKey: string) => {
                  const roleObj = award.roles?.find((r: any) => r.role === roleKey);
                  return roleObj ? roleObj.count : 0;
                };

                return (
                  <tr key={award.id}>
                    <td>
                      <Link to={`/awards/${award.id}`} className="entity-link" style={{ fontWeight: 700 }}>
                        {award.awardNumber}
                      </Link>
                    </td>
                    <td>{date(award.awardDate)}</td>
                    <td>{award.awardType || "—"}</td>

                    {CORE_ROLES.map(({ key }) => {
                      const count = getRoleCount(key);
                      return (
                        <td key={key}>
                          {count > 0 ? (
                            <span className="role-badge-available">
                              Available ({count})
                            </span>
                          ) : (
                            <button
                              className="btn-add-core-file"
                              onClick={() => openUploadModal(award.id, award.awardNumber, key)}
                              title={`Upload ${key} file`}
                            >
                              <IconPlus size={12} />
                              <span>Add</span>
                            </button>
                          )}
                        </td>
                      );
                    })}

                    <td>
                      <Link to={`/awards/${award.id}`} className="text-action text-xs font-semibold">
                        Open Award
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Add Award Modal */}
      {addAwardModalOpen && (
        <div className="lac-modal-backdrop" onClick={() => setAddAwardModalOpen(false)}>
          <div className="lac-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="lac-modal-header">
              <h3>Add New Award</h3>
              <button className="lac-modal-close" onClick={() => setAddAwardModalOpen(false)}>
                &times;
              </button>
            </div>

            <div className="field-grid">
              <label>
                Award Number *
                <input
                  value={newAward.awardNumber}
                  onChange={(e) => setNewAward({ ...newAward, awardNumber: e.target.value })}
                  placeholder="e.g. 63/86-87"
                  autoFocus
                />
              </label>

              <label>
                Award Date
                <input
                  type="date"
                  value={newAward.awardDate}
                  onChange={(e) => setNewAward({ ...newAward, awardDate: e.target.value })}
                />
              </label>

              <label className="span-two">
                Award Type
                <input
                  value={newAward.awardType}
                  onChange={(e) => setNewAward({ ...newAward, awardType: e.target.value })}
                  placeholder="e.g. Regular Acquisition"
                />
              </label>
            </div>

            <div className="lac-modal-footer">
              <button className="secondary-button" onClick={() => setAddAwardModalOpen(false)}>
                Cancel
              </button>
              <button
                disabled={!newAward.awardNumber.trim() || busy}
                onClick={() => void handleCreateAward()}
              >
                {busy ? "Saving…" : "Save Award"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Upload Core Document Modal */}
      {uploadModalOpen && uploadTarget && (
        <div className="lac-modal-backdrop" onClick={() => setUploadModalOpen(false)}>
          <div className="lac-modal-card" onClick={(e) => e.stopPropagation()}>
            <div className="lac-modal-header">
              <h3>Upload Core Document</h3>
              <button className="lac-modal-close" onClick={() => setUploadModalOpen(false)}>
                &times;
              </button>
            </div>

            <div className="flex flex-col gap-3">
              <div className="text-xs text-slate-600 bg-slate-50 p-3 rounded border border-slate-200">
                <strong>Target Award:</strong> {uploadTarget.awardNumber} <br />
                <strong>Document Role:</strong> {uploadTarget.role}
              </div>

              <label className="text-xs font-semibold text-slate-700 flex flex-col gap-2">
                Select PDF File *
                <input
                  type="file"
                  accept="application/pdf,.pdf"
                  onChange={(e) => setSelectedFile(e.target.files?.[0] || null)}
                />
              </label>
            </div>

            <div className="lac-modal-footer">
              <button className="secondary-button" onClick={() => setUploadModalOpen(false)}>
                Cancel
              </button>
              <button
                disabled={!selectedFile || busy}
                onClick={() => void handleUploadDocument()}
              >
                {busy ? "Uploading…" : "Upload Document"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
