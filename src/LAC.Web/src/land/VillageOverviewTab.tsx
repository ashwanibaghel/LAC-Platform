import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { IconChevronRight } from "../components/Icons";
import "./land.css";

const api = "/api";

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

export interface VillageOfficialSummary {
  khasraCount: number;
  awardCount: number;
  notificationCount: number;
  possessionEventCount: number;
  courtCaseCount: number;
  valuationRuleCount: number;
  compensationRuleCount: number;
  claimCount: number;
}

export interface PendingCandidateTypeCount {
  candidateType: string;
  count: number;
}

export interface VillagePendingReviewItem {
  sessionId: string;
  awardId?: string | null;
  awardNumber?: string | null;
  sourceDocumentName: string;
  status: string;
  pendingCandidateCount: number;
  candidateCounts: PendingCandidateTypeCount[];
}

export interface VillageSourceStatusItem {
  sourceType: string;
  status: string;
  detail: string;
}

export interface VillageOverviewResponse {
  village?: {
    id: string;
    name: string;
    subDivision?: {
      id: string;
      name: string;
    };
  };
  official?: VillageOfficialSummary;
  awards?: any[];
  notifications?: any[];
  pendingReview?: VillagePendingReviewItem[];
  sources?: VillageSourceStatusItem[];
}

export interface VillageOverviewTabProps {
  villageId: string;
}

