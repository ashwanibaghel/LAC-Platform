import React, { useEffect, useState, useCallback } from "react";
import type { Designation, Workstream, DeskItem, RoleDetail, PermissionDefinition } from "./types";
import { DakCategoryAdmin } from "../dak/DakCategoryAdmin";
import { WorkCatalogAdmin } from "./WorkCatalogAdmin";
import "./admin.css";

export const AccessAdmin: React.FC = () => {
  const [activeTab, setActiveTab] = useState<"roles" | "desks" | "work-catalog" | "designations" | "workstreams" | "dak-categories">("roles");
  const [roles, setRoles] = useState<RoleDetail[]>([]);
  const [permissions, setPermissions] = useState<PermissionDefinition[]>([]);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // New/edit role modal
  const [showRoleModal, setShowRoleModal] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleDetail | null>(null);
  const [roleCode, setRoleCode] = useState("");
  const [roleName, setRoleName] = useState("");
  const [roleDesc, setRoleDesc] = useState("");
  const [rolePerms, setRolePerms] = useState<Record<string, string>>({}); // permCode -> scopeMode

  // New/edit desk modal
  const [showDeskModal, setShowDeskModal] = useState(false);
  const [editingDesk, setEditingDesk] = useState<DeskItem | null>(null);
  const [deskCode, setDeskCode] = useState("");
  const [deskName, setDeskName] = useState("");
  const [deskDesc, setDeskDesc] = useState("");
  const [deskWorkstreamId, setDeskWorkstreamId] = useState("");

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [rolesRes, permsRes, desigRes, wsRes, desksRes] = await Promise.all([
        fetch("/api/admin/roles", { credentials: "include" }),
        fetch("/api/admin/permissions", { credentials: "include" }),
        fetch("/api/admin/designations", { credentials: "include" }),
        fetch("/api/admin/workstreams", { credentials: "include" }),
        fetch("/api/admin/desks", { credentials: "include" }),
      ]);

      if (!rolesRes.ok || !permsRes.ok || !desigRes.ok || !wsRes.ok || !desksRes.ok) {
        throw new Error("Failed to load access control configuration.");
      }

      setRoles((await rolesRes.json()) as RoleDetail[]);
      setPermissions((await permsRes.json()) as PermissionDefinition[]);
      setDesignations((await desigRes.json()) as Designation[]);
      setWorkstreams((await wsRes.json()) as Workstream[]);
      setDesks((await desksRes.json()) as DeskItem[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load access data.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const openNewRoleModal = () => {
    setEditingRole(null);
    setRoleCode("");
    setRoleName("");
    setRoleDesc("");
    setRolePerms({});
    setShowRoleModal(true);
  };

  const openEditRoleModal = (role: RoleDetail) => {
    setEditingRole(role);
    setRoleCode(role.code);
    setRoleName(role.name);
    setRoleDesc(role.description || "");
    const map: Record<string, string> = {};
    for (const p of role.permissions) {
      map[p.code] = p.scopeMode;
    }
    setRolePerms(map);
    setShowRoleModal(true);
  };

  const handleSaveRole = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const permsPayload = Object.entries(rolePerms).map(([code, scope]) => ({
        permissionCode: code,
        scopeMode: scope,
      }));

      if (editingRole) {
        const response = await fetch(`/api/admin/roles/${editingRole.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name: roleName.trim(),
            description: roleDesc.trim(),
            permissions: permsPayload,
          }),
          credentials: "include",
        });
        if (!response.ok) throw new Error("Failed to update role.");
      } else {
        const response = await fetch("/api/admin/roles", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            code: roleCode.trim().toUpperCase(),
            name: roleName.trim(),
            description: roleDesc.trim(),
            permissions: permsPayload,
          }),
          credentials: "include",
        });
        if (!response.ok) {
          const err = (await response.json().catch(() => null)) as { message?: string } | null;
          throw new Error(err?.message || "Failed to create role.");
        }
      }

      setShowRoleModal(false);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error saving role.");
    }
  };

  const openNewDeskModal = () => {
    setEditingDesk(null);
    setDeskCode("");
    setDeskName("");
    setDeskDesc("");
    setDeskWorkstreamId("");
    setShowDeskModal(true);
  };

  const openEditDeskModal = (d: DeskItem) => {
    setEditingDesk(d);
    setDeskCode(d.code);
    setDeskName(d.name);
    setDeskDesc(d.description || "");
    setDeskWorkstreamId(d.workstreamId || "");
    setShowDeskModal(true);
  };

  const handleSaveDesk = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      if (editingDesk) {
        const response = await fetch(`/api/admin/desks/${editingDesk.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            name: deskName.trim(),
            description: deskDesc.trim() || null,
            workstreamId: deskWorkstreamId || null,
          }),
          credentials: "include",
        });
        if (!response.ok) throw new Error("Failed to update office desk.");
      } else {
        const response = await fetch("/api/admin/desks", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            code: deskCode.trim().toUpperCase(),
            name: deskName.trim(),
            description: deskDesc.trim() || null,
            workstreamId: deskWorkstreamId || null,
          }),
          credentials: "include",
        });
        if (!response.ok) {
          const err = (await response.json().catch(() => null)) as { message?: string } | null;
          throw new Error(err?.message || "Failed to create office desk.");
        }
      }

      setShowDeskModal(false);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error saving office desk.");
    }
  };

  const handleToggleDeskStatus = async (d: DeskItem) => {
    try {
      const response = await fetch(`/api/admin/desks/${d.id}/toggle-status`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) throw new Error("Failed to toggle desk status.");
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error toggling desk status.");
    }
  };

  if (loading) {
    return (
      <div className="rbac-admin-root">
        <div className="state"><strong>Loading access configuration...</strong></div>
      </div>
    );
  }

  return (
    <div className="rbac-admin-root">
      {/* Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Access Architecture & RBAC Foundations</h2>
          <p>
            Role Bundles &ne; Operational Desks &ne; Statutory Works &ne; Civil Designations. Rigorous separation of responsibilities, operational posts, and permissions.
          </p>
        </div>
        <div className="rbac-actions-group">
          {activeTab === "roles" && (
            <button className="primary-button" onClick={openNewRoleModal}>
              + Create New Role Bundle
            </button>
          )}
          {activeTab === "desks" && (
            <button className="primary-button" onClick={openNewDeskModal}>
              + Create Office Desk (Seat)
            </button>
          )}
        </div>
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Navigation Tabs */}
      <div style={{ display: "flex", gap: "8px", margin: "10px 0 16px 0", flexWrap: "wrap" }}>
        <button
          className={`secondary-button ${activeTab === "roles" ? "active" : ""}`}
          onClick={() => setActiveTab("roles")}
        >
          Authority Roles ({roles.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "desks" ? "active" : ""}`}
          onClick={() => setActiveTab("desks")}
        >
          Operational Desks ({desks.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "work-catalog" ? "active" : ""}`}
          onClick={() => setActiveTab("work-catalog")}
        >
          Statutory Work Catalog
        </button>
        <button
          className={`secondary-button ${activeTab === "designations" ? "active" : ""}`}
          onClick={() => setActiveTab("designations")}
        >
          Civil Designations ({designations.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "workstreams" ? "active" : ""}`}
          onClick={() => setActiveTab("workstreams")}
        >
          Functional Branches ({workstreams.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "dak-categories" ? "active" : ""}`}
          onClick={() => setActiveTab("dak-categories")}
        >
          Dak Categories
        </button>
      </div>

      {/* Tab: Authority Roles */}
      {activeTab === "roles" && (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Role Code</th>
                <th>Role Name</th>
                <th>Description</th>
                <th>Classification</th>
                <th>Granted Platform Permissions & Scopes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {roles.map((r) => (
                <tr key={r.id}>
                  <td><strong>{r.code}</strong></td>
                  <td>{r.name}</td>
                  <td>{r.description || <span className="subtext">No description</span>}</td>
                  <td>
                    <span className={`status ${r.isSystemRole ? "warning" : "neutral"}`}>
                      {r.isSystemRole ? "System Core" : "Custom Bundle"}
                    </span>
                  </td>
                  <td>
                    <div className="role-tags-container" style={{ maxWidth: "420px" }}>
                      {r.permissions.map((p) => (
                        <span key={p.code} className="role-tag" title={`${p.name} (Scope: ${p.scopeMode})`}>
                          {p.code}
                          <small style={{ marginLeft: "4px", opacity: 0.85, fontWeight: 700 }}>
                            [{p.scopeMode}]
                          </small>
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <button
                      className="rbac-btn-edit"
                      onClick={() => openEditRoleModal(r)}
                    >
                      Configure Role
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab: Operational Desks */}
      {activeTab === "desks" && (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Desk Code</th>
                <th>Desk Name</th>
                <th>Description</th>
                <th>Associated Branch</th>
                <th>Active Officers (Members)</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {desks.length === 0 ? (
                <tr>
                  <td colSpan={7} style={{ textAlign: "center", padding: "24px" }}>
                    No office desks configured yet. Click <strong>+ Create Office Desk</strong> above.
                  </td>
                </tr>
              ) : (
                desks.map((d) => (
                  <tr key={d.id}>
                    <td><code>{d.code}</code></td>
                    <td><strong>{d.name}</strong></td>
                    <td>{d.description || <span className="subtext">—</span>}</td>
                    <td>
                      {d.workstreamName ? (
                        <span className="workstream-tag primary">
                          {d.workstreamName}
                        </span>
                      ) : (
                        <span className="subtext">General</span>
                      )}
                    </td>
                    <td>
                      <strong>{d.activeMembersCount || 0}</strong> assigned officer(s)
                    </td>
                    <td>
                      <span className={`status ${d.isActive ? "success" : "warning"}`}>
                        {d.isActive ? "Active Seat" : "Deactivated"}
                      </span>
                    </td>
                    <td>
                      <div className="rbac-action-buttons">
                        <button
                          className="rbac-btn-edit"
                          onClick={() => openEditDeskModal(d)}
                        >
                          Edit
                        </button>
                        <button
                          className="rbac-btn-edit"
                          onClick={() => handleToggleDeskStatus(d)}
                        >
                          {d.isActive ? "Deactivate" : "Activate"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab: Statutory Work Catalog */}
      {activeTab === "work-catalog" && (
        <div style={{ marginTop: "8px" }}>
          <WorkCatalogAdmin />
        </div>
      )}

      {/* Tab: Civil Designations */}
      {activeTab === "designations" && (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Civil Rank Code</th>
                <th>Official Civil Designation</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {designations.map((d) => (
                <tr key={d.id}>
                  <td><code>{d.code}</code></td>
                  <td><strong>{d.name}</strong></td>
                  <td><span className="status success">Active Post</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab: Functional Workstreams */}
      {activeTab === "workstreams" && (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Branch Code</th>
                <th>Functional Workstream (Branch)</th>
                <th>Description</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {workstreams.map((w) => (
                <tr key={w.id}>
                  <td><code>{w.code}</code></td>
                  <td><strong>{w.name}</strong></td>
                  <td>{w.description || "Core platform workstream"}</td>
                  <td><span className="status success">Active</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Tab: Dak Categories */}
      {activeTab === "dak-categories" && <DakCategoryAdmin />}

      {/* Role Edit/Create Modal */}
      {showRoleModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "860px" }}>
            <h3>{editingRole ? `Configure Authority Role: ${editingRole.name}` : "Create New Authority Role Bundle"}</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Define the permission set and default operational scope for this reusable authority bundle.
            </p>

            <form onSubmit={handleSaveRole} style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label>Role Code *</label>
                  <input
                    type="text"
                    required
                    disabled={Boolean(editingRole)}
                    value={roleCode}
                    onChange={(e) => setRoleCode(e.target.value)}
                    placeholder="e.g. RECORD_VERIFIER"
                  />
                </div>
                <div className="form-group">
                  <label>Role Name *</label>
                  <input
                    type="text"
                    required
                    value={roleName}
                    onChange={(e) => setRoleName(e.target.value)}
                    placeholder="e.g. Revenue Record Verifier"
                  />
                </div>
              </div>

              <div className="form-group">
                <label>Description of Authority</label>
                <input
                  type="text"
                  value={roleDesc}
                  onChange={(e) => setRoleDesc(e.target.value)}
                  placeholder="Functional duties and authority granted by this role bundle"
                />
              </div>

              {/* Permissions & Scopes Matrix */}
              <div className="form-group">
                <label>Granted Permissions & Scope Containment</label>
                <div className="rbac-table-container" style={{ maxHeight: "320px", overflowY: "auto" }}>
                  <table className="rbac-table" style={{ fontSize: "12.5px" }}>
                    <thead>
                      <tr>
                        <th style={{ width: "36px" }}>Grant</th>
                        <th>Permission Code</th>
                        <th>Name</th>
                        <th>Category</th>
                        <th>Scope Mode</th>
                      </tr>
                    </thead>
                    <tbody>
                      {permissions.map((p) => {
                        const isGranted = Boolean(rolePerms[p.code]);
                        const currentScope = rolePerms[p.code] || "All";
                        return (
                          <tr key={p.code}>
                            <td>
                              <input
                                type="checkbox"
                                checked={isGranted}
                                onChange={(e) => {
                                  setRolePerms((prev) => {
                                    const next = { ...prev };
                                    if (e.target.checked) next[p.code] = currentScope;
                                    else delete next[p.code];
                                    return next;
                                  });
                                }}
                              />
                            </td>
                            <td><code>{p.code}</code></td>
                            <td>{p.name}</td>
                            <td><span className="badge badge-neutral">{p.category}</span></td>
                            <td>
                              <select
                                disabled={!isGranted}
                                value={currentScope}
                                onChange={(e) => {
                                  const val = e.target.value;
                                  setRolePerms((prev) => ({ ...prev, [p.code]: val }));
                                }}
                                style={{ padding: "3px 8px", fontSize: "12px", borderRadius: "4px", border: "1px solid #cbd5e1" }}
                              >
                                <option value="All">All LAC (Platform)</option>
                                <option value="Workstream">Workstream (Branch)</option>
                                <option value="Assigned">Assigned Only</option>
                                <option value="Own">Own Submissions</option>
                              </select>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                </div>
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setShowRoleModal(false)}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button">
                  Save Role Bundle
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Desk Edit/Create Modal */}
      {showDeskModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "600px" }}>
            <h3>{editingDesk ? `Edit Office Desk: ${editingDesk.name}` : "Create New Office Desk"}</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Establish a physical or digital operational seat/post for dak flow and file custody.
            </p>

            <form onSubmit={handleSaveDesk} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div className="form-group">
                <label>Desk Code *</label>
                <input
                  type="text"
                  required
                  disabled={Boolean(editingDesk)}
                  value={deskCode}
                  onChange={(e) => setDeskCode(e.target.value)}
                  placeholder="e.g. DESK_NORTH_DA_1"
                />
              </div>

              <div className="form-group">
                <label>Desk Name (Post / Seat Title) *</label>
                <input
                  type="text"
                  required
                  value={deskName}
                  onChange={(e) => setDeskName(e.target.value)}
                  placeholder="e.g. Dealing Assistant Desk - North District"
                />
              </div>

              <div className="form-group">
                <label>Associated Functional Branch</label>
                <select
                  value={deskWorkstreamId}
                  onChange={(e) => setDeskWorkstreamId(e.target.value)}
                >
                  <option value="">No Specific Branch (General Seat)</option>
                  {workstreams.map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.name} ({w.code})
                    </option>
                  ))}
                </select>
              </div>

              <div className="form-group">
                <label>Description of Physical / Digital Seat</label>
                <textarea
                  rows={2}
                  value={deskDesc}
                  onChange={(e) => setDeskDesc(e.target.value)}
                  placeholder="Physical room, branch location, or jurisdiction handled by this seat."
                />
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setShowDeskModal(false)}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button">
                  Save Office Desk
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
