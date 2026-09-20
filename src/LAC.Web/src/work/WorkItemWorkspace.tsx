import { useEffect, useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthProvider';
import type {
  WorkItemDetail,
  WorkItemEvent,
  WorkItemPriority,
  WorkItemStatus,
  ContributorOption
} from './types';
import './work.css';

interface WorkItemWorkspaceProps {
  currentUserPermissions?: string[];
}

export function WorkItemWorkspace({ currentUserPermissions: _currentUserPermissions }: WorkItemWorkspaceProps = {}) {
  const { id } = useParams<{ id: string }>();
  const { user } = useAuth();

  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [item, setItem] = useState<WorkItemDetail | null>(null);
  const [timeline, setTimeline] = useState<WorkItemEvent[]>([]);

  // Update composer state
  const [updateMessage, setUpdateMessage] = useState('');
  const [postingUpdate, setPostingUpdate] = useState(false);
  const [updateError, setUpdateError] = useState<string | null>(null);

  // Attachment upload state
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadTitle, setUploadTitle] = useState('');
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [showUploadForm, setShowUploadForm] = useState(false);

  // Action states
  const [startingWork, setStartingWork] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  // Contributor modal states
  const [showAddContributorModal, setShowAddContributorModal] = useState(false);
  const [contributorSearchQuery, setContributorSearchQuery] = useState('');
  const [contributorOptions, setContributorOptions] = useState<ContributorOption[]>([]);
  const [selectedUserId, setSelectedUserId] = useState<string>('');
  const [contributorInstructions, setContributorInstructions] = useState('');
  const [loadingOptions, setLoadingOptions] = useState(false);
  const [addingContributor, setAddingContributor] = useState(false);
  const [addContributorError, setAddContributorError] = useState<string | null>(null);

  // Return remarks modal states
  const [returnModalContributorId, setReturnModalContributorId] = useState<string | null>(null);
  const [returnRemarks, setReturnRemarks] = useState('');
  const [returningContributor, setReturningContributor] = useState(false);
  const [returnError, setReturnError] = useState<string | null>(null);

  // Submit contribution modal states
  const [showSubmitModal, setShowSubmitModal] = useState(false);
  const [submitNote, setSubmitNote] = useState('');
  const [submittingContribution, setSubmittingContribution] = useState(false);

  // Load Work Item details and timeline
  const loadData = useCallback(async () => {
    if (!id) return;
    try {
      const [resDetail, resTimeline] = await Promise.all([
        fetch(`/api/work-items/${id}`, { credentials: 'include' }),
        fetch(`/api/work-items/${id}/timeline`, { credentials: 'include' })
      ]);

      if (!resDetail.ok) {
        if (resDetail.status === 403) {
          throw new Error('Access denied: You do not have permission to view this work item.');
        }
        if (resDetail.status === 404) {
          throw new Error('Work item not found.');
        }
        throw new Error(`Failed to load work item (${resDetail.status})`);
      }

      const detailJson: WorkItemDetail = await resDetail.json();
      setItem(detailJson);

      if (resTimeline.ok) {
        const timelineJson: WorkItemEvent[] = await resTimeline.json();
        setTimeline(timelineJson);
      }

      // Mark seen and refresh detail/timeline on first open so FirstSeen badge/event is live immediately
      if (detailJson.currentAssignment && !detailJson.currentAssignment.firstSeenAt) {
        try {
          const seenRes = await fetch(`/api/work-items/${id}/seen`, {
            method: 'POST',
            credentials: 'include'
          });
          if (seenRes.ok) {
            const [freshDetailRes, freshTimelineRes] = await Promise.all([
              fetch(`/api/work-items/${id}`, { credentials: 'include' }),
              fetch(`/api/work-items/${id}/timeline`, { credentials: 'include' })
            ]);
            if (freshDetailRes.ok) {
              const freshDetail: WorkItemDetail = await freshDetailRes.json();
              setItem(freshDetail);
            }
            if (freshTimelineRes.ok) {
              const freshTimeline: WorkItemEvent[] = await freshTimelineRes.json();
              setTimeline(freshTimeline);
            }
          }
        } catch {
          // ignore mark seen failure
        }
      }
    } catch (err: any) {
      setError(err.message || 'Error loading work item.');
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleStartWork = async () => {
    if (!item) return;
    setActionError(null);
    setStartingWork(true);

    try {
      const res = await fetch(`/api/work-items/${item.id}/start`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ expectedRevision: item.revision })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to start work.');
      }

      await loadData();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setStartingWork(false);
    }
  };

  const handlePostUpdate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!item || !updateMessage.trim()) return;

    setUpdateError(null);
    setPostingUpdate(true);

    try {
      const res = await fetch(`/api/work-items/${item.id}/updates`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          message: updateMessage.trim(),
          expectedRevision: item.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to post update.');
      }

      setUpdateMessage('');
      await loadData();
    } catch (err: any) {
      setUpdateError(err.message);
    } finally {
      setPostingUpdate(false);
    }
  };

  const handleUploadAttachment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!item || !uploadFile) return;

    setUploadError(null);
    setUploading(true);

    try {
      const formData = new FormData();
      formData.append('file', uploadFile);
      formData.append('title', uploadTitle.trim() || uploadFile.name);
      formData.append('attachmentType', 'SupportingDocument');
      formData.append('expectedRevision', item.revision.toString());

      const res = await fetch(`/api/work-items/${item.id}/attachments`, {
        method: 'POST',
        credentials: 'include',
        body: formData
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to upload attachment.');
      }

      setUploadFile(null);
      setUploadTitle('');
      setShowUploadForm(false);
      await loadData();
    } catch (err: any) {
      setUploadError(err.message);
    } finally {
      setUploading(false);
    }
  };

  const handleRemoveAttachment = async (attachmentId: string) => {
    if (!item || !confirm('Are you sure you want to remove this attachment from the work item?')) return;

    try {
      const res = await fetch(`/api/work-items/${item.id}/attachments/${attachmentId}?expectedRevision=${item.revision}`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ expectedRevision: item.revision })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to remove attachment.');
      }

      await loadData();
    } catch (err: any) {
      alert(err.message);
    }
  };

  const fetchContributorOptions = useCallback(async (search: string) => {
    if (!id) return;
    setLoadingOptions(true);
    try {
      const qParam = search.trim() ? `?q=${encodeURIComponent(search.trim())}` : '';
      const res = await fetch(`/api/work-items/${id}/contributor-options${qParam}`, { credentials: 'include' });
      if (res.ok) {
        const resData = await res.json();
        setContributorOptions(resData.options || []);
      }
    } catch {
      // ignore
    } finally {
      setLoadingOptions(false);
    }
  }, [id]);

  useEffect(() => {
    if (showAddContributorModal) {
      const timer = setTimeout(() => {
        fetchContributorOptions(contributorSearchQuery);
      }, 250);
      return () => clearTimeout(timer);
    }
  }, [showAddContributorModal, contributorSearchQuery, fetchContributorOptions]);

  const handleAddContributor = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!item || !selectedUserId) return;
    setAddingContributor(true);
    setAddContributorError(null);
    try {
      const res = await fetch(`/api/work-items/${item.id}/contributors`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          userId: selectedUserId,
          instructions: contributorInstructions.trim() || null,
          expectedRevision: item.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to add helper.');
      }

      setShowAddContributorModal(false);
      setSelectedUserId('');
      setContributorInstructions('');
      await loadData();
    } catch (err: any) {
      setAddContributorError(err.message);
    } finally {
      setAddingContributor(false);
    }
  };

  const handleSubmitContribution = async (contributorId: string) => {
    if (!item) return;
    setSubmittingContribution(true);
    setActionError(null);
    try {
      const res = await fetch(`/api/work-items/${item.id}/contributors/${contributorId}/submit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          expectedRevision: item.revision,
          note: submitNote.trim() || null
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to submit contribution.');
      }

      setShowSubmitModal(false);
      setSubmitNote('');
      await loadData();
    } catch (err: any) {
      setActionError(err.message);
    } finally {
      setSubmittingContribution(false);
    }
  };

  const handleReturnContribution = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!item || !returnModalContributorId || !returnRemarks.trim()) return;
    setReturningContributor(true);
    setReturnError(null);
    try {
      const res = await fetch(`/api/work-items/${item.id}/contributors/${returnModalContributorId}/return`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          expectedRevision: item.revision,
          remarks: returnRemarks.trim()
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to return contribution.');
      }

      setReturnModalContributorId(null);
      setReturnRemarks('');
      await loadData();
    } catch (err: any) {
      setReturnError(err.message);
    } finally {
      setReturningContributor(false);
    }
  };

  const handleAcceptContribution = async (contributorId: string) => {
    if (!item || !confirm('Are you sure you want to accept this contribution?')) return;
    setActionError(null);
    try {
      const res = await fetch(`/api/work-items/${item.id}/contributors/${contributorId}/accept`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          expectedRevision: item.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to accept contribution.');
      }

      await loadData();
    } catch (err: any) {
      setActionError(err.message);
    }
  };

  const handleRemoveContributor = async (contributorId: string, name: string) => {
    if (!item || !confirm(`Remove helper ${name} from this work item?`)) return;
    setActionError(null);
    try {
      const res = await fetch(`/api/work-items/${item.id}/contributors/${contributorId}?expectedRevision=${item.revision}`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({
          expectedRevision: item.revision
        })
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to remove helper.');
      }

      await loadData();
    } catch (err: any) {
      setActionError(err.message);
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

  const renderPriorityBadge = (priority: WorkItemPriority) => {
    const cls =
      priority === 'Immediate'
        ? 'badge badge-immediate'
        : priority === 'Urgent'
        ? 'badge badge-urgent'
        : 'badge badge-routine';
    return <span className={cls}>{priority}</span>;
  };

  const renderStatusBadge = (status: WorkItemStatus) => {
    let cls = 'badge badge-assigned';
    if (status === 'InProgress') cls = 'badge badge-inprogress';
    else if (status === 'SubmittedForReview') cls = 'badge badge-submitted';
    else if (status === 'ReturnedForCorrection') cls = 'badge badge-returned';
    else if (status === 'Completed') cls = 'badge badge-completed';
    else if (status === 'Cancelled') cls = 'badge badge-cancelled';

    return <span className={cls}>{status}</span>;
  };

  const renderContributorBadge = (status: string) => {
    switch (status) {
      case 'Active':
        return <span className="badge-contributor-active">Working</span>;
      case 'Submitted':
        return <span className="badge-contributor-submitted">Submitted for Review</span>;
      case 'Returned':
        return <span className="badge-contributor-returned">Returned for Correction</span>;
      case 'Accepted':
        return <span className="badge-contributor-accepted">Work Accepted</span>;
      case 'Removed':
        return <span className="badge-contributor-removed">Removed</span>;
      default:
        return <span className="badge">{status}</span>;
    }
  };

  if (loading) {
    return (
      <div className="workspace-page" style={{ textAlign: 'center', padding: '60px 0', color: '#64748b' }}>
        Loading work item workspace...
      </div>
    );
  }

  if (error || !item) {
    return (
      <div className="workspace-page">
        <div
          style={{
            background: '#fef2f2',
            border: '1px solid #fecaca',
            color: '#b91c1c',
            padding: '20px',
            borderRadius: '8px'
          }}
        >
          {error || 'Work item could not be loaded.'}
        </div>
      </div>
    );
  }

  const isTerminal = item.status === 'Completed' || item.status === 'Cancelled';
  const myContributor = user
    ? item.contributors.find(
        (c) =>
          c.userId.toLowerCase() === user.id.toLowerCase() &&
          (c.status === 'Active' || c.status === 'Returned' || c.status === 'Submitted')
      )
    : undefined;

  return (
    <div className="workspace-page">
      <nav className="breadcrumbs" aria-label="Breadcrumb">
        <Link to="/my-work">My Work</Link>
        <span className="breadcrumb-separator">/</span>
        <span>{item.workstreamName}</span>
        <span className="breadcrumb-separator">/</span>
        <span>{item.title}</span>
      </nav>

      {/* Top Banner */}
      <div className="workspace-header-banner">
        <div className="workspace-title-row">
          <div className="workspace-title-area">
            <h1>{item.title}</h1>
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              {renderPriorityBadge(item.priority)}
              {renderStatusBadge(item.status)}
              <span className="work-tag-workstream">{item.workstreamName}</span>
              <span style={{ fontSize: '0.8rem', color: '#64748b' }}>Rev {item.revision}</span>
            </div>
          </div>

          {/* Action Buttons */}
          {!isTerminal && (
            <div className="workspace-action-strip">
              {item.capabilities.canStartWork && (
                <button
                  className="button primary-button"
                  onClick={handleStartWork}
                  disabled={startingWork}
                >
                  {startingWork ? 'Starting...' : '▶ Start Work'}
                </button>
              )}

              {item.capabilities.canSubmitContribution && myContributor && (myContributor.status === 'Active' || myContributor.status === 'Returned') && (
                <button
                  className="button primary-button"
                  style={{ backgroundColor: '#0284c7', borderColor: '#0284c7' }}
                  onClick={() => setShowSubmitModal(true)}
                  disabled={submittingContribution}
                >
                  {myContributor.status === 'Returned' ? 'Submit Again for Review' : 'Submit Contribution for Review'}
                </button>
              )}

              {item.capabilities.canAddContributor && (
                <button
                  className="button secondary-button"
                  onClick={() => setShowAddContributorModal(true)}
                >
                  + Ask for Help
                </button>
              )}
            </div>
          )}
        </div>

        {actionError && (
          <div style={{ color: '#b91c1c', fontSize: '0.85rem', fontWeight: 500 }}>
            {actionError}
          </div>
        )}

        {/* Metadata Grid */}
        <div className="workspace-meta-grid">
          <div className="workspace-meta-item">
            <span className="workspace-meta-label">Responsible Desk</span>
            <span className="workspace-meta-value">
              {item.currentAssignment?.officeDeskName || '—'}
            </span>
          </div>

          <div className="workspace-meta-item">
            <span className="workspace-meta-label">Assigned Officer</span>
            <span className="workspace-meta-value">
              {item.currentAssignment?.assignedUserDisplayName || 'Unallocated'}
              {item.currentAssignment?.assignedUserDesignation && (
                <span style={{ fontSize: '0.8rem', color: '#64748b', marginLeft: 4 }}>
                  ({item.currentAssignment.assignedUserDesignation})
                </span>
              )}
            </span>
          </div>

          <div className="workspace-meta-item">
            <span className="workspace-meta-label">Requested By</span>
            <span className="workspace-meta-value">
              {item.requestedByDisplayName}
              {item.requestedByDesignation && (
                <span style={{ fontSize: '0.8rem', color: '#64748b', marginLeft: 4 }}>
                  ({item.requestedByDesignation})
                </span>
              )}
            </span>
          </div>

          <div className="workspace-meta-item">
            <span className="workspace-meta-label">Due Date</span>
            <span className="workspace-meta-value">{formatDateTime(item.dueAt)}</span>
          </div>

          <div className="workspace-meta-item">
            <span className="workspace-meta-label">Assigned At</span>
            <span className="workspace-meta-value">
              {formatDateTime(item.currentAssignment?.assignedAt)}
            </span>
          </div>

          <div className="workspace-meta-item">
            <span className="workspace-meta-label">First Seen</span>
            <span className="workspace-meta-value">
              {formatDateTime(item.currentAssignment?.firstSeenAt)}
            </span>
          </div>
        </div>
      </div>

      {/* Instructions */}
      {item.instructions && (
        <div className="workspace-instructions">
          <h3>Instructions & Expected Results</h3>
          <p>{item.instructions}</p>
        </div>
      )}

      {/* Linked Context (Matter / Dak) */}
      {(item.matterLinks.length > 0 || item.dakLinks.length > 0) && (
        <div className="workspace-card">
          <h2>Linked Official Context</h2>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            {item.matterLinks.map((m) => (
              <div
                key={m.matterId}
                style={{
                  background: '#f8fafc',
                  border: '1px solid #e2e8f0',
                  borderRadius: 6,
                  padding: '10px 14px',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 4
                }}
              >
                <div style={{ fontSize: '0.78rem', color: '#64748b', fontWeight: 600 }}>
                  ⚖ LEGAL MATTER
                </div>
                {m.isAuthorized ? (
                  <div>
                    <Link
                      to={`/matters/${m.matterId}`}
                      style={{ fontWeight: 600, color: '#1d4ed8', textDecoration: 'none' }}
                    >
                      {m.title || 'View Matter'}
                    </Link>
                    <div style={{ fontSize: '0.8rem', color: '#475569' }}>
                      Status: {m.status || 'Open'} · {m.matterType}
                    </div>
                  </div>
                ) : (
                  <div style={{ fontSize: '0.85rem', color: '#64748b', fontStyle: 'italic' }}>
                    Linked Matter (Confidential — requires independent authorization)
                  </div>
                )}
              </div>
            ))}

            {item.dakLinks.map((d) => (
              <div
                key={d.dakId}
                style={{
                  background: '#f8fafc',
                  border: '1px solid #e2e8f0',
                  borderRadius: 6,
                  padding: '10px 14px',
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 4
                }}
              >
                <div style={{ fontSize: '0.78rem', color: '#64748b', fontWeight: 600 }}>
                  📥 DAK CORRESPONDENCE
                </div>
                {d.isAuthorized ? (
                  <div>
                    <Link
                      to={`/dak/${d.dakId}`}
                      style={{ fontWeight: 600, color: '#1d4ed8', textDecoration: 'none' }}
                    >
                      Diary #{d.diaryNumber}: {d.subject || 'View Dak'}
                    </Link>
                    <div style={{ fontSize: '0.8rem', color: '#475569' }}>
                      Status: {d.status} · Priority: {d.priority}
                    </div>
                  </div>
                ) : (
                  <div style={{ fontSize: '0.85rem', color: '#64748b', fontStyle: 'italic' }}>
                    Linked Dak (Confidential — requires independent authorization)
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Main Workspace Layout */}
      <div className="workspace-grid-layout">
        {/* Left Column: Progress Updates & Attachments */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
          {/* Help & Contribution Section */}
          <div className="workspace-card">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
              <h2 style={{ margin: 0 }}>
                Delegated Assistance & Helpers ({item.contributors.filter((c) => c.status !== 'Removed').length})
              </h2>
              {item.capabilities.canAddContributor && !isTerminal && (
                <button
                  className="button secondary-button"
                  style={{ fontSize: '0.82rem', padding: '4px 10px' }}
                  onClick={() => setShowAddContributorModal(true)}
                >
                  + Ask for Help
                </button>
              )}
            </div>

            {/* If caller is a contributor and waiting for review */}
            {myContributor && myContributor.status === 'Submitted' && (
              <div
                style={{
                  background: '#fffbeb',
                  border: '1px solid #fde68a',
                  color: '#92400e',
                  padding: '10px 14px',
                  borderRadius: 6,
                  fontSize: '0.85rem',
                  marginBottom: 12
                }}
              >
                ℹ Your contribution was submitted for review on {formatDateTime(myContributor.submittedAt)}. Modifications are paused while under review.
              </div>
            )}

            {/* If caller is a contributor and returned for correction */}
            {myContributor && myContributor.status === 'Returned' && (
              <div
                style={{
                  background: '#fff7ed',
                  border: '1px solid #fed7aa',
                  color: '#9a3412',
                  padding: '10px 14px',
                  borderRadius: 6,
                  fontSize: '0.85rem',
                  marginBottom: 12
                }}
              >
                ⚠ Your contribution was returned for correction on {formatDateTime(myContributor.reviewedAt)} by {myContributor.reviewedByDisplayName || 'Reviewer'}. Please review the updates, make changes, and submit again.
              </div>
            )}

            {item.contributors.filter((c) => c.status !== 'Removed').length === 0 ? (
              <div style={{ color: '#94a3b8', fontSize: '0.88rem', fontStyle: 'italic' }}>
                No active helpers or delegated assistance assigned.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                {item.contributors
                  .filter((c) => c.status !== 'Removed')
                  .map((c) => (
                    <div key={c.contributorId} className={`contributor-card contributor-${c.status.toLowerCase()}`}>
                      <div className="contributor-header">
                        <div>
                          <span className="contributor-name">{c.displayName}</span>
                          {c.designation && (
                            <span style={{ fontSize: '0.8rem', color: '#64748b', marginLeft: 4 }}>
                              ({c.designation})
                            </span>
                          )}
                        </div>
                        <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                          {renderContributorBadge(c.status)}
                        </div>
                      </div>

                      <div className="contributor-meta">
                        Enlisted by {c.addedByDisplayName} on {formatDateTime(c.addedAt)}
                        {c.submittedAt && ` · Submitted on ${formatDateTime(c.submittedAt)}`}
                        {c.reviewedAt &&
                          ` · Reviewed on ${formatDateTime(c.reviewedAt)} by ${
                            c.reviewedByDisplayName || 'Reviewer'
                          }`}
                      </div>

                      {c.instructions && (
                        <div className="contributor-instructions">
                          <div
                            style={{
                              fontSize: '0.75rem',
                              fontWeight: 600,
                              color: '#475569',
                              marginBottom: 2
                            }}
                          >
                            TASK GUIDANCE:
                          </div>
                          {c.instructions}
                        </div>
                      )}

                      <div className="contributor-actions">
                        {/* Review actions for Responsible Officer */}
                        {item.capabilities.canReviewContributions && c.status === 'Submitted' && (
                          <>
                            <button
                              className="button primary-button"
                              style={{
                                fontSize: '0.8rem',
                                padding: '4px 10px',
                                backgroundColor: '#15803d',
                                borderColor: '#15803d'
                              }}
                              onClick={() => handleAcceptContribution(c.contributorId)}
                            >
                              ✓ Accept Work
                            </button>
                            <button
                              className="button secondary-button"
                              style={{
                                fontSize: '0.8rem',
                                padding: '4px 10px',
                                color: '#c2410c',
                                borderColor: '#fdba74'
                              }}
                              onClick={() => {
                                setReturnModalContributorId(c.contributorId);
                                setReturnRemarks('');
                                setReturnError(null);
                              }}
                            >
                              ↩ Return for Correction
                            </button>
                          </>
                        )}

                        {/* Contributor's own Submit action */}
                        {user &&
                          c.userId.toLowerCase() === user.id.toLowerCase() &&
                          item.capabilities.canSubmitContribution &&
                          (c.status === 'Active' || c.status === 'Returned') && (
                            <button
                              className="button primary-button"
                              style={{
                                fontSize: '0.8rem',
                                padding: '4px 10px',
                                backgroundColor: '#0284c7',
                                borderColor: '#0284c7'
                              }}
                              onClick={() => {
                                setShowSubmitModal(true);
                                setSubmitNote('');
                              }}
                            >
                              {c.status === 'Returned'
                                ? 'Submit Again for Review'
                                : 'Submit Contribution for Review'}
                            </button>
                          )}

                        {/* Remove action */}
                        {item.capabilities.canRemoveContributor && c.status !== 'Accepted' && (
                          <button
                            className="button secondary-button"
                            style={{
                              fontSize: '0.78rem',
                              padding: '3px 8px',
                              color: '#b91c1c',
                              marginLeft: 'auto'
                            }}
                            onClick={() => handleRemoveContributor(c.contributorId, c.displayName)}
                          >
                            Remove Helper
                          </button>
                        )}
                      </div>
                    </div>
                  ))}
              </div>
            )}
          </div>

          {/* Progress Updates Section */}
          <div className="workspace-card">
            <h2>Progress Updates ({item.updates.length})</h2>

            {!isTerminal && item.capabilities.canAddUpdate && (
              <form onSubmit={handlePostUpdate} className="update-composer">
                <textarea
                  placeholder="Record what has been done, current findings, or draft references..."
                  value={updateMessage}
                  onChange={(e) => setUpdateMessage(e.target.value)}
                  disabled={postingUpdate}
                  required
                />
                {updateError && (
                  <div style={{ color: '#b91c1c', fontSize: '0.82rem', marginTop: 4 }}>
                    {updateError}
                  </div>
                )}
                <div className="update-composer-actions">
                  <button
                    type="submit"
                    className="button primary-button"
                    disabled={postingUpdate || !updateMessage.trim()}
                  >
                    {postingUpdate ? 'Saving...' : 'Add Progress Update'}
                  </button>
                </div>
              </form>
            )}

            {item.updates.length === 0 ? (
              <div style={{ color: '#94a3b8', fontSize: '0.88rem', fontStyle: 'italic' }}>
                No progress updates recorded yet.
              </div>
            ) : (
              <div className="updates-list">
                {item.updates.map((update) => (
                  <div key={update.id} className="update-entry">
                    <div className="update-entry-meta">
                      <span className="update-entry-author">
                        {update.addedByDisplayName}
                        {update.addedByDesignation && ` (${update.addedByDesignation})`}
                      </span>
                      <span>{formatDateTime(update.addedAt)}</span>
                    </div>
                    <div className="update-entry-body">{update.message}</div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Attachments Section */}
          <div className="workspace-card">
            <h2>
              <span>Working Files & Attachments ({item.attachments.length})</span>
              {!isTerminal && item.capabilities.canUploadAttachment && !showUploadForm && (
                <button
                  className="button secondary-button"
                  style={{ fontSize: '0.8rem', padding: '4px 10px' }}
                  onClick={() => setShowUploadForm(true)}
                >
                  + Upload File
                </button>
              )}
            </h2>

            {showUploadForm && (
              <form
                onSubmit={handleUploadAttachment}
                style={{
                  background: '#f8fafc',
                  border: '1px solid #cbd5e1',
                  borderRadius: 6,
                  padding: 14,
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 10
                }}
              >
                <div style={{ fontWeight: 600, fontSize: '0.88rem', color: '#1e293b' }}>
                  Attach Document to Work Item
                </div>
                {uploadError && (
                  <div style={{ color: '#b91c1c', fontSize: '0.82rem' }}>{uploadError}</div>
                )}
                <input
                  type="file"
                  onChange={(e) => setUploadFile(e.target.files ? e.target.files[0] : null)}
                  required
                />
                <input
                  type="text"
                  placeholder="Document Title / Note (optional)"
                  value={uploadTitle}
                  onChange={(e) => setUploadTitle(e.target.value)}
                />
                <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8 }}>
                  <button
                    type="button"
                    className="button secondary-button"
                    onClick={() => {
                      setShowUploadForm(false);
                      setUploadFile(null);
                    }}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="button primary-button"
                    disabled={uploading || !uploadFile}
                  >
                    {uploading ? 'Uploading...' : 'Upload'}
                  </button>
                </div>
              </form>
            )}

            {item.attachments.length === 0 ? (
              <div style={{ color: '#94a3b8', fontSize: '0.88rem', fontStyle: 'italic' }}>
                No attachments uploaded.
              </div>
            ) : (
              <table className="attachments-table">
                <thead>
                  <tr>
                    <th>File Name</th>
                    <th>Size</th>
                    <th>Uploaded</th>
                    <th style={{ textAlign: 'right' }}>Action</th>
                  </tr>
                </thead>
                <tbody>
                  {item.attachments.map((att) => (
                    <tr key={att.id}>
                      <td>
                        <div style={{ fontWeight: 500 }}>{att.title || att.fileName}</div>
                        <div style={{ fontSize: '0.75rem', color: '#64748b' }}>{att.fileName}</div>
                      </td>
                      <td>{(att.fileSizeBytes / 1024).toFixed(1)} KB</td>
                      <td>{formatDateTime(att.createdAt)}</td>
                      <td style={{ textAlign: 'right' }}>
                        <a
                          href={`/api/work-items/${item.id}/attachments/${att.id}/content`}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="button secondary-button"
                          style={{
                            fontSize: '0.78rem',
                            padding: '3px 8px',
                            marginRight: 6,
                            textDecoration: 'none'
                          }}
                        >
                          Download
                        </a>
                        {!isTerminal && (item.capabilities.canAddUpdate || item.capabilities.canContribute) && (
                          <button
                            className="button secondary-button"
                            style={{
                              fontSize: '0.78rem',
                              padding: '3px 8px',
                              color: '#b91c1c'
                            }}
                            onClick={() => handleRemoveAttachment(att.id)}
                          >
                            Remove
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>

        {/* Right Column: Activity Timeline */}
        <div>
          <div className="workspace-card">
            <h2>Activity Timeline</h2>
            {timeline.length === 0 ? (
              <div style={{ color: '#94a3b8', fontSize: '0.88rem', fontStyle: 'italic' }}>
                No events recorded.
              </div>
            ) : (
              <div className="timeline-flow">
                {timeline.map((ev) => (
                  <div key={ev.id} className="timeline-event-item">
                    <div className="timeline-event-text">{ev.actionText}</div>
                    <div className="timeline-event-time">{formatDateTime(ev.actionAt)}</div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Ask for Help Modal */}
      {showAddContributorModal && (
        <div className="modal-overlay" onClick={() => setShowAddContributorModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3 className="modal-title">Ask for Help / Delegate Assistance</h3>
            <p style={{ fontSize: '0.85rem', color: '#64748b', margin: 0 }}>
              Select a colleague with WorkItem permissions to contribute to this work item.
            </p>

            {addContributorError && (
              <div
                style={{
                  background: '#fef2f2',
                  border: '1px solid #fecaca',
                  color: '#b91c1c',
                  padding: 10,
                  borderRadius: 6,
                  fontSize: '0.85rem'
                }}
              >
                {addContributorError}
              </div>
            )}

            <form onSubmit={handleAddContributor} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div className="form-group">
                <label>Search Colleague</label>
                <input
                  type="text"
                  placeholder="Type name or username..."
                  value={contributorSearchQuery}
                  onChange={(e) => setContributorSearchQuery(e.target.value)}
                  autoFocus
                />
              </div>

              <div className="form-group">
                <label>Eligible Candidates ({contributorOptions.length})</label>
                {loadingOptions ? (
                  <div style={{ color: '#64748b', fontSize: '0.85rem', padding: '8px 0' }}>
                    Loading candidates...
                  </div>
                ) : contributorOptions.length === 0 ? (
                  <div style={{ color: '#94a3b8', fontSize: '0.85rem', fontStyle: 'italic', padding: '8px 0' }}>
                    No eligible candidates found. Candidates must hold WorkItem.View and WorkItem.Contribute roles.
                  </div>
                ) : (
                  <div
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      gap: 6,
                      maxHeight: 180,
                      overflowY: 'auto'
                    }}
                  >
                    {contributorOptions.map((opt) => (
                      <div
                        key={opt.userId}
                        className={`contributor-option-item ${selectedUserId === opt.userId ? 'selected' : ''}`}
                        onClick={() => setSelectedUserId(opt.userId)}
                        role="button"
                        tabIndex={0}
                      >
                        <div style={{ fontWeight: 600, fontSize: '0.88rem', color: '#1e293b' }}>
                          {opt.displayName}
                          {opt.designation && (
                            <span style={{ fontSize: '0.8rem', color: '#64748b', marginLeft: 4 }}>
                              ({opt.designation})
                            </span>
                          )}
                        </div>
                        {opt.desks.length > 0 && (
                          <div style={{ fontSize: '0.75rem', color: '#64748b' }}>
                            Desks: {opt.desks.join(', ')}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>

              <div className="form-group">
                <label>Task Guidance / Instructions for Helper (optional)</label>
                <textarea
                  placeholder="Specific instructions, focus areas, draft expectations..."
                  value={contributorInstructions}
                  onChange={(e) => setContributorInstructions(e.target.value)}
                  rows={3}
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 4 }}>
                <button
                  type="button"
                  className="button secondary-button"
                  onClick={() => {
                    setShowAddContributorModal(false);
                    setSelectedUserId('');
                    setContributorInstructions('');
                  }}
                  disabled={addingContributor}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="button primary-button"
                  disabled={addingContributor || !selectedUserId}
                >
                  {addingContributor ? 'Assigning...' : 'Enlist Helper'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Return Contribution for Correction Modal */}
      {returnModalContributorId && (
        <div className="modal-overlay" onClick={() => setReturnModalContributorId(null)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3 className="modal-title">Return Contribution for Correction</h3>
            <p style={{ fontSize: '0.85rem', color: '#64748b', margin: 0 }}>
              Official administrative remarks are mandatory when returning work for corrections.
            </p>

            {returnError && (
              <div
                style={{
                  background: '#fef2f2',
                  border: '1px solid #fecaca',
                  color: '#b91c1c',
                  padding: 10,
                  borderRadius: 6,
                  fontSize: '0.85rem'
                }}
              >
                {returnError}
              </div>
            )}

            <form onSubmit={handleReturnContribution} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              <div className="form-group">
                <label>Correction Remarks & Guidance *</label>
                <textarea
                  placeholder="Specify precisely what requires correction or completion..."
                  value={returnRemarks}
                  onChange={(e) => setReturnRemarks(e.target.value)}
                  rows={4}
                  required
                  autoFocus
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
                <button
                  type="button"
                  className="button secondary-button"
                  onClick={() => {
                    setReturnModalContributorId(null);
                    setReturnRemarks('');
                  }}
                  disabled={returningContributor}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="button primary-button"
                  style={{ backgroundColor: '#c2410c', borderColor: '#c2410c' }}
                  disabled={returningContributor || !returnRemarks.trim()}
                >
                  {returningContributor ? 'Returning...' : 'Return for Correction'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Submit Contribution Modal */}
      {showSubmitModal && (
        <div className="modal-overlay" onClick={() => setShowSubmitModal(false)}>
          <div className="modal-content" onClick={(e) => e.stopPropagation()}>
            <h3 className="modal-title">Submit Contribution for Review</h3>
            <p style={{ fontSize: '0.85rem', color: '#64748b', margin: 0 }}>
              Once submitted, your work will be locked for review by the responsible officer.
            </p>

            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (myContributor) {
                  handleSubmitContribution(myContributor.contributorId);
                }
              }}
              style={{ display: 'flex', flexDirection: 'column', gap: 14 }}
            >
              <div className="form-group">
                <label>Submission Note / Summary of Prepared Work (optional)</label>
                <textarea
                  placeholder="Summary of findings, references prepared, drafts completed..."
                  value={submitNote}
                  onChange={(e) => setSubmitNote(e.target.value)}
                  rows={3}
                  autoFocus
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10 }}>
                <button
                  type="button"
                  className="button secondary-button"
                  onClick={() => {
                    setShowSubmitModal(false);
                    setSubmitNote('');
                  }}
                  disabled={submittingContribution}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="button primary-button"
                  style={{ backgroundColor: '#0284c7', borderColor: '#0284c7' }}
                  disabled={submittingContribution}
                >
                  {submittingContribution ? 'Submitting...' : 'Submit for Review'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
