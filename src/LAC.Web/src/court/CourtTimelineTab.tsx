import React, { useState, useEffect, useCallback } from "react";
import { CourtCaseDetailDto, CourtCaseEventDto } from "./types";

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
                  <span className="court-badge court-badge-ndoh">{evt.eventType}</span>
                </div>
                <span>{new Date(evt.createdAt).toLocaleString()}</span>
              </div>
              <div className="court-timeline-title">{evt.description}</div>
              <div style={{ fontSize: "12px", color: "#64748b", marginTop: "4px" }}>
                By {evt.actorDisplayNameSnapshot || "System"}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
