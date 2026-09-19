import { useEffect, useState } from "react";
import type { DakMovement } from "./types";

interface Props {
  dakId: string;
}

export function DakTimeline({ dakId }: Props) {
  const [movements, setMovements] = useState<DakMovement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    fetch(`/api/dak/${dakId}/timeline`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) throw new Error("Could not load movement timeline.");
        return res.json() as Promise<DakMovement[]>;
      })
      .then((data) => {
        if (active) {
          setMovements(data);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err.message);
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [dakId]);

  if (loading) return <p className="subtext">Loading movement timeline...</p>;
  if (error) return <p className="error-text">{error}</p>;
  if (!movements.length) return <p className="subtext">No movements recorded yet.</p>;

  return (
    <div className="timeline">
      {movements.map((m) => (
        <div key={m.id} className="timeline-item">
          <div className="timeline-badge">#{m.sequenceNumber}</div>
          <div className="timeline-content">
            <div className="timeline-header">
              <span className={`status-pill status-${m.action.toLowerCase()}`}>{m.action}</span>
              <span className="timeline-date">
                {new Date(m.actionAt).toLocaleString("en-IN", { dateStyle: "medium", timeStyle: "short" })}
              </span>
            </div>

            <div className="timeline-routing">
              {m.fromDeskName && (
                <span>
                  <strong>From:</strong> {m.fromDeskName}
                  {m.fromUserDisplayName && ` (${m.fromUserDisplayName})`}
                </span>
              )}
              {m.toDeskName && (
                <span>
                  <strong>To:</strong> {m.toDeskName}
                  {m.toUserDisplayName && ` (${m.toUserDisplayName})`}
                </span>
              )}
            </div>

            <div className="timeline-actor">
              <small className="subtext">Action taken by: {m.actionByDisplayName}</small>
            </div>

            {m.instructions && (
              <div className="timeline-instructions">
                <strong>Instructions:</strong> {m.instructions}
              </div>
            )}

            {m.remarks && (
              <div className="timeline-remarks">
                <strong>Noting / Remarks:</strong> {m.remarks}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
