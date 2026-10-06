import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { DakListItem } from "./types";
import "./dak.css";

interface DeskOption {
  id: string;
  code: string;
  name: string;
}

interface DakListResponse {
  items: DakListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export const DakDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const [items, setItems] = useState<DakListItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);
  const [q, setQ] = useState("");
  const [searchTerm, setSearchTerm] = useState("");
  const [status, setStatus] = useState<string>("");
  const [priority, setPriority] = useState<string>("");
  const [deskId, setDeskId] = useState<string>("");
  const [desks, setDesks] = useState<DeskOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Load active desks for filter via operational lookup
  useEffect(() => {
    fetch("/api/dak/lookups/directory", { credentials: "include" })
      .then((r) => r.json() as Promise<{ desks: DeskOption[] }>)
      .then((data) => setDesks(data.desks))
      .catch(() => {});
  }, []);

  const loadDaks = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const params = new URLSearchParams();
      params.append("page", page.toString());
      params.append("pageSize", pageSize.toString());
      if (searchTerm.trim()) params.append("q", searchTerm.trim());
      if (status) params.append("status", status);
      if (priority) params.append("priority", priority);
      if (deskId) params.append("deskId", deskId);

      const res = await fetch(`/api/dak?${params.toString()}`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view Dak.");
        throw new Error("Failed to load inward Dak records.");
      }

      const data = (await res.json()) as DakListResponse;
      setItems(data.items);
      setTotalCount(data.totalCount);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load Dak records.");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, searchTerm, status, priority, deskId]);

  useEffect(() => {
    void loadDaks();
  }, [loadDaks]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(0);
    setSearchTerm(q);
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="dak-directory-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <span>Dak / Inward Correspondence</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow">Correspondence & Intake</div>
          <h1>Inward Dak Register</h1>
          <p>Central register of inward letters, representations, judicial notices, and official references.</p>
        </div>
        {hasPermission("Dak.Register") && (
          <button className="primary-button" onClick={() => navigate("/dak/register")}>
            + Quick Intake Dak
          </button>
        )}
      </div>

      {/* Filters Bar */}
      <div className="summary-strip directory-filters">
        <form onSubmit={handleSearchSubmit} className="search-form">
          <input
            type="text"
            placeholder="Search by diary no, subject, sender, ref..."
            value={q}
            onChange={(e) => setQ(e.target.value)}
            className="search-input"
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

        <div className="filter-controls">
          <label className="filter-label">
            Status:
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                setPage(0);
              }}
              className="filter-select"
            >
              <option value="">All Statuses</option>
              <option value="Registered">Registered (Intake)</option>
              <option value="InProcess">In Process</option>
              <option value="Disposed">Disposed</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </label>

          <label className="filter-label">
            Priority:
            <select
              value={priority}
              onChange={(e) => {
                setPriority(e.target.value);
                setPage(0);
              }}
              className="filter-select"
            >
              <option value="">All Priorities</option>
              <option value="Routine">Routine</option>
              <option value="Urgent">Urgent</option>
              <option value="Immediate">Immediate</option>
            </select>
          </label>

          <label className="filter-label">
            Desk:
            <select
              value={deskId}
              onChange={(e) => {
                setDeskId(e.target.value);
                setPage(0);
              }}
              className="filter-select"
            >
              <option value="">All Desks</option>
              {desks.map((d) => (
                <option key={d.id} value={d.id}>{d.name} ({d.code})</option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Table */}
      {loading ? (
        <div className="state"><strong>Loading Inward Register...</strong></div>
      ) : items.length === 0 ? (
        <div className="state">
          <strong>No correspondence found.</strong>
          <span>Adjust your filters or register new inward dak.</span>
        </div>
      ) : (
        <>
          <div className="table-wrap compact-table-wrap">
            <table className="compact-dak-table">
              <thead>
                <tr>
                  <th style={{ width: "13%" }}>Diary No.</th>
                  <th style={{ width: "10%" }}>Received Date</th>
                  <th style={{ width: "18%" }}>From / Sender</th>
                  <th style={{ width: "23%" }}>Subject</th>
                  <th style={{ width: "16%" }}>Current Desk / Officer</th>
                  <th style={{ width: "8%" }}>Workstream</th>
                  <th style={{ width: "7%" }}>Status</th>
                  <th style={{ width: "5%", textAlign: "center" }}>Doc</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id} className={!item.assignedDeskName ? "row-unmarked" : ""}>
                    <td>
                      <Link to={`/dak/${item.id}`} className="entity-link diary-no-link">
                        {item.diaryNumber}
                      </Link>
                      <div className="subtext mode-subtext">{item.inwardMode}</div>
                    </td>
                    <td>
                      <span className="date-display">{item.receivedDate}</span>
                    </td>
                    <td>
                      <strong className="sender-name-text">{item.senderName}</strong>
                      {item.senderDepartment && (
                        <div className="subtext sender-dept-text">{item.senderDepartment}</div>
                      )}
                    </td>
                    <td>
                      <div className="subject-cell-text" title={item.subject}>
                        {item.subject}
                      </div>
                    </td>
                    <td>
                      {item.assignedDeskName ? (
                        <div className="custody-cell">
                          <strong className="desk-name-text">{item.assignedDeskName}</strong>
                          {item.assignedUserDisplayName && (
                            <div className="subtext officer-name-text">{item.assignedUserDisplayName}</div>
                          )}
                        </div>
                      ) : (
                        <span className="unmarked-badge" title="Intake received; operational marking pending">
                          ⚠️ UNMARKED / Intake Queue
                        </span>
                      )}
                    </td>
                    <td>
                      <span className="workstream-tag">{item.workstreamName || "General"}</span>
                    </td>
                    <td>
                      <span className={`status-pill status-${item.status.toLowerCase()}`}>
                        {item.status}
                      </span>
                    </td>
                    <td style={{ textAlign: "center" }}>
                      {item.hasDocument ? <span title="Primary scan attached">📎</span> : <span style={{ color: "#cbd5e1" }}>—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="pagination">
            <span>
              Showing {items.length} of {totalCount} correspondence entries
            </span>
            <div>
              <button
                disabled={page <= 0}
                onClick={() => setPage((p) => Math.max(0, p - 1))}
              >
                Previous
              </button>
              <span style={{ padding: "8px 12px" }}>
                Page {page + 1} of {Math.max(1, totalPages)}
              </span>
              <button
                disabled={page + 1 >= totalPages}
                onClick={() => setPage((p) => p + 1)}
              >
                Next
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  );
};
