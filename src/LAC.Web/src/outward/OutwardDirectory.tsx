import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { OutwardListItem, OutwardListResponse, OutwardRegistrationContext } from "./types";
import "./outward.css";

export const OutwardDirectory: React.FC = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { hasPermission } = useAuth();

  const dakIdParam = searchParams.get("dakId") || "";
  const matterIdParam = searchParams.get("matterId") || "";

  const [items, setItems] = useState<OutwardListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);
  const [q, setQ] = useState("");
  const [searchTerm, setSearchTerm] = useState("");
  const [status, setStatus] = useState<string>("");
  const [deskId, setDeskId] = useState<string>("");
  const [workstreamId, setWorkstreamId] = useState<string>("");
  const [context, setContext] = useState<OutwardRegistrationContext | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetch("/api/outward/context", { credentials: "include" })
      .then((r) => (r.ok ? (r.json() as Promise<OutwardRegistrationContext>) : null))
      .then((data) => {
        if (data) setContext(data);
      })
      .catch(() => {});
  }, []);

  const loadOutwards = useCallback(
    async (signal?: AbortSignal) => {
      try {
        setLoading(true);
        setError(null);
        const params = new URLSearchParams();
        params.append("page", page.toString());
        params.append("pageSize", pageSize.toString());
        if (searchTerm.trim()) params.append("q", searchTerm.trim());
        if (status) params.append("status", status);
        if (deskId) params.append("deskId", deskId);
        if (workstreamId) params.append("workstreamId", workstreamId);
        if (dakIdParam) params.append("dakId", dakIdParam);
        if (matterIdParam) params.append("matterId", matterIdParam);

        const res = await fetch(`/api/outward?${params.toString()}`, {
          credentials: "include",
          signal,
        });

        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view Outward records.");
          throw new Error("Failed to load outward records.");
        }

        const data = (await res.json()) as OutwardListResponse;
        setItems(data.items);
        setTotalCount(data.totalCount);
      } catch (err: unknown) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setError(err instanceof Error ? err.message : "Failed to load Outward records.");
      } finally {
        setLoading(false);
      }
    },
    [page, pageSize, searchTerm, status, deskId, workstreamId, dakIdParam, matterIdParam]
  );

  useEffect(() => {
    const controller = new AbortController();
    void loadOutwards(controller.signal);
    return () => controller.abort();
  }, [loadOutwards]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(0);
    setSearchTerm(q);
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  const getStatusBadge = (itemStatus: string) => {
    switch (itemStatus) {
      case "Dispatched":
        return <span className="outward-pill outward-pill-dispatched">Dispatched</span>;
      case "Cancelled":
        return <span className="outward-pill outward-pill-cancelled">Cancelled</span>;
      default:
        return <span className="outward-pill outward-pill-registered">Registered</span>;
    }
  };

  return (
    <div className="outward-directory-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <span>Outward / Dispatch Register</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow">Dispatch & Issue</div>
          <h1>Outward / Dispatch Register</h1>
          <p>Central register of official outward dispatches, government letters, compliance intimations, and notices.</p>
        </div>
        {hasPermission("Outward.Create") && (
          <button className="primary-button" onClick={() => navigate("/outward/new")}>
            + Register Outward
          </button>
        )}
      </div>

      {dakIdParam && (
        <div className="dak-attention-banner" style={{ background: "#eff6ff", borderColor: "#3b82f6", color: "#1e40af" }}>
          <span>Showing outward replies linked to Dak: <strong>{dakIdParam}</strong></span>
          <button className="secondary-button" style={{ marginLeft: "auto", fontSize: "12px", padding: "4px 8px" }} onClick={() => navigate("/outward")}>
            Show All Outwards
          </button>
        </div>
      )}

      {matterIdParam && (
        <div className="dak-attention-banner" style={{ background: "#f0fdf4", borderColor: "#22c55e", color: "#166534" }}>
          <span>Showing outward communications linked to Matter: <strong>{matterIdParam}</strong></span>
          <button className="secondary-button" style={{ marginLeft: "auto", fontSize: "12px", padding: "4px 8px" }} onClick={() => navigate("/outward")}>
            Show All Outwards
          </button>
        </div>
      )}

      {/* Filters Bar */}
      <div className="summary-strip" style={{ padding: "12px 16px", display: "flex", gap: "12px", alignItems: "center", flexWrap: "wrap", width: "100%" }}>
        <form onSubmit={handleSearchSubmit} style={{ display: "flex", gap: "8px", flex: "1 1 300px" }}>
          <input
            type="text"
            placeholder="Search outward no, subject, recipient, dispatch ref..."
            value={q}
            onChange={(e) => setQ(e.target.value)}
            style={{ flex: 1, padding: "8px 12px", border: "1px solid #cbd5e1", borderRadius: "6px" }}
          />
          <button type="submit" className="secondary-button">
            Search
          </button>
          {searchTerm && (
            <button
              type="button"
              className="secondary-button"
              onClick={() => {
                setQ("");
                setSearchTerm("");
                setPage(0);
              }}
            >
              Clear
            </button>
          )}
        </form>

        <div style={{ display: "flex", gap: "10px", alignItems: "center", flexWrap: "wrap" }}>
          <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
            Status:
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(0);
              }}
              style={{ padding: "6px 10px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
            >
              <option value="">All Statuses</option>
              <option value="Registered">Registered (Draft/Issued)</option>
              <option value="Dispatched">Dispatched (Sealed)</option>
              <option value="Cancelled">Cancelled (Revoked)</option>
            </select>
          </label>

          {context && context.desks.length > 0 && (
            <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
              Issuing Desk:
              <select
                value={deskId}
                onChange={(e) => {
                  setDeskId(e.target.value);
                  setPage(0);
                }}
                style={{ padding: "6px 10px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
              >
                <option value="">All Desks</option>
                {context.desks.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name} ({d.code})
                  </option>
                ))}
              </select>
            </label>
          )}

          {context && context.workstreams.length > 0 && (
            <label style={{ fontSize: "12px", display: "flex", alignItems: "center", gap: "4px" }}>
              Workstream:
              <select
                value={workstreamId}
                onChange={(e) => {
                  setWorkstreamId(e.target.value);
                  setPage(0);
                }}
                style={{ padding: "6px 10px", borderRadius: "5px", border: "1px solid #cbd5e1" }}
              >
                <option value="">All Workstreams</option>
                {context.workstreams.map((w) => (
                  <option key={w.id} value={w.id}>
                    {w.name} ({w.code})
                  </option>
                ))}
              </select>
            </label>
          )}
        </div>
      </div>

      {error && (
        <div className="dak-attention-banner" style={{ marginTop: "16px" }}>
          <span>{error}</span>
        </div>
      )}

      {/* Directory Table */}
      <div className="table-responsive" style={{ marginTop: "16px" }}>
        <table className="data-table">
          <thead>
            <tr>
              <th style={{ width: "160px" }}>Outward No. & Date</th>
              <th>Subject & Recipient</th>
              <th style={{ width: "180px" }}>Issuing Desk</th>
              <th style={{ width: "110px" }}>Status</th>
              <th style={{ width: "160px" }}>Dispatch Info</th>
              <th style={{ width: "90px", textAlign: "center" }}>Docs</th>
              <th style={{ width: "90px", textAlign: "center" }}>Action</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={7} style={{ textAlign: "center", padding: "40px", color: "#64748b" }}>
                  Loading outward records...
                </td>
              </tr>
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={7} style={{ textAlign: "center", padding: "40px", color: "#64748b" }}>
                  No outward records found.
                </td>
              </tr>
            ) : (
              items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link to={`/outward/${item.id}`} style={{ fontWeight: 700, color: "#1e609e" }}>
                      {item.outwardNumber}
                    </Link>
                    <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                      {item.outwardDate}
                    </div>
                  </td>
                  <td>
                    <div style={{ fontWeight: 600, color: "#1e293b", marginBottom: "3px" }}>
                      {item.subject}
                    </div>
                    <div style={{ fontSize: "12px", color: "#475569" }}>
                      To: <strong>{item.recipientName}</strong>
                      {item.recipientDesignation && ` (${item.recipientDesignation})`}
                    </div>
                  </td>
                  <td>
                    <div style={{ fontWeight: 600, fontSize: "13px", color: "#334155" }}>
                      {item.issuingDeskName}
                    </div>
                    {item.workstreamName && (
                      <div style={{ fontSize: "11px", color: "#64748b" }}>
                        WS: {item.workstreamName}
                      </div>
                    )}
                  </td>
                  <td>{getStatusBadge(item.status)}</td>
                  <td>
                    {item.status === "Dispatched" ? (
                      <div style={{ fontSize: "12px", color: "#166534" }}>
                        <div>{item.dispatchMode}</div>
                        <div style={{ fontSize: "11px", color: "#64748b" }}>
                          {item.dispatchDate}
                          {item.dispatchReferenceNumber && ` • Ref: ${item.dispatchReferenceNumber}`}
                        </div>
                      </div>
                    ) : item.status === "Cancelled" ? (
                      <div style={{ fontSize: "12px", color: "#dc2626" }} title={item.cancellationReason}>
                        Cancelled
                      </div>
                    ) : (
                      <span style={{ fontSize: "12px", color: "#64748b" }}>Pending Dispatch</span>
                    )}
                  </td>
                  <td style={{ textAlign: "center" }}>
                    <div style={{ display: "flex", gap: "4px", justifyContent: "center", alignItems: "center" }}>
                      {item.hasDocument && (
                        <span title="Main Document Attached" style={{ fontSize: "14px" }}>
                          📄
                        </span>
                      )}
                      {item.attachmentCount > 0 && (
                        <span
                          title={`${item.attachmentCount} Attachment(s)`}
                          style={{
                            background: "#e2e8f0",
                            color: "#334155",
                            borderRadius: "10px",
                            padding: "1px 6px",
                            fontSize: "11px",
                            fontWeight: 700,
                          }}
                        >
                          +{item.attachmentCount}
                        </span>
                      )}
                      {!item.hasDocument && item.attachmentCount === 0 && (
                        <span style={{ color: "#cbd5e1", fontSize: "12px" }}>—</span>
                      )}
                    </div>
                  </td>
                  <td style={{ textAlign: "center" }}>
                    <button
                      className="secondary-button"
                      style={{ fontSize: "12px", padding: "4px 8px" }}
                      onClick={() => navigate(`/outward/${item.id}`)}
                    >
                      Open
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: "16px" }}>
          <div style={{ fontSize: "13px", color: "#64748b" }}>
            Showing {items.length > 0 ? page * pageSize + 1 : 0} to{" "}
            {Math.min((page + 1) * pageSize, totalCount)} of {totalCount} records
          </div>
          <div style={{ display: "flex", gap: "8px" }}>
            <button
              className="secondary-button"
              disabled={page === 0}
              onClick={() => setPage((p) => Math.max(0, p - 1))}
            >
              Previous
            </button>
            <span style={{ display: "flex", alignItems: "center", padding: "0 8px", fontSize: "13px" }}>
              Page {page + 1} of {totalPages}
            </span>
            <button
              className="secondary-button"
              disabled={page >= totalPages - 1}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
};
