import React, { useEffect, useState, useCallback, useMemo } from "react";
import type { Designation, Workstream, DeskItem, UserDeskMembershipItem, RoleDetail, OfficerItem, OfficerDetail } from "./types";
import { PasswordInput } from "../auth/PasswordInput";
import "./admin.css";

export const UsersAdmin: React.FC = () => {
  const [users, setUsers] = useState<OfficerItem[]>([]);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [roles, setRoles] = useState<RoleDetail[]>([]);
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters & Search
  const [searchQuery, setSearchQuery] = useState("");
  const [filterDesignationId, setFilterDesignationId] = useState("ALL");
  const [filterWorkstreamCode, setFilterWorkstreamCode] = useState("ALL");
  const [filterRoleCode, setFilterRoleCode] = useState("ALL");
  const [filterStatus, setFilterStatus] = useState("ALL");

  // Modals & Drawers state
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [editingOfficer, setEditingOfficer] = useState<OfficerDetail | null>(null);
  const [inspectingOfficer, setInspectingOfficer] = useState<OfficerItem | null>(null);
  const [resetTargetUser, setResetTargetUser] = useState<OfficerItem | null>(null);
  const [deskTargetUser, setDeskTargetUser] = useState<OfficerItem | null>(null);
  const [userDesks, setUserDesks] = useState<UserDeskMembershipItem[]>([]);
  const [userDesksLoading, setUserDesksLoading] = useState(false);

  // Form state - Edit Officer Allocation Drawer
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editDesignationId, setEditDesignationId] = useState("");
  const [editRoleIds, setEditRoleIds] = useState<string[]>([]);
  const [editWorkstreamIds, setEditWorkstreamIds] = useState<string[]>([]);
  const [editPrimaryWorkstreamId, setEditPrimaryWorkstreamId] = useState("");
  const [editDrawerLoading, setEditDrawerLoading] = useState(false);

  // Form states - Create User Modal
  const [newUsername, setNewUsername] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedWorkstreamIds, setSelectedWorkstreamIds] = useState<string[]>([]);
  const [primaryWorkstreamId, setPrimaryWorkstreamId] = useState("");

  // Form states - Assign Desk Modal
  const [assignDeskId, setAssignDeskId] = useState("");
  const [assignDeskPrimary, setAssignDeskPrimary] = useState(false);

  // Reset Password State
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

      setUsers((await usersRes.json()) as OfficerItem[]);
      setDesignations((await desigRes.json()) as Designation[]);
      setWorkstreams((await wsRes.json()) as Workstream[]);
      setRoles((await rolesRes.json()) as RoleDetail[]);
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

  // Open Edit Officer Drawer
  const openEditDrawer = async (officer: OfficerItem) => {
    try {
      setEditDrawerLoading(true);
      const res = await fetch(`/api/admin/users/${officer.id}`, { credentials: "include" });
      if (!res.ok) throw new Error("Failed to load officer details.");
      const detail = (await res.json()) as OfficerDetail;

      setEditingOfficer(detail);
      setEditDisplayName(detail.displayName);
      setEditDesignationId(detail.designationId || "");
      setEditRoleIds(detail.roleIds || []);
      const wsIds = detail.workstreams.map((w) => w.id);
      setEditWorkstreamIds(wsIds);
      const primaryWs = detail.workstreams.find((w) => w.isPrimary);
      setEditPrimaryWorkstreamId(primaryWs ? primaryWs.id : wsIds[0] || "");
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error loading officer.");
    } finally {
      setEditDrawerLoading(false);
    }
  };

  const handleSaveOfficerAllocations = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;

    // Staged pending Codex RBAC hardened allocation contract to fix known privilege escalation gap
    setActionMessage("Officer allocations staged in UI. Backend persistence paused pending Codex RBAC hardened authorization contract.");
    setEditingOfficer(null);
  };

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

  const openManageDesksModal = (u: OfficerItem) => {
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
        roleIds: [], // Basic identity creation only; privileged roles staged pending Codex RBAC contract
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
        throw new Error(err?.message || "Failed to create officer account.");
      }

      setShowCreateModal(false);
      setNewUsername("");
      setNewDisplayName("");
      setNewPassword("");
      setNewDesignationId("");
      setSelectedRoleIds([]);
      setSelectedWorkstreamIds([]);
      setPrimaryWorkstreamId("");
      setActionMessage("Officer account created successfully.");
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to create officer.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleToggleStatus = async (user: OfficerItem) => {
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
    if (!resetTargetUser) return;

    // Staged pending Codex RBAC backend session invalidation hardening
    alert("Temporary password reset execution is paused pending Codex RBAC backend session invalidation hardening. Active sessions currently survive credential changes.");
    setResetTargetUser(null);
    setResetPasswordValue("");
  };

  // Helper for Civil Rank Class
  const getRankClass = (code?: string) => {
    if (!code) return "staff";
    const c = code.toUpperCase();
    if (c.includes("SDM") || c.includes("COLLECTOR") || c.includes("LAC")) return "sdm";
    if (c.includes("TEHSILDAR") && !c.includes("NAIB")) return "tehsildar";
    if (c.includes("NAIB")) return "naib";
    if (c.includes("KANUNGO")) return "kanungo";
    if (c.includes("PATWARI")) return "patwari";
    return "staff";
  };

  // Filtered Officers List
  const filteredOfficers = useMemo(() => {
    return users.filter((u) => {
      const matchesSearch =
        searchQuery.trim() === "" ||
        u.displayName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        u.username.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (u.designation && u.designation.name.toLowerCase().includes(searchQuery.toLowerCase()));

      const matchesDesignation =
        filterDesignationId === "ALL" || (u.designation && u.designation.id === filterDesignationId);

      const matchesWorkstream =
        filterWorkstreamCode === "ALL" || u.workstreams.includes(filterWorkstreamCode);

      const matchesRole =
        filterRoleCode === "ALL" || u.roles.includes(filterRoleCode);

      const matchesStatus =
        filterStatus === "ALL" ||
        (filterStatus === "ACTIVE" && u.isActive) ||
        (filterStatus === "INACTIVE" && !u.isActive);

      return matchesSearch && matchesDesignation && matchesWorkstream && matchesRole && matchesStatus;
    });
  }, [users, searchQuery, filterDesignationId, filterWorkstreamCode, filterRoleCode, filterStatus]);

  if (loading) {
    return (
      <div className="rbac-admin-root">
        <div className="state"><strong>Loading officer directory...</strong></div>
      </div>
    );
  }

  const activeMemberships = userDesks.filter((m) => m.isActive);

  return (
    <div className="rbac-admin-root">
      {/* Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Officers & Staff Directory</h2>
          <p>
            Official personnel registry, civil rank assignments, authority roles, functional workstreams, and operational desks.
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="primary-button" onClick={() => setShowCreateModal(true)}>
            + Add Officer / Staff
          </button>
        </div>
      </div>

      {actionMessage && <div className="form-message">{actionMessage}</div>}
      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search officer name, username, or post..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={filterDesignationId}
          onChange={(e) => setFilterDesignationId(e.target.value)}
        >
          <option value="ALL">All Civil Designations</option>
          {designations.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name} ({d.code})
            </option>
          ))}
        </select>

        <select
          className="rbac-filter-select"
          value={filterWorkstreamCode}
          onChange={(e) => setFilterWorkstreamCode(e.target.value)}
        >
          <option value="ALL">All Branches</option>
          {workstreams.map((w) => (
            <option key={w.code} value={w.code}>
              {w.name}
            </option>
          ))}
        </select>

        <select
          className="rbac-filter-select"
          value={filterRoleCode}
          onChange={(e) => setFilterRoleCode(e.target.value)}
        >
          <option value="ALL">All Roles</option>
          {roles.map((r) => (
            <option key={r.code} value={r.code}>
              {r.name} ({r.code})
            </option>
          ))}
        </select>

        <select
          className="rbac-filter-select"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value)}
        >
          <option value="ALL">All Statuses</option>
          <option value="ACTIVE">Active Only</option>
          <option value="INACTIVE">Inactive Only</option>
        </select>

        <span style={{ fontSize: "13px", color: "#64748b", marginLeft: "auto" }}>
          Showing <strong>{filteredOfficers.length}</strong> of {users.length} Officers
        </span>
      </div>

      {/* Officer Directory Table */}
      <div className="rbac-table-container">
        <table className="rbac-table">
          <thead>
            <tr>
              <th>Officer & Username</th>
              <th>Civil Rank</th>
              <th>Status</th>
              <th>Assigned Authority Roles</th>
              <th>Functional Branches</th>
              <th>Operational Desk</th>
              <th>Last Active</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filteredOfficers.map((u) => {
              const rankClass = getRankClass(u.designation?.code);
              const initials = u.displayName
                .split(" ")
                .filter(Boolean)
                .map((n) => n[0])
                .slice(0, 2)
                .join("")
                .toUpperCase() || "OF";

              return (
                <tr key={u.id}>
                  <td>
                    <div className="officer-cell">
                      <div className={`officer-avatar ${rankClass}`}>
                        {initials}
                      </div>
                      <div className="officer-info">
                        <span className="officer-name">{u.displayName}</span>
                        <span className="officer-user">@{u.username}</span>
                      </div>
                    </div>
                  </td>
                  <td>
                    {u.designation ? (
                      <span className={`rank-badge ${rankClass}`}>
                        {u.designation.name}
                      </span>
                    ) : (
                      <span className="subtext">Unassigned</span>
                    )}
                  </td>
                  <td>
                    <span className={`status ${u.isActive ? "success" : "warning"}`}>
                      {u.isActive ? "Active" : "Inactive"}
                    </span>
                  </td>
                  <td>
                    <div className="role-tags-container">
                      {u.roles.length ? (
                        u.roles.map((r) => (
                          <span key={r} className={`role-tag ${r === "SYSTEM_ADMIN" ? "admin" : ""}`}>
                            {r.replace("_", " ")}
                          </span>
                        ))
                      ) : (
                        <span className="subtext">No roles</span>
                      )}
                    </div>
                  </td>
                  <td>
                    <div className="role-tags-container">
                      {u.workstreams.length ? (
                        u.workstreams.map((w, idx) => (
                          <span key={w} className={`workstream-tag ${idx === 0 ? "primary" : ""}`}>
                            {w}
                          </span>
                        ))
                      ) : (
                        <span className="subtext">No branches</span>
                      )}
                    </div>
                  </td>
                  <td>
                    {u.primaryDeskName ? (
                      <div className="desk-chip primary" title="Primary operational seat">
                        🪑 {u.primaryDeskName}
                      </div>
                    ) : u.activeDesksCount > 0 ? (
                      <span className="subtext">{u.activeDesksCount} seat(s)</span>
                    ) : (
                      <span className="subtext">No desk</span>
                    )}
                  </td>
                  <td>
                    <span style={{ fontSize: "12px", color: "#64748b" }}>
                      {u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleDateString() : "Never"}
                    </span>
                  </td>
                  <td>
                    <div className="rbac-action-buttons">
                      <button
                        className="rbac-btn-edit"
                        onClick={() => openEditDrawer(u)}
                        title="Edit Designation, Roles, and Branch allocations"
                      >
                        Edit Allocations
                      </button>
                      <button
                        className="rbac-btn-inspect"
                        onClick={() => setInspectingOfficer(u)}
                        title="Inspect effective operational powers and scopes in plain English"
                      >
                        Access Inspector
                      </button>
                      <button
                        className="rbac-btn-edit"
                        onClick={() => openManageDesksModal(u)}
                        title="Manage assigned office desks and primary seat"
                      >
                        Desks
                      </button>
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {/* Slide-over Drawer: Edit Officer Allocation */}
      {editingOfficer && (
        <div className="drawer-backdrop" onClick={() => setEditingOfficer(null)}>
          <div className="drawer-panel" onClick={(e) => e.stopPropagation()}>
            <div className="drawer-header">
              <div>
                <h3>Edit Officer Allocations</h3>
                <p>
                  Update civil designation, multiple roles, and branch assignments for <strong>{editingOfficer.displayName}</strong> (@{editingOfficer.username})
                </p>
              </div>
              <button className="drawer-close-btn" onClick={() => setEditingOfficer(null)}>
                ✕
              </button>
            </div>

            {editDrawerLoading ? (
              <div className="state"><strong>Loading officer allocations...</strong></div>
            ) : (
              <form onSubmit={handleSaveOfficerAllocations} style={{ display: "flex", flexDirection: "column", height: "100%" }}>
                <div className="drawer-body">
                  {/* Staged Allocation UX Notice */}
                  <div style={{ background: "#f0f9ff", border: "1px solid #bae6fd", color: "#0369a1", padding: "10px 12px", borderRadius: "6px", marginBottom: "16px", fontSize: "12px" }}>
                    <strong>Staged Allocation UX:</strong>
                    <p style={{ margin: "4px 0 0", color: "#0c4a6e" }}>
                      Role and work allocations are staged in this administrative UX. Live save via PUT /api/admin/users/:id is paused while Codex RBAC hardens role-assignment validation to prevent privilege escalation.
                    </p>
                  </div>

                  {/* Official Identity */}
                  <div>
                    <div className="form-section-title">1. Official Identity & Civil Rank</div>
                    <div className="form-group" style={{ marginBottom: "12px" }}>
                      <label>Official Display Name</label>
                      <input
                        type="text"
                        required
                        value={editDisplayName}
                        onChange={(e) => setEditDisplayName(e.target.value)}
                      />
                    </div>
                    <div className="form-group">
                      <label>Civil Designation (Post / Rank)</label>
                      <select
                        value={editDesignationId}
                        onChange={(e) => setEditDesignationId(e.target.value)}
                      >
                        <option value="">No Civil Designation</option>
                        {designations.map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name} ({d.code})
                          </option>
                        ))}
                      </select>
                    </div>
                  </div>

                  {/* Authority Roles (Multi-Role Model) */}
                  <div>
                    <div className="form-section-title">2. Authority Roles (Multi-Role Assignment)</div>
                    <p className="subtext" style={{ margin: "0 0 8px 0" }}>
                      Select all authority roles this officer holds. Multi-role assignment allows combined responsibility (e.g. Intake Officer + Record Verifier).
                    </p>
                    <div className="multi-select-grid">
                      {roles.map((r) => (
                        <label key={r.id} className="multi-select-item">
                          <input
                            type="checkbox"
                            checked={editRoleIds.includes(r.id)}
                            onChange={() => {
                              setEditRoleIds((prev) =>
                                prev.includes(r.id) ? prev.filter((id) => id !== r.id) : [...prev, r.id]
                              );
                            }}
                          />
                          <div>
                            <strong>{r.name}</strong>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>{r.code}</div>
                          </div>
                        </label>
                      ))}
                    </div>
                  </div>

                  {/* Functional Branches & Primary Branch */}
                  <div>
                    <div className="form-section-title">3. Functional Branches (Workstreams)</div>
                    <p className="subtext" style={{ margin: "0 0 8px 0" }}>
                      Assign branches where this officer operates. Radio selects their primary administrative branch.
                    </p>
                    <div className="multi-select-grid">
                      {workstreams.map((w) => {
                        const isAssigned = editWorkstreamIds.includes(w.id);
                        return (
                          <div key={w.id} style={{ display: "flex", flexDirection: "column", gap: "4px" }}>
                            <label className="multi-select-item">
                              <input
                                type="checkbox"
                                checked={isAssigned}
                                onChange={() => {
                                  setEditWorkstreamIds((prev) => {
                                    const updated = prev.includes(w.id)
                                      ? prev.filter((id) => id !== w.id)
                                      : [...prev, w.id];
                                    if (!updated.includes(editPrimaryWorkstreamId)) {
                                      setEditPrimaryWorkstreamId(updated[0] || "");
                                    }
                                    return updated;
                                  });
                                }}
                              />
                              <span>{w.name}</span>
                            </label>
                            {isAssigned && (
                              <label style={{ fontSize: "11px", color: "#2563eb", marginLeft: "22px", cursor: "pointer" }}>
                                <input
                                  type="radio"
                                  name="primaryWorkstream"
                                  checked={editPrimaryWorkstreamId === w.id}
                                  onChange={() => setEditPrimaryWorkstreamId(w.id)}
                                />{" "}
                                Primary Branch
                              </label>
                            )}
                          </div>
                        );
                      })}
                    </div>
                  </div>

                  {/* Account Actions */}
                  <div>
                    <div className="form-section-title">4. Account State & Credentials</div>
                    <div style={{ display: "flex", gap: "10px" }}>
                      <button
                        type="button"
                        className="quiet-button text-action"
                        onClick={() => handleToggleStatus(editingOfficer as any)}
                      >
                        {editingOfficer.isActive ? "Deactivate Account" : "Activate Account"}
                      </button>
                      <button
                        type="button"
                        className="quiet-button text-action"
                        onClick={() => {
                          setResetTargetUser(editingOfficer as any);
                          setResetPasswordValue("");
                        }}
                      >
                        Issue Temporary Password Reset (Security Update Pending)
                      </button>
                    </div>
                  </div>
                </div>

                <div className="drawer-footer">
                  <button
                    type="button"
                    className="quiet-button"
                    onClick={() => setEditingOfficer(null)}
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    className="primary-button"
                    disabled={actionLoading}
                  >
                    {actionLoading ? "Saving..." : "Save Allocations (Staged — Pending Hardened RBAC)"}
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}

      {/* Slide-over Drawer: Plain-English Access Inspector */}
      {inspectingOfficer && (
        <div className="drawer-backdrop" onClick={() => setInspectingOfficer(null)}>
          <div className="drawer-panel" onClick={(e) => e.stopPropagation()}>
            <div className="drawer-header">
              <div>
                <h3>Access Inspector: Executive Summary</h3>
                <p>Plain-English operational authority and effective powers</p>
              </div>
              <button className="drawer-close-btn" onClick={() => setInspectingOfficer(null)}>
                ✕
              </button>
            </div>

            <div className="drawer-body">
              {/* Officer Identity Card */}
              <div className="inspector-officer-banner">
                <div className={`officer-avatar ${getRankClass(inspectingOfficer.designation?.code)}`} style={{ width: "48px", height: "48px", fontSize: "16px" }}>
                  {inspectingOfficer.displayName
                    .split(" ")
                    .filter(Boolean)
                    .map((n) => n[0])
                    .slice(0, 2)
                    .join("")
                    .toUpperCase()}
                </div>
                <div>
                  <div style={{ fontSize: "16px", fontWeight: 700, color: "#0f172a" }}>
                    {inspectingOfficer.displayName}
                  </div>
                  <div style={{ fontSize: "13px", color: "#64748b" }}>
                    @{inspectingOfficer.username} · Civil Rank:{" "}
                    <strong>{inspectingOfficer.designation?.name || "Unassigned"}</strong>
                  </div>
                  <div style={{ marginTop: "6px", display: "flex", gap: "6px" }}>
                    {inspectingOfficer.roles.map((r) => (
                      <span key={r} className="role-tag">
                        {r.replace("_", " ")}
                      </span>
                    ))}
                  </div>
                </div>
              </div>

              {/* Plain English Operational Powers */}
              <div className="inspector-powers-grid">
                {/* Land Records & Award Powers */}
                <div className="power-card">
                  <div className="power-card-header">
                    <span className="power-card-title">
                      🏛 Land Records & Award Formulation
                    </span>
                    <span className="scope-indicator all">Platform-wide Scope</span>
                  </div>
                  <ul className="power-item-list">
                    <li className="power-item">
                      <i>✓</i> Can inspect and verify village Khasra and Khatauni land registers.
                    </li>
                    <li className="power-item">
                      <i>✓</i> Authorized to review draft Statement-A schedules and revenue reports.
                    </li>
                    {inspectingOfficer.roles.includes("LAC_OFFICER") && (
                      <li className="power-item">
                        <i>✓</i> Statutory authority to approve and sign Land Acquisition Awards.
                      </li>
                    )}
                  </ul>
                </div>

                {/* Correspondence & Inward Dak Powers */}
                <div className="power-card">
                  <div className="power-card-header">
                    <span className="power-card-title">
                      📥 Correspondence & Dak Intake
                    </span>
                    <span className="scope-indicator workstream">Branch Scope</span>
                  </div>
                  <ul className="power-item-list">
                    <li className="power-item">
                      <i>✓</i> Access to incoming letters and communications delivered to assigned desks.
                    </li>
                    {inspectingOfficer.roles.includes("INTAKE_OFFICER") ? (
                      <li className="power-item">
                        <i>✓</i> Can stamp, index, and register newly received inward dak into the central register.
                      </li>
                    ) : (
                      <li className="power-item">
                        <i>✓</i> Can receive, acknowledge, and forward dak within assigned branch movement channels.
                      </li>
                    )}
                  </ul>
                </div>

                {/* Matters & Note Sheets */}
                <div className="power-card">
                  <div className="power-card-header">
                    <span className="power-card-title">
                      ⚖ Matters & Note Sheets
                    </span>
                    <span className="scope-indicator assigned">Assigned Matters Only</span>
                  </div>
                  <ul className="power-item-list">
                    <li className="power-item">
                      <i>✓</i> Authorized to review case matters and acquisition dossiers assigned to their desk.
                    </li>
                    <li className="power-item">
                      <i>✓</i> Can compose departmental notes and attach references to the noting mirror.
                    </li>
                  </ul>
                </div>

                {/* Court Litigation & High Court */}
                <div className="power-card">
                  <div className="power-card-header">
                    <span className="power-card-title">
                      ⚖ Court & Litigation
                    </span>
                    <span className="scope-indicator workstream">Workstream Scope</span>
                  </div>
                  <ul className="power-item-list">
                    <li className="power-item">
                      <i>✓</i> View court reference matters, upcoming hearing dates, and judicial order sheets.
                    </li>
                  </ul>
                </div>
              </div>

              {/* Delegation & Assistant Rule */}
              <div className="contract-notice-banner">
                <strong>Officer Assistant Delegation Rights:</strong>
                <span>
                  This officer may attach Data Entry Operators (DEOs) or Dealing Assistants to assist with their assigned responsibilities. Delegated permissions will be strictly bounded as a subset of the powers shown above.
                </span>
              </div>
            </div>

            <div className="drawer-footer">
              <button className="primary-button" onClick={() => setInspectingOfficer(null)}>
                Close Inspector
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create New Officer Account Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "650px" }}>
            <h3>Create New Officer / Staff Account</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Provision a new official login with civil rank, authority roles, and branch memberships.
            </p>

            <form onSubmit={handleCreateUser} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label>Official Display Name</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. Sh. Vikram Singh"
                    value={newDisplayName}
                    onChange={(e) => setNewDisplayName(e.target.value)}
                  />
                </div>
                <div className="form-group">
                  <label>Official Username</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. tehsildar.vikram"
                    value={newUsername}
                    onChange={(e) => setNewUsername(e.target.value)}
                  />
                </div>
              </div>

              <div className="form-group">
                <label>Initial Temporary Password</label>
                <PasswordInput
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  placeholder="Set initial temporary password"
                  required
                />
                <small style={{ color: "#64748b" }}>
                  Officer will be prompted to reset password on first login. Plaintext is never stored.
                </small>
              </div>

              <div className="form-group">
                <label>Civil Designation (Post / Rank)</label>
                <select
                  value={newDesignationId}
                  onChange={(e) => setNewDesignationId(e.target.value)}
                >
                  <option value="">Select Civil Designation</option>
                  {designations.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              {/* Roles - Staged for Basic Identity Creation */}
              <div className="form-group">
                <label>Assigned Authority Role(s)</label>
                <div style={{ marginBottom: "6px", fontSize: "12px", color: "#64748b" }}>
                  <em>Basic identity provisioning only. Role allocations are staged pending Codex RBAC role-assignment authorization contract.</em>
                </div>
                <div className="multi-select-grid" style={{ maxHeight: "150px", opacity: 0.65 }}>
                  {roles.map((r) => (
                    <label key={r.id} className="multi-select-item" style={{ cursor: "not-allowed" }} title="Role assignment disabled during basic account provisioning">
                      <input
                        type="checkbox"
                        disabled={true}
                        checked={selectedRoleIds.includes(r.id)}
                        onChange={() => {}}
                      />
                      <span>{r.name}</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Workstreams */}
              <div className="form-group">
                <label>Functional Branch(es)</label>
                <div className="multi-select-grid" style={{ maxHeight: "150px" }}>
                  {workstreams.map((w) => {
                    const isSelected = selectedWorkstreamIds.includes(w.id);
                    return (
                      <div key={w.id} style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
                        <label className="multi-select-item">
                          <input
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => {
                              setSelectedWorkstreamIds((prev) => {
                                const updated = prev.includes(w.id)
                                  ? prev.filter((id) => id !== w.id)
                                  : [...prev, w.id];
                                if (!updated.includes(primaryWorkstreamId)) {
                                  setPrimaryWorkstreamId(updated[0] || "");
                                }
                                return updated;
                              });
                            }}
                          />
                          <span>{w.name}</span>
                        </label>
                        {isSelected && (
                          <label style={{ fontSize: "11px", color: "#2563eb", marginLeft: "22px" }}>
                            <input
                              type="radio"
                              name="createPrimaryWorkstream"
                              checked={primaryWorkstreamId === w.id}
                              onChange={() => setPrimaryWorkstreamId(w.id)}
                            />{" "}
                            Primary
                          </label>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setShowCreateModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  disabled={actionLoading}
                >
                  {actionLoading ? "Creating..." : "Create Officer Account"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Manage Desks Modal */}
      {deskTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "700px" }}>
            <h3>Manage Office Desks: {deskTargetUser.displayName} (@{deskTargetUser.username})</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Operational physical/digital seats assigned to this officer for dak movement and file custody.
            </p>

            {userDesksLoading ? (
              <div className="state">Loading desk memberships...</div>
            ) : (
              <div>
                <div style={{ marginBottom: "16px" }}>
                  <h4 style={{ margin: "10px 0 6px 0" }}>Active Desk Memberships ({activeMemberships.length})</h4>
                  {activeMemberships.length === 0 ? (
                    <p className="subtext">Officer is not currently assigned to any active office desk.</p>
                  ) : (
                    <table style={{ width: "100%", fontSize: "13px" }}>
                      <thead>
                        <tr>
                          <th>Desk Name</th>
                          <th>Code</th>
                          <th>Branch</th>
                          <th>Primary Seat</th>
                          <th>Actions</th>
                        </tr>
                      </thead>
                      <tbody>
                        {activeMemberships.map((m) => (
                          <tr key={m.id}>
                            <td><strong>{m.deskName}</strong></td>
                            <td><code>{m.deskCode}</code></td>
                            <td>{m.workstreamName || "—"}</td>
                            <td>
                              {m.isPrimary ? (
                                <span className="status success">Primary Seat</span>
                              ) : (
                                <button
                                  className="quiet-button text-action"
                                  onClick={() => handleSetPrimaryDesk(m.id)}
                                >
                                  Make Primary
                                </button>
                              )}
                            </td>
                            <td>
                              <button
                                className="quiet-button text-action"
                                style={{ color: "#b91c1c" }}
                                onClick={() => handleRemoveDesk(m.id)}
                              >
                                Remove
                              </button>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  )}
                </div>

                {/* Assign New Desk */}
                <form onSubmit={handleAssignDesk} style={{ borderTop: "1px solid #e2e8f0", paddingTop: "14px", marginTop: "14px" }}>
                  <h4 style={{ margin: "0 0 10px 0" }}>Assign New Office Desk</h4>
                  <div style={{ display: "flex", gap: "10px", alignItems: "flex-end" }}>
                    <div style={{ flex: 1 }}>
                      <label style={{ fontSize: "12px", fontWeight: 600, color: "#475569" }}>Select Desk</label>
                      <select
                        style={{ width: "100%", padding: "7px 10px", borderRadius: "6px", border: "1px solid #cbd5e1" }}
                        value={assignDeskId}
                        onChange={(e) => setAssignDeskId(e.target.value)}
                        required
                      >
                        <option value="">Choose an available office desk</option>
                        {desks
                          .filter((d) => d.isActive && !activeMemberships.some((m) => m.officeDeskId === d.id))
                          .map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name} ({d.code}) {d.workstreamName ? `— ${d.workstreamName}` : ""}
                            </option>
                          ))}
                      </select>
                    </div>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "13px", paddingBottom: "8px" }}>
                      <input
                        type="checkbox"
                        checked={assignDeskPrimary}
                        onChange={(e) => setAssignDeskPrimary(e.target.checked)}
                      />
                      Set as Primary Seat
                    </label>
                    <button
                      type="submit"
                      className="primary-button"
                      disabled={!assignDeskId || actionLoading}
                    >
                      Assign Desk
                    </button>
                  </div>
                </form>
              </div>
            )}

            <div className="modal-actions" style={{ marginTop: "20px" }}>
              <button
                type="button"
                className="quiet-button"
                onClick={() => setDeskTargetUser(null)}
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Reset Password Modal */}
      {resetTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "480px" }}>
            <h3>Issue Temporary Password Reset</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Set initial temporary credential for <strong>{resetTargetUser.displayName}</strong> (@{resetTargetUser.username}).
            </p>

            <form onSubmit={handleResetPassword}>
              <div style={{ background: "#fffbeb", border: "1px solid #fde68a", color: "#92400e", padding: "10px 12px", borderRadius: "6px", marginBottom: "16px", fontSize: "12.5px" }}>
                <strong>Backend Security Update Pending</strong>
                <p style={{ margin: "4px 0 0", color: "#78350f", fontSize: "12px" }}>
                  Password reset execution is temporarily disabled while Codex RBAC implements session revocation hardening to prevent active sessions from surviving credential changes.
                </p>
              </div>

              <div className="form-group" style={{ marginBottom: "16px" }}>
                <label>New Temporary Password</label>
                <PasswordInput
                  value={resetPasswordValue}
                  onChange={(e) => setResetPasswordValue(e.target.value)}
                  placeholder="Enter temporary password"
                  required
                />
                <small style={{ color: "#64748b" }}>
                  Stored passwords are never displayed in plaintext.
                </small>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setResetTargetUser(null)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  disabled={true}
                >
                  Backend Security Update Pending
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
