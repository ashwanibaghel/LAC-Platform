import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
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

function reviewSectionName(candidateType: string) {
  switch (candidateType) {
    case "Khasra":
      return "Khasra findings";
    case "RecordedPerson":
      return "Recorded person findings";
    case "PossessionEvent":
      return "Possession findings";
    case "CourtCase":
      return "Court case findings";
    default:
      return `${candidateType} findings`;
  }
}

export interface VillageOverviewTabProps {
  villageId: string;
}

export const VillageOverviewTab: React.FC<VillageOverviewTabProps> = ({ villageId }) => {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<any>(null);

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
        return r.json();
      })
      .then((d) => {
        if (active) {
          setData(d);
          setLoading(false);
        }
      })
      .catch((e) => {
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

  const official = data.official || {};
  const awards: any[] = data.awards || [];
  const pending: any[] = data.pendingReview || [];
  const sources: any[] = data.sources || [];

  return (
    <div className="village-overview-tab">
      {/* Official Committed Facts Section */}
      <section className="section" style={{ marginBottom: "28px" }}>
        <div className="section-heading">
          <div>
            <h2>Official Committed Facts</h2>
            <span>Verified, committed facts for {data.village?.name || "this village"}.</span>
          </div>
        </div>

        <div className="summary-strip" style={{ marginBottom: "20px" }}>
          <div className="metric">
            <strong>{official.khasraCount ?? 0}</strong>
            <span>Official Khasras</span>
          </div>
          <div className="metric">
            <strong>
              {official.totalVerifiedBigha ?? 0} B {official.totalVerifiedBiswa ?? 0} Bis
            </strong>
            <span>Verified Area</span>
          </div>
          <div className="metric">
            <strong>{official.totalAwardsCount ?? 0}</strong>
            <span>Total Awards</span>
          </div>
          <div className="metric">
            <strong>{official.totalRecordedOwnersCount ?? 0}</strong>
            <span>Recorded Owners</span>
          </div>
        </div>

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
                      <Link to={`/awards/${a.id}`} className="entity-link" style={{ fontWeight: 650 }}>
                        Award #{a.awardNumber}
                      </Link>
                    </td>
                    <td>{date(a.awardDate)}</td>
                    <td>{a.khasraCount ?? 0} khasras</td>
                    <td>
                      <Link
                        to={`/awards/${a.id}`}
                        className="text-action"
                        style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                      >
                        <span>View Award</span>
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

      {/* Pending Review Findings Section */}
      <section className="section" style={{ marginBottom: "28px" }}>
        <div className="section-heading">
          <div>
            <h2>Pending Review Queue</h2>
            <span>Source-extracted findings pending human review and verification.</span>
          </div>
          <span>{pending.length} queues</span>
        </div>

        {pending.length === 0 ? (
          <div className="state empty">
            <strong>No pending review items</strong>
            <span>All source findings for this village are verified or up to date.</span>
          </div>
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">Finding Stream</th>
                  <th scope="col">Pending Count</th>
                  <th scope="col">Action</th>
                </tr>
              </thead>
              <tbody>
                {pending.map((p: any) => (
                  <tr key={p.candidateType}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{reviewSectionName(p.candidateType)}</span>
                    </td>
                    <td>
                      <span style={{ fontWeight: 600, color: "#d97706" }}>{p.count}</span> items
                    </td>
                    <td>
                      {p.targetUrl ? (
                        <Link
                          to={p.targetUrl}
                          className="text-action"
                          style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                        >
                          <span>Review Queue</span>
                          <IconChevronRight size={14} />
                        </Link>
                      ) : (
                        <span style={{ fontSize: "13px", color: "#64748b" }}>Queue not available</span>
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
            <span>Document availability and digitization status across core record categories.</span>
          </div>
        </div>

        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Category</th>
                <th scope="col">Status</th>
                <th scope="col">Document Count</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {sources.length === 0 ? (
                <tr>
                  <td colSpan={4} className="text-center-muted" style={{ padding: "16px", textAlign: "center", color: "#64748b" }}>
                    No source coverage status recorded.
                  </td>
                </tr>
              ) : (
                sources.map((s: any) => (
                  <tr key={s.category}>
                    <td>
                      <span style={{ fontWeight: 650 }}>{s.category}</span>
                    </td>
                    <td>
                      <span className={`status-badge status-${s.hasDocuments ? "committed" : "draft"}`}>
                        {s.hasDocuments ? "Available" : "Missing / Pending"}
                      </span>
                    </td>
                    <td>{s.documentCount ?? 0} docs</td>
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
                ))
              )}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
};
