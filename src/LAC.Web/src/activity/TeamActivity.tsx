import { useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import type { ActivityFeedResult, ActivityItemDto } from './types';
import './activity.css';

export function TeamActivity() {
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<ActivityFeedResult | null>(null);

  // Filters state
  const [search, setSearch] = useState<string>('');
  const [entityType, setEntityType] = useState<string>('');
  const [action, setAction] = useState<string>('');
  const [dateFrom, setDateFrom] = useState<string>('');
  const [dateTo, setDateTo] = useState<string>('');
  const [includeReads, setIncludeReads] = useState<boolean>(true);
  const [page, setPage] = useState<number>(1);
  const pageSize = 20;

  const fetchTeamActivity = useCallback(
    async (signal?: AbortSignal) => {
      setLoading(true);
      setError(null);

      const params = new URLSearchParams();
      if (search.trim()) params.set('search', search.trim());
      if (entityType) params.set('entityType', entityType);
      if (action.trim()) params.set('action', action.trim());
      if (dateFrom) params.set('dateFrom', new Date(dateFrom).toISOString());
      if (dateTo) params.set('dateTo', new Date(dateTo).toISOString());
      params.set('includeReads', includeReads.toString());
      params.set('page', page.toString());
      params.set('pageSize', pageSize.toString());

      try {
        const res = await fetch(`/api/activity/team?${params.toString()}`, {
          credentials: 'include',
          signal,
        });

        if (!res.ok) {
          if (res.status === 401) throw new Error('Please sign in to view team activity.');
          if (res.status === 403) throw new Error('Access denied: You require Audit.View permission with operational area scope.');
          throw new Error(`Failed to load team activity (${res.status})`);
        }

        const json: ActivityFeedResult = await res.json();
        setData(json);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'An error occurred loading team activity.');
        }
      } finally {
        setLoading(false);
      }
    },
    [search, entityType, action, dateFrom, dateTo, includeReads, page]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchTeamActivity(controller.signal);
    return () => controller.abort();
  }, [fetchTeamActivity]);

  const totalPages = data ? Math.ceil(data.totalCount / pageSize) : 1;

  const formatTime = (dateStr: string) => {
    try {
      const d = new Date(dateStr);
      return d.toLocaleString(undefined, {
        dateStyle: 'medium',
        timeStyle: 'short',
      });
    } catch {
      return dateStr;
    }
  };

  const getSourceClass = (item: ActivityItemDto) => {
    if (item.isReadEvent) return 'source-read';
    switch (item.sourceType) {
      case 'Dak':
        return 'source-dak';
      case 'Matter':
        return 'source-matter';
      case 'Outward':
        return 'source-outward';
      case 'WorkItem':
        return 'source-workitem';
      default:
        return '';
    }
  };

  return (
    <div className="activity-container">
      <div className="activity-header">
        <div>
          <h1 className="activity-title">Team Activity</h1>
          <p className="activity-subtitle">
            Unified mutation timeline and document access oversight across your authorized operational jurisdiction.
          </p>
        </div>
      </div>

      {error && <div className="activity-error-banner" role="alert">{error}</div>}

      <div className="activity-filters-card">
        <div className="activity-filters-grid">
          <div className="filter-group">
            <label htmlFor="team-filter-search">Search Keyword</label>
            <input
              id="team-filter-search"
              type="text"
              className="filter-input"
              placeholder="Officer name, title, remarks..."
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div className="filter-group">
            <label htmlFor="team-filter-entity">Record Type</label>
            <select
              id="team-filter-entity"
              className="filter-select"
              value={entityType}
              onChange={(e) => {
                setEntityType(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All Types</option>
              <option value="Dak">Dak / Correspondence</option>
              <option value="Matter">Legal Matter</option>
              <option value="Outward">Outward / Dispatch</option>
              <option value="WorkItem">Work Item</option>
              <option value="Document">Document Reads</option>
            </select>
          </div>

          <div className="filter-group">
            <label htmlFor="team-filter-action">Action Name</label>
            <input
              id="team-filter-action"
              type="text"
              className="filter-input"
              placeholder="e.g. Created, Reassigned..."
              value={action}
              onChange={(e) => {
                setAction(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div className="filter-group">
            <label htmlFor="team-filter-date-from">From Date</label>
            <input
              id="team-filter-date-from"
              type="date"
              className="filter-input"
              value={dateFrom}
              onChange={(e) => {
                setDateFrom(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div className="filter-group">
            <label htmlFor="team-filter-date-to">To Date</label>
            <input
              id="team-filter-date-to"
              type="date"
              className="filter-input"
              value={dateTo}
              onChange={(e) => {
                setDateTo(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div>
            <label className="filter-checkbox-label">
              <input
                type="checkbox"
                checked={includeReads}
                onChange={(e) => {
                  setIncludeReads(e.target.checked);
                  setPage(1);
                }}
              />
              Include Document Reads
            </label>
          </div>
        </div>
      </div>

      <div className="activity-meta-summary">
        {loading ? (
          <span>Loading team activity...</span>
        ) : (
          <span>
            Showing {data?.items.length ?? 0} of {data?.totalCount ?? 0} operational events
          </span>
        )}
      </div>

      {!loading && data?.items.length === 0 && (
        <div className="activity-empty-state">
          <div className="activity-empty-icon">👥</div>
          <h3>No team activity records found</h3>
          <p>There are no operational events matching the filter criteria within your authorized jurisdiction.</p>
        </div>
      )}

      <div className="activity-list">
        {data?.items.map((item) => (
          <div key={`${item.sourceType}-${item.eventId}`} className={`activity-card ${getSourceClass(item)}`}>
            <div className="activity-main">
              <div className="activity-badges-row">
                <span className="badge badge-source">{item.sourceType}</span>
                <span className="badge badge-actor">By: {item.actorDisplayName}</span>
                {item.isReadEvent ? (
                  <span className="badge badge-read">Read / Access</span>
                ) : (
                  <span className="badge badge-source">Action: {item.action}</span>
                )}
                {item.workstreamName && <span className="badge badge-workstream">{item.workstreamName}</span>}
                {item.deskName && <span className="badge badge-desk">{item.deskName}</span>}
              </div>

              <div className="activity-summary-text">{item.summary}</div>

              <div className="activity-entity-title">
                <span>{item.entityTitle}</span>
                {item.entityReferenceNumber && (
                  <span className="activity-ref-number">{item.entityReferenceNumber}</span>
                )}
              </div>
            </div>

            <div className="activity-time-col">
              <span className="activity-timestamp">{formatTime(item.occurredAt)}</span>
              {item.canOpen && item.navigationUrl ? (
                <Link to={item.navigationUrl} className="btn-open-record">
                  Open Record →
                </Link>
              ) : (
                <span className="badge-restricted" title="Record access restricted or archived">
                  🔒 Restricted
                </span>
              )}
            </div>
          </div>
        ))}
      </div>

      {data && data.totalCount > pageSize && (
        <div className="activity-pagination">
          <button
            type="button"
            className="btn-page"
            disabled={page <= 1 || loading}
            onClick={() => setPage((p) => Math.max(1, p - 1))}
          >
            ← Previous
          </button>
          <span>
            Page {page} of {Math.max(1, totalPages)}
          </span>
          <button
            type="button"
            className="btn-page"
            disabled={page >= totalPages || loading}
            onClick={() => setPage((p) => p + 1)}
          >
            Next →
          </button>
        </div>
      )}
    </div>
  );
}
