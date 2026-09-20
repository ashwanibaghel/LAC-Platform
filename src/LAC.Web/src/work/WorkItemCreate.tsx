import { useState, useEffect } from 'react';
import { useNavigate, useSearchParams, Link } from 'react-router-dom';
import type {
  WorkstreamOption,
  DeskOption,
  WorkItemPriority,
  AssignmentOptionsResponse,
  CreateContextResponse
} from './types';
import './work.css';

export function WorkItemCreate() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const matterIdParam = searchParams.get('matterId');
  const dakIdParam = searchParams.get('dakId');

  // Form states
  const [title, setTitle] = useState('');
  const [instructions, setInstructions] = useState('');
  const [selectedWorkstreamId, setSelectedWorkstreamId] = useState('');
  const [selectedDeskId, setSelectedDeskId] = useState('');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [priority, setPriority] = useState<WorkItemPriority>('Routine');
  const [dueDate, setDueDate] = useState('');
  const [dueTime, setDueTime] = useState('');
  const [files, setFiles] = useState<File[]>([]);

  // Metadata lookups
  const [loadingContext, setLoadingContext] = useState(true);
  const [workstreams, setWorkstreams] = useState<WorkstreamOption[]>([]);
  const [desks, setDesks] = useState<DeskOption[]>([]);

  // Submission states
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // Load authorized workstreams
  useEffect(() => {
    fetch('/api/work-items/create-context', { credentials: 'include' })
      .then((res) => {
        if (!res.ok) throw new Error('Failed to load authorized workstreams.');
        return res.json() as Promise<CreateContextResponse>;
      })
      .then((data) => {
        setWorkstreams(data.workstreams);
        if (data.workstreams.length > 0) {
          setSelectedWorkstreamId(data.workstreams[0].id);
        }
      })
      .catch((err) => setFormError(err.message))
      .finally(() => setLoadingContext(false));
  }, []);

  // Load assignment options when workstream changes
  useEffect(() => {
    if (!selectedWorkstreamId) {
      setDesks([]);
      return;
    }

    fetch(`/api/work-items/assignment-options?workstreamId=${selectedWorkstreamId}`, {
      credentials: 'include'
    })
      .then((res) => {
        if (!res.ok) throw new Error('Failed to load assignment options.');
        return res.json() as Promise<AssignmentOptionsResponse>;
      })
      .then((data) => {
        setDesks(data.desks);
        if (data.desks.length > 0) {
          setSelectedDeskId(data.desks[0].id);
          setSelectedUserId('');
        } else {
          setSelectedDeskId('');
          setSelectedUserId('');
        }
      })
      .catch((err) => setFormError(err.message));
  }, [selectedWorkstreamId]);

  const currentDesk = desks.find((d) => d.id === selectedDeskId);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files) {
      const newFiles = Array.from(e.target.files);
      setFiles((prev) => [...prev, ...newFiles]);
    }
  };

  const handleRemoveFile = (index: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    if (!title.trim()) {
      setFormError('Please enter what needs to be done (Title is required).');
      return;
    }

    if (!selectedWorkstreamId) {
      setFormError('Please select a valid workstream.');
      return;
    }

    if (!selectedDeskId) {
      setFormError('Please select an office desk to assign this work to.');
      return;
    }

    let combinedDueAt: string | null = null;
    if (dueDate) {
      if (dueTime) {
        combinedDueAt = new Date(`${dueDate}T${dueTime}`).toISOString();
      } else {
        combinedDueAt = new Date(`${dueDate}T17:00:00`).toISOString();
      }
    }

    setSubmitting(true);

    try {
      const createPayload = {
        title: title.trim(),
        instructions: instructions.trim() || null,
        workstreamId: selectedWorkstreamId,
        officeDeskId: selectedDeskId,
        assignedUserId: selectedUserId || null,
        priority,
        dueAt: combinedDueAt,
        matterId: matterIdParam || null,
        dakId: dakIdParam || null
      };

      const res = await fetch('/api/work-items', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify(createPayload)
      });

      if (!res.ok) {
        const errJson = await res.json().catch(() => null);
        throw new Error(errJson?.detail || errJson?.message || 'Failed to create work item.');
      }

      const createdItem = await res.json();
      const workItemId = createdItem.workItemId;

      // Upload any attachments if present
      if (files.length > 0) {
        let uploadRevision = createdItem.revision;
        const failedUploads: string[] = [];

        for (const file of files) {
          try {
            const formData = new FormData();
            formData.append('file', file);
            formData.append('title', file.name);
            formData.append('attachmentType', 'SupportingDocument');
            formData.append('expectedRevision', uploadRevision.toString());

            const attRes = await fetch(`/api/work-items/${workItemId}/attachments`, {
              method: 'POST',
              credentials: 'include',
              body: formData
            });

            if (attRes.ok) {
              uploadRevision++;
            } else {
              failedUploads.push(file.name);
            }
          } catch {
            failedUploads.push(file.name);
          }
        }

        if (failedUploads.length > 0) {
          alert(
            `Work item was created successfully, but the following file(s) failed to upload: ${failedUploads.join(
              ', '
            )}. You can attach them from the work item workspace.`
          );
        }
      }

      navigate(`/work/${workItemId}`);
    } catch (err: any) {
      setFormError(err.message || 'An error occurred while creating work item.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="create-work-container">
      <nav className="breadcrumbs" aria-label="Breadcrumb" style={{ marginBottom: 16 }}>
        <Link to="/my-work">My Work</Link>
        <span className="breadcrumb-separator">/</span>
        <span>Assign Work</span>
      </nav>

      <div className="create-work-card">
        <div>
          <h1 style={{ fontSize: '1.4rem', fontWeight: 700, margin: '0 0 6px 0', color: '#0f172a' }}>
            Assign Official Work
          </h1>
          <p style={{ margin: 0, color: '#64748b', fontSize: '0.88rem' }}>
            Create an actionable work item and assign responsibility to an office desk or officer
          </p>
        </div>

        {formError && (
          <div
            style={{
              background: '#fef2f2',
              border: '1px solid #fecaca',
              color: '#b91c1c',
              padding: '12px 16px',
              borderRadius: '6px',
              fontSize: '0.88rem'
            }}
          >
            {formError}
          </div>
        )}

        {/* Linked Context Chips */}
        {(matterIdParam || dakIdParam) && (
          <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
            {matterIdParam && (
              <span className="context-chip">
                ⚖ Linked to Matter ({matterIdParam.slice(0, 8)}...)
              </span>
            )}
            {dakIdParam && (
              <span className="context-chip">
                📥 Linked to Dak ({dakIdParam.slice(0, 8)}...)
              </span>
            )}
          </div>
        )}

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
          <div className="form-group">
            <label htmlFor="title">
              What needs to be done? <span style={{ color: '#b91c1c' }}>*</span>
            </label>
            <input
              id="title"
              type="text"
              placeholder="e.g. Prepare reply to High Court in WP 412/2026"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
            />
          </div>

          <div className="form-group">
            <label htmlFor="instructions">Instructions / Expected Deliverables</label>
            <textarea
              id="instructions"
              placeholder="Specific directives, points to cover, or required outputs..."
              rows={4}
              value={instructions}
              onChange={(e) => setInstructions(e.target.value)}
            />
          </div>

          <div className="form-row-2col">
            <div className="form-group">
              <label htmlFor="workstream">
                Workstream <span style={{ color: '#b91c1c' }}>*</span>
              </label>
              <select
                id="workstream"
                value={selectedWorkstreamId}
                onChange={(e) => setSelectedWorkstreamId(e.target.value)}
                disabled={loadingContext || workstreams.length === 0}
                required
              >
                {workstreams.map((ws) => (
                  <option key={ws.id} value={ws.id}>
                    {ws.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label htmlFor="priority">Priority</label>
              <select
                id="priority"
                value={priority}
                onChange={(e) => setPriority(e.target.value as WorkItemPriority)}
              >
                <option value="Routine">Routine</option>
                <option value="Urgent">Urgent</option>
                <option value="Immediate">Immediate</option>
              </select>
            </div>
          </div>

          <div className="form-row-2col">
            <div className="form-group">
              <label htmlFor="desk">
                Assign to Office Desk <span style={{ color: '#b91c1c' }}>*</span>
              </label>
              <select
                id="desk"
                value={selectedDeskId}
                onChange={(e) => {
                  setSelectedDeskId(e.target.value);
                  setSelectedUserId('');
                }}
                disabled={desks.length === 0}
                required
              >
                {desks.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name} ({d.code})
                  </option>
                ))}
              </select>
            </div>

            <div className="form-group">
              <label htmlFor="namedOfficer">Responsible Officer (Optional)</label>
              <select
                id="namedOfficer"
                value={selectedUserId}
                onChange={(e) => setSelectedUserId(e.target.value)}
                disabled={!currentDesk || currentDesk.members.length === 0}
              >
                <option value="">Unallocated (Desk Level)</option>
                {currentDesk?.members.map((m) => (
                  <option key={m.userId} value={m.userId}>
                    {m.displayName} {m.designation ? `— ${m.designation}` : ''}
                  </option>
                ))}
              </select>
            </div>
          </div>

          <div className="form-row-2col">
            <div className="form-group">
              <label htmlFor="dueDate">Target Due Date</label>
              <input
                id="dueDate"
                type="date"
                value={dueDate}
                onChange={(e) => setDueDate(e.target.value)}
              />
            </div>

            <div className="form-group">
              <label htmlFor="dueTime">Target Due Time</label>
              <input
                id="dueTime"
                type="time"
                value={dueTime}
                onChange={(e) => setDueTime(e.target.value)}
                disabled={!dueDate}
              />
            </div>
          </div>

          {/* Supporting Files */}
          <div className="form-group">
            <label htmlFor="files">Supporting Files</label>
            <input
              id="files"
              type="file"
              multiple
              onChange={handleFileChange}
              style={{ padding: '6px' }}
            />
            {files.length > 0 && (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8 }}>
                {files.map((file, idx) => (
                  <div
                    key={idx}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      background: '#f8fafc',
                      padding: '6px 12px',
                      borderRadius: 4,
                      fontSize: '0.85rem',
                      border: '1px solid #e2e8f0'
                    }}
                  >
                    <span>
                      📄 {file.name} ({(file.size / 1024).toFixed(1)} KB)
                    </span>
                    <button
                      type="button"
                      onClick={() => handleRemoveFile(idx)}
                      style={{
                        background: 'none',
                        border: 'none',
                        color: '#b91c1c',
                        cursor: 'pointer'
                      }}
                    >
                      ✕
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 10, marginTop: 10 }}>
            <Link
              to="/my-work"
              className="button secondary-button"
              style={{ textDecoration: 'none' }}
            >
              Cancel
            </Link>
            <button
              type="submit"
              className="button primary-button"
              disabled={submitting || loadingContext}
            >
              {submitting ? 'Assigning Work...' : 'Assign Work'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
