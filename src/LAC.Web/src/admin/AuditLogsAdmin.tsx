import React, { useEffect, useState, useCallback } from "react";
import type { RbacAuditLog, RbacAuditResponse } from "./types";

interface GeneralAuditItem {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  changedAt: string;
  changedBy?: string;
  oldValues?: string;
  newValues?: string;
}

export const AuditLogsAdmin: React.FC = () => {
  const [activeTab, setActiveTab] = useState<"rbac" | "general">("rbac");
  const [rbacLogs, setRbacLogs] = useState<RbacAuditLog[]>([]);
  const [generalLogs, setGeneralLogs] = useState<GeneralAuditItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [actorUserIdFilter, setActorUserIdFilter] = useState("");
  const [onBehalfOfFilter, setOnBehalfOfFilter] = useState("");
  const [entityFilter, setEntityFilter] = useState("");

  const [selectedLog, setSelectedLog] = useState<RbacAuditLog | GeneralAuditItem | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);

      if (activeTab === "rbac") {
        const params = new URLSearchParams({
          page: page.toString(),
          pageSize: pageSize.toString(),
        });
        if (actorUserIdFilter.trim()) params.append("actorUserId", actorUserIdFilter.trim());
        if (onBehalfOfFilter.trim()) params.append("onBehalfOfUserId", onBehalfOfFilter.trim());

        const response = await fetch(`/api/admin/rbac-audit?${params.toString()}`, { credentials: "include" });
        if (response.status === 401) throw new Error("Session expired. Please log in again.");
        if (response.status === 403) throw new Error("Access denied. Audit.View permission required to inspect RBAC audit trail.");
        if (!response.ok) throw new Error(`Failed to load RBAC audit trail (${response.status}).`);

        const data = (await response.json()) as RbacAuditResponse;
        setRbacLogs(data.items || []);
        setTotalCount(data.total ?? (data.items?.length || 0));
      } else {
        const params = new URLSearchParams({
          page: page.toString(),
          pageSize: pageSize.toString(),
        });
        if (entityFilter.trim()) params.append("entityType", entityFilter.trim());

        const response = await fetch(`/api/audit-logs?${params.toString()}`, { credentials: "include" });
        if (!response.ok) throw new Error("Failed to load general audit ledger.");

        const data = await response.json();
        setGeneralLogs(data.items || []);
        setTotalCount(data.totalCount ?? data.total ?? (data.items?.length || 0));
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load audit records.");
    } finally {
      setLoading(false);
    }
  }, [activeTab, page, pageSize, actorUserIdFilter, onBehalfOfFilter, entityFilter]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleTabChange = (tab: "rbac" | "general") => {
    setActiveTab(tab);
    setPage(0);
    setError(null);
  };

  return (
    <div className="rbac-admin-root">
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Statutory Audit & Access Trail</h2>
          <span>Authoritative, immutable ledger of identity mutations, delegations, and operational actions</span>
        </div>
        <div className="rbac-actions-group">
          <button className="secondary-button" onClick={() => void loadData()} disabled={loading}>
            Refresh Ledger
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div className="rbac-tab-bar" style={{ display: "flex", gap: "8px", borderBottom: "2px solid #e2e8f0", paddingBottom: "8px" }}>
        <button
          className={`tab-btn ${activeTab === "rbac" ? "primary-button" : "secondary-button"}`}
          onClick={() => handleTabChange("rbac")}
          style={{ fontWeight: 600 }}
        >
          🛡️ RBAC & Delegation Audit (GET /api/admin/rbac-audit)
        </button>
        <button
          className={`tab-btn ${activeTab === "general" ? "primary-button" : "secondary-button"}`}
          onClick={() => handleTabChange("general")}
          style={{ fontWeight: 600 }}
        >
          📜 General Entity Ledger (GET /api/audit-logs)
        </button>
      </div>

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        {activeTab === "rbac" ? (
          <>
            <div style={{ display: "flex", gap: "10px", flex: 1, flexWrap: "wrap" }}>
              <input
                type="text"
                placeholder="Filter by Actor User ID..."
                value={actorUserIdFilter}
                onChange={(e) => setActorUserIdFilter(e.target.value)}
                style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px", minWidth: "220px" }}
              />
              <input
                type="text"
                placeholder="Filter by Supervising Officer ID..."
                value={onBehalfOfFilter}
                onChange={(e) => setOnBehalfOfFilter(e.target.value)}
                style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px", minWidth: "220px" }}
              />
              <button className="secondary-button" onClick={() => { setPage(0); void loadData(); }}>
                Apply Filter
              </button>
              {(actorUserIdFilter || onBehalfOfFilter) && (
                <button
                  className="quiet-button"
                  onClick={() => {
                    setActorUserIdFilter("");
                    setOnBehalfOfFilter("");
                    setPage(0);
                  }}
                >
                  Clear Filters
                </button>
              )}
            </div>
            <div style={{ fontSize: "13px", color: "#64748b" }}>
              Dual-Actor presentation conforms to statutory Delegation Rules
            </div>
          </>
        ) : (
          <div style={{ display: "flex", gap: "10px", flex: 1 }}>
            <input
              type="text"
              placeholder="Filter by entity type (e.g. User, Matter)..."
              value={entityFilter}
              onChange={(e) => setEntityFilter(e.target.value)}
              style={{ padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px", fontSize: "13px", minWidth: "250px" }}
            />
            <button className="secondary-button" onClick={() => { setPage(0); void loadData(); }}>
              Filter
            </button>
            {entityFilter && (
              <button className="quiet-button" onClick={() => { setEntityFilter(""); setPage(0); }}>
                Clear
              </button>
            )}
          </div>
        )}
      </div>

      {error && (
        <div className="state error" style={{ background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "12px 16px", borderRadius: "8px" }}>
          <strong>Audit Access Error:</strong> {error}
        </div>
      )}

      {loading ? (
        <div className="state" style={{ padding: "32px", textAlign: "center", color: "#64748b" }}>
          <strong>Loading audit trail records...</strong>
        </div>
      ) : activeTab === "rbac" ? (
        <div className="table-wrap" style={{ background: "#ffffff", borderRadius: "8px", border: "1px solid #e2e8f0", overflow: "hidden" }}>
          <table>
            <thead>
              <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0" }}>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Timestamp</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Action</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Target Entity</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Official Acting Authority</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Snapshots</th>
                <th style={{ padding: "12px 16px", textAlign: "right", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Inspection</th>
              </tr>
            </thead>
            <tbody>
              {rbacLogs.map((log) => {
                const isDelegated = Boolean(log.actorLabel && log.actorLabel.includes("on behalf of"));
                return (
                  <tr key={log.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                    <td style={{ padding: "12px 16px", fontSize: "13px", whiteSpace: "nowrap" }}>
                      {new Date(log.changedAt).toLocaleString()}
                    </td>
                    <td style={{ padding: "12px 16px" }}>
                      <span className={`status ${
                        log.action.includes("Create") || log.action === "Created" ? "success" :
                        log.action.includes("Revoke") || log.action === "Revoked" ? "danger" :
                        log.action.includes("Reset") ? "warning" : "neutral"
                      }`} style={{ fontSize: "11.5px", fontWeight: 600 }}>
                        {log.action}
                      </span>
                    </td>
                    <td style={{ padding: "12px 16px", fontSize: "13px" }}>
                      <strong>{log.entityType}</strong>
                      <div style={{ fontSize: "11.5px", color: "#64748b", fontFamily: "monospace" }}>
                        {log.entityId.length > 16 ? `${log.entityId.substring(0, 14)}…` : log.entityId}
                      </div>
                    </td>
                    <td style={{ padding: "12px 16px" }}>
                      {/* Exact server-supplied actorLabel MUST be rendered verbatim */}
                      {log.actorLabel ? (
                        <div className="rbac-actor-label-container" style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                          <div style={{
                            display: "inline-flex",
                            alignItems: "center",
                            gap: "6px",
                            background: isDelegated ? "#e0f2fe" : "#f1f5f9",
                            border: `1px solid ${isDelegated ? "#bae6fd" : "#cbd5e1"}`,
                            color: isDelegated ? "#0369a1" : "#334155",
                            padding: "4px 8px",
                            borderRadius: "6px",
                            fontSize: "12.5px",
                            fontWeight: 600,
                          }}>
                            {isDelegated && <span title="Delegated Action" style={{ fontSize: "13px" }}>🤝</span>}
                            <span>{log.actorLabel}</span>
                          </div>
                          {isDelegated && (
                            <span style={{ fontSize: "11px", color: "#0284c7" }}>
                              Statutory Dual-Actor Attribution
                            </span>
                          )}
                        </div>
                      ) : (
                        <span style={{ fontWeight: 600, color: "#334155", fontSize: "13px" }}>
                          {log.changedBy || "System / Seed"}
                        </span>
                      )}
                    </td>
                    <td style={{ padding: "12px 16px", fontSize: "12px", color: "#64748b" }}>
                      {log.actorDisplayNameSnapshot && (
                        <div>Actor: <strong style={{ color: "#1e293b" }}>{log.actorDisplayNameSnapshot}</strong></div>
                      )}
                      {log.onBehalfOfDisplayNameSnapshot && (
                        <div>Supervisor: <strong style={{ color: "#2563eb" }}>{log.onBehalfOfDisplayNameSnapshot}</strong></div>
                      )}
                      {!log.actorDisplayNameSnapshot && !log.onBehalfOfDisplayNameSnapshot && (
                        <span style={{ fontStyle: "italic", color: "#94a3b8" }}>Direct action</span>
                      )}
                    </td>
                    <td style={{ padding: "12px 16px", textAlign: "right" }}>
                      <button className="quiet-button text-action" onClick={() => setSelectedLog(log)}>
                        Inspect
                      </button>
                    </td>
                  </tr>
                );
              })}
              {rbacLogs.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "36px", color: "#64748b" }}>
                    No RBAC audit records matching query parameters.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="table-wrap" style={{ background: "#ffffff", borderRadius: "8px", border: "1px solid #e2e8f0", overflow: "hidden" }}>
          <table>
            <thead>
              <tr style={{ background: "#f8fafc", borderBottom: "1px solid #e2e8f0" }}>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Timestamp</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Action</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Entity Type</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Entity ID</th>
                <th style={{ padding: "12px 16px", textAlign: "left", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Changed By</th>
                <th style={{ padding: "12px 16px", textAlign: "right", fontSize: "12px", textTransform: "uppercase", color: "#475569" }}>Details</th>
              </tr>
            </thead>
            <tbody>
              {generalLogs.map((log) => (
                <tr key={log.id} style={{ borderBottom: "1px solid #f1f5f9" }}>
                  <td style={{ padding: "12px 16px", fontSize: "13px" }}>{new Date(log.changedAt).toLocaleString()}</td>
                  <td style={{ padding: "12px 16px" }}>
                    <span className={`status ${log.action === "Created" ? "success" : log.action === "Archived" ? "warning" : ""}`} style={{ fontSize: "11.5px" }}>
                      {log.action}
                    </span>
                  </td>
                  <td style={{ padding: "12px 16px", fontSize: "13px" }}><strong>{log.entityType}</strong></td>
                  <td style={{ padding: "12px 16px", fontSize: "12px", fontFamily: "monospace" }}>{log.entityId.substring(0, 8)}...</td>
                  <td style={{ padding: "12px 16px", fontSize: "13px", fontWeight: 600, color: "#1c5d9f" }}>
                    {log.changedBy || "System / Seed"}
                  </td>
                  <td style={{ padding: "12px 16px", textAlign: "right" }}>
                    <button className="quiet-button text-action" onClick={() => setSelectedLog(log)}>
                      Inspect
                    </button>
                  </td>
                </tr>
              ))}
              {generalLogs.length === 0 && (
                <tr>
                  <td colSpan={6} style={{ textAlign: "center", padding: "36px", color: "#64748b" }}>
                    No audit records matching query.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Pagination */}
      <div className="pagination" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "12px 0" }}>
        <span style={{ fontSize: "13px", color: "#64748b" }}>Total Records: {totalCount}</span>
        <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
          <button className="secondary-button" disabled={page === 0} onClick={() => setPage((p) => p - 1)}>
            Previous
          </button>
          <span style={{ padding: "6px 12px", fontSize: "13px", fontWeight: 600 }}>Page {page + 1}</span>
          <button className="secondary-button" disabled={(page + 1) * pageSize >= totalCount} onClick={() => setPage((p) => p + 1)}>
            Next
          </button>
        </div>
      </div>

      {/* Detail Modal */}
      {selectedLog && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "800px", width: "90%" }}>
            <h3>Audit Record Details</h3>
            <p style={{ margin: "0 0 16px 0", color: "#475569" }}>
              <strong>{selectedLog.action}</strong> on <strong>{selectedLog.entityType}</strong> (<code>{selectedLog.entityId}</code>)
              <br />
              Timestamp: {new Date(selectedLog.changedAt).toLocaleString()}
            </p>

            {"actorLabel" in selectedLog && selectedLog.actorLabel && (
              <div style={{
                background: "#f0fdf4",
                border: "1px solid #bbf7d0",
                padding: "12px 16px",
                borderRadius: "6px",
                marginBottom: "16px",
              }}>
                <div style={{ fontSize: "12px", color: "#166534", fontWeight: 600, textTransform: "uppercase" }}>
                  Official Acting Attribution (Server Label)
                </div>
                <div style={{ fontSize: "16px", fontWeight: 700, color: "#14532d", marginTop: "4px" }}>
                  {selectedLog.actorLabel}
                </div>
                <div style={{ fontSize: "12px", color: "#15803d", marginTop: "6px", display: "flex", gap: "16px" }}>
                  <span>Actor Snapshot: <strong>{selectedLog.actorDisplayNameSnapshot || "N/A"}</strong></span>
                  <span>Supervisor Snapshot: <strong>{selectedLog.onBehalfOfDisplayNameSnapshot || "N/A"}</strong></span>
                </div>
              </div>
            )}

            {selectedLog.oldValues && (
              <div style={{ marginBottom: "16px" }}>
                <strong>Previous State (Old Values):</strong>
                <pre style={{ background: "#f8f9fa", border: "1px solid #e2e8f0", padding: "10px", borderRadius: "6px", maxHeight: "160px", overflow: "auto", fontSize: "12px" }}>
                  {(() => {
                    try {
                      return JSON.stringify(JSON.parse(selectedLog.oldValues), null, 2);
                    } catch {
                      return selectedLog.oldValues;
                    }
                  })()}
                </pre>
              </div>
            )}

            {selectedLog.newValues && (
              <div style={{ marginBottom: "16px" }}>
                <strong>Mutated State (New Values):</strong>
                <pre style={{ background: "#f8f9fa", border: "1px solid #e2e8f0", padding: "10px", borderRadius: "6px", maxHeight: "160px", overflow: "auto", fontSize: "12px" }}>
                  {(() => {
                    try {
                      return JSON.stringify(JSON.parse(selectedLog.newValues), null, 2);
                    } catch {
                      return selectedLog.newValues;
                    }
                  })()}
                </pre>
              </div>
            )}

            <div className="form-footer" style={{ display: "flex", justifyContent: "flex-end", marginTop: "20px" }}>
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
