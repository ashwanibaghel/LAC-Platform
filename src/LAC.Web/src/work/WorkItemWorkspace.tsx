import { useEffect, useState, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import type {
  WorkItemDetail,
  WorkItemEvent,
  WorkItemPriority,
  WorkItemStatus
} from './types';
import './work.css';

interface WorkItemWorkspaceProps {
  currentUserPermissions?: string[];
}

export function WorkItemWorkspace({ currentUserPermissions }: WorkItemWorkspaceProps) {
  const { id } = useParams<{ id: string }>();

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

  const hasPermission = (code: string) => {
    return currentUserPermissions ? currentUserPermissions.includes(code) : true;
  };

  const canUpdate = hasPermission('WorkItem.Update') || hasPermission('WorkItem.Contribute');

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

      // Mark seen silently
      fetch(`/api/work-items/${id}/seen`, {
        method: 'POST',
        credentials: 'include'
      }).catch(() => {});
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
      const res = await fetch(`/api/work-items/${item.id}/attachments/${attachmentId}`, {
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

  const isAssigned = item.status === 'Assigned';
  const isTerminal = item.status === 'Completed' || item.status === 'Cancelled';

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

          {/* Action Button */}
          {!isTerminal && canUpdate && (
            <div className="workspace-action-strip">
              {isAssigned && (
                <button
                  className="button primary-button"
                  onClick={handleStartWork}
                  disabled={startingWork}
                >
                  {startingWork ? 'Starting...' : '▶ Start Work'}
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
          {/* Progress Updates Section */}
          <div className="workspace-card">
            <h2>Progress Updates ({item.updates.length})</h2>

            {!isTerminal && canUpdate && (
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
              {!isTerminal && canUpdate && !showUploadForm && (
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
                        {!isTerminal && canUpdate && (
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
    </div>
  );
}
