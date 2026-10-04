import React, { useMemo } from "react";
import { Link } from "react-router-dom";
import { formatTitleCase } from "./VillagesDirectory";
import "./land.css";

interface VillageOverviewProps {
  id: string;
}

interface AwardItem {
  id: string;
  awardNumber: string;
  awardDate: string | null;
  awardType: string | null;
  status?: string;
  khasraCount: number;
  documentCount: number;
}

interface NotificationItem {
  id: string;
  notificationNumber: string;
  sectionType: string;
  notificationDate: string | null;
}

interface PendingReviewCandidateCount {
  candidateType: string;
  count: number;
}

interface PendingReviewSession {
  sessionId: string;
  awardId: string;
  awardNumber: string;
  sourceDocumentName: string;
  status: string;
  pendingCandidateCount: number;
  candidateCounts: PendingReviewCandidateCount[];
}

interface VillageOverviewData {
  village: {
    id: string;
    name: string;
    subDivision: {
      id: string;
      name: string;
      district: {
        id: string;
        name: string;
      };
    };
    totalKhasras: number;
    linkedAwards: number;
    documentCount: number;
    lrAvailable: boolean;
  };
  official: {
    khasraCount: number;
    awardCount: number;
    notificationCount: number;
    possessionEventCount: number;
    courtCaseCount: number;
    valuationRuleCount: number;
    compensationRuleCount: number;
    claimCount: number;
  };
  awards: AwardItem[];
  notifications: NotificationItem[];
  pendingReview: PendingReviewSession[];
}

// Format ISO date strings into readable officer format: "09 Dec 2002"
function formatDate(iso?: string | null): string {
  if (!iso) return "—";
  try {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return iso;
    return d.toLocaleDateString("en-IN", {
      day: "2-digit",
      month: "short",
      year: "numeric",
    });
  } catch {
    return iso;
  }
}

// Friendly name mapping for pending review candidate types
function formatCandidateType(type: string): string {
  switch (type) {
    case "AwardKhasra":
      return "Khasras";
    case "CourtCase":
      return "Court Cases";
    case "AwardValuationRule":
      return "Valuation Rules";
    case "AwardCompensationRule":
      return "Compensation Rules";
    case "Notification":
      return "Notifications";
    case "PossessionEvent":
      return "Possession Events";
    case "AwardSupplementaryMatter":
      return "Supplementary Matters";
    case "AwardLandClass":
      return "Land Classes";
    case "AwardCore":
      return "Award Core Facts";
    case "AwardVillage":
      return "Village Links";
    case "UnmappedAwardFinding":
      return "General Findings";
    default:
      return type.replace(/([A-Z])/g, " $1").trim();
  }
}

