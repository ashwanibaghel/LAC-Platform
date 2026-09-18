import React, { useEffect, useState, useCallback } from "react";
import type { Designation, Workstream } from "../auth/types";

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
}

interface RoleItem {
  id: string;
  code: string;
  name: string;
}

export const UsersAdmin: React.FC = () => {
  const [users, setUsers] = useState<UserItem[]>([]);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modals state
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [resetTargetUser, setResetTargetUser] = useState<UserItem | null>(null);

  // Form states
  const [newUsername, setNewUsername] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedWorkstreamIds, setSelectedWorkstreamIds] = useState<string[]>([]);
  const [primaryWorkstreamId, setPrimaryWorkstreamId] = useState("");

  const [resetPasswordValue, setResetPasswordValue] = useState("");
  const [actionLoading, setActionLoading] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [usersRes, desigRes, wsRes, rolesRes] = await Promise.all([
        fetch("/api/admin/users", { credentials: "include" }),
        fetch("/api/admin/designations", { credentials: "include" }),
        fetch("/api/admin/workstreams", { credentials: "include" }),
        fetch("/api/admin/roles", { credentials: "include" }),
      ]);

      if (!usersRes.ok || !desigRes.ok || !wsRes.ok || !rolesRes.ok) {
        throw new Error("Failed to load user administration data.");
      }

      setUsers((await usersRes.json()) as UserItem[]);
      setDesignations((await desigRes.json()) as Designation[]);
      setWorkstreams((await wsRes.json()) as Workstream[]);
      setRoles((await rolesRes.json()) as RoleItem[]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load data.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

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
        throw new Error(err?.message || "Failed to create user.");
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
      setActionMessage(err instanceof Error ? err.message : "Failed to create user.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleToggleStatus = async (user: UserItem) => {
    if (!confirm(`Are you sure you want to ${user.isActive ? "deactivate" : "activate"} user ${user.username}?`)) {
      return;
    }

    try {
      const response = await fetch(`/api/admin/users/${user.id}/toggle-status`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) {
        const err = (await response.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to update user status.");
      }
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

  return (
    <div className="admin-page">
      <div className="section-heading">
        <div>
          <h2>User Accounts & Assignments</h2>
          <span>Manage personnel, official civil designations, roles, and workstream memberships</span>
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
                  {u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : "Never"}
                </td>
                <td>
                  <div style={{ display: "flex", gap: "6px" }}>
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

      {/* Create User Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>Create Official Account</h3>
            <form onSubmit={handleCreateUser} className="lr-form">
              <div className="field-grid">
                <label>
                  Username *
                  <input
                    type="text"
                    required
                    value={newUsername}
                    onChange={(e) => setNewUsername(e.target.value)}
                    placeholder="e.g. s_kumar"
                  />
                </label>
                <label>
                  Official Display Name *
                  <input
                    type="text"
                    required
                    value={newDisplayName}
                    onChange={(e) => setNewDisplayName(e.target.value)}
                    placeholder="e.g. Shri Suresh Kumar"
                  />
                </label>
                <label>
                  Initial Password *
                  <input
                    type="password"
                    required
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    placeholder="••••••••"
                  />
                </label>
                <label>
                  Designation (Civil Post)
                  <select
                    value={newDesignationId}
                    onChange={(e) => setNewDesignationId(e.target.value)}
                  >
                    <option value="">-- Select Civil Designation --</option>
                    {designations.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name} ({d.code})
                      </option>
                    ))}
                  </select>
                </label>
              </div>

              <fieldset>
                <legend>Role Authorities (Software Access Bundle)</legend>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: "8px" }}>
                  {roles.map((r) => (
                    <label key={r.id} style={{ display: "flex", alignItems: "center", gap: "6px" }}>
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
                      <span>{r.name} ({r.code})</span>
                    </label>
                  ))}
                </div>
              </fieldset>

              <fieldset>
                <legend>Functional Workstreams</legend>
                <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: "8px" }}>
                  {workstreams.map((w) => (
                    <label key={w.id} style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                      <input
                        type="checkbox"
                        checked={selectedWorkstreamIds.includes(w.id)}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setSelectedWorkstreamIds([...selectedWorkstreamIds, w.id]);
                            if (!primaryWorkstreamId) setPrimaryWorkstreamId(w.id);
                          } else {
                            setSelectedWorkstreamIds(selectedWorkstreamIds.filter((id) => id !== w.id));
                            if (primaryWorkstreamId === w.id) setPrimaryWorkstreamId("");
                          }
                        }}
                      />
                      <span>{w.name} ({w.code})</span>
                    </label>
                  ))}
                </div>
              </fieldset>

              <div className="form-footer">
                <button type="button" className="secondary-button" onClick={() => setShowCreateModal(false)}>
                  Cancel
                </button>
                <button type="submit" disabled={actionLoading}>
                  {actionLoading ? "Saving..." : "Create Account"}
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
            <h3>Reset Password: {resetTargetUser.username}</h3>
            <form onSubmit={handleResetPassword} className="lr-form">
              <label>
                New Password *
                <input
                  type="password"
                  required
                  value={resetPasswordValue}
                  onChange={(e) => setResetPasswordValue(e.target.value)}
                  placeholder="Enter new strong password"
                />
              </label>
              <div className="form-footer">
                <button type="button" className="secondary-button" onClick={() => setResetTargetUser(null)}>
                  Cancel
                </button>
                <button type="submit" disabled={actionLoading}>
                  {actionLoading ? "Updating..." : "Update Password"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
