import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import type { DakListItem, DakDeliveryQueueItem } from "./types";
import { formatDakStatus, isLongPendingReceipt } from "./dakConfig";
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

export type DirectoryTab = "all" | "incoming" | "sent" | "with-me" | "resolved" | "attention";

export const DakDirectory: React.FC = () => {
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();

  const bucketParam = searchParams.get("bucket");
  const initialTab: DirectoryTab =
    bucketParam === "incoming" || bucketParam === "sent" || bucketParam === "with-me" || bucketParam === "resolved" || bucketParam === "attention"
      ? bucketParam
      : "all";

  const [activeTab, setActiveTab] = useState<DirectoryTab>(initialTab);
  const [items, setItems] = useState<DakListItem[]>([]);
  const [queueItems, setQueueItems] = useState<DakDeliveryQueueItem[]>([]);
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

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);

      if (activeTab === "all") {
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
      } else {
        const params = new URLSearchParams();
        params.append("bucket", activeTab);
        params.append("page", page.toString());
        params.append("pageSize", pageSize.toString());

        const res = await fetch(`/api/dak/delivery-queue?${params.toString()}`, { credentials: "include" });
        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view this delivery queue.");
          throw new Error("Failed to load delivery queue records.");
        }

        const data = (await res.json()) as { items: DakDeliveryQueueItem[]; totalCount: number; page: number; pageSize: number };
        setQueueItems(data.items);
        setTotalCount(data.totalCount);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load records.");
    } finally {
      setLoading(false);
    }
  }, [activeTab, page, pageSize, searchTerm, status, priority, deskId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const handleTabChange = (newTab: DirectoryTab) => {
    setActiveTab(newTab);
    setPage(0);
    const newParams = new URLSearchParams(searchParams);
    if (newTab === "all") {
      newParams.delete("bucket");
    } else {
      newParams.set("bucket", newTab);
    }
    setSearchParams(newParams);
  };

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(0);
    setSearchTerm(q);
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  const bucketDescriptions: Record<Exclude<DirectoryTab, "all">, { title: string; desc: string }> = {
    incoming: {
      title: "Incoming Dispatches",
      desc: "Correspondence dispatched to you awaiting formal receipt acknowledgment. Click to inspect and acknowledge receipt."
    },
    sent: {
      title: "Sent / In-Transit Dispatches",
      desc: "Dispatches you initiated that are currently in transit awaiting acknowledgment by the recipient officer. Can be pulled back before receipt."
    },
    "with-me": {
      title: "With Me (Active Custody)",
      desc: "Correspondence currently in your active possession ready for noting, official action, or subsequent dispatch."
    },
    resolved: {
      title: "Resolved Correspondence",
      desc: "Settled correspondence records maintained for historical accountability."
    },
    attention: {
      title: "Attention Required",
      desc: "Correspondence requiring administrative resolution, return verification, or custody realignment."
    }
  };

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

      {/* Delivery Queue Tabs Bar */}
      <div className="directory-tabs-bar">
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "all" ? "active" : ""}`}
          onClick={() => handleTabChange("all")}
        >
          All Register
        </button>
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "incoming" ? "active" : ""}`}
          onClick={() => handleTabChange("incoming")}
        >
          📥 Incoming Dispatches
        </button>
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "sent" ? "active" : ""}`}
          onClick={() => handleTabChange("sent")}
        >
          📤 Sent / In-Transit
        </button>
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "with-me" ? "active" : ""}`}
          onClick={() => handleTabChange("with-me")}
        >
          📂 With Me
        </button>
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "resolved" ? "active" : ""}`}
          onClick={() => handleTabChange("resolved")}
        >
          ✓ Resolved
        </button>
        <button
          type="button"
          className={`directory-tab-button ${activeTab === "attention" ? "active" : ""}`}
          onClick={() => handleTabChange("attention")}
        >
          ⚠️ Attention Needed
        </button>
      </div>

      {/* Filters Bar or Queue Intro */}
      {activeTab === "all" ? (
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
                <option value="Resolved">Resolved</option>
                <option value="Disposed">Disposed (Historical)</option>
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
      ) : (
        <div className="queue-bucket-intro">
          <strong>{bucketDescriptions[activeTab].title}:</strong> {bucketDescriptions[activeTab].desc}
        </div>
      )}

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Table */}
      {loading ? (
        <div className="state"><strong>Loading {activeTab === "all" ? "Inward Register..." : "Delivery Queue..."}</strong></div>
      ) : (activeTab === "all" ? items.length === 0 : queueItems.length === 0) ? (
        <div className="state">
          <strong>No correspondence found.</strong>
          <span>{activeTab === "all" ? "Adjust your filters or register new inward dak." : "Your queue for this category is currently clear."}</span>
        </div>
      ) : activeTab === "all" ? (
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
                {items.map((item) => {
                  const isLongPending = item.status !== "Disposed" && item.status !== "Cancelled" && isLongPendingReceipt(item.createdAt);
                  return (
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
                            {isLongPending && (
                              <span className="attention-badge" title="Pending > 48h">
                                ⏱ &gt;48h
                              </span>
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
                          {formatDakStatus(item.status)}
                        </span>
                      </td>
                      <td style={{ textAlign: "center" }}>
                        {item.hasDocument ? <span title="Primary scan attached">📎</span> : <span style={{ color: "#cbd5e1" }}>—</span>}
                      </td>
                    </tr>
                  );
                })}
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
      ) : (
        <>
          <div className="table-wrap compact-table-wrap">
            <table className="compact-dak-table">
              <thead>
                <tr>
                  <th style={{ width: "16%" }}>Diary No.</th>
                  <th style={{ width: "22%" }}>Routing / Custody State</th>
                  <th style={{ width: "22%" }}>Physical File Status</th>
                  <th style={{ width: "16%" }}>Status</th>
                  <th style={{ width: "10%" }}>Revision</th>
                  <th style={{ width: "14%", textAlign: "right" }}>Action</th>
                </tr>
              </thead>
              <tbody>
                {queueItems.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <Link to={`/dak/${item.id}`} className="entity-link diary-no-link">
                        {item.diaryNumber}
                      </Link>
                    </td>
                    <td>
                      {item.routingState === "InTransit" && (
                        <span className="badge badge-warning" style={{ background: "#fef3c7", color: "#92400e", padding: "2px 8px", borderRadius: "12px", border: "1px solid #fde68a" }}>
                          ⏳ In Transit
                        </span>
                      )}
                      {item.routingState === "WithHolder" && (
                        <span className="badge" style={{ background: "#f0fdf4", color: "#166534", padding: "2px 8px", borderRadius: "12px", border: "1px solid #bbf7d0" }}>
                          ● With Holder
                        </span>
                      )}
                      {item.routingState === "Unassigned" && (
                        <span className="badge" style={{ background: "#f1f5f9", color: "#475569", padding: "2px 8px", borderRadius: "12px", border: "1px solid #cbd5e1" }}>
                          Unassigned
                        </span>
                      )}
                      {item.routingState === "LegacyUnconfirmed" && (
                        <span className="badge badge-danger" style={{ background: "#fee2e2", color: "#991b1b", padding: "2px 8px", borderRadius: "12px", border: "1px solid #fca5a5" }}>
                          ⚠️ Legacy Unconfirmed
                        </span>
                      )}
                      {!["InTransit", "WithHolder", "Unassigned", "LegacyUnconfirmed"].includes(item.routingState) && (
                        <span className="subtext">{item.routingState}</span>
                      )}
                    </td>
                    <td>
                      {item.physicalState === "ReturnPending" && (
                        <span className="badge badge-danger" style={{ background: "#fee2e2", color: "#991b1b", padding: "2px 8px", borderRadius: "12px", border: "1px solid #fca5a5" }}>
                          ⚠️ Return Pending
                        </span>
                      )}
                      {item.physicalState === "CustodyConfirmed" && (
                        <span className="badge" style={{ background: "#f0fdf4", color: "#166534", padding: "2px 8px", borderRadius: "12px", border: "1px solid #bbf7d0" }}>
                          📁 Custody Confirmed
                        </span>
                      )}
                      {item.physicalState === "AtRecordedLocation" && (
                        <span className="badge" style={{ background: "#f0fdf4", color: "#166534", padding: "2px 8px", borderRadius: "12px", border: "1px solid #bbf7d0" }}>
                          📁 At Recorded Location
                        </span>
                      )}
                      {(item.physicalState === "NotPresent" || item.physicalState === "Unknown") && (
                        <span className="subtext" style={{ color: "#64748b" }}>
                          {item.physicalState === "NotPresent" ? "Digital Only" : "Not Recorded"}
                        </span>
                      )}
                      {!["ReturnPending", "CustodyConfirmed", "AtRecordedLocation", "NotPresent", "Unknown"].includes(item.physicalState) && (
                        <span className="subtext">{item.physicalState}</span>
                      )}
                    </td>
                    <td>
                      <span className={`status-pill status-${item.status.toLowerCase()}`}>
                        {formatDakStatus(item.status)}
                      </span>
                    </td>
                    <td>
                      <span className="subtext" style={{ fontFamily: "monospace" }}>Rev {item.revision}</span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <Link to={`/dak/${item.id}`} className="secondary-button btn-xs">
                        Open Workspace ➔
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          <div className="pagination">
            <span>
              Showing {queueItems.length} of {totalCount} queue entries
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
