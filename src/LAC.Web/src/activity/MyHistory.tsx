import { useEffect, useState, useCallback } from 'react';
import { Link } from 'react-router-dom';
import type { ActivityFeedResult, ActivityItemDto } from './types';
import './activity.css';

export function MyHistory() {
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

  const fetchHistory = useCallback(
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
        const res = await fetch(`/api/activity/my-history?${params.toString()}`, {
          credentials: 'include',
          signal,
        });

        if (!res.ok) {
          if (res.status === 401) throw new Error('Please sign in to view your history.');
          if (res.status === 403) throw new Error('Access denied: account inactive or unauthorized.');
          throw new Error(`Failed to load history (${res.status})`);
        }

        const json: ActivityFeedResult = await res.json();
        setData(json);
      } catch (err: any) {
        if (err.name !== 'AbortError') {
          setError(err.message || 'An error occurred loading your history.');
        }
      } finally {
        setLoading(false);
      }
    },
    [search, entityType, action, dateFrom, dateTo, includeReads, page]
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchHistory(controller.signal);
    return () => controller.abort();
  }, [fetchHistory]);

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
          <h1 className="activity-title">My History</h1>
          <p className="activity-subtitle">
            Immutable accountability record of actions and document reads performed by your account.
          </p>
        </div>
      </div>

      {error && <div className="activity-error-banner" role="alert">{error}</div>}

      <div className="activity-filters-card">
        <div className="activity-filters-grid">
          <div className="filter-group">
            <label htmlFor="filter-search">Search Keyword</label>
            <input
              id="filter-search"
              type="text"
              className="filter-input"
              placeholder="Title, diary number, remarks..."
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div className="filter-group">
            <label htmlFor="filter-entity">Record Type</label>
            <select
              id="filter-entity"
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
            <label htmlFor="filter-action">Action Name</label>
            <input
              id="filter-action"
              type="text"
              className="filter-input"
              placeholder="e.g. Created, Dispatched..."
              value={action}
              onChange={(e) => {
                setAction(e.target.value);
                setPage(1);
              }}
            />
          </div>

          <div className="filter-group">
            <label htmlFor="filter-date-from">From Date</label>
            <input
              id="filter-date-from"
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
            <label htmlFor="filter-date-to">To Date</label>
            <input
              id="filter-date-to"
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
              Include Document Access Reads
            </label>
          </div>
        </div>
      </div>

      <div className="activity-meta-summary">
        {loading ? (
          <span>Loading activity history...</span>
        ) : (
          <span>
            Showing {data?.items.length ?? 0} of {data?.totalCount ?? 0} historical events
          </span>
        )}
      </div>

      {!loading && data?.items.length === 0 && (
        <div className="activity-empty-state">
          <div className="activity-empty-icon">📜</div>
          <h3>No activity records found</h3>
          <p>You have no logged events matching the selected filter criteria.</p>
        </div>
      )}

      <div className="activity-list">
        {data?.items.map((item) => (
          <div key={`${item.sourceType}-${item.eventId}`} className={`activity-card ${getSourceClass(item)}`}>
            <div className="activity-main">
              <div className="activity-badges-row">
                <span className="badge badge-source">{item.sourceType}</span>
                {item.isReadEvent ? (
                  <span className="badge badge-read">Read / Access</span>
                ) : (
                  <span className="badge badge-actor">Action: {item.action}</span>
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
