import React, { useEffect, useState, useCallback } from "react";

interface AuditLogItem {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  changedAt: string;
  changedBy?: string;
  oldValues?: string;
  newValues?: string;
}

interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const AuditLogsAdmin: React.FC = () => {
  const [logs, setLogs] = useState<AuditLogItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);
  const [entityFilter, setEntityFilter] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedLog, setSelectedLog] = useState<AuditLogItem | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const params = new URLSearchParams({
        page: page.toString(),
        pageSize: pageSize.toString(),
      });
      if (entityFilter.trim()) {
        params.append("entityType", entityFilter.trim());
      }

      const response = await fetch(`/api/audit-logs?${params.toString()}`, { credentials: "include" });
      if (!response.ok) {
        throw new Error("Failed to load audit logs.");
      }

      const data = (await response.json()) as Page<AuditLogItem>;
      setLogs(data.items);
      setTotalCount(data.totalCount);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load audit records.");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, entityFilter]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  return (
    <div className="admin-page">
      <div className="section-heading">
        <div>
          <h2>System Audit Trail</h2>
          <span>Statutory immutable ledger of all entity mutations, official record creations, updates, and archiving</span>
        </div>
        <div style={{ display: "flex", gap: "8px" }}>
          <input
            type="text"
            placeholder="Filter by entity type..."
            value={entityFilter}
            onChange={(e) => setEntityFilter(e.target.value)}
            style={{ padding: "6px 10px", border: "1px solid #cbd5de", borderRadius: "5px" }}
          />
          <button className="secondary-button" onClick={() => { setPage(0); void loadData(); }}>
            Filter
          </button>
        </div>
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {loading ? (
        <div className="state"><strong>Loading audit records...</strong></div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Timestamp</th>
                <th>Action</th>
                <th>Entity Type</th>
                <th>Entity ID</th>
                <th>Official Actor (Changed By)</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {logs.map((log) => (
                <tr key={log.id}>
                  <td>{new Date(log.changedAt).toLocaleString()}</td>
                  <td>
                    <span className={`status ${log.action === "Created" ? "success" : log.action === "Archived" ? "warning" : ""}`}>
                      {log.action}
                    </span>
                  </td>
                  <td><strong>{log.entityType}</strong></td>
                  <td><code>{log.entityId.substring(0, 8)}...</code></td>
                  <td>
                    {log.changedBy ? (
                      <span style={{ fontWeight: 600, color: "#1c5d9f" }}>{log.changedBy}</span>
                    ) : (
                      <span className="subtext">System / Seed</span>
                    )}
                  </td>
                  <td>
                    <button className="quiet-button text-action" onClick={() => setSelectedLog(log)}>
                      Inspect
                    </button>
                  </td>
                </tr>
              ))}
              {logs.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "24px" }}>
                    No audit records matching query.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      <div className="pagination">
        <span>Total Records: {totalCount}</span>
        <div>
          <button disabled={page === 0} onClick={() => setPage((p) => p - 1)}>
            Previous
          </button>
          <span style={{ padding: "8px 12px" }}>Page {page + 1}</span>
          <button disabled={(page + 1) * pageSize >= totalCount} onClick={() => setPage((p) => p + 1)}>
            Next
          </button>
        </div>
      </div>

      {/* Detail Modal */}
      {selectedLog && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "800px" }}>
            <h3>Audit Record Details</h3>
            <p>
              <strong>{selectedLog.action}</strong> on <strong>{selectedLog.entityType}</strong> (<code>{selectedLog.entityId}</code>)
              <br />
              At: {new Date(selectedLog.changedAt).toLocaleString()} by <strong>{selectedLog.changedBy || "System / Seed"}</strong>
            </p>

            {selectedLog.oldValues && (
              <div>
                <strong>Previous Values:</strong>
                <pre style={{ background: "#f8f9fa", padding: "10px", borderRadius: "5px", maxHeight: "150px", overflow: "auto" }}>
                  {JSON.stringify(JSON.parse(selectedLog.oldValues), null, 2)}
                </pre>
              </div>
            )}

            {selectedLog.newValues && (
              <div>
                <strong>New Values:</strong>
                <pre style={{ background: "#f8f9fa", padding: "10px", borderRadius: "5px", maxHeight: "150px", overflow: "auto" }}>
                  {JSON.stringify(JSON.parse(selectedLog.newValues), null, 2)}
                </pre>
              </div>
            )}

            <div className="form-footer">
              <button type="button" className="secondary-button" onClick={() => setSelectedLog(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