export const VillageOverviewTab: React.FC<VillageOverviewTabProps> = ({ villageId }) => {
  const { hasPermission } = useAuth();
  const canViewAward = hasPermission("Award.View");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<VillageOverviewResponse | null>(null);

  useEffect(() => {
    if (!villageId) return;
    let active = true;
    setLoading(true);
    setError(null);

    fetch(`${api}/villages/${villageId}/overview`, { credentials: "include" })
      .then(async (r) => {
        if (!r.ok) {
          if (r.status === 403) throw new Error("Access denied: You do not have permission to view this village overview.");
          throw new Error("Could not load village overview.");
        }
        return r.json() as Promise<VillageOverviewResponse>;
      })
      .then((d) => {
        if (active) {
          setData(d);
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setError(e?.message || "Overview unavailable.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [villageId]);

  if (loading) return <div className="state loading">Loading village overview…</div>;
  if (error || !data)
    return (
      <div className="state error">
        <strong>Unable to load overview.</strong>
        <span>{error || "Overview unavailable."}</span>
      </div>
    );

  const official = data.official;
  const awards = data.awards || [];
  const pending = data.pendingReview || [];
  const sources = data.sources || [];

  return (
    <div className="village-overview-tab">
      {/* Official / Committed Data Section */}
      <section className="section" style={{ marginBottom: "28px" }}>
        <div className="section-heading">
          <div>
            <h2>Official / Committed Data</h2>
            <span>Canonical records and committed facts for {data.village?.name || "this village"}.</span>
          </div>
        </div>

        {official && (
          <div className="summary-strip" style={{ marginBottom: "20px" }}>
            <div className="metric">
              <strong>{official.khasraCount ?? 0}</strong>
              <span>Khasras</span>
            </div>
            <div className="metric">
              <strong>{official.awardCount ?? 0}</strong>
              <span>Awards</span>
            </div>
            <div className="metric">
              <strong>{official.notificationCount ?? 0}</strong>
              <span>Notifications</span>
            </div>
            <div className="metric">
              <strong>{official.possessionEventCount ?? 0}</strong>
              <span>Possession Events</span>
            </div>
            <div className="metric">
              <strong>{official.courtCaseCount ?? 0}</strong>
              <span>Court Cases</span>
            </div>
          </div>
        )}

        {awards.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Award Number</th>
                  <th scope="col">Award Date</th>
                  <th scope="col">Khasras Count</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {awards.map((a: any) => (
                  <tr key={a.id}>
                    <td>
                      {canViewAward ? (
                        <Link to={`/awards/${a.id}`} className="entity-link" style={{ fontWeight: 650 }}>
                          Award #{a.awardNumber}
                        </Link>
                      ) : (
                        <span style={{ fontWeight: 650 }}>Award #{a.awardNumber}</span>
                      )}
                    </td>
                    <td>{date(a.awardDate)}</td>
                    <td>{a.khasraCount ?? 0} khasras</td>
                    <td>
                      {canViewAward ? (
                        <Link
                          to={`/awards/${a.id}`}
                          className="text-action"
                          style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                        >
                          <span>View Award</span>
                          <IconChevronRight size={14} />
                        </Link>
                      ) : (
                        <span style={{ fontSize: "13px", color: "#64748b" }}>Read-only</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Pending Review Findings Section */}
      <section className="section" style={{ marginBottom: "28px" }}>
        <div className="section-heading">
          <div>
            <h2>Pending Review Queue</h2>
            <span>Source-extracted findings pending human review. Unresolved findings are not committed village facts.</span>
          </div>
          <span>{pending.length} session(s)</span>
        </div>

        {pending.length === 0 ? (
          <div className="state empty">
            <strong>No pending review items</strong>
            <span>No pending review sessions are currently recorded for this village.</span>
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Source Document</th>
                  <th scope="col">Award Ref</th>
                  <th scope="col">Pending Findings Breakdown</th>
                  <th scope="col">Status</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {pending.map((p) => (
                  <tr key={p.sessionId}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{p.sourceDocumentName}</span>
                    </td>
                    <td>{p.awardNumber ? `Award #${p.awardNumber}` : "Unlinked"}</td>
                    <td>
                      <div style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                        <span style={{ fontWeight: 600, color: "#d97706", fontSize: "13px" }}>
                          Total Unresolved: {p.pendingCandidateCount}
                        </span>
                        {p.candidateCounts?.length > 0 && (
                          <div style={{ fontSize: "12px", color: "#64748b" }}>
                            {p.candidateCounts.map((c) => `${c.candidateType}: ${c.count}`).join(" • ")}
                          </div>
                        )}
                      </div>
                    </td>
                    <td>
                      <span className="status-badge status-draft">{p.status}</span>
                    </td>
                    <td>
                      {p.awardId && canViewAward ? (
                        <Link
                          to={`/awards/${p.awardId}/ingestion/${p.sessionId}`}
                          className="text-action"
                          style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                        >
                          <span>Review Session</span>
                          <IconChevronRight size={14} />
                        </Link>
                      ) : (
                        <span style={{ fontSize: "13px", color: "#64748b" }}>
                          {p.awardId ? "View access restricted" : "Award link required"}
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* Source Document Coverage Section */}
      <section className="section">
        <div className="section-heading">
          <div>
            <h2>Source Coverage Grid</h2>
            <span>Digitization and ingestion status across source document categories.</span>
          </div>
        </div>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Source Category</th>
                <th scope="col">Status</th>
                <th scope="col">Coverage Detail</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {sources.length === 0 ? (
                <tr>
                  <td colSpan={4} style={{ padding: "16px", textAlign: "center", color: "#64748b" }}>
                    No source coverage status recorded.
                  </td>
                </tr>
              ) : (
                sources.map((s) => (
                  <tr key={s.sourceType}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{s.sourceType}</span>
                    </td>
                    <td>
                      <span
                        className={`status-badge status-${
                          s.status === "Loaded" ? "committed" : "draft"
                        }`}
                      >
                        {s.status}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: "13px", color: "#475569" }}>{s.detail}</span>
                    </td>
                    <td>
                      <Link
                        to={`/villages/${villageId}?tab=core-records`}
                        className="text-action"
                        style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      >
                        <span>Core Records</span>
                        <IconChevronRight size={14} />
                      </Link>
                    </td>
                  </tr>
                ))}
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
};
