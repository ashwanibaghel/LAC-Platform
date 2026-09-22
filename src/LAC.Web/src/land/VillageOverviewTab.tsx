import React, { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { IconChevronRight } from "../components/Icons";

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

export const VillageOverviewTab: React.FC<{ villageId?: string; id?: string }> = ({ villageId, id }) => {
  const targetId = villageId || id || "";
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<any>(null);

  useEffect(() => {
    if (!targetId) return;
    let active = true;
    setLoading(true);
    fetch(`${api}/villages/${targetId}/overview`, { credentials: "include" })
      .then(async (r) => {
        if (!r.ok) throw new Error("Could not load village overview.");
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
  }, [targetId]);

  if (loading) return <div className="state loading">Loading village overview…</div>;
  if (error || !data) return <div className="state error"><strong>Unable to load overview.</strong><span>{error || "Overview unavailable."}</span></div>;

  const official = data.official || {};

  return (
    <div className="village-overview-tab flex flex-col gap-6">
      {/* 1. Official / Committed Data */}
      <section className="section trust-section official-section">
        <div className="section-heading">
          <div>
            <h2>Official / Committed Data</h2>
            <span>Canonical records. Pending document findings are separate.</span>
          </div>
          <span className="status success">Official</span>
        </div>

        <div className="summary-strip compact-summary">
          <div className="metric">
            <strong>{official.awardCount || 0}</strong>
            <span>Awards</span>
          </div>
          <div className="metric">
            <strong>{official.notificationCount || 0}</strong>
            <span>Notifications</span>
          </div>
          <div className="metric">
            <strong>
              {data.awards?.reduce((sum: number, award: any) => sum + (award.khasraCount || 0), 0) || 0}
            </strong>
            <span>Award Khasras</span>
          </div>
          <div className="metric">
            <strong>{official.possessionEventCount || 0}</strong>
            <span>Possession</span>
          </div>
          <div className="metric">
            <strong>{official.courtCaseCount || 0}</strong>
            <span>Court Cases</span>
          </div>
        </div>

        {data.awards?.length ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Award Number</th>
                  <th>Award Date</th>
                  <th>Award Type</th>
                  <th>Official Khasras</th>
                  <th>Source PDF</th>
                </tr>
              </thead>
              <tbody>
                {data.awards.map((award: any) => (
                  <tr key={award.id}>
                    <td>
                      <Link to={`/awards/${award.id}`} className="entity-link" style={{ fontWeight: 700 }}>
                        {award.awardNumber}
                      </Link>
                    </td>
                    <td>{date(award.awardDate)}</td>
                    <td>{award.awardType || "—"}</td>
                    <td>{award.khasraCount}</td>
                    <td>
                      <span className={`status ${award.documentCount ? "success" : ""}`}>
                        {award.documentCount ? "Source PDF loaded" : "Source PDF not loaded"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="state empty">
            <strong>No official Award links</strong>
            <span>No canonical Award is currently linked to this village.</span>
          </div>
        )}
      </section>

      {/* 2. Pending Review Findings */}
      {data.pendingReview?.length > 0 && (
        <section className="section trust-section pending-section">
          <div className="section-heading">
            <div>
              <h2>Pending Review Findings</h2>
              <span>Document-review counts only — not committed village facts.</span>
            </div>
            <span className="status warning">Review Required</span>
          </div>

          <div className="pending-review-list">
            {data.pendingReview.map((item: any) => (
              <div className="pending-review-card" key={item.sessionId}>
                <div>
                  <strong>{item.sourceDocumentName}</strong>
                  <span>
                    {item.awardNumber ? `Award ${item.awardNumber}` : "Award context pending"} &middot;{" "}
                    {item.pendingCandidateCount} findings waiting for review
                  </span>
                  <small>
                    {item.candidateCounts?.map((count: any) => `${count.count} ${reviewSectionName(count.candidateType)}`).join(" · ")}
                  </small>
                </div>

                {item.awardId ? (
                  <Link
                    to={`/awards/${item.awardId}/ingestion/${item.sessionId}`}
                    className="secondary-button"
                    style={{ display: "inline-flex", alignItems: "center", gap: "4px" }}
                  >
                    <span>Open Review</span>
                    <IconChevronRight size={14} />
                  </Link>
                ) : (
                  <span className="hint">No Award review route</span>
                )}
              </div>
            ))}
          </div>
        </section>
      )}

      {/* 3. Source Coverage */}
      <section className="section trust-section source-section">
        <div className="section-heading">
          <div>
            <h2>Record & Source Coverage</h2>
            <span>A missing source means no conclusion has been drawn for that module.</span>
          </div>
        </div>

        <div className="source-status-grid">
          {data.sources?.map((source: any) => (
            <div key={source.sourceType}>
              <strong>{source.sourceType}</strong>
              <span className={`status ${source.status === "Loaded" ? "success" : "warning"}`}>
                {source.status}
              </span>
              <span>{source.detail}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
};
