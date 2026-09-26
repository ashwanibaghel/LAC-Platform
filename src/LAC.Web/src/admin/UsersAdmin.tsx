import React, { useEffect, useState, useCallback } from "react";
import type { Designation, Workstream } from "../auth/types";
import { PasswordInput } from "../auth/PasswordInput";

interface UserItem {
  id: string;
  username: string;
  displayName: string;
  designation?: Designation;
  isActive: boolean;
  lastLoginAt?: string;
  createdAt: string;
  roles: string[];
  workstreams: string[];
  primaryDeskName?: string;
  activeDesksCount: number;
}

interface RoleItem {
  id: string;
  code: string;
  name: string;
}

interface DeskItem {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

interface UserDeskMembershipItem {
  id: string;
  officeDeskId: string;
  deskCode: string;
  deskName: string;
  workstreamName?: string;
  isPrimary: boolean;
  isActive: boolean;
  assignedAt: string;
  removedAt?: string;
}

export const UsersAdmin: React.FC = () => {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modals state
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [resetTargetUser, setResetTargetUser] = useState<UserItem | null>(null);
  const [deskTargetUser, setDeskTargetUser] = useState<UserItem | null>(null);
  const [userDesks, setUserDesks] = useState<UserDeskMembershipItem[]>([]);
  const [userDesksLoading, setUserDesksLoading] = useState(false);

  // Form states - Create User
  const [newUsername, setNewUsername] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedWorkstreamIds, setSelectedWorkstreamIds] = useState<string[]>([]);
  const [primaryWorkstreamId, setPrimaryWorkstreamId] = useState("");

  // Form states - Assign Desk
  const [assignDeskId, setAssignDeskId] = useState("");
  const [assignDeskPrimary, setAssignDeskPrimary] = useState(false);

  const [resetPasswordValue, setResetPasswordValue] = useState("");
  const [actionLoading, setActionLoading] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [usersRes, desigRes, wsRes, rolesRes, desksRes] = await Promise.all([
        fetch("/api/admin/users", { credentials: "include" }),
        fetch("/api/admin/designations", { credentials: "include" }),
        fetch("/api/admin/workstreams", { credentials: "include" }),
        fetch("/api/admin/roles", { credentials: "include" }),
        fetch("/api/admin/desks", { credentials: "include" }),
      ]);

      if (!usersRes.ok || !desigRes.ok || !wsRes.ok || !rolesRes.ok || !desksRes.ok) {
        throw new Error("Failed to load user administration data.");
      }

