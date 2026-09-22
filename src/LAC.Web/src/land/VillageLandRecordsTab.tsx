import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { IconChevronRight } from "../components/Icons";
import "./land.css";

const api = "/api";

interface VillageLrListItem {
  id: string;
  registerReference: string;
  entryCount: number;
}

interface LrProgress {
  totalRows: number;
  draft: number;
  needsReview: number;
  verified: number;
  committed: number;
}

interface KhatauniListItem {
  id: string;
  referenceNumber?: string;
  recordYearText?: string;
  asOfDate?: string;
  verificationStatus?: string;
  khataCount: number;
  recordedKhasraCount: number;
}

export interface VillageLandRecordsTabProps {
  villageId: string;
}

export const VillageLandRecordsTab: React.FC<VillageLandRecordsTabProps> = ({ villageId }) => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [registers, setRegisters] = useState<VillageLrListItem[]>([]);
  const [progress, setProgress] = useState<LrProgress | null>(null);
  const [khataunis, setKhataunis] = useState<KhatauniListItem[]>([]);

  const loadData = async () => {
    setLoading(true);
    setError(null);
    try {
      const [regRes, progRes, khataRes] = await Promise.all([
        fetch(`${api}/villages/${villageId}/lrs`, { credentials: "include" }),
        fetch(`${api}/villages/${villageId}/lr-progress`, { credentials: "include" }),
        fetch(`${api}/villages/${villageId}/khatauni`, { credentials: "include" }),
      ]);

      if (regRes.status === 403 || progRes.status === 403 || khataRes.status === 403) {
        throw new Error("Access denied: You do not have permission to view Land Records.");
      }

      if (!regRes.ok || !progRes.ok || !khataRes.ok) {
        throw new Error("Failed to load land records data.");
      }

      setRegisters(await regRes.json());
      setProgress(await progRes.json());
      setKhataunis(await khataRes.json());
    } catch (err: any) {
      setError(err?.message || "Failed to load land records data.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (!villageId) return;
    loadData();
  }, [villageId]);

  if (loading) {
    return <div className="state loading">Loading land records & progress…</div>;
  }

  if (error) {
    return (
      <div className="state error">
        <strong>Error loading land records.</strong>
        <span>{error}</span>
      </div>
    );
  }

  const verifiedCommitted = (progress?.verified ?? 0) + (progress?.committed ?? 0);
  const totalRows = progress?.totalRows ?? 0;
  const progressPct = totalRows > 0 ? Math.min(100, Math.round((verifiedCommitted / totalRows) * 100)) : 0;

  return (
    <div className="land-records-tab-container">
      {/* Adaptive LR Verification Progress Banner */}
      <div className="lr-progress-card">
        <div className="lr-prog-header">
          <div>
            <h3 style={{ margin: 0, fontSize: "16px", fontWeight: 800 }}>LR Register Verification Progress</h3>
            <span style={{ fontSize: "12.5px", color: "#64748b" }}>
              {totalRows === 0
                ? "0 entries · Not started"
                : `${verifiedCommitted} verified / ${totalRows} total entries (${progressPct}% complete)`}
            </span>
          </div>
          {totalRows > 0 && (
            <span className="lr-prog-percent">{progressPct}%</span>
          )}
        </div>

        {totalRows > 0 && (
          <div className="lr-prog-track">
            <div className="lr-prog-fill" style={{ width: `${progressPct}%` }} />
          </div>
        )}

        {progress && totalRows > 0 && (
          <div className="lr-metrics-row">
            <div className="lr-m-item">
              <span className="lr-m-num">{progress.totalRows}</span>
              <span className="lr-m-lbl">Total Rows</span>
            </div>
            <div className="lr-m-item">
              <span className="lr-m-num" style={{ color: "#64748b" }}>{progress.draft}</span>
              <span className="lr-m-lbl">Draft</span>
            </div>
            <div className="lr-m-item">
              <span className="lr-m-num" style={{ color: "#d97706" }}>{progress.needsReview}</span>
              <span className="lr-m-lbl">Needs Review</span>
            </div>
            <div className="lr-m-item">
              <span className="lr-m-num" style={{ color: "#2563eb" }}>{progress.verified}</span>
              <span className="lr-m-lbl">Verified</span>
            </div>
            <div className="lr-m-item">
              <span className="lr-m-num" style={{ color: "#16a34a" }}>{progress.committed}</span>
              <span className="lr-m-lbl">Committed</span>
            </div>
          </div>
        )}
      </div>

      {/* LR Registers Section */}
      <section className="section" style={{ marginBottom: "32px" }}>
        <div className="section-heading">
          <div>
            <h2>LR Registers</h2>
            <span>Land record registers imported or created for this village.</span>
          </div>
          <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
            <span className="v-count-badge">{registers.length} register(s)</span>
            <Link to="/imports/lr" className="secondary-button" style={{ padding: "6px 12px", fontSize: "12.5px" }}>
              Import Workspace
            </Link>
          </div>
        </div>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Register Reference</th>
                <th scope="col">Entries Count</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {registers.length === 0 ? (
                <tr>
                  <td colSpan={3} style={{ padding: "20px", textAlign: "center", color: "#64748b" }}>
                    No LR registers found for this village.
                  </td>
                </tr>
              ) : (
                registers.map((reg) => (
                  <tr key={reg.id}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{reg.registerReference}</span>
                    </td>
                    <td>
                      <span>{reg.entryCount} entries</span>
                    </td>
                    <td>
                      <Link
                        to={`/villages/${villageId}/lr/${reg.id}`}
                        className="text-action"
                        style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      >
                        <span>Open Register</span>
                        <IconChevronRight size={14} />
                      </Link>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>

      {/* Khatauni Revenue Records Section */}
      <section className="section">
        <div className="section-heading">
          <div>
            <h2>Khatauni Revenue Records</h2>
            <span>Official Khatauni records and recorded owner holdings.</span>
          </div>
          <span className="v-count-badge">{khataunis.length} record(s)</span>
        </div>

        {khataunis.length === 0 ? (
          <div className="state empty" style={{ padding: "24px", textAlign: "center" }}>
            <strong>No Khatauni records loaded yet.</strong>
            <p style={{ margin: "4px 0 0", color: "#64748b", fontSize: "13px" }}>
              No official Khatauni revenue records registered for this village.
            </p>
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Reference Number</th>
                  <th scope="col">Record Year</th>
                  <th scope="col">As Of Date</th>
                  <th scope="col">Khatas Count</th>
                  <th scope="col">Recorded Khasras</th>
                  <th scope="col">Status</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {khataunis.map((kh) => (
                  <tr key={kh.id}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{kh.referenceNumber || "—"}</span>
                    </td>
                    <td>{kh.recordYearText || "—"}</td>
                    <td>{kh.asOfDate || "—"}</td>
                    <td>{kh.khataCount ?? 0} khatas</td>
                    <td>{kh.recordedKhasraCount ?? 0} khasras</td>
                    <td>
                      <span className={`status-badge status-${(kh.verificationStatus || "draft").toLowerCase()}`}>
                        {kh.verificationStatus || "Draft"}
                      </span>
                    </td>
                    <td>
                      <Link
                        to={`/khatauni/${kh.id}`}
                        className="text-action"
                        style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      >
                        <span>View Khatauni</span>
                        <IconChevronRight size={14} />
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
};
