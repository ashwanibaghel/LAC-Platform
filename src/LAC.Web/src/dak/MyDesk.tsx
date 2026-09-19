import React, { useState, useEffect, useCallback } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { MyDeskItem, MyDeskDesk, MyDeskSummary, MyDeskResponse } from "./types";
import "./dak.css";

export const MyDesk: React.FC = () => {
  const navigate = useNavigate();

  const [items, setItems] = useState<MyDeskItem[]>([]);
  const [desks, setDesks] = useState<MyDeskDesk[]>([]);
  const [summary, setSummary] = useState<MyDeskSummary>({
    total: 0,
    immediate: 0,
    urgent: 0,
    overdue: 0,
    dueToday: 0,
    assignedToMe: 0,
    unallocated: 0,
  });
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(0);
  const [pageSize] = useState(25);

  // Filters
  const [selectedDeskId, setSelectedDeskId] = useState<string>("");
  const [qInput, setQInput] = useState("");
  const [searchQuery, setSearchQuery] = useState("");
  const [priorityFilter, setPriorityFilter] = useState<string>("all");
  const [dueFilter, setDueFilter] = useState<string>("all");
  const [handlerFilter, setHandlerFilter] = useState<string>("all");

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadMyDesk = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const params = new URLSearchParams();
      params.append("page", page.toString());
      params.append("pageSize", pageSize.toString());
      if (selectedDeskId) params.append("deskId", selectedDeskId);
      if (searchQuery.trim()) params.append("q", searchQuery.trim());
      if (priorityFilter !== "all") params.append("priority", priorityFilter);
      if (dueFilter !== "all") params.append("due", dueFilter);
      if (handlerFilter !== "all") params.append("handler", handlerFilter);

      const res = await fetch(`/api/dak/my-desk?${params.toString()}`, { credentials: "include" });
      if (!res.ok) {
        if (res.status === 403) throw new Error("Access denied: You do not have permission to view Dak.");
        if (res.status === 400) {
          const errData = (await res.json().catch(() => null)) as { message?: string } | null;
          throw new Error(errData?.message || "Invalid My Desk filter criteria.");
        }
        throw new Error("Failed to load My Desk records.");
      }

      const data = (await res.json()) as MyDeskResponse;
      setItems(data.items);
      setDesks(data.desks);
      setSummary(data.summary);
      setTotalCount(data.totalCount);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load My Desk records.");
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, selectedDeskId, searchQuery, priorityFilter, dueFilter, handlerFilter]);

  useEffect(() => {
    void loadMyDesk();
  }, [loadMyDesk]);

  const handleSearchSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(0);
    setSearchQuery(qInput);
  };

  const handleResetFilters = () => {
    setQInput("");
    setSearchQuery("");
    setPriorityFilter("all");
    setDueFilter("all");
    setHandlerFilter("all");
    setPage(0);
  };

  const handleCardClick = (filterType: "total" | "immediate" | "urgent" | "overdue" | "dueToday" | "assignedToMe" | "unallocated") => {
    setPage(0);
    switch (filterType) {
      case "total":
        handleResetFilters();
        break;
      case "immediate":
        setPriorityFilter(priorityFilter === "immediate" ? "all" : "immediate");
        break;
      case "urgent":
        setPriorityFilter(priorityFilter === "urgent" ? "all" : "urgent");
        break;
      case "overdue":
        setDueFilter(dueFilter === "overdue" ? "all" : "overdue");
        break;
      case "dueToday":
        setDueFilter(dueFilter === "today" ? "all" : "today");
        break;
      case "assignedToMe":
        setHandlerFilter(handlerFilter === "me" ? "all" : "me");
        break;
      case "unallocated":
        setHandlerFilter(handlerFilter === "unallocated" ? "all" : "unallocated");
        break;
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  const getPriorityBadgeClass = (priority: string) => {
    switch (priority) {
      case "Immediate":
        return "priority-badge priority-immediate";
      case "Urgent":
        return "priority-badge priority-urgent";
      default:
        return "priority-badge priority-routine";
    }
  };

  const getHandlerBadgeClass = (handlerState: string) => {
    switch (handlerState) {
      case "AssignedToMe":
        return "handler-badge handler-me";
      case "Unallocated":
        return "handler-badge handler-unallocated";
      default:
        return "handler-badge handler-other";
    }
  };

  return (
    <div className="my-desk-page">
      <div className="breadcrumbs">
        <Link to="/">Home</Link> <i>/</i> <span>My Desk</span>
      </div>

      <div className="page-header">
        <div>
          <div className="eyebrow">Operational Custody Queue</div>
          <h1>My Desk</h1>
          <p>Active institutional custody for your assigned office desks. Actionable inward Dak awaiting processing.</p>
        </div>
        <div className="header-actions">
          <button className="secondary-button" onClick={() => void loadMyDesk()} title="Refresh queue">
            ↻ Refresh Queue
          </button>
        </div>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {/* Summary Cards */}
      <div className="my-desk-summary-strip" role="region" aria-label="Queue Summary Metrics">
        <button
          type="button"
          className={`summary-card ${priorityFilter === "all" && dueFilter === "all" && handlerFilter === "all" && !searchQuery ? "active" : ""}`}
          onClick={() => handleCardClick("total")}
        >
          <div className="card-value">{summary.total}</div>
          <div className="card-label">Total In Custody</div>
        </button>
        <button
          type="button"
          className={`summary-card card-immediate ${priorityFilter === "immediate" ? "active" : ""}`}
          onClick={() => handleCardClick("immediate")}
        >
          <div className="card-value">{summary.immediate}</div>
          <div className="card-label">Immediate</div>
        </button>
        <button
          type="button"
          className={`summary-card card-urgent ${priorityFilter === "urgent" ? "active" : ""}`}
          onClick={() => handleCardClick("urgent")}
        >
          <div className="card-value">{summary.urgent}</div>
          <div className="card-label">Urgent</div>
        </button>
        <button
          type="button"
          className={`summary-card card-overdue ${dueFilter === "overdue" ? "active" : ""}`}
          onClick={() => handleCardClick("overdue")}
        >
          <div className="card-value">{summary.overdue}</div>
          <div className="card-label">Overdue</div>
        </button>
        <button
          type="button"
          className={`summary-card card-today ${dueFilter === "today" ? "active" : ""}`}
          onClick={() => handleCardClick("dueToday")}
        >
          <div className="card-value">{summary.dueToday}</div>
          <div className="card-label">Due Today</div>
        </button>
        <button
          type="button"
          className={`summary-card card-me ${handlerFilter === "me" ? "active" : ""}`}
          onClick={() => handleCardClick("assignedToMe")}
        >
          <div className="card-value">{summary.assignedToMe}</div>
          <div className="card-label">Assigned to Me</div>
        </button>
        <button
          type="button"
          className={`summary-card card-unallocated ${handlerFilter === "unallocated" ? "active" : ""}`}
          onClick={() => handleCardClick("unallocated")}
        >
          <div className="card-value">{summary.unallocated}</div>
          <div className="card-label">Unallocated</div>
        </button>
      </div>

      {/* Filter and Search Bar */}
      <div className="my-desk-filter-bar">
        <div className="filter-group desk-filter-group">
          <label htmlFor="desk-select">Desk:</label>
          <select
            id="desk-select"
            value={selectedDeskId}
            onChange={(e) => {
              setSelectedDeskId(e.target.value);
              setPage(0);
            }}
          >
            <option value="">All My Desks ({desks.length})</option>
            {desks.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name} [{d.code}]{d.isPrimary ? " ★ (Primary)" : ""}
              </option>
            ))}
          </select>
        </div>

        <div className="filter-group">
          <label htmlFor="handler-select">Handler:</label>
          <select
            id="handler-select"
            value={handlerFilter}
            onChange={(e) => {
              setHandlerFilter(e.target.value);
              setPage(0);
            }}
          >
            <option value="all">All Handlers</option>
            <option value="me">Assigned to Me</option>
            <option value="unallocated">Unallocated</option>
            <option value="others">Other Officers</option>
          </select>
        </div>

        <div className="filter-group">
          <label htmlFor="priority-select">Priority:</label>
          <select
            id="priority-select"
            value={priorityFilter}
            onChange={(e) => {
              setPriorityFilter(e.target.value);
              setPage(0);
            }}
          >
            <option value="all">All Priorities</option>
            <option value="routine">Routine</option>
            <option value="urgent">Urgent</option>
            <option value="immediate">Immediate</option>
          </select>
        </div>

        <div className="filter-group">
          <label htmlFor="due-select">Due Status:</label>
          <select
            id="due-select"
            value={dueFilter}
            onChange={(e) => {
              setDueFilter(e.target.value);
              setPage(0);
            }}
          >
            <option value="all">All Due Dates</option>
            <option value="overdue">Overdue</option>
            <option value="today">Due Today</option>
            <option value="upcoming">Upcoming</option>
            <option value="none">No Due Date</option>
          </select>
        </div>

        <form className="search-box" onSubmit={handleSearchSubmit}>
          <input
            type="text"
            placeholder="Search Diary #, Subject, Sender..."
            value={qInput}
            onChange={(e) => setQInput(e.target.value)}
          />
          <button type="submit" className="secondary-button">
            Search
          </button>
        </form>

        {(priorityFilter !== "all" || dueFilter !== "all" || handlerFilter !== "all" || searchQuery || selectedDeskId) && (
          <button type="button" className="text-button" onClick={() => { handleResetFilters(); setSelectedDeskId(""); }}>
            Reset
          </button>
        )}
      </div>

      {/* Table Section */}
      <div className="table-card">
        {loading ? (
          <div className="loading-state">Loading My Desk operational queue...</div>
        ) : items.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state-icon">🗂</div>
            <h3>No active Dak in queue</h3>
            <p>
              {totalCount === 0 && desks.length === 0
                ? "You do not have any active office desk memberships. Contact your administrator for desk assignment."
                : "No items match your active desk custody and filter criteria."}
            </p>
          </div>
        ) : (
          <div className="table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Diary Number</th>
                  <th>Received Date</th>
                  <th>Subject & Sender</th>
                  <th>Priority</th>
                  <th>Due Date</th>
                  <th>Current Desk</th>
                  <th>Handler</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr
                    key={item.id}
                    className="clickable-row"
                    onClick={() => navigate(`/dak/${item.id}`)}
                    title="Click to view Dak details and workflow actions"
                  >
                    <td>
                      <strong className="diary-num-link">{item.diaryNumber}</strong>
                    </td>
                    <td>{item.receivedDate}</td>
                    <td>
                      <div className="subject-line">{item.subject}</div>
                      <div className="sender-subtext">
                        {item.senderName}
                        {item.senderDepartment ? ` (${item.senderDepartment})` : ""}
                      </div>
                    </td>
                    <td>
                      <span className={getPriorityBadgeClass(item.priority)}>{item.priority}</span>
                    </td>
                    <td>
                      {item.dueDate ? (
                        <span>
                          {item.dueDate}
                        </span>
                      ) : (
                        <span className="muted-dash">—</span>
                      )}
                    </td>
                    <td>
                      <div className="desk-code-badge">{item.assignment.deskCode}</div>
                      <div className="desk-name-subtext">{item.assignment.deskName}</div>
                    </td>
                    <td>
                      <span className={getHandlerBadgeClass(item.assignment.handlerState)}>
                        {item.assignment.handlerState === "AssignedToMe"
                          ? "Assigned to Me"
                          : item.assignment.handlerState === "Unallocated"
                          ? "Unallocated"
                          : item.assignment.assignedUserDisplayName || "Other Officer"}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="pagination-bar">
            <div className="pagination-info">
              Showing {page * pageSize + 1} to {Math.min((page + 1) * pageSize, totalCount)} of {totalCount} records
            </div>
            <div className="pagination-buttons">
              <button
                className="secondary-button"
                disabled={page === 0}
                onClick={() => setPage((p) => Math.max(0, p - 1))}
              >
                Previous
              </button>
              <span className="page-indicator">
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
    </div>
  );
};