export const VillageOverviewWorkspace: React.FC<VillageOverviewProps> = ({ id }) => {
  const [data, setData] = React.useState<VillageOverviewData | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let active = true;
    setLoading(true);
    fetch(`/api/villages/${id}/overview`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) {
          throw new Error(`Failed to load village overview (${res.status})`);
        }
        return res.json();
      })
      .then((resData) => {
        if (active) {
          setData(resData);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err.message || "Unable to load village overview.");
          setLoading(false);
        }
      });
    return () => {
      active = false;
    };
  }, [id]);

  // Aggregate total award-linked khasras
  const totalAwardKhasras = useMemo(() => {
    if (!data?.awards) return 0;
    return data.awards.reduce((sum, a) => sum + (a.khasraCount || 0), 0);
  }, [data]);

  // Build Chronological Acquisition Timeline from real, authoritative records
  const timelineEvents = useMemo(() => {
    if (!data) return [];
    const events: Array<{
      dateIso: string;
      dateFormatted: string;
      category: "Notification" | "Award";
      categoryBadge: string;
      badgeTone: string;
      title: string;
      subtitle: string;
      awardId?: string;
    }> = [];

    // Add dated Notifications
    if (data.notifications) {
      data.notifications.forEach((notif) => {
        if (notif.notificationDate) {
          const sec = notif.sectionType || "Statutory";
          let tone = "blue";
          if (sec.includes("4")) tone = "green";
          else if (sec.includes("6")) tone = "blue";
          else if (sec.includes("17")) tone = "amber";

          events.push({
            dateIso: notif.notificationDate,
            dateFormatted: formatDate(notif.notificationDate),
            category: "Notification",
            categoryBadge: sec,
            badgeTone: tone,
            title: `${sec} Notification Issued`,
            subtitle: `Gazette Reference: ${notif.notificationNumber}`,
          });
        }
      });
    }

    // Add dated Awards
    if (data.awards) {
      data.awards.forEach((award) => {
        if (award.awardDate) {
          events.push({
            dateIso: award.awardDate,
            dateFormatted: formatDate(award.awardDate),
            category: "Award",
            categoryBadge: "Award Pronounced",
            badgeTone: "purple",
            title: `Award ${award.awardNumber}`,
            subtitle: `${award.khasraCount || 0} Khasras registered${
              award.documentCount ? " · Source document loaded" : " · Pending document upload"
            }`,
            awardId: award.id,
          });
        }
      });
    }

    // Sort chronologically (earliest milestone to latest milestone)
    return events.sort((a, b) => (a.dateIso > b.dateIso ? 1 : -1));
  }, [data]);

  // Group Pending Review sessions by sourceDocumentName to distinguish multiple extraction batches
  const groupedPendingSessions = useMemo(() => {
    if (!data?.pendingReview) return [];
    return data.pendingReview.map((session, index) => ({
      ...session,
      batchLabel: `Extraction Run #${index + 1} (${session.sessionId.slice(0, 8)})`,
    }));
  }, [data]);

  if (loading) {
    return (
      <div className="land-loading-container" style={{ padding: "40px", textAlign: "center" }}>
        <div className="land-spinner" />
        <p style={{ marginTop: "12px", color: "#64748b", fontSize: "14px" }}>
          Loading village acquisition overview…
        </p>
      </div>
    );
  }

  if (error || !data) {
    return (
      <div className="land-error-banner" style={{ margin: "20px 0" }}>
        <span>⚠️</span>
        <div>{error || "Village acquisition overview is unavailable."}</div>
      </div>
    );
  }

  const { official, awards } = data;

  return (
    <div className="village-overview-container" style={{ display: "flex", flexDirection: "column", gap: "20px", marginTop: "12px" }}>
      {/* 1. Acquisition Record Summary Strip */}
      <section className="village-summary-card">
        <div className="village-summary-header">
          <div>
            <h2 className="village-summary-title">Acquisition Record Summary</h2>
            <p className="village-summary-subtitle">
              Authoritative statutory records and official parcel allocations registered for {formatTitleCase(data.village.name)}.
            </p>
          </div>
          <span className="village-status-pill canonical-pill">
            Canonical Baseline
          </span>
        </div>

        <div className="village-metrics-row">
          <div className="village-metric-cell">
            <span className="village-metric-value">{official.awardCount || awards.length}</span>
            <span className="village-metric-label">Awards</span>
          </div>

          <div className="village-metric-cell">
            <span className="village-metric-value">{official.notificationCount || data.notifications.length}</span>
            <span className="village-metric-label">Notifications</span>
          </div>

          <div className="village-metric-cell">
            <span className="village-metric-value">{totalAwardKhasras}</span>
            <span className="village-metric-label">Award-Linked Khasras</span>
          </div>

          {/* Possession is displayed ONLY when authoritative count > 0 */}
          {official.possessionEventCount > 0 && (
            <div className="village-metric-cell">
              <span className="village-metric-value">{official.possessionEventCount}</span>
              <span className="village-metric-label">Possession Events</span>
            </div>
          )}

          {/* Note: Court cases is deliberately omitted when 0 or unauthoritative */}
        </div>
      </section>

      {/* 2. Canonical Awards Register */}
      <section className="village-card">
        <div className="village-card-header">
          <div>
            <h3 className="village-card-title">Canonical Awards ({awards.length})</h3>
            <p className="village-card-subtitle">
              Statutory acquisition awards linked to this village. Select an Award number to open its dedicated workspace.
            </p>
          </div>
        </div>

        {awards.length === 0 ? (
          <div className="village-empty-state">
            <p className="village-empty-title">No official Awards linked yet</p>
            <p className="village-empty-desc">
              No statutory acquisition award is currently associated with this village record.
            </p>
          </div>
        ) : (
          <div className="village-table-wrapper">
            <table className="village-table">
              <thead>
                <tr>
                  <th>Award No.</th>
                  <th>Award Date</th>
                  <th>Type</th>
                  <th>Official Khasras</th>
                  <th>Core Document Status</th>
                  <th style={{ textAlign: "right" }}>Workspace</th>
                </tr>
              </thead>
              <tbody>
                {awards.map((award) => (
                  <tr key={award.id}>
                    <td>
                      <Link to={`/awards/${award.id}`} className="award-link-bold">
                        {award.awardNumber}
                      </Link>
                    </td>
                    <td>{formatDate(award.awardDate)}</td>
                    <td>
                      <span className="village-type-tag">
                        {award.awardType || "Acquisition"}
                      </span>
                    </td>
                    <td>
                      <strong>{award.khasraCount}</strong>{" "}
                      <span style={{ color: "#64748b", fontSize: "12px" }}>
                        {award.khasraCount === 1 ? "parcel" : "parcels"}
                      </span>
                    </td>
                    <td>
                      {award.documentCount > 0 ? (
                        <span className="land-doc-status loaded">
                          ● Source PDF Available
                        </span>
                      ) : (
                        <span className="land-doc-status pending">
                          ○ Source PDF Not Uploaded
                        </span>
                      )}
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <Link
                        to={`/awards/${award.id}`}
                        className="village-action-link"
                      >
                        Open Award &rarr;
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* 3. Acquisition Timeline — PAUSED: backend attribution correction pending.
              Do not infer notification→Award relation in frontend.
              Code preserved below; suppress render until backend is authoritative. */}
      {false && (
      <section className="village-card">
        <div className="village-card-header">
          <div>
            <h3 className="village-card-title">Acquisition Timeline</h3>
            <p className="village-card-subtitle">
              Chronological milestones constructed exclusively from authoritative dated notifications and awards.
            </p>
          </div>
        </div>

        {timelineEvents.length === 0 ? (
          <div className="village-empty-state">
            <p className="village-empty-title">No dated statutory milestones</p>
            <p className="village-empty-desc">
              Dated acquisition notifications and award pronouncements will appear here chronologically.
            </p>
          </div>
        ) : (
          <div className="village-timeline">
            {timelineEvents.map((event, idx) => (
              <div key={idx} className="village-timeline-item">
                <div className="village-timeline-date-col">
                  <span className="village-timeline-date">{event.dateFormatted}</span>
                </div>
                <div className="village-timeline-marker-col">
                  <div className={`village-timeline-dot ${event.badgeTone}`} />
                  {idx < timelineEvents.length - 1 && <div className="village-timeline-line" />}
                </div>
                <div className="village-timeline-content-col">
                  <div className="village-timeline-card">
                    <div className="village-timeline-card-header">
                      <span className={`village-timeline-badge ${event.badgeTone}`}>
                        {event.categoryBadge}
                      </span>
                      {event.awardId && (
                        <Link to={`/awards/${event.awardId}`} className="village-timeline-link">
                          Open Award &rarr;
                        </Link>
                      )}
                    </div>
                    <div className="village-timeline-card-title">{event.title}</div>
                    <div className="village-timeline-card-sub">{event.subtitle}</div>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>
      )}

      {/* 4. Compact Pending Review Section */}
      {groupedPendingSessions.length > 0 && (
        <section className="village-card pending-review-section">
          <div className="village-card-header">
            <div>
              <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                <h3 className="village-card-title">Pending Document Review</h3>
                <span className="village-review-pill">
                  {groupedPendingSessions.length} {groupedPendingSessions.length === 1 ? "Session" : "Sessions"}
                </span>
              </div>
              <p className="village-card-subtitle">
                Automated document extraction sessions awaiting officer review. Distinct runs from the same PDF are separated.
              </p>
            </div>
          </div>

          <div className="village-pending-grid">
            {groupedPendingSessions.map((session) => (
              <div key={session.sessionId} className="village-pending-card">
                <div className="village-pending-card-header">
                  <div style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                    <div className="village-pending-doc-name">
                      📄 {session.sourceDocumentName}
                    </div>
                    <div className="village-pending-award-ctx">
                      Award {session.awardNumber} · <span className="village-batch-tag">{session.batchLabel}</span>
                    </div>
                  </div>
                  {session.awardId ? (
                    <Link
                      to={`/awards/${session.awardId}/ingestion/${session.sessionId}`}
                      className="village-review-cta"
                    >
                      Open Review &rarr;
                    </Link>
                  ) : (
                    <span className="village-review-dim">No route</span>
                  )}
                </div>

                <div className="village-pending-findings-count">
                  <strong>{session.pendingCandidateCount}</strong> findings waiting for verification
                </div>

                <div className="village-candidate-tags">
                  {session.candidateCounts.map((count) => (
                    <span key={count.candidateType} className="village-candidate-tag">
                      {count.count} {formatCandidateType(count.candidateType)}
                    </span>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  );
};
