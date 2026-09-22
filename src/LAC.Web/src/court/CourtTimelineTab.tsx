import React, { useState, useEffect, useCallback } from "react";
import type { CourtCaseDetailDto, CourtCaseEventDto } from "./types";

interface CourtTimelineTabProps {
  courtCase: CourtCaseDetailDto;
}

export const CourtTimelineTab: React.FC<CourtTimelineTabProps> = ({ courtCase }) => {
  const [events, setEvents] = useState<CourtCaseEventDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchTimeline = useCallback(async () => {
    try {
      setLoading(true);
      const res = await fetch(`/api/court-cases/${courtCase.id}/timeline`, { credentials: "include" });
      if (res.ok) {
        const data = await res.json();
        setEvents(data);
      } else {
        setError("Failed to load timeline events");
      }
    } catch {
      setError("Network error loading timeline");
    } finally {
      setLoading(false);
    }
  }, [courtCase.id]);

  useEffect(() => {
    void fetchTimeline();
  }, [fetchTimeline]);

  return (
    <div className="court-timeline-tab">
      <div className="court-section-header">
        <h3>Immutable Event History & Audit Ledger ({events.length})</h3>
      </div>

      {error && <div style={{ color: "#dc2626", marginBottom: "16px" }}>{error}</div>}

      {loading ? (
        <div style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>Loading timeline...</div>
      ) : events.length === 0 ? (
        <div style={{ background: "#fff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "40px", textAlign: "center", color: "#64748b" }}>
          No events recorded.
        </div>
      ) : (
        <div className="court-timeline">
          {events.map((evt) => (
            <div key={evt.id} className="court-timeline-item">
              <div className="court-timeline-header">
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span style={{ fontWeight: 700, color: "#1e293b" }}>#{evt.sequenceNumber}</span>
                  <span className="court-badge court-badge-ndoh">{evt.action || evt.eventType}</span>
                </div>
                <span>{new Date(evt.actionAt || evt.createdAt || "").toLocaleString()}</span>
              </div>
              <div className="court-timeline-title">
                {evt.notes || evt.reason || evt.description || evt.action}
              </div>
              {(evt.oldStatus || evt.newStatus) && (
                <div style={{ fontSize: "13px", color: "#475569", marginTop: "4px" }}>
                  Status: <strong>{evt.oldStatus || "None"}</strong> → <strong>{evt.newStatus || "None"}</strong>
                </div>
              )}
              <div style={{ fontSize: "12px", color: "#64748b", marginTop: "4px" }}>
                By {evt.actorDisplayName || evt.actorDisplayNameSnapshot || "Officer"}
                {evt.actorDesignation ? ` (${evt.actorDesignation})` : ""}
                {(evt.sourceDeskName || evt.targetDeskName) && (
                  <span> • Desk: {evt.sourceDeskName || "None"} → {evt.targetDeskName || "None"}</span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
