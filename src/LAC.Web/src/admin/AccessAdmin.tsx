import React, { useEffect, useState, useCallback } from "react";
import type { Designation, Workstream } from "../auth/types";

interface PermissionItem {
  id: string;
  code: string;
  name: string;
  description?: string;
  category: string;
}

interface RolePermissionItem {
  permissionId: string;
  code: string;
  name: string;
  category: string;
  scopeMode: string;
}

interface RoleDetail {
  id: string;
  code: string;
  name: string;
  description?: string;
  isSystemRole: boolean;
  isActive: boolean;
  permissions: RolePermissionItem[];
}

interface DeskListItem {
  id: string;
  code: string;
  name: string;
  description?: string;
  workstreamId?: string;
  workstreamCode?: string;
  workstreamName?: string;
  isActive: boolean;
  activeMembersCount: number;
  createdAt: string;
}

export const AccessAdmin: React.FC = () => {
  const [activeTab, setActiveTab] = useState<"roles" | "designations" | "workstreams" | "desks">("roles");
  const [roles, setRoles] = useState<RoleDetail[]>([]);
  const [permissions, setPermissions] = useState<PermissionItem[]>([]);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [desks, setDesks] = useState<DeskListItem[]>([]);
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
  const [editingDesk, setEditingDesk] = useState<DeskListItem | null>(null);
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
      setPermissions((await permsRes.json()) as PermissionItem[]);
      setDesignations((await desigRes.json()) as Designation[]);
      setWorkstreams((await wsRes.json()) as Workstream[]);
      setDesks((await desksRes.json()) as DeskListItem[]);
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

  const openEditDeskModal = (d: DeskListItem) => {
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

  const handleToggleDeskStatus = async (d: DeskListItem) => {
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

  if (loading) return <div className="state"><strong>Loading access configuration...</strong></div>;

  return (
    <div className="admin-page">
      <div className="section-heading">
        <div>
          <h2>Access Architecture & RBAC Foundations</h2>
          <span>Designations &ne; Roles &ne; Workstreams &ne; Desks. Clear separation of organizational structure and permissions.</span>
        </div>
        {activeTab === "roles" && (
          <button className="primary-button" onClick={openNewRoleModal}>
            + Create New Role
          </button>
        )}
        {activeTab === "desks" && (
          <button className="primary-button" onClick={openNewDeskModal}>
            + Create New Office Desk
          </button>
        )}
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      <div style={{ display: "flex", gap: "10px", margin: "14px 0" }}>
        <button
          className={`secondary-button ${activeTab === "roles" ? "active" : ""}`}
          onClick={() => setActiveTab("roles")}
        >
          Roles & Authority Bundles ({roles.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "desks" ? "active" : ""}`}
          onClick={() => setActiveTab("desks")}
        >
          Office Desks ({desks.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "designations" ? "active" : ""}`}
          onClick={() => setActiveTab("designations")}
        >
          Official Designations ({designations.length})
        </button>
        <button
          className={`secondary-button ${activeTab === "workstreams" ? "active" : ""}`}
          onClick={() => setActiveTab("workstreams")}
        >
          Workstreams ({workstreams.length})
        </button>
      </div>

      {activeTab === "roles" && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Role Code</th>
                <th>Role Name</th>
                <th>Description</th>
                <th>Type</th>
                <th>Granted Permissions & Scopes</th>
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
                      {r.isSystemRole ? "System Role" : "Custom"}
                    </span>
                  </td>
                  <td>
                    <div style={{ display: "flex", flexWrap: "wrap", gap: "4px" }}>
                      {r.permissions.map((p) => (
                        <span key={p.code} className="badge" title={`${p.name} (${p.scopeMode})`}>
                          {p.code}
                          <small style={{ marginLeft: "4px", opacity: 0.8 }}>[{p.scopeMode}]</small>
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <button
                      className="quiet-button text-action"
                      onClick={() => openEditRoleModal(r)}
                    >
                      Configure
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {activeTab === "desks" && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Desk Code</th>
                <th>Desk Name</th>
                <th>Description</th>
                <th>Associated Workstream</th>
                <th>Active Officers / Members</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {desks.length === 0 ? (
                <tr>
                  <td colSpan={7} style={{ textAlign: "center", padding: "20px" }}>
                    No office desks configured yet. Click <strong>+ Create New Office Desk</strong> above to establish operational desks.
                  </td>
                </tr>
              ) : (
                desks.map((d) => (
                  <tr key={d.id}>
                    <td><strong>{d.code}</strong></td>
                    <td>{d.name}</td>
                    <td>{d.description || <span className="subtext">No description</span>}</td>
                    <td>
                      {d.workstreamName ? (
                        <span className="badge badge-neutral" title={`Workstream: ${d.workstreamCode}`}>
                          {d.workstreamName}
                        </span>
                      ) : (
                        <span className="subtext">Unclassified</span>
                      )}
                    </td>
                    <td>
                      <strong>{d.activeMembersCount}</strong> member{d.activeMembersCount !== 1 ? "s" : ""}
                    </td>
                    <td>
                      <span className={`status ${d.isActive ? "success" : "warning"}`}>
                        {d.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td>
                      <div style={{ display: "flex", gap: "6px" }}>
                        <button
                          className="quiet-button text-action"
                          onClick={() => openEditDeskModal(d)}
                        >
                          Edit
                        </button>
                        <button
                          className="quiet-button text-action"
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

      {activeTab === "designations" && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Code</th>
                <th>Official Designation Title</th>
              </tr>
            </thead>
            <tbody>
              {designations.map((d) => (
                <tr key={d.id}>
                  <td><strong>{d.code}</strong></td>
                  <td>{d.name}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {activeTab === "workstreams" && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Code</th>
                <th>Functional Workstream</th>
              </tr>
            </thead>
            <tbody>
              {workstreams.map((w) => (
                <tr key={w.id}>
                  <td><strong>{w.code}</strong></td>
                  <td>{w.name}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Role Edit/Create Modal */}
      {showRoleModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "820px" }}>
            <h3>{editingRole ? `Configure Role: ${editingRole.name}` : "Create New Role"}</h3>
            <form onSubmit={handleSaveRole} className="lr-form">
              <div className="field-grid">
                <label>
                  Role Code *
                  <input
                    type="text"
                    required
                    disabled={Boolean(editingRole)}
                    value={roleCode}
                    onChange={(e) => setRoleCode(e.target.value)}
                    placeholder="e.g. RECORD_VERIFIER"
                  />
                </label>
                <label>
                  Role Name *
                  <input
                    type="text"
                    required
                    value={roleName}
                    onChange={(e) => setRoleName(e.target.value)}
                    placeholder="e.g. Revenue Record Verifier"
                  />
                </label>
                <label className="span-two">
                  Description
                  <input
                    type="text"
                    value={roleDesc}
                    onChange={(e) => setRoleDesc(e.target.value)}
                    placeholder="Functional authority granted by this role bundle"
                  />
                </label>
              </div>

              <fieldset style={{ maxHeight: "360px", overflowY: "auto" }}>
                <legend>Permissions & Scope Controls</legend>
                <table style={{ width: "100%", fontSize: "12px" }}>
                  <thead>
                    <tr>
                      <th style={{ width: "32px" }}>Enable</th>
                      <th>Permission</th>
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
                                const copy = { ...rolePerms };
                                if (e.target.checked) {
                                  copy[p.code] = "All";
                                } else {
                                  delete copy[p.code];
                                }
                                setRolePerms(copy);
                              }}
                            />
                          </td>
                          <td>
                            <strong>{p.code}</strong>
                            <div style={{ color: "#667" }}>{p.name}</div>
                          </td>
                          <td>{p.category}</td>
                          <td>
                            <select
                              disabled={!isGranted}
                              value={currentScope}
                              onChange={(e) => {
                                setRolePerms({ ...rolePerms, [p.code]: e.target.value });
                              }}
                              style={{ padding: "4px 8px", fontSize: "12px" }}
                            >
                              <option value="All">All (Entire Office)</option>
                              <option value="Workstream">Workstream (Scoped to User Workstream)</option>
                              <option value="Own">Own (Created by User)</option>
                              <option value="Assigned">Assigned (Fails closed in Phase 1)</option>
                            </select>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </fieldset>

              <div className="form-footer">
                <button type="button" className="secondary-button" onClick={() => setShowRoleModal(false)}>
                  Cancel
                </button>
                <button type="submit">
                  {editingRole ? "Save Changes" : "Create Role"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Office Desk Create/Edit Modal */}
      {showDeskModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "560px" }}>
            <h3>{editingDesk ? `Edit Desk: ${editingDesk.name}` : "Create New Office Desk"}</h3>
            <form onSubmit={handleSaveDesk} className="lr-form">
              <div className="field-grid">
                <label>
                  Desk Code *
                  <input
                    type="text"
                    required
                    disabled={Boolean(editingDesk)}
                    value={deskCode}
                    onChange={(e) => setDeskCode(e.target.value)}
                    placeholder="e.g. NT_DESK, ACCOUNTS_CLERK"
                  />
                </label>
                <label>
                  Desk Name *
                  <input
                    type="text"
                    required
                    value={deskName}
                    onChange={(e) => setDeskName(e.target.value)}
                    placeholder="e.g. Naib Tehsildar Desk"
                  />
                </label>
                <label className="span-two">
                  Description
                  <input
                    type="text"
                    value={deskDesc}
                    onChange={(e) => setDeskDesc(e.target.value)}
                    placeholder="Physical or functional desk description"
                  />
                </label>
                <label className="span-two">
                  Associated Workstream (Classification)
                  <select
                    value={deskWorkstreamId}
                    onChange={(e) => setDeskWorkstreamId(e.target.value)}
                  >
                    <option value="">-- Unclassified (No specific workstream) --</option>
                    {workstreams.map((ws) => (
                      <option key={ws.id} value={ws.id}>
                        {ws.name} ({ws.code})
                      </option>
                    ))}
                  </select>
                  <small style={{ color: "#667", display: "block", marginTop: "4px" }}>
                    Workstream classification is informational and provides default context; it does not grant or restrict software permissions.
                  </small>
                </label>
              </div>

              <div className="form-footer">
                <button type="button" className="secondary-button" onClick={() => setShowDeskModal(false)}>
                  Cancel
                </button>
                <button type="submit">
                  {editingDesk ? "Save Changes" : "Create Office Desk"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
