import React, { useState, useEffect, useCallback, useMemo } from "react";
import type { WorkDefinitionItem, WorkKind, Workstream } from "./types";
import "./admin.css";

const WORK_KINDS: WorkKind[] = [
  "LandAcquisition",
  "Award",
  "LandRecords",
  "Nm",
  "Enm",
  "Possession",
  "Accounts",
  "Compensation",
  "StatementA",
  "Court",
  "Rti",
  "Correspondence",
  "General",
];

export const WorkCatalogAdmin: React.FC = () => {
  const [works, setWorks] = useState<WorkDefinitionItem[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  // Filters
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedWorkstreamId, setSelectedWorkstreamId] = useState("ALL");
  const [selectedKind, setSelectedKind] = useState("ALL");
  const [selectedStatus, setSelectedStatus] = useState("ALL");

  // Create Modal
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newCode, setNewCode] = useState("");
  const [newName, setNewName] = useState("");
  const [newKind, setNewKind] = useState<WorkKind>("General");
  const [newWorkstreamId, setNewWorkstreamId] = useState("");
  const [newDesc, setNewDesc] = useState("");
  const [createLoading, setCreateLoading] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Edit Modal
  const [editingWork, setEditingWork] = useState<WorkDefinitionItem | null>(null);
  const [editName, setEditName] = useState("");
  const [editDesc, setEditDesc] = useState("");
  const [editIsActive, setEditIsActive] = useState(true);
  const [editLoading, setEditLoading] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [worksRes, wsRes] = await Promise.all([
        fetch("/api/admin/works", { credentials: "include" }),
        fetch("/api/admin/workstreams", { credentials: "include" }),
      ]);

      if (!worksRes.ok) {
        if (worksRes.status === 401) throw new Error("Session expired. Please log in again.");
        if (worksRes.status === 403) throw new Error("Access denied: WorkCatalog.Manage permission required.");
        throw new Error(`Failed to load work catalog (status ${worksRes.status}).`);
      }

      const worksData = (await worksRes.json()) as WorkDefinitionItem[];
      setWorks(worksData);

      if (wsRes.ok) {
        const wsData = (await wsRes.json()) as Workstream[];
        setWorkstreams(wsData);
        if (wsData.length > 0 && !newWorkstreamId) {
          setNewWorkstreamId(wsData[0].id);
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load work catalog.");
    } finally {
      setLoading(false);
    }
  }, [newWorkstreamId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const workstreamMap = useMemo(() => {
    const map = new Map<string, string>();
    for (const ws of workstreams) {
      map.set(ws.id, ws.name);
    }
    return map;
  }, [workstreams]);

  const filteredWorks = useMemo(() => {
    return works.filter((w) => {
      const matchesSearch =
        searchQuery.trim() === "" ||
        w.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        w.code.toLowerCase().includes(searchQuery.toLowerCase()) ||
        w.kind.toLowerCase().includes(searchQuery.toLowerCase()) ||
        w.description.toLowerCase().includes(searchQuery.toLowerCase());

      const matchesWorkstream =
        selectedWorkstreamId === "ALL" || w.workstreamId === selectedWorkstreamId;

      const matchesKind = selectedKind === "ALL" || w.kind === selectedKind;

      const matchesStatus =
        selectedStatus === "ALL" ||
        (selectedStatus === "ACTIVE" && w.isActive) ||
        (selectedStatus === "INACTIVE" && !w.isActive);

      return matchesSearch && matchesWorkstream && matchesKind && matchesStatus;
    });
  }, [works, searchQuery, selectedWorkstreamId, selectedKind, selectedStatus]);

  const handleCreateWork = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCode.trim() || !newName.trim() || !newWorkstreamId) return;

    try {
      setCreateLoading(true);
      setCreateError(null);
      const res = await fetch("/api/admin/works", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          code: newCode.trim().toUpperCase(),
          name: newName.trim(),
          description: newDesc.trim(),
          kind: newKind,
          workstreamId: newWorkstreamId,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          throw new Error("A work category with this code already exists. Please choose a unique code.");
        }
        if (res.status === 403) {
          throw new Error("Access denied: WorkCatalog.Manage permission required.");
        }
        const errJson = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to create work (status ${res.status}).`);
      }

      setShowCreateModal(false);
      setNewCode("");
      setNewName("");
      setNewDesc("");
      setNewKind("General");
      setActionMessage("New work category created and added to the official catalog.");
      await loadData();
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : "Error creating work category.");
    } finally {
      setCreateLoading(false);
    }
  };

  const openEditModal = (item: WorkDefinitionItem) => {
    setEditingWork(item);
    setEditName(item.name);
    setEditDesc(item.description);
    setEditIsActive(item.isActive);
    setEditError(null);
  };

  const handleUpdateWork = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingWork || !editName.trim()) return;

    try {
      setEditLoading(true);
      setEditError(null);
      const res = await fetch(`/api/admin/works/${editingWork.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: editName.trim(),
          description: editDesc.trim(),
          isActive: editIsActive,
          expectedRevision: editingWork.revision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadData();
          throw new Error("Revision conflict: The work category was modified concurrently. Data reloaded.");
        }
        if (res.status === 403) {
          throw new Error("Access denied: WorkCatalog.Manage permission required.");
        }
        const errJson = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to update work (status ${res.status}).`);
      }

      setEditingWork(null);
      setActionMessage(`Work category "${editName.trim()}" updated successfully.`);
      await loadData();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : "Error updating work category.");
    } finally {
      setEditLoading(false);
    }
  };

  const handleToggleActive = async (item: WorkDefinitionItem) => {
    try {
      setActionMessage(null);
      const res = await fetch(`/api/admin/works/${item.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: item.name,
          description: item.description,
          isActive: !item.isActive,
          expectedRevision: item.revision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadData();
          alert("Revision conflict: This work item was modified concurrently. Catalog has been refreshed.");
          return;
        }
        throw new Error(`Failed to toggle status (status ${res.status}).`);
      }

      setActionMessage(`Work "${item.name}" ${!item.isActive ? "activated" : "deactivated"}.`);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error toggling work status.");
    }
  };

  if (loading) {
    return (
      <div className="rbac-admin-root">
        <div className="state"><strong>Loading official work catalog...</strong></div>
      </div>
    );
  }

  return (
    <div className="rbac-admin-root">
      {/* Page Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Statutory Work Catalog</h2>
          <p>
            Configurable catalog of official statutory responsibilities and operational categories. Works govern officer operational allocation distinct from civil designations, authority roles, or desk seating.
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="primary-button" onClick={() => setShowCreateModal(true)}>
            + Define New Work Category
          </button>
        </div>
      </div>

      {actionMessage && <div className="form-message">{actionMessage}</div>}
      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Model Architecture Clarity Banner */}
      <div className="contract-notice-banner">
        <strong>Official Architecture Distinction: Work vs. Role vs. Desk vs. Designation</strong>
        <span>
          • <strong>Work:</strong> Specific statutory responsibility (e.g. Compensation Disbursement, Award Formulation, Statement-A). Immutable Code, Kind & Workstream once defined.<br />
          • <strong>Role:</strong> Reusable permission bundle (e.g. LAC_OFFICER, DEALING_ASSISTANT). Officers hold multiple roles simultaneously.<br />
          • <strong>OfficeDesk:</strong> Operational/custody seat where physical or digital files arrive.<br />
          • <strong>Designation:</strong> Civil administrative post/rank (e.g. SDM, Tehsildar, Naib Tehsildar). Never implies implicit authority without role/allocation.
        </span>
      </div>

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search work title, code, kind, or description..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={selectedWorkstreamId}
          onChange={(e) => setSelectedWorkstreamId(e.target.value)}
        >
          <option value="ALL">All Functional Branches</option>
          {workstreams.map((ws) => (
            <option key={ws.id} value={ws.id}>
              {ws.name}
            </option>
          ))}
        </select>

        <select
          className="rbac-filter-select"
          value={selectedKind}
          onChange={(e) => setSelectedKind(e.target.value)}
        >
          <option value="ALL">All Work Kinds</option>
          {WORK_KINDS.map((k) => (
            <option key={k} value={k}>
              {k}
            </option>
          ))}
        </select>

        <select
          className="rbac-filter-select"
          value={selectedStatus}
          onChange={(e) => setSelectedStatus(e.target.value)}
        >
          <option value="ALL">All Statuses</option>
          <option value="ACTIVE">Active Only</option>
          <option value="INACTIVE">Inactive Only</option>
        </select>

        <span style={{ fontSize: "13px", color: "#64748b", marginLeft: "auto" }}>
          Showing <strong>{filteredWorks.length}</strong> of {works.length} Work Definitions
        </span>
      </div>

      {/* Work Catalog Grid */}
      <div className="work-catalog-grid">
        {filteredWorks.map((work) => (
          <div key={work.id} className={`work-card ${!work.isActive ? "work-card-inactive" : ""}`}>
            <div>
              <div className="work-card-header">
                <div>
                  <span className="work-card-title">{work.name}</span>
                  <div style={{ marginTop: "4px", display: "flex", gap: "6px", alignItems: "center", flexWrap: "wrap" }}>
                    <span className="work-card-code">{work.code}</span>
                    <span className="workstream-tag primary">
                      {workstreamMap.get(work.workstreamId) || "Branch"}
                    </span>
                    <span className="workstream-tag" style={{ background: "#f1f5f9", color: "#334155" }}>
                      Kind: {work.kind}
                    </span>
                  </div>
                </div>
                <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "4px" }}>
                  <span className={`status ${work.isActive ? "success" : "warning"}`}>
                    {work.isActive ? "Active" : "Inactive"}
                  </span>
                  <span style={{ fontSize: "11px", color: "#94a3b8" }}>
                    Rev {work.revision}
                  </span>
                </div>
              </div>

              <p className="work-card-desc" style={{ marginTop: "10px" }}>
                {work.description || <em>No detailed description provided.</em>}
              </p>
            </div>

            <div style={{ marginTop: "12px", paddingTop: "10px", borderTop: "1px solid #f1f5f9", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <div style={{ fontSize: "12px", color: "#64748b" }}>
                <span>ID: <code>{work.id.substring(0, 8)}...</code></span>
              </div>
              <div style={{ display: "flex", gap: "6px" }}>
                <button
                  type="button"
                  className="secondary-button"
                  style={{ padding: "4px 10px", fontSize: "12px" }}
                  onClick={() => openEditModal(work)}
                >
                  Edit
                </button>
                <button
                  type="button"
                  className="quiet-button"
                  style={{ padding: "4px 8px", fontSize: "12px" }}
                  onClick={() => void handleToggleActive(work)}
                >
                  {work.isActive ? "Deactivate" : "Activate"}
                </button>
              </div>
            </div>
          </div>
        ))}

        {filteredWorks.length === 0 && (
          <div style={{ gridColumn: "1 / -1", textAlign: "center", padding: "40px", background: "#f8fafc", borderRadius: "8px", border: "1px dashed #cbd5e1" }}>
            <p style={{ color: "#64748b", margin: 0 }}>No work categories match the selected filters.</p>
          </div>
        )}
      </div>

      {/* Create Work Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "620px" }}>
            <h3>Define New Statutory Work Category</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Creates a reusable responsibility category in the central work catalog. Code, Kind, and Branch are immutable after creation.
            </p>

            {createError && (
              <div className="state error" style={{ marginBottom: "12px" }}>
                <strong>Error:</strong> {createError}
              </div>
            )}

            <form onSubmit={handleCreateWork} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div className="form-group">
                <label>Work Code (Immutable Identifier) *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. WORK_ENCROACH_EVICT"
                  value={newCode}
                  onChange={(e) => setNewCode(e.target.value.toUpperCase().replace(/\s+/g, "_"))}
                />
                <small style={{ color: "#64748b" }}>
                  Unique uppercase identifier used in official allocations and audit attribution.
                </small>
              </div>

              <div className="form-group">
                <label>Work Name / Title *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Encroachment Eviction & Demolition"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                />
              </div>

              <div className="form-row" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label>Operational Kind *</label>
                  <select
                    value={newKind}
                    onChange={(e) => setNewKind(e.target.value as WorkKind)}
                  >
                    {WORK_KINDS.map((k) => (
                      <option key={k} value={k}>
                        {k}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="form-group">
                  <label>Functional Branch (Workstream) *</label>
                  <select
                    value={newWorkstreamId}
                    onChange={(e) => setNewWorkstreamId(e.target.value)}
                    required
                  >
                    {workstreams.map((ws) => (
                      <option key={ws.id} value={ws.id}>
                        {ws.name} ({ws.code})
                      </option>
                    ))}
                  </select>
                </div>
              </div>

              <div className="form-group">
                <label>Description of Responsibility</label>
                <textarea
                  rows={3}
                  placeholder="Describe the operational scope, statutory remit, and duties covered under this work category."
                  value={newDesc}
                  onChange={(e) => setNewDesc(e.target.value)}
                />
              </div>

              <div className="contract-notice-banner" style={{ margin: "4px 0" }}>
                <strong>Server Contract Guarantee:</strong>
                <span>
                  POST to <code>/api/admin/works</code> validated with <code>WorkCatalog.Manage</code> authority. Invalid shapes or duplicate codes fail with clear validation.
                </span>
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setShowCreateModal(false)}
                  disabled={createLoading}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={createLoading}>
                  {createLoading ? "Creating Work..." : "Save Work Category"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit Work Modal */}
      {editingWork && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "600px" }}>
            <h3>Edit Work Category</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Update title, description, and status for <strong>{editingWork.code}</strong>. Kind and branch remain immutable.
            </p>

            {editError && (
              <div className="state error" style={{ marginBottom: "12px" }}>
                <strong>Error:</strong> {editError}
              </div>
            )}

            <form onSubmit={handleUpdateWork} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div style={{ display: "flex", gap: "10px", background: "#f8fafc", padding: "10px 14px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                <div>
                  <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase" }}>Work Code:</span>{" "}
                  <strong>{editingWork.code}</strong>
                </div>
                <div>
                  <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase" }}>Kind:</span>{" "}
                  <strong>{editingWork.kind}</strong>
                </div>
                <div>
                  <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase" }}>Revision:</span>{" "}
                  <strong>{editingWork.revision}</strong>
                </div>
              </div>

              <div className="form-group">
                <label>Work Name / Title *</label>
                <input
                  type="text"
                  required
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label>Description of Responsibility</label>
                <textarea
                  rows={3}
                  value={editDesc}
                  onChange={(e) => setEditDesc(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label style={{ display: "flex", alignItems: "center", gap: "8px", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={editIsActive}
                    onChange={(e) => setEditIsActive(e.target.checked)}
                  />
                  <span>Active for Allocation (Officers can be assigned to this work)</span>
                </label>
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setEditingWork(null)}
                  disabled={editLoading}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={editLoading}>
                  {editLoading ? "Updating..." : "Update Work Category"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