      setUsers((await usersRes.json()) as UserItem[]);
      setDesignations((await desigRes.json()) as Designation[]);
      setWorkstreams((await wsRes.json()) as Workstream[]);
      setRoles((await rolesRes.json()) as RoleItem[]);
      setDesks((await desksRes.json()) as DeskItem[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const loadUserDesks = async (userId: string) => {
    try {
      setUserDesksLoading(true);
      const res = await fetch(`/api/admin/users/${userId}/desks`, { credentials: "include" });
      if (!res.ok) throw new Error("Failed to load user desks.");
      const list = (await res.json()) as UserDeskMembershipItem[];
      setUserDesks(list);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error loading user desks.");
    } finally {
      setUserDesksLoading(false);
    }
  };

  const openManageDesksModal = (u: UserItem) => {
    setDeskTargetUser(u);
    setAssignDeskId("");
    setAssignDeskPrimary(false);
    void loadUserDesks(u.id);
  };

  const handleAssignDesk = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!deskTargetUser || !assignDeskId) return;

    try {
      setActionLoading(true);
      const res = await fetch(`/api/admin/users/${deskTargetUser.id}/desks`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          officeDeskId: assignDeskId,
          isPrimary: assignDeskPrimary,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        const err = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to assign desk.");
      }

      setAssignDeskId("");
      setAssignDeskPrimary(false);
      await loadUserDesks(deskTargetUser.id);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error assigning desk.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleSetPrimaryDesk = async (membershipId: string) => {
    if (!deskTargetUser) return;
    try {
      const res = await fetch(`/api/admin/users/${deskTargetUser.id}/desks/${membershipId}/set-primary`, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to set primary desk.");
      await loadUserDesks(deskTargetUser.id);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error setting primary desk.");
    }
  };

  const handleRemoveDesk = async (membershipId: string) => {
    if (!deskTargetUser) return;
    if (!confirm("Are you sure you want to remove this user from this desk?")) return;
    try {
      const res = await fetch(`/api/admin/users/${deskTargetUser.id}/desks/${membershipId}/remove`, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to remove desk membership.");
      await loadUserDesks(deskTargetUser.id);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error removing desk membership.");
    }
  };

  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setActionLoading(true);
      setActionMessage(null);
      const payload = {
        username: newUsername.trim(),
        displayName: newDisplayName.trim(),
        password: newPassword,
        designationId: newDesignationId || null,
        roleIds: selectedRoleIds,
        workstreamIds: selectedWorkstreamIds,
        primaryWorkstreamId: primaryWorkstreamId || null,
      };

      const response = await fetch("/api/admin/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!response.ok) {
        const err = (await response.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to create user account.");
      }

      setShowCreateModal(false);
      setNewUsername("");
      setNewDisplayName("");
      setNewPassword("");
      setNewDesignationId("");
      setSelectedRoleIds([]);
      setSelectedWorkstreamIds([]);
      setPrimaryWorkstreamId("");
      setActionMessage("User created successfully.");
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to create user.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleToggleStatus = async (user: UserItem) => {
    try {
      const response = await fetch(`/api/admin/users/${user.id}/toggle-status`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) throw new Error("Failed to toggle status.");
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to toggle status.");
    }
  };

  const handleResetPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!resetTargetUser || !resetPasswordValue) return;

    try {
      setActionLoading(true);
      const response = await fetch(`/api/admin/users/${resetTargetUser.id}/reset-password`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ newPassword: resetPasswordValue }),
        credentials: "include",
      });

      if (!response.ok) {
        const err = (await response.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to reset password.");
      }

      setResetTargetUser(null);
      setResetPasswordValue("");
      alert("Password has been reset successfully.");
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to reset password.");
    } finally {
      setActionLoading(false);
    }
  };

  if (loading) {
    return <div className="state"><strong>Loading users...</strong></div>;
  }

  const activeMemberships = userDesks.filter((m) => m.isActive);
  const inactiveMemberships = userDesks.filter((m) => !m.isActive);

  return (
    <div className="admin-page">
      <div className="section-heading">
        <div>
          <h2>User Accounts & Assignments</h2>
          <span>Manage personnel, official civil designations, roles, workstreams, and office desk memberships</span>
        </div>
        <button className="primary-button" onClick={() => setShowCreateModal(true)}>
          + Create New Account
        </button>
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}
      {actionMessage && <div className="form-message">{actionMessage}</div>}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Username</th>
              <th>Official Name</th>
              <th>Designation</th>
              <th>Status</th>
              <th>Assigned Roles</th>
              <th>Workstreams</th>
              <th>Office Desks</th>
              <th>Last Login</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {users.map((u) => (
              <tr key={u.id}>
                <td><strong>{u.username}</strong></td>
                <td>{u.displayName}</td>
                <td>{u.designation ? `${u.designation.name} (${u.designation.code})` : "—"}</td>
                <td>
                  <span className={`status ${u.isActive ? "success" : "warning"}`}>
                    {u.isActive ? "Active" : "Inactive"}
                  </span>
                </td>
                <td>
                  {u.roles.length ? u.roles.join(", ") : <span className="subtext">None</span>}
                </td>
                <td>
                  {u.workstreams.length ? u.workstreams.join(", ") : <span className="subtext">None</span>}
                </td>
                <td>
                  {u.primaryDeskName ? (
                    <div>
                      <span className="badge badge-primary" title="Primary Office Desk">
                        {u.primaryDeskName}
                      </span>
                      {u.activeDesksCount > 1 && (
                        <span className="subtext" style={{ marginLeft: "4px" }}>
                          (+{u.activeDesksCount - 1} more)
                        </span>
                      )}
                    </div>
                  ) : u.activeDesksCount > 0 ? (
                    <span className="subtext">{u.activeDesksCount} desk(s) (no primary)</span>
                  ) : (
                    <span className="subtext">No desks</span>
                  )}
                </td>
                <td>
                  {u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : "Never"}
                </td>
                <td>
                  <div style={{ display: "flex", gap: "6px" }}>
                    <button
                      className="quiet-button text-action"
                      onClick={() => openManageDesksModal(u)}
                    >
                      Manage Desks
                    </button>
                    <button
                      className="quiet-button text-action"
                      onClick={() => handleToggleStatus(u)}
                    >
                      {u.isActive ? "Deactivate" : "Activate"}
                    </button>
                    <button
                      className="quiet-button text-action"
                      onClick={() => {
                        setResetTargetUser(u);
                        setResetPasswordValue("");
                      }}
                    >
                      Reset Pass
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Manage Desks Modal */}
      {deskTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "700px" }}>
            <h3>Manage Office Desks: {deskTargetUser.displayName} (@{deskTargetUser.username})</h3>

            {userDesksLoading ? (
              <div className="state">Loading desk memberships...</div>
            ) : (
              <div>
                <div style={{ marginBottom: "16px" }}>
                  <h4 style={{ margin: "10px 0 6px 0" }}>Active Desk Memberships ({activeMemberships.length})</h4>
                  {activeMemberships.length === 0 ? (
                    <p className="subtext">User is not currently assigned to any active office desk.</p>
                  ) : (
                    <table style={{ width: "100%", fontSize: "13px" }}>
                      <thead>
                        <tr>
                          <th>Desk</th>
                          <th>Workstream</th>
                          <th>Role</th>
                          <th>Assigned Since</th>
                          <th>Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {activeMemberships.map((m) => (
                          <tr key={m.id}>
                            <td>
                              <strong>{m.deskName}</strong> <small style={{ color: "#667" }}>({m.deskCode})</small>
                            </td>
                            <td>{m.workstreamName || <span className="subtext">—</span>}</td>
                            <td>
                              {m.isPrimary ? (
                                <span className="badge badge-primary">Primary Desk</span>
                              ) : (
                                <span className="subtext">Secondary</span>
                              )}
                            </td>
                            <td>{new Date(m.assignedAt).toLocaleDateString()}</td>
                            <td>
                              <div style={{ display: "flex", gap: "4px" }}>
                                {!m.isPrimary && (
                                  <button
                                    type="button"
                                    className="quiet-button text-action"
                                    onClick={() => handleSetPrimaryDesk(m.id)}
                                  >
                                    Set Primary
                                  </button>
                                )}
                                <button
                                  type="button"
                                  className="quiet-button text-action"
                                  style={{ color: "#c00" }}
                                  onClick={() => handleRemoveDesk(m.id)}
                                >
                                  Remove
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>

                <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginBottom: "16px" }}>
                  <h4 style={{ margin: "0 0 8px 0" }}>Assign New Office Desk</h4>
                  <form onSubmit={handleAssignDesk} style={{ display: "flex", gap: "10px", alignItems: "flex-end", flexWrap: "wrap" }}>
                    <label style={{ flex: 1, minWidth: "200px" }}>
                      Select Desk
                      <select
                        required
                        value={assignDeskId}
                        onChange={(e) => setAssignDeskId(e.target.value)}
                        style={{ width: "100%", padding: "6px 8px" }}
                      >
                        <option value="">-- Choose an operational desk --</option>
                        {desks
                          .filter((d) => d.isActive)
                          .map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name} ({d.code})
                            </option>
                          ))}
                      </select>
                    </label>

                    <label style={{ display: "flex", alignItems: "center", gap: "6px", marginBottom: "8px" }}>
                      <input
                        type="checkbox"
                        checked={assignDeskPrimary}
                        onChange={(e) => setAssignDeskPrimary(e.target.checked)}
                      />
                      Set as Primary Desk
                    </label>

                    <button
                      type="submit"
                      disabled={!assignDeskId || actionLoading}
                      className="primary-button"
                      style={{ marginBottom: "6px" }}
                    >
                      Assign Desk
                    </button>
                  </form>
                </div>

                {inactiveMemberships.length > 0 && (
                  <div style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
                    <h4 style={{ margin: "0 0 6px 0", color: "#667" }}>Membership History ({inactiveMemberships.length})</h4>
                    <table style={{ width: "100%", fontSize: "12px", color: "#667" }}>
                      <thead>
                        <tr>
                          <th>Desk</th>
                          <th>Assigned At</th>
                          <th>Removed At</th>
                          <th>Status</th>
                        </tr>
                      </thead>
                      <tbody>
                        {inactiveMemberships.map((m) => (
                          <tr key={m.id}>
                            <td>{m.deskName} ({m.deskCode})</td>
                            <td>{new Date(m.assignedAt).toLocaleDateString()}</td>
                            <td>{m.removedAt ? new Date(m.removedAt).toLocaleDateString() : "—"}</td>
                            <td><span className="status neutral">Historical</span></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                )}
              </div>
            )}

            <div className="form-footer" style={{ marginTop: "16px" }}>
              <button
                type="button"
                className="secondary-button"
                onClick={() => setDeskTargetUser(null)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create User Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Create New User Account</h3>
            <form onSubmit={handleCreateUser} className="lr-form">
              <div className="field-grid">
                <label>
                  Username *
                  <input
                    type="text"
                    required
                    value={newUsername}
                    onChange={(e) => setNewUsername(e.target.value)}
                    placeholder="e.g. s.verma"
                  />
                </label>
                <label>
                  Display / Official Name *
                  <input
                    type="text"
                    required
                    value={newDisplayName}
                    onChange={(e) => setNewDisplayName(e.target.value)}
                    placeholder="e.g. Sh. Satish Verma"
                  />
                </label>
                <label className="span-two">
                  Initial Password *
                  <PasswordInput
                    required
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    placeholder="Temporary password"
                  />
                </label>
                <label className="span-two">
                  Official Civil Designation
                  <select
                    value={newDesignationId}
                    onChange={(e) => setNewDesignationId(e.target.value)}
                  >
                    <option value="">-- No Designation --</option>
                    {designations.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name} ({d.code})
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              <fieldset>
                <legend>Assigned Role Bundles (Software Authorities)</legend>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                  {roles.map((r) => (
                    <label key={r.id} style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px" }}>
                      <input
                        type="checkbox"
                        checked={selectedRoleIds.includes(r.id)}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setSelectedRoleIds([...selectedRoleIds, r.id]);
                          } else {
                            setSelectedRoleIds(selectedRoleIds.filter((id) => id !== r.id));
                          }
                        }}
                      />
                      <span>{r.name} <code>({r.code})</code></span>
                    </label>
                  ))}
                </div>
              </fieldset>

              <fieldset>
                <legend>Workstream Memberships (Functional Context)</legend>
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                  {workstreams.map((ws) => (
                    <label key={ws.id} style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px" }}>
                      <input
                        type="checkbox"
                        checked={selectedWorkstreamIds.includes(ws.id)}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setSelectedWorkstreamIds([...selectedWorkstreamIds, ws.id]);
                            if (!primaryWorkstreamId) setPrimaryWorkstreamId(ws.id);
                          } else {
                            setSelectedWorkstreamIds(selectedWorkstreamIds.filter((id) => id !== ws.id));
                            if (primaryWorkstreamId === ws.id) setPrimaryWorkstreamId("");
                          }
                        }}
                      />
                      <span>{ws.name}</span>
                    </label>
                  ))}
                </div>
                {selectedWorkstreamIds.length > 0 && (
                  <label style={{ marginTop: "12px", display: "block" }}>
                    Primary Workstream
                    <select
                      value={primaryWorkstreamId}
                      onChange={(e) => setPrimaryWorkstreamId(e.target.value)}
                    >
                      <option value="">-- None --</option>
                      {workstreams
                        .filter((ws) => selectedWorkstreamIds.includes(ws.id))
                        .map((ws) => (
                          <option key={ws.id} value={ws.id}>
                            {ws.name} ({ws.code})
                          </option>
                        ))}
                    </select>
                  </label>
                )}
              </fieldset>

              <div className="form-footer">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setShowCreateModal(false)}
                >
                  Cancel
                </button>
                <button type="submit" disabled={actionLoading}>
                  Create User
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reset Password Modal */}
      {resetTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Reset Password: {resetTargetUser.displayName}</h3>
            <form onSubmit={handleResetPassword} className="lr-form">
              <label>
                New Password *
                <PasswordInput
                  required
                  value={resetPasswordValue}
                  onChange={(e) => setResetPasswordValue(e.target.value)}
                  placeholder="Enter new password"
                />
              </label>
              <div className="form-footer">
                <button
                  type="button"
                  className="secondary-button"
                  onClick={() => setResetTargetUser(null)}
                >
                  Cancel
                </button>
                <button type="submit" disabled={actionLoading}>
                  Update Password
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
