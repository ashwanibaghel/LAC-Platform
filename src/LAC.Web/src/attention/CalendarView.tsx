import { useEffect, useState, useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';
import type {
  CalendarEventDto,
  ScheduleCalendarResult,
  ScheduledEventDetail,
  ScheduledEventKind,
  ScheduledEventPriority,
  ScheduleOptionsResponse
} from './types';
import './attention.css';

export function CalendarView() {
  const [searchParams] = useSearchParams();
  const [currentYear, setCurrentYear] = useState(() => new Date().getFullYear());
  const [currentMonth, setCurrentMonth] = useState(() => new Date().getMonth()); // 0-indexed
  const [viewMode, setViewMode] = useState<'month' | 'agenda'>('month');

  // Filters
  const [workstreamId, setWorkstreamId] = useState('');
  const [deskId, setDeskId] = useState('');
  const [eventKind, setEventKind] = useState('');
  const [status, setStatus] = useState('');

  // Data
  const [events, setEvents] = useState<CalendarEventDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Options
  const [options, setOptions] = useState<ScheduleOptionsResponse | null>(null);

  // Detail Drawer / Modal
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ScheduledEventDetail | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailError, setDetailError] = useState<string | null>(null);

  // Create Event Modal
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createWsId, setCreateWsId] = useState('');
  const [createDeskId, setCreateDeskId] = useState('');
  const [createUserId, setCreateUserId] = useState('');
  const [createKind, setCreateKind] = useState<ScheduledEventKind>('Hearing');
  const [createTitle, setCreateTitle] = useState('');
  const [createDescription, setCreateDescription] = useState('');
  const [createDate, setCreateDate] = useState('');
  const [createTime, setCreateTime] = useState('');
  const [createPriority, setCreatePriority] = useState<ScheduledEventPriority>('Medium');
  const [createMatterId, setCreateMatterId] = useState('');
  const [createDakId, setCreateDakId] = useState('');
  const [createOutwardId, setCreateOutwardId] = useState('');
  const [createWorkItemId, setCreateWorkItemId] = useState('');
  const [createCourtCaseId, setCreateCourtCaseId] = useState('');
  const [createCourtProceedingId, setCreateCourtProceedingId] = useState('');
  const [createSubmitting, setCreateSubmitting] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Promote Court Proceeding Modal
  const [showPromoteModal, setShowPromoteModal] = useState(false);
  const [promoteProceedingId, setPromoteProceedingId] = useState('');
  const [promoteTitle, setPromoteTitle] = useState('');
  const [promotePriority, setPromotePriority] = useState<ScheduledEventPriority>('High');
  const [promoteDeskId, setPromoteDeskId] = useState('');
  const [promoteUserId, setPromoteUserId] = useState('');
  const [promoteSubmitting, setPromoteSubmitting] = useState(false);
  const [promoteError, setPromoteError] = useState<string | null>(null);

  // Action modals on Detail: Reschedule, Reassign, Complete, Cancel, Add Reminder, Link WorkItem
  const [activeActionModal, setActiveActionModal] = useState<
    'reschedule' | 'reassign' | 'complete' | 'cancel' | 'addReminder' | 'linkWork' | null
  >(null);
  const [actionDate, setActionDate] = useState('');
  const [actionTime, setActionTime] = useState('');
  const [actionReason, setActionReason] = useState('');
  const [actionTargetDeskId, setActionTargetDeskId] = useState('');
  const [actionTargetUserId, setActionTargetUserId] = useState('');
  const [actionDaysBefore, setActionDaysBefore] = useState(1);
  const [actionReminderNote, setActionReminderNote] = useState('');
  const [actionWorkItemId, setActionWorkItemId] = useState('');
  const [actionSubmitting, setActionSubmitting] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Load options
  useEffect(() => {
    fetch('/api/scheduled-events/options', { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => {
        if (data) setOptions(data);
      })
      .catch(() => {});
  }, []);

  // Handle URL query parameters (e.g. ?proceedingId=...)
  useEffect(() => {
    const procId = searchParams.get('proceedingId');
    if (procId) {
      setPromoteProceedingId(procId);
      setShowPromoteModal(true);
    }
  }, [searchParams]);

  // Compute month bounds (from 1st of month to last of month)
  const { fromDateStr, toDateStr } = useMemo(() => {
    const firstDay = new Date(currentYear, currentMonth, 1);
    const lastDay = new Date(currentYear, currentMonth + 1, 0);

    // Format YYYY-MM-DD
    const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
    const fromStr = `${firstDay.getFullYear()}-${pad(firstDay.getMonth() + 1)}-01`;
    const toStr = `${lastDay.getFullYear()}-${pad(lastDay.getMonth() + 1)}-${pad(lastDay.getDate())}`;

    return { fromDateStr: fromStr, toDateStr: toStr };
  }, [currentYear, currentMonth]);

  // Fetch calendar events
  const fetchCalendar = useCallback(async () => {
    setLoading(true);
    setError(null);

    const params = new URLSearchParams();
    params.set('from', fromDateStr);
    params.set('to', toDateStr);
    if (workstreamId) params.set('workstreamId', workstreamId);
    if (deskId) params.set('deskId', deskId);
    if (eventKind) params.set('eventKind', eventKind);
    if (status) params.set('status', status);

    try {
      const res = await fetch(`/api/scheduled-events/calendar?${params.toString()}`, {
        credentials: 'include'
      });

      if (!res.ok) throw new Error(`Failed to load calendar events (${res.status})`);
      const json: ScheduleCalendarResult = await res.json();
      setEvents(json.events || []);
    } catch (err: any) {
      setError(err.message || 'Error fetching calendar.');
    } finally {
      setLoading(false);
    }
  }, [fromDateStr, toDateStr, workstreamId, deskId, eventKind, status]);

  useEffect(() => {
    fetchCalendar();
  }, [fetchCalendar]);

  // Load event detail
  const loadEventDetail = async (id: string) => {
    setSelectedEventId(id);
    setDetailLoading(true);
    setDetailError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${id}`, { credentials: 'include' });
      if (!res.ok) throw new Error(`Failed to load event details (${res.status})`);
      const data: ScheduledEventDetail = await res.json();
      setDetail(data);
    } catch (err: any) {
      setDetailError(err.message);
    } finally {
      setDetailLoading(false);
    }
  };

  const closeDetail = () => {
    setSelectedEventId(null);
    setDetail(null);
    setDetailError(null);
  };

  // Month navigation
  const prevMonth = () => {
    if (currentMonth === 0) {
      setCurrentMonth(11);
      setCurrentYear((y) => y - 1);
    } else {
      setCurrentMonth((m) => m - 1);
    }
  };

  const nextMonth = () => {
    if (currentMonth === 11) {
      setCurrentMonth(0);
      setCurrentYear((y) => y + 1);
    } else {
      setCurrentMonth((m) => m + 1);
    }
  };

  const goToToday = () => {
    const today = new Date();
    setCurrentYear(today.getFullYear());
    setCurrentMonth(today.getMonth());
  };

  // Month grid calculations
  const monthMatrix = useMemo(() => {
    const firstDayIndex = new Date(currentYear, currentMonth, 1).getDay(); // 0 = Sun
    const totalDaysInMonth = new Date(currentYear, currentMonth + 1, 0).getDate();
    const prevMonthDays = new Date(currentYear, currentMonth, 0).getDate();

    const cells: {
      dayNum: number;
      dateStr: string;
      isCurrentMonth: boolean;
      isToday: boolean;
    }[] = [];

    const pad = (n: number) => (n < 10 ? `0${n}` : `${n}`);
    const today = new Date();
    const todayStr = `${today.getFullYear()}-${pad(today.getMonth() + 1)}-${pad(today.getDate())}`;

    // Leading days from prev month
    for (let i = firstDayIndex - 1; i >= 0; i--) {
      const d = prevMonthDays - i;
      const prevM = currentMonth === 0 ? 12 : currentMonth;
      const prevY = currentMonth === 0 ? currentYear - 1 : currentYear;
      const dateStr = `${prevY}-${pad(prevM)}-${pad(d)}`;
      cells.push({ dayNum: d, dateStr, isCurrentMonth: false, isToday: dateStr === todayStr });
    }

    // Current month days
    for (let d = 1; d <= totalDaysInMonth; d++) {
      const dateStr = `${currentYear}-${pad(currentMonth + 1)}-${pad(d)}`;
      cells.push({ dayNum: d, dateStr, isCurrentMonth: true, isToday: dateStr === todayStr });
    }

    // Trailing days to fill 35 or 42 grid
    const remaining = (7 - (cells.length % 7)) % 7;
    for (let d = 1; d <= remaining; d++) {
      const nextM = currentMonth === 11 ? 1 : currentMonth + 2;
      const nextY = currentMonth === 11 ? currentYear + 1 : currentYear;
      const dateStr = `${nextY}-${pad(nextM)}-${pad(d)}`;
      cells.push({ dayNum: d, dateStr, isCurrentMonth: false, isToday: dateStr === todayStr });
    }

    return cells;
  }, [currentYear, currentMonth]);

  // Group events by date for fast lookup
  const eventsByDate = useMemo(() => {
    const map = new Map<string, CalendarEventDto[]>();
    for (const evt of events) {
      const list = map.get(evt.scheduledDate) || [];
      list.push(evt);
      map.set(evt.scheduledDate, list);
    }
    return map;
  }, [events]);

  const monthNames = [
    'January', 'February', 'March', 'April', 'May', 'June',
    'July', 'August', 'September', 'October', 'November', 'December'
  ];

  // Create Event Submit
  const handleCreateSubmit = async () => {
    if (!createWsId || !createTitle.trim() || !createDate) {
      setCreateError('Workstream, Title, and Scheduled Date are required.');
      return;
    }
    setCreateSubmitting(true);
    setCreateError(null);

    try {
      const res = await fetch('/api/scheduled-events', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          workstreamId: createWsId,
          responsibleOfficeDeskId: createDeskId || null,
          assignedUserId: createUserId || null,
          eventKind: createKind,
          title: createTitle.trim(),
          description: createDescription.trim() || null,
          scheduledDate: createDate,
          scheduledTime: createTime || null,
          priority: createPriority,
          matterId: createMatterId || null,
          dakId: createDakId || null,
          outwardId: createOutwardId || null,
          workItemId: createWorkItemId || null,
          courtCaseId: createCourtCaseId || null,
          courtProceedingId: createCourtProceedingId || null
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Create failed (${res.status})`);
      }

      setShowCreateModal(false);
      resetCreateForm();
      fetchCalendar();
    } catch (err: any) {
      setCreateError(err.message);
    } finally {
      setCreateSubmitting(false);
    }
  };

  const resetCreateForm = () => {
    setCreateWsId('');
    setCreateDeskId('');
    setCreateUserId('');
    setCreateKind('Hearing');
    setCreateTitle('');
    setCreateDescription('');
    setCreateDate('');
    setCreateTime('');
    setCreatePriority('Medium');
    setCreateMatterId('');
    setCreateDakId('');
    setCreateOutwardId('');
    setCreateWorkItemId('');
    setCreateCourtCaseId('');
    setCreateCourtProceedingId('');
    setCreateError(null);
  };

  // Promote Court Proceeding Submit
  const handlePromoteSubmit = async () => {
    if (!promoteProceedingId.trim()) {
      setPromoteError('Court Proceeding ID is required.');
      return;
    }
    setPromoteSubmitting(true);
    setPromoteError(null);

    try {
      const res = await fetch(
        `/api/scheduled-events/from-court-proceeding/${promoteProceedingId.trim()}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
          body: JSON.stringify({
            title: promoteTitle.trim() || null,
            priority: promotePriority,
            responsibleOfficeDeskId: promoteDeskId || null,
            assignedUserId: promoteUserId || null
          })
        }
      );

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Promotion failed (${res.status})`);
      }

      setShowPromoteModal(false);
      setPromoteProceedingId('');
      setPromoteTitle('');
      fetchCalendar();
    } catch (err: any) {
      setPromoteError(err.message);
    } finally {
      setPromoteSubmitting(false);
    }
  };

  // Action Handlers for detail drawer
  const handleReschedule = async () => {
    if (!detail) return;
    if (!actionDate) {
      setActionError('New scheduled date is required.');
      return;
    }
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/reschedule`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          scheduledDate: actionDate,
          scheduledTime: actionTime || null,
          reason: actionReason.trim() || null,
          expectedRevision: detail.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Reschedule failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleReassign = async () => {
    if (!detail) return;
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/reassign`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          targetDeskId: actionTargetDeskId || null,
          targetUserId: actionTargetUserId || null,
          reason: actionReason.trim() || null,
          expectedRevision: detail.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Reassign failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleComplete = async () => {
    if (!detail) return;
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/complete`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          completionNotes: actionReason.trim() || null,
          expectedRevision: detail.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Complete failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleCancel = async () => {
    if (!detail) return;
    if (!actionReason.trim()) {
      setActionError('Cancellation reason is mandatory.');
      return;
    }
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/cancel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          cancellationReason: actionReason.trim(),
          expectedRevision: detail.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Cancel failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleAddReminder = async () => {
    if (!detail) return;
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/reminders`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          daysBefore: actionDaysBefore,
          note: actionReminderNote.trim() || null
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Add reminder failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleDeactivateReminder = async (reminderId: string) => {
    if (!detail) return;
    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/reminders/${reminderId}`, {
        method: 'DELETE',
        credentials: 'include'
      });
      if (!res.ok) throw new Error('Failed to deactivate reminder');
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      alert(err.message);
    }
  };

  const handleLinkWorkItem = async () => {
    if (!detail || !actionWorkItemId.trim()) return;
    setActionSubmitting(true);
    setActionError(null);

    try {
      const res = await fetch(`/api/scheduled-events/${detail.id}/work-item`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          workItemId: actionWorkItemId.trim(),
          expectedRevision: detail.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => ({}));
        throw new Error(errJson.detail || errJson.message || `Link work item failed (${res.status})`);
      }

      setActiveActionModal(null);
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setActionSubmitting(false);
    }
  };

  const handleUnlinkWorkItem = async () => {
    if (!detail) return;
    if (!confirm('Are you sure you want to unlink this work item?')) return;

    try {
      const res = await fetch(
        `/api/scheduled-events/${detail.id}/work-item?expectedRevision=${detail.revision}`,
        {
          method: 'DELETE',
          credentials: 'include'
        }
      );
      if (!res.ok) throw new Error('Failed to unlink work item');
      loadEventDetail(detail.id);
      fetchCalendar();
    } catch (err: any) {
      alert(err.message);
    }
  };

  const createDeskOptions = useMemo(() => {
    if (!createWsId || !options) return options?.desks || [];
    return options.desks.filter((d) => !d.workstreamId || d.workstreamId === createWsId);
  }, [createWsId, options]);

  const createMemberOptions = useMemo(() => {
    if (!createDeskId || !options) return [];
    const desk = options.desks.find((d) => d.id === createDeskId);
    return desk ? desk.members : [];
  }, [createDeskId, options]);

  const actionDeskMembers = useMemo(() => {
    if (!actionTargetDeskId || !options) return [];
    const desk = options.desks.find((d) => d.id === actionTargetDeskId);
    return desk ? desk.members : [];
  }, [actionTargetDeskId, options]);

  return (
    <div className="attention-container">
      {/* Header */}
      <header className="attention-header">
        <div className="attention-title-group">
          <h1>Official Schedule & Calendar</h1>
          <p className="attention-subtitle">
            Court hearings, compliance dates, inspections, and time-bound statutory obligations.
          </p>
        </div>
        <div className="attention-header-actions">
          <button
            className="btn-sm btn-primary"
            onClick={() => {
              resetCreateForm();
              setShowCreateModal(true);
            }}
          >
            + Schedule Official Event
          </button>
          <button className="btn-sm" onClick={() => setShowPromoteModal(true)}>
            ⚖ Promote Court Proceeding
          </button>
        </div>
      </header>

      {/* Calendar Controls Bar */}
      <div className="calendar-controls">
        <div className="calendar-nav-group">
          <button className="btn-sm" onClick={prevMonth} title="Previous Month">
            ◀
          </button>
          <span className="calendar-nav-title">
            {monthNames[currentMonth]} {currentYear}
          </span>
          <button className="btn-sm" onClick={nextMonth} title="Next Month">
            ▶
          </button>
          <button className="btn-sm" onClick={goToToday}>
            Today
          </button>
        </div>

        {/* Filters */}
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <select
            value={workstreamId}
            onChange={(e) => setWorkstreamId(e.target.value)}
            style={{ padding: '5px 8px', fontSize: '0.82rem', borderRadius: 4, border: '1px solid #cbd5e1' }}
          >
            <option value="">All Workstreams</option>
            {options?.workstreams.map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </select>

          <select
            value={deskId}
            onChange={(e) => setDeskId(e.target.value)}
            style={{ padding: '5px 8px', fontSize: '0.82rem', borderRadius: 4, border: '1px solid #cbd5e1' }}
          >
            <option value="">All Desks</option>
            {options?.desks.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>

          <select
            value={eventKind}
            onChange={(e) => setEventKind(e.target.value)}
            style={{ padding: '5px 8px', fontSize: '0.82rem', borderRadius: 4, border: '1px solid #cbd5e1' }}
          >
            <option value="">All Event Kinds</option>
            <option value="Hearing">Hearing</option>
            <option value="ComplianceDeadline">Compliance Deadline</option>
            <option value="SiteInspection">Site Inspection</option>
            <option value="Meeting">Meeting</option>
            <option value="OrderDelivery">Order Delivery</option>
            <option value="CompensationDisbursement">Compensation Disbursement</option>
            <option value="ReportSubmission">Report Submission</option>
            <option value="NoticeExpiry">Notice Expiry</option>
            <option value="Other">Other</option>
          </select>

          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            style={{ padding: '5px 8px', fontSize: '0.82rem', borderRadius: 4, border: '1px solid #cbd5e1' }}
          >
            <option value="">All Statuses</option>
            <option value="Scheduled">Scheduled</option>
            <option value="Rescheduled">Rescheduled</option>
            <option value="Completed">Completed</option>
            <option value="Cancelled">Cancelled</option>
          </select>

          {/* View Mode Toggle */}
          <div style={{ display: 'flex', border: '1px solid #cbd5e1', borderRadius: 4, overflow: 'hidden' }}>
            <button
              className="btn-sm"
              style={{
                borderRadius: 0,
                border: 'none',
                background: viewMode === 'month' ? '#1e293b' : '#ffffff',
                color: viewMode === 'month' ? '#ffffff' : '#334155'
              }}
              onClick={() => setViewMode('month')}
            >
              Month
            </button>
            <button
              className="btn-sm"
              style={{
                borderRadius: 0,
                border: 'none',
                background: viewMode === 'agenda' ? '#1e293b' : '#ffffff',
                color: viewMode === 'agenda' ? '#ffffff' : '#334155'
              }}
              onClick={() => setViewMode('agenda')}
            >
              Agenda
            </button>
          </div>
        </div>
      </div>

      {error && (
        <div style={{ padding: 12, background: '#fef2f2', border: '1px solid #fecaca', borderRadius: 8, color: '#b91c1c' }}>
          {error}
        </div>
      )}

      {loading && (
        <div style={{ padding: 30, textAlign: 'center', color: '#64748b' }}>
          Loading schedule data...
        </div>
      )}

      {/* Month View Grid */}
      {!loading && viewMode === 'month' && (
        <div className="calendar-month-grid">
          {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((d) => (
            <div key={d} className="calendar-day-header">
              {d}
            </div>
          ))}

          {monthMatrix.map((cell, idx) => {
            const dayEvents = eventsByDate.get(cell.dateStr) || [];
            return (
              <div
                key={idx}
                className={`calendar-day-cell ${!cell.isCurrentMonth ? 'other-month' : ''} ${
                  cell.isToday ? 'today' : ''
                }`}
              >
                <span className="calendar-day-num">{cell.dayNum}</span>

                {dayEvents.map((evt) => (
                  <div
                    key={evt.id}
                    className={`calendar-event-chip chip-priority-${evt.priority} chip-status-${evt.status}`}
                    onClick={() => loadEventDetail(evt.id)}
                    title={`${evt.title} (${evt.eventKind})`}
                  >
                    <span>
                      {evt.scheduledTime ? `${evt.scheduledTime.slice(0, 5)} ` : ''}
                      {evt.title}
                    </span>
                    {evt.activeRemindersCount > 0 && <span title="Has active reminders">🔔</span>}
                  </div>
                ))}
              </div>
            );
          })}
        </div>
      )}

      {/* Agenda View */}
      {!loading && viewMode === 'agenda' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {events.length === 0 ? (
            <div style={{ padding: 30, textAlign: 'center', background: '#fff', borderRadius: 8, border: '1px solid #e2e8f0' }}>
              No events scheduled in this period.
            </div>
          ) : (
            Array.from(eventsByDate.entries())
              .sort(([d1], [d2]) => d1.localeCompare(d2))
              .map(([date, dateEvts]) => (
                <div key={date} className="agenda-day-group">
                  <div className="agenda-day-header">{date}</div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                    {dateEvts.map((evt) => (
                      <div
                        key={evt.id}
                        className={`attention-feed-item chip-priority-${evt.priority}`}
                        style={{ cursor: 'pointer', padding: '10px 14px' }}
                        onClick={() => loadEventDetail(evt.id)}
                      >
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                          <div>
                            <strong>{evt.title}</strong>{' '}
                            <span style={{ fontSize: '0.78rem', color: '#64748b' }}>
                              ({evt.eventKind} • {evt.workstreamName})
                            </span>
                          </div>
                          <div style={{ fontSize: '0.82rem' }}>
                            {evt.scheduledTime && <span>at {evt.scheduledTime} </span>}
                            <span className="btn-sm">View Details</span>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ))
          )}
        </div>
      )}

      {/* Event Detail Modal / Drawer */}
      {selectedEventId && (
        <div className="attention-modal-overlay" onClick={closeDetail}>
          <div
            className="attention-modal"
            style={{ maxWidth: 700 }}
            onClick={(e) => e.stopPropagation()}
          >
            <div className="modal-header">
              <h3>Event Details: {detail?.title || 'Loading...'}</h3>
              <button className="modal-close" onClick={closeDetail}>
                ×
              </button>
            </div>
            <div className="modal-body">
              {detailLoading && <div>Loading event full details...</div>}
              {detailError && <div style={{ color: '#dc2626' }}>{detailError}</div>}

              {detail && (
                <>
                  <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap', alignItems: 'center' }}>
                    <span className="feed-source-badge badge-scheduled">{detail.eventKind}</span>
                    <span
                      style={{
                        fontSize: '0.78rem',
                        fontWeight: 600,
                        padding: '2px 8px',
                        borderRadius: 4,
                        background: detail.status === 'Completed' ? '#dcfce7' : '#f1f5f9',
                        color: detail.status === 'Completed' ? '#166534' : '#334155'
                      }}
                    >
                      Status: {detail.status}
                    </span>
                    <span style={{ fontSize: '0.78rem', fontWeight: 600, color: '#dc2626' }}>
                      Priority: {detail.priority}
                    </span>
                  </div>

                  <p style={{ margin: 0, fontSize: '0.92rem', color: '#475569' }}>
                    {detail.description || 'No description provided.'}
                  </p>

                  <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 10, fontSize: '0.84rem', background: '#f8fafc', padding: 12, borderRadius: 6 }}>
                    <div>
                      <strong>Date:</strong> {detail.scheduledDate}{' '}
                      {detail.scheduledTime && `at ${detail.scheduledTime}`}
                    </div>
                    <div>
                      <strong>Workstream:</strong> {detail.workstreamName}
                    </div>
                    <div>
                      <strong>Responsible Desk:</strong>{' '}
                      {detail.responsibleOfficeDeskName || 'Unassigned'}
                    </div>
                    <div>
                      <strong>Assigned Handler:</strong>{' '}
                      {detail.assignedUserDisplayName || 'None (Desk general)'}
                    </div>
                    <div>
                      <strong>Created By:</strong>{' '}
                      {detail.createdByDisplayNameSnapshot || 'System'} ({detail.createdByDesignationSnapshot || 'Officer'})
                    </div>
                    <div>
                      <strong>Revision:</strong> #{detail.revision}
                    </div>
                  </div>

                  {detail.cancellationReason && (
                    <div style={{ padding: 10, background: '#fef2f2', border: '1px solid #fecaca', borderRadius: 6, fontSize: '0.84rem', color: '#b91c1c' }}>
                      <strong>Cancellation Reason:</strong> {detail.cancellationReason}
                    </div>
                  )}

                  {/* Context Links */}
                  <div>
                    <h4 style={{ margin: '8px 0 4px 0', fontSize: '0.9rem', color: '#1e293b' }}>Linked Context</h4>
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                      {detail.context.matter && (
                        <span className={`context-pill ${detail.context.matter.canOpen ? 'clickable' : 'locked'}`}>
                          ⚖ Matter: {detail.context.matter.title || detail.context.matter.referenceNumber}
                        </span>
                      )}
                      {detail.context.dak && (
                        <span className={`context-pill ${detail.context.dak.canOpen ? 'clickable' : 'locked'}`}>
                          📥 Dak: {detail.context.dak.referenceNumber}
                        </span>
                      )}
                      {detail.context.outward && (
                        <span className={`context-pill ${detail.context.outward.canOpen ? 'clickable' : 'locked'}`}>
                          📤 Outward: {detail.context.outward.referenceNumber}
                        </span>
                      )}
                      {detail.context.courtProceeding && (
                        <span className="context-pill clickable">
                          🏛 Proceeding: {detail.context.courtProceeding.title}
                        </span>
                      )}
                      {!detail.context.matter && !detail.context.dak && !detail.context.outward && !detail.context.courtProceeding && (
                        <span style={{ fontSize: '0.8rem', color: '#94a3b8' }}>No linked records</span>
                      )}
                    </div>
                  </div>

                  {/* Work Item Linking */}
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <h4 style={{ margin: '8px 0 4px 0', fontSize: '0.9rem', color: '#1e293b' }}>
                        Linked Work Item
                      </h4>
                      {detail.context.workItem ? (
                        detail.capabilities.canLinkWorkItem && (
                          <button className="btn-sm btn-danger" onClick={handleUnlinkWorkItem}>
                            Unlink Work Item
                          </button>
                        )
                      ) : (
                        detail.capabilities.canLinkWorkItem && (
                          <button
                            className="btn-sm"
                            onClick={() => {
                              setActionWorkItemId('');
                              setActionError(null);
                              setActiveActionModal('linkWork');
                            }}
                          >
                            + Link Work Item
                          </button>
                        )
                      )}
                    </div>
                    {detail.context.workItem ? (
                      <span className="context-pill clickable">
                        📋 Work Item: {detail.context.workItem.title}
                      </span>
                    ) : (
                      <p style={{ margin: 0, fontSize: '0.8rem', color: '#94a3b8' }}>
                        No work item linked.
                      </p>
                    )}
                  </div>

                  {/* Reminders Management */}
                  <div>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                      <h4 style={{ margin: '8px 0 4px 0', fontSize: '0.9rem', color: '#1e293b' }}>
                        Surfacing Reminders ({detail.reminders.filter((r) => r.isActive).length} active)
                      </h4>
                      {detail.capabilities.canManageReminders && (
                        <button
                          className="btn-sm"
                          onClick={() => {
                            setActionDaysBefore(1);
                            setActionReminderNote('');
                            setActionError(null);
                            setActiveActionModal('addReminder');
                          }}
                        >
                          + Add Reminder
                        </button>
                      )}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 4 }}>
                      {detail.reminders.length === 0 ? (
                        <span style={{ fontSize: '0.8rem', color: '#94a3b8' }}>No reminders set.</span>
                      ) : (
                        detail.reminders.map((rem) => (
                          <div
                            key={rem.id}
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              padding: '6px 10px',
                              background: rem.isActive ? '#f8fafc' : '#f1f5f9',
                              borderRadius: 4,
                              fontSize: '0.82rem',
                              opacity: rem.isActive ? 1 : 0.6
                            }}
                          >
                            <div>
                              🔔 <strong>{rem.daysBefore} days before</strong> (surfaces on {rem.targetReminderDate})
                              {rem.note && <span> — {rem.note}</span>}
                              {!rem.isActive && <span style={{ color: '#94a3b8' }}> (Inactive)</span>}
                            </div>
                            {rem.isActive && detail.capabilities.canManageReminders && (
                              <button
                                className="btn-sm"
                                onClick={() => handleDeactivateReminder(rem.id)}
                              >
                                Deactivate
                              </button>
                            )}
                          </div>
                        ))
                      )}
                    </div>
                  </div>

                  {/* Audit History Timeline */}
                  <div>
                    <h4 style={{ margin: '8px 0 4px 0', fontSize: '0.9rem', color: '#1e293b' }}>
                      Official Audit History
                    </h4>
                    <div className="history-timeline">
                      {detail.history.map((h) => (
                        <div key={h.id} className="history-item">
                          <div className="history-dot" />
                          <div className="history-action">{h.action}</div>
                          <div className="history-actor">
                            By {h.actorDisplayNameSnapshot || 'User'} ({h.actorDesignationSnapshot || 'Staff'}) on{' '}
                            {new Date(h.occurredAt).toLocaleString()}
                          </div>
                          {h.remarks && <div className="history-remarks">"{h.remarks}"</div>}
                        </div>
                      ))}
                    </div>
                  </div>
                </>
              )}
            </div>
            <div className="modal-footer">
              {detail?.capabilities.canReschedule && (
                <button
                  className="btn-sm"
                  onClick={() => {
                    setActionDate(detail.scheduledDate);
                    setActionTime(detail.scheduledTime || '');
                    setActionReason('');
                    setActionError(null);
                    setActiveActionModal('reschedule');
                  }}
                >
                  🕒 Reschedule
                </button>
              )}
              {detail?.capabilities.canReassign && (
                <button
                  className="btn-sm"
                  onClick={() => {
                    setActionTargetDeskId(detail.responsibleOfficeDeskId || '');
                    setActionTargetUserId(detail.assignedUserId || '');
                    setActionReason('');
                    setActionError(null);
                    setActiveActionModal('reassign');
                  }}
                >
                  🔄 Reassign
                </button>
              )}
              {detail?.capabilities.canComplete && (
                <button
                  className="btn-sm btn-success"
                  onClick={() => {
                    setActionReason('');
                    setActionError(null);
                    setActiveActionModal('complete');
                  }}
                >
                  ✓ Complete
                </button>
              )}
              {detail?.capabilities.canCancel && (
                <button
                  className="btn-sm btn-danger"
                  onClick={() => {
                    setActionReason('');
                    setActionError(null);
                    setActiveActionModal('cancel');
                  }}
                >
                  ✕ Cancel
                </button>
              )}
              <button className="btn-sm" onClick={closeDetail}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Action Modals from Detail: Reschedule, Reassign, Complete, Cancel, Add Reminder, Link Work */}
      {activeActionModal === 'reschedule' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Reschedule Event</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <div className="form-row">
                <div className="form-group">
                  <label>New Date <span className="required-star">*</span></label>
                  <input type="date" value={actionDate} onChange={(e) => setActionDate(e.target.value)} />
                </div>
                <div className="form-group">
                  <label>New Time</label>
                  <input type="time" value={actionTime} onChange={(e) => setActionTime(e.target.value)} />
                </div>
              </div>
              <div className="form-group">
                <label>Reason for Reschedule</label>
                <textarea rows={3} value={actionReason} onChange={(e) => setActionReason(e.target.value)} />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handleReschedule} disabled={actionSubmitting}>
                {actionSubmitting ? 'Saving...' : 'Save Reschedule'}
              </button>
            </div>
          </div>
        </div>
      )}

      {activeActionModal === 'reassign' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Reassign Event</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <div className="form-group">
                <label>Target Desk</label>
                <select
                  value={actionTargetDeskId}
                  onChange={(e) => {
                    setActionTargetDeskId(e.target.value);
                    setActionTargetUserId('');
                  }}
                >
                  <option value="">-- Select Desk --</option>
                  {options?.desks.map((d) => (
                    <option key={d.id} value={d.id}>{d.name}</option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label>Target Handler (Desk Member)</label>
                <select
                  value={actionTargetUserId}
                  onChange={(e) => setActionTargetUserId(e.target.value)}
                  disabled={!actionTargetDeskId}
                >
                  <option value="">-- No specific handler --</option>
                  {actionDeskMembers.map((m) => (
                    <option key={m.userId} value={m.userId}>{m.displayName} ({m.designationName || 'Staff'})</option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label>Reason</label>
                <textarea rows={3} value={actionReason} onChange={(e) => setActionReason(e.target.value)} />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handleReassign} disabled={actionSubmitting}>
                {actionSubmitting ? 'Saving...' : 'Save Reassign'}
              </button>
            </div>
          </div>
        </div>
      )}

      {activeActionModal === 'complete' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Complete Event</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <p>Are you sure you want to complete this event?</p>
              <div className="form-group">
                <label>Completion Notes</label>
                <textarea rows={3} value={actionReason} onChange={(e) => setActionReason(e.target.value)} />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Cancel</button>
              <button className="btn-sm btn-success" onClick={handleComplete} disabled={actionSubmitting}>
                {actionSubmitting ? 'Saving...' : 'Mark Completed'}
              </button>
            </div>
          </div>
        </div>
      )}

      {activeActionModal === 'cancel' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Cancel Event</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <div className="form-group">
                <label>Cancellation Reason <span className="required-star">*</span></label>
                <textarea rows={3} value={actionReason} onChange={(e) => setActionReason(e.target.value)} required />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Back</button>
              <button className="btn-sm btn-danger" onClick={handleCancel} disabled={actionSubmitting}>
                {actionSubmitting ? 'Cancelling...' : 'Confirm Cancel'}
              </button>
            </div>
          </div>
        </div>
      )}

      {activeActionModal === 'addReminder' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Add Reminder</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <div className="form-row">
                <div className="form-group">
                  <label>Days Before Event <span className="required-star">*</span></label>
                  <input
                    type="number"
                    min={0}
                    max={365}
                    value={actionDaysBefore}
                    onChange={(e) => setActionDaysBefore(parseInt(e.target.value, 10) || 0)}
                  />
                </div>
                <div className="form-group">
                  <label>Note</label>
                  <input
                    type="text"
                    value={actionReminderNote}
                    onChange={(e) => setActionReminderNote(e.target.value)}
                  />
                </div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handleAddReminder} disabled={actionSubmitting}>
                {actionSubmitting ? 'Adding...' : 'Add Reminder'}
              </button>
            </div>
          </div>
        </div>
      )}

      {activeActionModal === 'linkWork' && detail && (
        <div className="attention-modal-overlay" onClick={() => setActiveActionModal(null)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Link Work Item</h3>
              <button className="modal-close" onClick={() => setActiveActionModal(null)}>×</button>
            </div>
            <div className="modal-body">
              {actionError && <div style={{ color: '#b91c1c' }}>{actionError}</div>}
              <div className="form-group">
                <label>Work Item ID (GUID) <span className="required-star">*</span></label>
                <input
                  type="text"
                  placeholder="e.g. 11111111-1111-1111-1111-111111111111"
                  value={actionWorkItemId}
                  onChange={(e) => setActionWorkItemId(e.target.value)}
                />
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setActiveActionModal(null)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handleLinkWorkItem} disabled={actionSubmitting}>
                {actionSubmitting ? 'Linking...' : 'Confirm Link'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create Scheduled Event Modal */}
      {showCreateModal && (
        <div className="attention-modal-overlay" onClick={() => setShowCreateModal(false)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Schedule Official Event</h3>
              <button className="modal-close" onClick={() => setShowCreateModal(false)}>×</button>
            </div>
            <div className="modal-body">
              {createError && <div style={{ color: '#b91c1c' }}>{createError}</div>}

              <div className="form-row">
                <div className="form-group">
                  <label>Workstream <span className="required-star">*</span></label>
                  <select
                    value={createWsId}
                    onChange={(e) => {
                      setCreateWsId(e.target.value);
                      setCreateDeskId('');
                      setCreateUserId('');
                    }}
                  >
                    <option value="">-- Select Workstream --</option>
                    {options?.workstreams.map((w) => (
                      <option key={w.id} value={w.id}>{w.name}</option>
                    ))}
                  </select>
                </div>

                <div className="form-group">
                  <label>Event Kind <span className="required-star">*</span></label>
                  <select
                    value={createKind}
                    onChange={(e) => setCreateKind(e.target.value as ScheduledEventKind)}
                  >
                    <option value="Hearing">Hearing</option>
                    <option value="ComplianceDeadline">Compliance Deadline</option>
                    <option value="SiteInspection">Site Inspection</option>
                    <option value="Meeting">Meeting</option>
                    <option value="OrderDelivery">Order Delivery</option>
                    <option value="CompensationDisbursement">Compensation Disbursement</option>
                    <option value="ReportSubmission">Report Submission</option>
                    <option value="NoticeExpiry">Notice Expiry</option>
                    <option value="Other">Other</option>
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Title <span className="required-star">*</span></label>
                <input
                  type="text"
                  placeholder="e.g. High Court Compliance Hearing"
                  value={createTitle}
                  onChange={(e) => setCreateTitle(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label>Description</label>
                <textarea
                  rows={2}
                  placeholder="Official obligation instructions, court details..."
                  value={createDescription}
                  onChange={(e) => setCreateDescription(e.target.value)}
                />
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Scheduled Date <span className="required-star">*</span></label>
                  <input type="date" value={createDate} onChange={(e) => setCreateDate(e.target.value)} />
                </div>
                <div className="form-group">
                  <label>Scheduled Time</label>
                  <input type="time" value={createTime} onChange={(e) => setCreateTime(e.target.value)} />
                </div>
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Priority</label>
                  <select
                    value={createPriority}
                    onChange={(e) => setCreatePriority(e.target.value as ScheduledEventPriority)}
                  >
                    <option value="Low">Low</option>
                    <option value="Medium">Medium</option>
                    <option value="High">High</option>
                    <option value="Urgent">Urgent</option>
                  </select>
                </div>

                <div className="form-group">
                  <label>Responsible Desk</label>
                  <select
                    value={createDeskId}
                    onChange={(e) => {
                      setCreateDeskId(e.target.value);
                      setCreateUserId('');
                    }}
                  >
                    <option value="">-- No Desk Assigned --</option>
                    {createDeskOptions.map((d) => (
                      <option key={d.id} value={d.id}>{d.name}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Assigned Handler (Desk Member)</label>
                <select
                  value={createUserId}
                  onChange={(e) => setCreateUserId(e.target.value)}
                  disabled={!createDeskId}
                >
                  <option value="">-- Desk General (Unassigned to officer) --</option>
                  {createMemberOptions.map((m) => (
                    <option key={m.userId} value={m.userId}>{m.displayName} ({m.designationName || 'Staff'})</option>
                  ))}
                </select>
              </div>

              <details style={{ fontSize: '0.84rem', color: '#475569' }}>
                <summary style={{ cursor: 'pointer', fontWeight: 600 }}>Optional Context Links (IDs)</summary>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8, marginTop: 8 }}>
                  <input
                    type="text"
                    placeholder="Matter ID"
                    value={createMatterId}
                    onChange={(e) => setCreateMatterId(e.target.value)}
                  />
                  <input
                    type="text"
                    placeholder="Dak ID"
                    value={createDakId}
                    onChange={(e) => setCreateDakId(e.target.value)}
                  />
                  <input
                    type="text"
                    placeholder="Outward ID"
                    value={createOutwardId}
                    onChange={(e) => setCreateOutwardId(e.target.value)}
                  />
                  <input
                    type="text"
                    placeholder="WorkItem ID"
                    value={createWorkItemId}
                    onChange={(e) => setCreateWorkItemId(e.target.value)}
                  />
                  <input
                    type="text"
                    placeholder="Court Case ID"
                    value={createCourtCaseId}
                    onChange={(e) => setCreateCourtCaseId(e.target.value)}
                  />
                  <input
                    type="text"
                    placeholder="Court Proceeding ID"
                    value={createCourtProceedingId}
                    onChange={(e) => setCreateCourtProceedingId(e.target.value)}
                  />
                </div>
              </details>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setShowCreateModal(false)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handleCreateSubmit} disabled={createSubmitting}>
                {createSubmitting ? 'Scheduling...' : 'Create Scheduled Event'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Promote Court Proceeding Modal */}
      {showPromoteModal && (
        <div className="attention-modal-overlay" onClick={() => setShowPromoteModal(false)}>
          <div className="attention-modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <h3>Promote Court Proceeding to Official Schedule</h3>
              <button className="modal-close" onClick={() => setShowPromoteModal(false)}>×</button>
            </div>
            <div className="modal-body">
              {promoteError && <div style={{ color: '#b91c1c' }}>{promoteError}</div>}
              <div className="form-group">
                <label>Court Proceeding ID <span className="required-star">*</span></label>
                <input
                  type="text"
                  placeholder="e.g. proceeding GUID"
                  value={promoteProceedingId}
                  onChange={(e) => setPromoteProceedingId(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label>Custom Title (Optional - defaults to Case Number & Proceeding)</label>
                <input
                  type="text"
                  placeholder="e.g. Next Hearing - Court of ADJ"
                  value={promoteTitle}
                  onChange={(e) => setPromoteTitle(e.target.value)}
                />
              </div>

              <div className="form-row">
                <div className="form-group">
                  <label>Priority</label>
                  <select
                    value={promotePriority}
                    onChange={(e) => setPromotePriority(e.target.value as ScheduledEventPriority)}
                  >
                    <option value="Low">Low</option>
                    <option value="Medium">Medium</option>
                    <option value="High">High</option>
                    <option value="Urgent">Urgent</option>
                  </select>
                </div>

                <div className="form-group">
                  <label>Responsible Desk</label>
                  <select
                    value={promoteDeskId}
                    onChange={(e) => {
                      setPromoteDeskId(e.target.value);
                      setPromoteUserId('');
                    }}
                  >
                    <option value="">-- No Desk Assigned --</option>
                    {options?.desks.map((d) => (
                      <option key={d.id} value={d.id}>{d.name}</option>
                    ))}
                  </select>
                </div>
              </div>
            </div>
            <div className="modal-footer">
              <button className="btn-sm" onClick={() => setShowPromoteModal(false)}>Cancel</button>
              <button className="btn-sm btn-primary" onClick={handlePromoteSubmit} disabled={promoteSubmitting}>
                {promoteSubmitting ? 'Promoting...' : 'Promote to Schedule'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
