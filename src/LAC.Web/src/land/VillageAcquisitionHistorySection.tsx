import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";

// ─── API Types ────────────────────────────────────────────────────────────────

export interface AcquisitionHistoryEvent {
  eventId: string;
  type: "Award" | "Notification" | "Possession" | string;
  date: string | null;
  section: string | null;
  notificationId: string | null;
  possessionEventId: string | null;
  reference: string | null;
  eventType: string | null;
  status: string | null;
  relationshipBasis: string;
  isShared: boolean;
  linkedAwardIds: string[];
}

export interface AwardAcquisitionHistory {
  awardId: string;
  awardNumber: string;
  awardDate: string | null;
  events: AcquisitionHistoryEvent[];
}

export interface VillageAcquisitionHistory {
  villageId: string;
  awards: AwardAcquisitionHistory[];
  unassignedEvents: AcquisitionHistoryEvent[];
  possessionEventsVisible: boolean;
}

interface VillageAcquisitionHistorySectionProps {
  villageId: string;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

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

function normalizeSectionLabel(section?: string | null): string | null {
  if (!section) return null;
  const trimmed = section.trim();
  if (!trimmed) return null;
  if (/^section\b/i.test(trimmed)) {
    return trimmed;
  }
  if (/^sec\.?\b/i.test(trimmed)) {
    return trimmed.replace(/^sec\.?\s*/i, "Section ");
  }
  return `Section ${trimmed}`;
}

function extractSectionNumber(section?: string | null): string {
  if (!section) return "";
  return section.trim().replace(/^sec(?:tion|\.)?\s*/i, "").trim();
}

function renderEventTitle(ev: AcquisitionHistoryEvent): string {
  if (ev.type === "Notification") {
    const secLabel = normalizeSectionLabel(ev.section);
    return secLabel ? `${secLabel} Notification` : "Statutory Notification";
  }
  if (ev.type === "Award") {
    return "Award pronounced";
  }
  if (ev.type === "Possession") {
    return `Possession proceeding${ev.eventType ? ` (${ev.eventType})` : ""}`;
  }
  return ev.type;
}

function getEventTone(ev: AcquisitionHistoryEvent): string {
  if (ev.type === "Award") return "purple";
  if (ev.type === "Possession") return "amber";
  if (ev.type === "Notification") {
    const sec = extractSectionNumber(ev.section);
    if (/^4(\(|$|\s)/.test(sec)) return "green";
    if (/^6(\(|$|\s)/.test(sec)) return "blue";
    if (/^17(\(|$|\s)/.test(sec)) return "amber";
    return "blue";
  }
  return "blue";
}

// ─── Component ────────────────────────────────────────────────────────────────

export const VillageAcquisitionHistorySection: React.FC<VillageAcquisitionHistorySectionProps> = ({
  villageId,
}) => {
  const [history, setHistory] = useState<VillageAcquisitionHistory | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<Record<string, boolean>>({});

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError(null);
    fetch(`/api/villages/${villageId}/acquisition-history`, {
      credentials: "include",
    })
      .then((res) => {
        if (!res.ok) throw new Error(`Failed to load acquisition history (${res.status})`);
        return res.json();
      })
      .then((data: VillageAcquisitionHistory) => {
        if (active) {
          setHistory(data);
          setLoading(false);

          // Default: most relevant / most recent Award expanded by date, others collapsed
          if (data.awards && data.awards.length > 0) {
            const sortedByDateDesc = [...data.awards].sort((a, b) => {
              if (!a.awardDate) return 1;
              if (!b.awardDate) return -1;
              return b.awardDate.localeCompare(a.awardDate);
            });
            const defaultId = sortedByDateDesc[0]?.awardId;
            if (defaultId) {
              setExpanded({ [defaultId]: true });
            }
          }
        }
      })
      .catch((err: any) => {
        if (active) {
          setError(err.message || "Failed to load acquisition history.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [villageId]);

  const toggleAward = (awardId: string) => {
    setExpanded((prev) => ({
      ...prev,
      [awardId]: !prev[awardId],
    }));
  };

  if (loading) {
    return (
      <section className="village-card">
        <div className="village-card-header">
          <div>
            <h3 className="village-card-title">Acquisition History</h3>
            <p className="village-card-subtitle">
              Authoritative statutory milestones grouped strictly by explicit Award relationships.
            </p>
          </div>
        </div>
        <div style={{ padding: "24px", textAlign: "center" }}>
          <div className="land-spinner" style={{ margin: "0 auto" }} />
          <p style={{ marginTop: "10px", color: "#64748b", fontSize: "13px" }}>
            Loading acquisition milestones…
          </p>
        </div>
      </section>
    );
  }

  if (error || !history) {
    return (
      <section className="village-card">
        <div className="village-card-header">
          <div>
            <h3 className="village-card-title">Acquisition History</h3>
            <p className="village-card-subtitle">
              Authoritative statutory milestones grouped strictly by explicit Award relationships.
            </p>
          </div>
        </div>
        <div className="land-error-banner" style={{ margin: "14px" }}>
          <span>⚠️</span>
          <div>{error || "Acquisition history is currently unavailable."}</div>
        </div>
      </section>
    );
  }

  const { awards, unassignedEvents, possessionEventsVisible } = history;

  return (
    <section className="village-card">
      <div className="village-card-header">
        <div>
          <h3 className="village-card-title">Acquisition History</h3>
          <p className="village-card-subtitle">
            Authoritative statutory milestones grouped strictly by explicit Award relationships.
          </p>
        </div>
        {!possessionEventsVisible && (
          <div className="village-history-scope-notice">
            <span>ℹ️ Possession events are not available in your current access scope.</span>
          </div>
        )}
      </div>

      {awards.length === 0 ? (
        <div className="village-empty-state">
          <p className="village-empty-title">
            {unassignedEvents && unassignedEvents.length > 0
              ? "No Awards Linked Yet"
              : "No Award Acquisition Records"}
          </p>
          <p className="village-empty-desc">
            {unassignedEvents && unassignedEvents.length > 0
              ? "No Awards linked yet. Unassigned statutory notifications are shown below."
              : "No canonical acquisition awards or statutory notifications have been registered for this village yet."}
          </p>
        </div>
      ) : (
        <div className="village-history-award-list">
          {awards.map((award) => {
            const isExpanded = !!expanded[award.awardId];
            const milestoneCount = award.events.length;

            return (
              <div
                key={award.awardId}
                className={`village-history-award-group ${isExpanded ? "expanded" : "collapsed"}`}
              >
                {/* Expandable Award Header Bar */}
                <button
                  type="button"
                  className="village-history-award-header"
                  onClick={() => toggleAward(award.awardId)}
                  aria-expanded={isExpanded}
                >
                  <div className="village-history-award-header-left">
                    <span className="village-history-toggle-icon">
                      {isExpanded ? "▼" : "▶"}
                    </span>
                    <span className="village-history-award-title">
                      Award {award.awardNumber}
                    </span>
                    {award.awardDate && (
                      <span className="village-history-award-date">
                        {formatDate(award.awardDate)}
                      </span>
                    )}
                  </div>
                  <div className="village-history-award-header-right">
                    <span className="village-history-milestone-pill">
                      {milestoneCount} {milestoneCount === 1 ? "milestone" : "milestones"}
                    </span>
                  </div>
                </button>

                {/* Expanded Milestones Content */}
                {isExpanded && (
                  <div className="village-history-award-body">
                    <div className="village-history-milestones-container">
                      {award.events.map((ev, idx) => {
                        const tone = getEventTone(ev);
                        const isLast = idx === award.events.length - 1;

                        return (
                          <div
                            key={`${ev.type}-${ev.eventId}-${idx}`}
                            className="village-history-milestone-row"
                          >
                            <div className="village-history-milestone-date">
                              {formatDate(ev.date)}
                            </div>
                            <div className="village-history-milestone-track">
                              <span className={`village-history-track-dot ${tone}`} />
                              {!isLast && <span className="village-history-track-line" />}
                            </div>
                            <div className="village-history-milestone-detail">
                              <div className="village-history-milestone-title-row">
                                <span className="village-history-milestone-title">
                                  {renderEventTitle(ev)}
                                </span>
                                {ev.isShared && (
                                  <span className="village-history-shared-badge">
                                    Shared notification
                                  </span>
                                )}
                              </div>
                              {ev.reference && ev.type === "Notification" && (
                                <div className="village-history-milestone-ref">
                                  Ref: {ev.reference}
                                </div>
                              )}
                              {ev.status && ev.type === "Possession" && (
                                <div className="village-history-milestone-ref">
                                  Status: {ev.status}
                                </div>
                              )}
                            </div>
                          </div>
                        );
                      })}
                    </div>

                    <div className="village-history-award-footer">
                      <Link
                        to={`/awards/${award.awardId}`}
                        className="village-history-open-award-btn"
                      >
                        Open Award &rarr;
                      </Link>
                    </div>
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Unassigned Notifications (Not yet linked to an Award) */}
      {unassignedEvents && unassignedEvents.length > 0 && (
        <div className="village-history-unassigned-section">
          <div className="village-history-unassigned-header">
            <div>
              <h4 className="village-history-unassigned-title">
                Not yet linked to an Award
              </h4>
              <p className="village-history-unassigned-subtitle">
                Authoritative notifications verified for village khasras but not yet linked to any canonical Award.
              </p>
            </div>
            <span className="village-history-unassigned-pill">
              {unassignedEvents.length} {unassignedEvents.length === 1 ? "record" : "records"}
            </span>
          </div>

          <div className="village-history-unassigned-list">
            {unassignedEvents.map((ev, idx) => (
              <div
                key={`${ev.eventId}-${idx}`}
                className="village-history-unassigned-item"
              >
                <div className="village-history-unassigned-item-left">
                  <span className="village-history-unassigned-item-name">
                    {renderEventTitle(ev)}
                  </span>
                  {ev.reference && (
                    <span className="village-history-unassigned-item-ref">
                      · {ev.reference}
                    </span>
                  )}
                  {ev.isShared && (
                    <span className="village-history-shared-badge">
                      Shared notification
                    </span>
                  )}
                </div>
                <div className="village-history-unassigned-item-date">
                  {formatDate(ev.date)}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
};
