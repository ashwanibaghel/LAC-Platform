import { useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import type {
  MyWorkResponse,
  WorkItemPriority,
  WorkItemStatus,
  WorkItemDueFilter,
  WorkItemRelationshipFilter,
  WorkstreamOption
} from './types';
import './work.css';

interface MyWorkProps {
  currentUserPermissions?: string[];
}

export function MyWork({ currentUserPermissions }: MyWorkProps) {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<MyWorkResponse | null>(null);

  // Filters state
  const [q, setQ] = useState<string>('');
  const [workstreamId, setWorkstreamId] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [due, setDue] = useState<WorkItemDueFilter>('all');
  const [relationship, setRelationship] = useState<WorkItemRelationshipFilter>('all');
  const [page, setPage] = useState<number>(0);
  const pageSize = 25;

  // Workstreams lookup for filter
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);

  const hasPermission = (code: string) => {
    return currentUserPermissions ? currentUserPermissions.includes(code) : true;
  };

  const canCreate = hasPermission('WorkItem.Create');

  // Load available workstreams
  useEffect(() => {
    fetch('/api/work-items/create-context', { credentials: 'include' })
      .then((res) => (res.ok ? res.json() : null))
      .then((resData) => {
        if (resData?.workstreams) {
          setWorkstreams(resData.workstreams);
        }
      })
      .catch(() => {});
  }, []);

  const fetchWorkItems = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      const params = new URLSearchParams();
      if (q.trim()) params.set('q', q.trim());
      if (workstreamId) params.set('workstreamId', workstreamId);
      if (status) params.set('status', status);
      if (due !== 'all') params.set('due', due);
      if (relationship !== 'all') params.set('relationship', relationship);
      params.set('page', page.toString());
      params.set('pageSize', pageSize.toString());

      try {
        const res = await fetch(`/api/work-items/my-work?${params.toString()}`, {
          credentials: 'include',
          signal
        });

        if (!res.ok) {
          if (res.status === 401) {
            throw new Error('Please sign in to view your work items.');
          }
          if (res.status === 403) {
            throw new Error('Access denied: You do not have permission to view work items.');
          }
          throw new Error(`Failed to load work items (${res.status})`);
        }

        const json: MyWorkResponse = await res.json();
        setData(json);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'An error occurred loading work items.');
        }
      } finally {
        setLoading(false);
      }
    },
    [q, workstreamId, status, due, relationship, page]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchWorkItems(controller.signal);
    return () => controller.abort();
  }, [fetchWorkItems]);

  const handleClearFilters = () => {
    setQ('');
    setWorkstreamId('');
    setStatus('');
    setDue('all');
    setRelationship('all');
    setPage(0);
  };

  const handleSummaryCardClick = (cardType: 'overdue' | 'today' | 'week' | 'assigned' | 'requested') => {
    setPage(0);
    if (cardType === 'overdue') {
      setDue(due === 'overdue' ? 'all' : 'overdue');
    } else if (cardType === 'today') {
      setDue(due === 'today' ? 'all' : 'today');
    } else if (cardType === 'week') {
      setDue(due === 'week' ? 'all' : 'week');
    } else if (cardType === 'assigned') {
      setRelationship(relationship === 'assigned' ? 'all' : 'assigned');
    } else if (cardType === 'requested') {
      setRelationship(relationship === 'requested' ? 'all' : 'requested');
    }
  };

  const formatDateTime = (isoString?: string | null) => {
    if (!isoString) return '—';
    try {
      const d = new Date(isoString);
      return d.toLocaleDateString('en-IN', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    } catch {
      return isoString;
    }
  };

  const formatRelativeActivity = (isoString: string) => {
    try {
      const date = new Date(isoString);
      const now = new Date();
      const diffMs = now.getTime() - date.getTime();
      const diffMins = Math.floor(diffMs / 60000);
      const diffHours = Math.floor(diffMins / 60);
      const diffDays = Math.floor(diffHours / 24);

      if (diffMins < 2) return 'Just now';
      if (diffMins < 60) return `${diffMins}m ago`;
      if (diffHours < 24) return `${diffHours}h ago`;
      if (diffDays === 1) return 'Yesterday';
      if (diffDays < 7) return `${diffDays}d ago`;
      return date.toLocaleDateString('en-IN', { day: '2-digit', month: 'short' });
    } catch {
      return isoString;
    }
  };

  const renderPriorityBadge = (priority: WorkItemPriority) => {
    const cls =
      priority === 'Immediate'
        ? 'badge badge-immediate'
        : priority === 'Urgent'
        ? 'badge badge-urgent'
        : 'badge badge-routine';
    return <span className={cls}>{priority}</span>;
  };

  const renderStatusBadge = (itemStatus: WorkItemStatus) => {
    let cls = 'badge badge-assigned';
    if (itemStatus === 'InProgress') cls = 'badge badge-inprogress';
    else if (itemStatus === 'SubmittedForReview') cls = 'badge badge-submitted';
    else if (itemStatus === 'ReturnedForCorrection') cls = 'badge badge-returned';
    else if (itemStatus === 'Completed') cls = 'badge badge-completed';
    else if (itemStatus === 'Cancelled') cls = 'badge badge-cancelled';

    return <span className={cls}>{itemStatus}</span>;
  };

  const summary = data?.summary || {
    totalOpen: 0,
    assignedToMe: 0,
    requestedByMe: 0,
    overdue: 0,
    dueToday: 0,
    dueThisWeek: 0,
    needsReview: 0
  };

  return (
    <div className="my-work-container">
      {/* Top Header */}
      <div className="my-work-header">
        <div className="my-work-title-group">
          <h1>My Work</h1>
          <p className="my-work-subtitle">
            Actionable official tasks, responsibility assignments, and progress tracking
          </p>
        </div>
        <div className="my-work-actions">
          {canCreate && (
            <Link to="/work/new" className="button primary-button" style={{ textDecoration: 'none' }}>
              + Assign Work
            </Link>
          )}
        </div>
      </div>

      {/* Summary Cards */}
      <div className="my-work-summary-grid">
        <div
          className={`my-work-summary-card card-overdue ${due === 'overdue' ? 'active' : ''}`}
          onClick={() => handleSummaryCardClick('overdue')}
          role="button"
          tabIndex={0}
          title="Filter by Overdue work"
        >
          <div className="summary-card-count">{summary.overdue}</div>
          <div className="summary-card-label">Overdue</div>
        </div>

        <div
          className={`my-work-summary-card card-today ${due === 'today' ? 'active' : ''}`}
          onClick={() => handleSummaryCardClick('today')}
          role="button"
          tabIndex={0}
          title="Filter by items Due Today"
        >
          <div className="summary-card-count">{summary.dueToday}</div>
          <div className="summary-card-label">Due Today</div>
        </div>

        <div
          className={`my-work-summary-card ${due === 'week' ? 'active' : ''}`}
          onClick={() => handleSummaryCardClick('week')}
          role="button"
          tabIndex={0}
          title="Filter by items Due This Week"
        >
          <div className="summary-card-count">{summary.dueThisWeek}</div>
          <div className="summary-card-label">This Week</div>
        </div>

        <div
          className={`my-work-summary-card ${relationship === 'assigned' ? 'active' : ''}`}
          onClick={() => handleSummaryCardClick('assigned')}
          role="button"
          tabIndex={0}
          title="Filter by Assigned to Me or My Desk"
        >
          <div className="summary-card-count">{summary.assignedToMe}</div>
          <div className="summary-card-label">Assigned to Me</div>
        </div>

        <div
          className={`my-work-summary-card ${relationship === 'requested' ? 'active' : ''}`}
          onClick={() => handleSummaryCardClick('requested')}
          role="button"
          tabIndex={0}
          title="Filter by Requested by Me"
        >
          <div className="summary-card-count">{summary.requestedByMe}</div>
          <div className="summary-card-label">Requested by Me</div>
        </div>
      </div>

      {/* Filter Bar */}
      <div className="my-work-filter-bar">
        <div className="filter-row-primary">
          <div className="work-search-box">
            <span className="work-search-icon">🔍</span>
            <input
              type="text"
              placeholder="Search by title, instructions, requester..."
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(0);
              }}
            />
          </div>

          <select
            className="filter-select"
            value={workstreamId}
            onChange={(e) => {
              setWorkstreamId(e.target.value);
              setPage(0);
            }}
            aria-label="Filter by workstream"
          >
            <option value="">All Workstreams</option>
            {workstreams.map((ws) => (
              <option key={ws.id} value={ws.id}>
                {ws.name}
              </option>
            ))}
          </select>

          <select
            className="filter-select"
            value={status}
            onChange={(e) => {
              setStatus(e.target.value);
              setPage(0);
            }}
            aria-label="Filter by status"
          >
            <option value="">Open Items (Default)</option>
            <option value="all">All Statuses</option>
            <option value="Assigned">Assigned</option>
            <option value="InProgress">In Progress</option>
            <option value="SubmittedForReview">Submitted for Review</option>
            <option value="ReturnedForCorrection">Returned for Correction</option>
            <option value="Completed">Completed</option>
            <option value="Cancelled">Cancelled</option>
          </select>

          {(q || workstreamId || status || due !== 'all' || relationship !== 'all') && (
            <button className="clear-filter-btn" onClick={handleClearFilters}>
              Reset Filters
            </button>
          )}
        </div>

        <div className="filter-pill-group">
          <span style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 600 }}>Due:</span>
          {(['all', 'overdue', 'today', 'week', 'upcoming', 'none'] as WorkItemDueFilter[]).map((d) => (
            <button
              key={d}
              className={`filter-pill ${due === d ? 'active' : ''}`}
              onClick={() => {
                setDue(d);
                setPage(0);
              }}
            >
              {d === 'all'
                ? 'All Due'
                : d === 'today'
                ? 'Due Today'
                : d === 'week'
                ? 'This Week'
                : d === 'upcoming'
                ? 'Upcoming'
                : d === 'none'
                ? 'No Due Date'
                : 'Overdue'}
            </button>
          ))}

          <span style={{ fontSize: '0.8rem', color: '#64748b', fontWeight: 600, marginLeft: 10 }}>
            Role:
          </span>
          {(['all', 'assigned', 'requested'] as WorkItemRelationshipFilter[]).map((r) => (
            <button
              key={r}
              className={`filter-pill ${relationship === r ? 'active' : ''}`}
              onClick={() => {
                setRelationship(r);
                setPage(0);
              }}
            >
              {r === 'all' ? 'All Roles' : r === 'assigned' ? 'Assigned' : 'Requested'}
            </button>
          ))}
        </div>
      </div>

      {/* Main Content Area */}
      {loading ? (
        <div style={{ textAlign: 'center', padding: '40px 0', color: '#64748b' }}>
          Loading work items...
        </div>
      ) : error ? (
        <div
          style={{
            background: '#fef2f2',
            border: '1px solid #fecaca',
            color: '#b91c1c',
            padding: '16px 20px',
            borderRadius: '8px'
          }}
        >
          {error}
        </div>
      ) : data?.items.length === 0 ? (
        <div className="my-work-empty-state">
          <h3 className="my-work-empty-title">You're clear for now</h3>
          <p className="my-work-empty-subtitle">
            No active work items match your current selection or filters.
          </p>
        </div>
      ) : (
        <div className="work-items-list">
          {data?.items.map((item) => (
            <Link
              key={item.id}
              to={`/work/${item.id}`}
              className={`work-item-card ${
                item.dueState === 'overdue'
                  ? 'card-border-overdue'
                  : item.dueState === 'today'
                  ? 'card-border-today'
                  : ''
              }`}
            >
              <div className="work-card-top-row">
                <h2 className="work-card-title">{item.title}</h2>
                <div className="work-card-badges">
                  {renderPriorityBadge(item.priority)}
                  {renderStatusBadge(item.status)}
                </div>
              </div>

              <div className="work-card-meta-row">
                <div className="work-card-actors">
                  <div className="work-card-actor-item">
                    <span className="work-card-actor-label">Desk:</span>
                    <span className="work-card-actor-val">{item.officeDeskName}</span>
                    {item.assignedUserDisplayName && (
                      <span style={{ color: '#475569' }}>({item.assignedUserDisplayName})</span>
                    )}
                  </div>

                  <div className="work-card-actor-item">
                    <span className="work-card-actor-label">By:</span>
                    <span className="work-card-actor-val">{item.requestedByDisplayName}</span>
                  </div>

                  {item.dueAt && (
                    <div className="work-card-actor-item">
                      <span className="work-card-actor-label">Due:</span>
                      <span
                        className={
                          item.dueState === 'overdue'
                            ? 'badge-overdue'
                            : item.dueState === 'today'
                            ? 'badge-today'
                            : 'work-card-actor-val'
                        }
                      >
                        {formatDateTime(item.dueAt)}
                      </span>
                    </div>
                  )}
                </div>

                <div className="work-card-tags">
                  <span className="work-tag-workstream">{item.workstreamName}</span>
                  {item.hasLinkedMatter && (
                    <span
                      className="work-tag-context"
                      title={item.linkedMatterTitle ? `Matter: ${item.linkedMatterTitle}` : 'Linked Matter'}
                    >
                      ⚖ {item.linkedMatterTitle || 'Matter'}
                    </span>
                  )}
                  {item.hasLinkedDak && (
                    <span
                      className="work-tag-context"
                      title={item.linkedDakSubject ? `Dak: ${item.linkedDakSubject}` : 'Linked Dak'}
                    >
                      📥 {item.linkedDakSubject ? item.linkedDakSubject.slice(0, 30) + '...' : 'Dak'}
                    </span>
                  )}
                  <span style={{ fontSize: '0.75rem', color: '#94a3b8' }}>
                    {formatRelativeActivity(item.lastActivityAt)}
                  </span>
                </div>
              </div>
            </Link>
          ))}

          {/* Pagination */}
          {data && data.totalCount > pageSize && (
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                padding: '12px 4px',
                marginTop: '10px'
              }}
            >
              <div style={{ fontSize: '0.85rem', color: '#64748b' }}>
                Showing {page * pageSize + 1}–
                {Math.min((page + 1) * pageSize, data.totalCount)} of {data.totalCount} items
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button
                  className="button secondary-button"
                  disabled={page === 0}
                  onClick={() => setPage((p) => Math.max(0, p - 1))}
                >
                  Previous
                </button>
                <button
                  className="button secondary-button"
                  disabled={(page + 1) * pageSize >= data.totalCount}
                  onClick={() => setPage((p) => p + 1)}
                >
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
