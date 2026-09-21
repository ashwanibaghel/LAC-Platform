import { useEffect, useState, useCallback } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import type {
  AttentionFeedResponse,
  AttentionItem,
  ScheduleOptionsResponse
} from './types';
import './attention.css';

export function MyAttention() {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<AttentionFeedResponse | null>(null);

  // Filters
  const [bucket, setBucket] = useState<string>('');
  const [sourceType, setSourceType] = useState<string>('');
  const [workstreamId, setWorkstreamId] = useState<string>('');
  const [deskId, setDeskId] = useState<string>('');
  const [priority, setPriority] = useState<string>('');
  const [q, setQ] = useState<string>('');
  const [page, setPage] = useState<number>(1);
  const pageSize = 25;

  // Modals state
  const [activeModal, setActiveModal] = useState<
    'reschedule' | 'reassign' | 'cancel' | 'complete' | 'reminder' | null
  >(null);
  const [selectedItem, setSelectedItem] = useState<AttentionItem | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Reschedule form
  const [rescheduleDate, setRescheduleDate] = useState('');
  const [rescheduleTime, setRescheduleTime] = useState('');
  const [rescheduleReason, setRescheduleReason] = useState('');

  // Reassign form & options
  const [options, setOptions] = useState<ScheduleOptionsResponse | null>(null);
  const [targetDeskId, setTargetDeskId] = useState('');
  const [targetUserId, setTargetUserId] = useState('');
  const [reassignReason, setReassignReason] = useState('');

  // Cancel form
  const [cancelReason, setCancelReason] = useState('');

  // Complete form
  const [completionNotes, setCompletionNotes] = useState('');

  // Reminder form
  const [reminderDaysBefore, setReminderDaysBefore] = useState<number>(1);
  const [reminderNote, setReminderNote] = useState('');

  const fetchAttentionFeed = useCallback(async () => {
    setLoading(true);
    setError(null);

    const params = new URLSearchParams();
    if (bucket) params.set('bucket', bucket);
    if (sourceType) params.set('sourceType', sourceType);
    if (workstreamId) params.set('workstreamId', workstreamId);
    if (deskId) params.set('deskId', deskId);
    if (priority) params.set('priority', priority);
    if (q.trim()) params.set('q', q.trim());
    params.set('page', page.toString());
    params.set('pageSize', pageSize.toString());

    try {
      const res = await fetch(`/api/attention/my?${params.toString()}`, {
        credentials: 'include'
      });

      if (!res.ok) {
        if (res.status === 401) throw new Error('Please sign in to view attention feed.');
        if (res.status === 403) throw new Error('Access denied: You do not have permission to view schedule.');
        throw new Error(`Failed to load attention feed (${res.status})`);
      }

      const json: AttentionFeedResponse = await res.json();
      setData(json);
    } catch (err: any) {
      setError(err.message || 'Failed to load attention feed.');
    } finally {
      setLoading(false);
    }
  }, [bucket, sourceType, workstreamId, deskId, priority, q, page]);

  useEffect(() => {
    fetchAttentionFeed();
  }, [fetchAttentionFeed]);

  // Load options for reassign modal if opened
  useEffect(() => {
    if (activeModal === 'reassign' && !options) {
      fetch('/api/scheduled-events/options', { credentials: 'include' })
        .then((res) => (res.ok ? res.json() : null))
        .then((data) => {
          if (data) setOptions(data);
        })
        .catch(() => {});
    }
  }, [activeModal, options]);

  // Open modals
  const openRescheduleModal = (item: AttentionItem) => {
    setSelectedItem(item);
    setRescheduleDate(item.scheduledDate || '');
    setRescheduleTime(item.scheduledTime || '');
    setRescheduleReason('');
    setActionError(null);
    setActiveModal('reschedule');
  };

  const openReassignModal = (item: AttentionItem) => {
    setSelectedItem(item);
    setTargetDeskId(item.responsibleDeskId || '');
    setTargetUserId(item.assignedUserId || '');
    setReassignReason('');
    setActionError(null);
    setActiveModal('reassign');
  };

  const openCancelModal = (item: AttentionItem) => {
    setSelectedItem(item);
    setCancelReason('');
    setActionError(null);
    setActiveModal('cancel');
  };

  const openCompleteModal = (item: AttentionItem) => {
    setSelectedItem(item);
    setCompletionNotes('');
    setActionError(null);
    setActiveModal('complete');
  };

  const openReminderModal = (item: AttentionItem) => {
    setSelectedItem(item);
    setReminderDaysBefore(1);
    setReminderNote('');
    setActionError(null);
    setActiveModal('reminder');
  };

  const closeModal = () => {
    setActiveModal(null);
    setSelectedItem(null);
    setActionError(null);
  };

  // Submit Actions
  const handleRescheduleSubmit = async () => {
    if (!selectedItem) return;
    if (!rescheduleDate) {
      setActionError('Scheduled date is required.');
      return;
    }
    setSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${selectedItem.id}/reschedule`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          newScheduledDate: rescheduleDate,
          newScheduledTime: rescheduleTime || null,
          reason: rescheduleReason.trim() || null,
          expectedRevision: selectedItem.revision ?? 1
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || errJson.error || `Reschedule failed (${res.status})`);
      }

      closeModal();
      fetchAttentionFeed();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleReassignSubmit = async () => {
    if (!selectedItem) return;
    setSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${selectedItem.id}/reassign`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          targetDeskId: targetDeskId || null,
          targetUserId: targetUserId || null,
          reason: reassignReason.trim() || null,
          expectedRevision: selectedItem.revision ?? 1
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || errJson.error || `Reassign failed (${res.status})`);
      }

      closeModal();
      fetchAttentionFeed();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCompleteSubmit = async () => {
    if (!selectedItem) return;
    setSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${selectedItem.id}/complete`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          notes: completionNotes.trim() || null,
          expectedRevision: selectedItem.revision ?? 1
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || errJson.error || `Completion failed (${res.status})`);
      }

      closeModal();
      fetchAttentionFeed();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleCancelSubmit = async () => {
    if (!selectedItem) return;
    if (!cancelReason.trim()) {
      setActionError('Cancellation reason is mandatory.');
      return;
    }
    setSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${selectedItem.id}/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          reason: cancelReason.trim(),
          expectedRevision: selectedItem.revision ?? 1
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || errJson.error || `Cancellation failed (${res.status})`);
      }

      closeModal();
      fetchAttentionFeed();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const handleAddReminderSubmit = async () => {
    if (!selectedItem) return;
    setSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${selectedItem.id}/reminders`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          daysBefore: reminderDaysBefore,
          reminderTime: null,
          expectedRevision: selectedItem.revision ?? 1
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || errJson.error || `Add reminder failed (${res.status})`);
      }

      closeModal();
      fetchAttentionFeed();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const summary = {
    overdueCount: data?.summary ? (data.summary.overdue ?? data.summary.overdueCount ?? 0) : 0,
    todayCount: data?.summary ? (data.summary.today ?? data.summary.todayCount ?? 0) : 0,
    tomorrowCount: data?.summary ? (data.summary.tomorrow ?? data.summary.tomorrowCount ?? 0) : 0,
    next7DaysCount: data?.summary ? (data.summary.next7Days ?? data.summary.next7DaysCount ?? 0) : 0,
    reminderActiveCount: data?.summary ? (data.summary.reminderActive ?? data.summary.reminderActiveCount ?? 0) : 0,
    needsRoutingCount: data?.summary ? (data.summary.needsRouting ?? data.summary.needsRoutingCount ?? 0) : 0
  };

  const selectedDeskMembers = options?.desks.find((d) => d.id === targetDeskId)?.members || [];

  return (
    <div className="attention-container">
      {/* Header */}
      <header className="attention-header">
        <div className="attention-title-group">
          <h1>Unified Time & Attention</h1>
          <p className="attention-subtitle">
            What needs attention now, what is due next, and upcoming official deadlines.
          </p>
        </div>
        <div className="attention-header-actions">
          <button className="btn-sm btn-primary" onClick={() => navigate('/calendar')}>
            📅 Open Calendar
          </button>
          <button className="btn-sm" onClick={fetchAttentionFeed}>
            🔄 Refresh
          </button>
        </div>
      </header>

      {/* Summary Cards */}
      <section className="attention-summary-grid" aria-label="Attention Overview">
        <div
          className={`attention-card card-overdue ${bucket === 'Overdue' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'Overdue' ? '' : 'Overdue');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Overdue</span>
          <span className="attention-card-value">{summary.overdueCount}</span>
          <span className="attention-card-sub">Requires immediate action</span>
        </div>

        <div
          className={`attention-card card-today ${bucket === 'Today' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'Today' ? '' : 'Today');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Today</span>
          <span className="attention-card-value">{summary.todayCount}</span>
          <span className="attention-card-sub">Due by end of day</span>
        </div>

        <div
          className={`attention-card card-tomorrow ${bucket === 'Tomorrow' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'Tomorrow' ? '' : 'Tomorrow');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Tomorrow</span>
          <span className="attention-card-value">{summary.tomorrowCount}</span>
          <span className="attention-card-sub">Prepare ahead</span>
        </div>

        <div
          className={`attention-card card-next7days ${bucket === 'Next7Days' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'Next7Days' ? '' : 'Next7Days');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Next 7 Days</span>
          <span className="attention-card-value">{summary.next7DaysCount}</span>
          <span className="attention-card-sub">Upcoming week</span>
        </div>

        <div
          className={`attention-card card-reminders ${bucket === 'ReminderActive' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'ReminderActive' ? '' : 'ReminderActive');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Reminders Active</span>
          <span className="attention-card-value">{summary.reminderActiveCount}</span>
          <span className="attention-card-sub">Early attention triggered</span>
        </div>

        <div
          className={`attention-card card-routing ${bucket === 'NeedsRouting' ? 'active' : ''}`}
          onClick={() => {
            setBucket(bucket === 'NeedsRouting' ? '' : 'NeedsRouting');
            setPage(1);
          }}
          role="button"
          tabIndex={0}
        >
          <span className="attention-card-label">Needs Routing</span>
          <span className="attention-card-value">{summary.needsRoutingCount}</span>
          <span className="attention-card-sub">Unassigned to desk</span>
        </div>
      </section>

      {/* Filter Bar */}
      <section className="attention-filter-bar">
        {/* Bucket Tabs */}
        <div className="bucket-tabs">
          {[
            { id: '', label: 'All Active' },
            { id: 'Overdue', label: `Overdue (${summary.overdueCount})` },
            { id: 'Today', label: `Today (${summary.todayCount})` },
            { id: 'Tomorrow', label: `Tomorrow (${summary.tomorrowCount})` },
            { id: 'Next7Days', label: `Next 7 Days (${summary.next7DaysCount})` },
            { id: 'ReminderActive', label: `Reminders (${summary.reminderActiveCount})` },
            { id: 'NeedsRouting', label: `Needs Routing (${summary.needsRoutingCount})` }
          ].map((tab) => (
            <button
              key={tab.id}
              className={`tab-btn ${bucket === tab.id ? 'active' : ''}`}
              onClick={() => {
                setBucket(tab.id);
                setPage(1);
              }}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {/* Filter Controls Row */}
        <div className="filter-controls-row">
          <div className="filter-control">
            <label htmlFor="sourceTypeFilter">Source:</label>
            <select
              id="sourceTypeFilter"
              value={sourceType}
              onChange={(e) => {
                setSourceType(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All Sources</option>
              <option value="ScheduledEvent">Scheduled Events</option>
              <option value="WorkItemDue">Work Items Due</option>
              <option value="DakDue">Dak Due</option>
            </select>
          </div>

          <div className="filter-control">
            <label htmlFor="wsFilter">Workstream:</label>
            <select
              id="wsFilter"
              value={workstreamId}
              onChange={(e) => {
                setWorkstreamId(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All Workstreams</option>
              {(data?.workstreams || data?.workstreamOptions || []).map((ws) => (
                <option key={ws.id} value={ws.id}>
                  {ws.name}
                </option>
              ))}
            </select>
          </div>

          <div className="filter-control">
            <label htmlFor="deskFilter">Desk:</label>
            <select
              id="deskFilter"
              value={deskId}
              onChange={(e) => {
                setDeskId(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All Desks</option>
              {(data?.desks || data?.deskOptions || []).map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </select>
          </div>

          <div className="filter-control">
            <label htmlFor="priorityFilter">Priority:</label>
            <select
              id="priorityFilter"
              value={priority}
              onChange={(e) => {
                setPriority(e.target.value);
                setPage(1);
              }}
            >
              <option value="">All Priorities</option>
              <option value="Routine">Routine</option>
              <option value="Urgent">Urgent</option>
              <option value="Immediate">Immediate</option>
            </select>
          </div>

          <div className="filter-control search-input">
            <input
              type="search"
              placeholder="Search by title, description, ref..."
              value={q}
              onChange={(e) => {
                setQ(e.target.value);
                setPage(1);
              }}
            />
          </div>
        </div>
      </section>

      {/* Error or Feed List */}
      {error && (
        <div className="error-banner" style={{ padding: '12px 16px', background: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#b91c1c' }}>
          <strong>Error:</strong> {error}
        </div>
      )}

      {loading && (
        <div style={{ padding: '30px', textAlign: 'center', color: '#64748b' }}>
          Loading unified attention feed...
        </div>
      )}

      {!loading && data?.items.length === 0 && (
        <div style={{ padding: '40px', textAlign: 'center', background: '#ffffff', border: '1px solid #e2e8f0', borderRadius: 8 }}>
          <h3 style={{ color: '#334155', margin: '0 0 6px 0' }}>No Attention Items</h3>
          <p style={{ color: '#64748b', margin: 0, fontSize: '0.9rem' }}>
            There are no obligations or events matching your selected filters.
          </p>
        </div>
      )}

      {!loading && data && data.items.length > 0 && (
        <div className="attention-feed">
          {data.items.map((item) => {
            const isOverdue = item.buckets.includes('Overdue');
            const isToday = item.buckets.includes('Today');
            const isTomorrow = item.buckets.includes('Tomorrow');

            const feedClass = isOverdue
              ? 'feed-overdue'
              : isToday
              ? 'feed-today'
              : isTomorrow
              ? 'feed-tomorrow'
              : '';

            const dateClass = isOverdue ? 'date-overdue' : isToday ? 'date-today' : '';

            return (
              <article key={`${item.sourceType}-${item.id}`} className={`attention-feed-item ${feedClass}`}>
                <div className="feed-item-top">
                  <div className="feed-item-main">
                    <div>
                      {item.sourceType === 'ScheduledEvent' && (
                        <span className="feed-source-badge badge-scheduled">📅 Scheduled Event</span>
                      )}
                      {item.sourceType === 'WorkItemDue' && (
                        <span className="feed-source-badge badge-workitem">📋 Work Item Due</span>
                      )}
                      {item.sourceType === 'DakDue' && (
                        <span className="feed-source-badge badge-dak">📥 Dak Due</span>
                      )}
                    </div>

                    <h2 className="feed-item-title">{item.title}</h2>
                    {item.description && <p className="feed-item-desc">{item.description}</p>}
                  </div>

                  <div className="feed-item-timing">
                    <span className={`feed-date ${dateClass}`}>{item.scheduledDate}</span>
                    {item.scheduledTime && <span className="feed-time">at {item.scheduledTime}</span>}
                    {item.remindersActive && (
                      <span className="badge-reminder" title="Active reminder surfaced">
                        🔔 Reminder active
                      </span>
                    )}
                  </div>
                </div>

                {/* Metadata Row */}
                <div className="feed-item-meta-row">
                  <div className="meta-group">
                    <strong>Workstream:</strong> {item.workstreamName}
                  </div>

                  {item.responsibleDeskName ? (
                    <div className="meta-group">
                      <strong>Desk:</strong> {item.responsibleDeskName}
                    </div>
                  ) : (
                    <div className="meta-group" style={{ color: '#d97706', fontWeight: 600 }}>
                      ⚠️ Desk Unassigned
                    </div>
                  )}

                  {item.assignedUserDisplayName && (
                    <div className="meta-group">
                      <strong>Assignee:</strong> {item.assignedUserDisplayName}{' '}
                      {item.isAssignedToCaller && <span style={{ color: '#2563eb' }}>(You)</span>}
                    </div>
                  )}

                  <div className="meta-group">
                    <strong>Priority:</strong>{' '}
                    <span
                      style={{
                        fontWeight: 600,
                        color:
                          item.priority === 'Immediate'
                            ? '#dc2626'
                            : item.priority === 'Urgent'
                            ? '#ea580c'
                            : '#475569'
                      }}
                    >
                      {item.priority}
                    </span>
                  </div>

                  {/* Context Pill */}
                  {item.context && (
                    <div className="meta-group">
                      <strong>Context:</strong>{' '}
                      {item.context.canOpen && item.context.navigationUrl ? (
                        <Link to={item.context.navigationUrl} className="context-pill clickable">
                          🔗 {item.context.entityType}: {item.context.title || item.context.referenceNumber || 'View'}
                        </Link>
                      ) : (
                        <span className="context-pill locked" title="Access restricted">
                          🔒 {item.context.entityType} (Restricted)
                        </span>
                      )}
                    </div>
                  )}

                  {/* Actions Row */}
                  <div className="feed-actions">
                    {/* Source navigation */}
                    {item.sourceType === 'WorkItemDue' && (
                      <Link to={`/work/${item.sourceEntityId || item.id}`} className="btn-sm btn-primary">
                        Open Work Item
                      </Link>
                    )}
                    {item.sourceType === 'DakDue' && (
                      <Link to={`/dak/${item.sourceEntityId || item.id}`} className="btn-sm btn-primary">
                        Open Dak
                      </Link>
                    )}

                    {/* ScheduledEvent Actions */}
                    {item.sourceType === 'ScheduledEvent' && (
                      <>
                        <Link
                          to={`/calendar?eventId=${item.id}`}
                          className="btn-sm"
                          title="View details in Calendar"
                        >
                          📅 Calendar
                        </Link>

                        {item.capabilities?.canReschedule && (
                          <button
                            className="btn-sm"
                            onClick={() => openRescheduleModal(item)}
                            title="Reschedule event date/time"
                          >
                            🕒 Reschedule
                          </button>
                        )}

                        {item.capabilities?.canReassign && (
                          <button
                            className="btn-sm"
                            onClick={() => openReassignModal(item)}
                            title="Reassign to another desk or handler"
                          >
                            🔄 Reassign
                          </button>
                        )}

                        {item.capabilities?.canManageReminders && (
                          <button
                            className="btn-sm"
                            onClick={() => openReminderModal(item)}
                            title="Manage reminders"
                          >
                            🔔 Reminders ({item.activeRemindersCount ?? 0})
                          </button>
                        )}

                        {item.capabilities?.canComplete && (
                          <button
                            className="btn-sm btn-success"
                            onClick={() => openCompleteModal(item)}
                            title="Mark as completed"
                          >
                            ✓ Complete
                          </button>
                        )}

                        {item.capabilities?.canCancel && (
                          <button
                            className="btn-sm btn-danger"
                            onClick={() => openCancelModal(item)}
                            title="Cancel event"
                          >
                            ✕ Cancel
                          </button>
                        )}
                      </>
                    )}
                  </div>
                </div>
              </article>
            );
          })}
        </div>
      )}

      {/* Reschedule Modal */}
      {activeModal === 'reschedule' && selectedItem && (
        <div className="attention-modal-overlay" onClick={closeModal}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Reschedule Event: {selectedItem.title}</h3>
              <button className="modal-close" onClick={closeModal}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {actionError && (
                <div style={{ color: '#b91c1c', fontSize: '0.85rem' }}>{actionError}</div>
              )}
              <div className="form-row">
                <div className="form-group">
                  <label>
                    New Date <span className="required-star">*</span>
                  </label>
                  <input
                    type="date"
                    value={rescheduleDate}
                    onChange={(e) => setRescheduleDate(e.target.value)}
                  />
                </div>
                <div className="form-group">
                  <label>New Time (Optional)</label>
                  <input
                    type="time"
                    value={rescheduleTime}
                    onChange={(e) => setRescheduleTime(e.target.value)}
                  />
                </div>
              </div>
              <div className="form-group">
                <label>Reason for Rescheduling</label>
                <textarea
                  rows={3}
                  placeholder="Official reason for adjourning/rescheduling..."
                  value={rescheduleReason}
                  onChange={(e) => setRescheduleReason(e.target.value)}
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={closeModal} disabled={submitting}>
                Cancel
              </button>
              <button
                className="btn-sm btn-primary"
                onClick={handleRescheduleSubmit}
                disabled={submitting}
              >
                {submitting ? 'Saving...' : 'Save Reschedule'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Reassign Modal */}
      {activeModal === 'reassign' && selectedItem && (
        <div className="attention-modal-overlay" onClick={closeModal}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Reassign Event: {selectedItem.title}</h3>
              <button className="modal-close" onClick={closeModal}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {actionError && (
                <div style={{ color: '#b91c1c', fontSize: '0.85rem' }}>{actionError}</div>
              )}
              <div className="form-group">
                <label>Responsible Office Desk</label>
                <select
                  value={targetDeskId}
                  onChange={(e) => {
                    setTargetDeskId(e.target.value);
                    setTargetUserId('');
                  }}
                >
                  <option value="">-- Select Desk --</option>
                  {options?.desks.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label>Assigned Handler (Desk Member)</label>
                <select
                  value={targetUserId}
                  onChange={(e) => setTargetUserId(e.target.value)}
                  disabled={!targetDeskId}
                >
                  <option value="">-- No specific handler (Desk general) --</option>
                  {selectedDeskMembers.map((m) => (
                    <option key={m.userId} value={m.userId}>
                      {m.displayName} ({m.designationName || 'Staff'})
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label>Reason for Reassignment</label>
                <textarea
                  rows={3}
                  placeholder="Official reason for reassignment..."
                  value={reassignReason}
                  onChange={(e) => setReassignReason(e.target.value)}
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={closeModal} disabled={submitting}>
                Cancel
              </button>
              <button
                className="btn-sm btn-primary"
                onClick={handleReassignSubmit}
                disabled={submitting}
              >
                {submitting ? 'Reassigning...' : 'Confirm Reassign'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Complete Modal */}
      {activeModal === 'complete' && selectedItem && (
        <div className="attention-modal-overlay" onClick={closeModal}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Complete Event: {selectedItem.title}</h3>
              <button className="modal-close" onClick={closeModal}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {actionError && (
                <div style={{ color: '#b91c1c', fontSize: '0.85rem' }}>{actionError}</div>
              )}
              <p style={{ margin: 0, fontSize: '0.9rem', color: '#475569' }}>
                Are you sure you want to mark this official event as <strong>Completed</strong>?
              </p>
              <div className="form-group">
                <label>Completion Notes (Optional)</label>
                <textarea
                  rows={3}
                  placeholder="Notes on proceedings outcome, attendance, compliance..."
                  value={completionNotes}
                  onChange={(e) => setCompletionNotes(e.target.value)}
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={closeModal} disabled={submitting}>
                Cancel
              </button>
              <button
                className="btn-sm btn-success"
                onClick={handleCompleteSubmit}
                disabled={submitting}
              >
                {submitting ? 'Completing...' : 'Mark Completed'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Cancel Modal */}
      {activeModal === 'cancel' && selectedItem && (
        <div className="attention-modal-overlay" onClick={closeModal}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Cancel Event: {selectedItem.title}</h3>
              <button className="modal-close" onClick={closeModal}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {actionError && (
                <div style={{ color: '#b91c1c', fontSize: '0.85rem' }}>{actionError}</div>
              )}
              <div className="form-group">
                <label>
                  Cancellation Reason <span className="required-star">*</span>
                </label>
                <textarea
                  rows={3}
                  placeholder="Official mandatory reason for cancelling this event..."
                  value={cancelReason}
                  onChange={(e) => setCancelReason(e.target.value)}
                  required
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={closeModal} disabled={submitting}>
                Back
              </button>
              <button
                className="btn-sm btn-danger"
                onClick={handleCancelSubmit}
                disabled={submitting}
              >
                {submitting ? 'Cancelling...' : 'Confirm Cancellation'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Reminder Management Modal */}
      {activeModal === 'reminder' && selectedItem && (
        <div className="attention-modal-overlay" onClick={closeModal}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Manage Reminders: {selectedItem.title}</h3>
              <button className="modal-close" onClick={closeModal}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {actionError && (
                <div style={{ color: '#b91c1c', fontSize: '0.85rem' }}>{actionError}</div>
              )}
              <div className="form-row">
                <div className="form-group">
                  <label>
                    Days Before Event <span className="required-star">*</span>
                  </label>
                  <input
                    type="number"
                    min={0}
                    max={365}
                    value={reminderDaysBefore}
                    onChange={(e) => setReminderDaysBefore(parseInt(e.target.value, 10) || 0)}
                  />
                </div>
                <div className="form-group">
                  <label>Reminder Note (Optional)</label>
                  <input
                    type="text"
                    placeholder="e.g. Gather status report"
                    value={reminderNote}
                    onChange={(e) => setReminderNote(e.target.value)}
                  />
                </div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={closeModal} disabled={submitting}>
                Close
              </button>
              <button
                className="btn-sm btn-primary"
                onClick={handleAddReminderSubmit}
                disabled={submitting}
              >
                {submitting ? 'Adding...' : 'Add Reminder'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
