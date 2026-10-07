import React, { useEffect, useState, useCallback, useMemo } from "react";
import type {
  Designation,
  Workstream,
  UserDeskMembershipItem,
  OfficerItem,
  OfficerDetail,
  AccountOptionsResponse,
  Allocation,
  AllocationInput,
  AllocationScope,
  ScopeKind,
} from "./types";
import {
  localDateInputToIso,
  isoToLocalDateInput,
  getTodayLocalDateInput,
  formatLocalDate,
  validateDateRange,
} from "./dateUtils";
import "./admin.css";

export const UsersAdmin: React.FC = () => {
  const [users, setUsers] = useState<OfficerItem[]>([]);
  const [accountOptions, setAccountOptions] = useState<AccountOptionsResponse | null>(null);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [roles, setRoles] = useState<{ id: string; code: string; name: string }[]>([]);
  const [desks, setDesks] = useState<{ id: string; code: string; name: string; workstreamId: string | null }[]>([]);
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
  const [officerAllocations, setOfficerAllocations] = useState<Allocation[]>([]);
  const [allocationsLoading, setAllocationsLoading] = useState(false);
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

  // Form states - Create User Modal (Generated Temporary Credential Only)
  const [newUsername, setNewUsername] = useState("");
  const [newDisplayName, setNewDisplayName] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [selectedRoleIds, setSelectedRoleIds] = useState<string[]>([]);
  const [selectedWorkstreamIds, setSelectedWorkstreamIds] = useState<string[]>([]);
  const [primaryWorkstreamId, setPrimaryWorkstreamId] = useState("");

  // Form states - Assign Desk Modal
  const [assignDeskId, setAssignDeskId] = useState("");
  const [assignDeskPrimary, setAssignDeskPrimary] = useState(false);

  // Allocation Add/Edit Sub-Form state in drawer
  const [showAllocForm, setShowAllocForm] = useState(false);
  const [editingAllocation, setEditingAllocation] = useState<Allocation | null>(null);
  const [allocWorkDefId, setAllocWorkDefId] = useState("");
  const [allocOrderRef, setAllocOrderRef] = useState("");
  const [allocReason, setAllocReason] = useState("");
  const [allocValidFrom, setAllocValidFrom] = useState(getTodayLocalDateInput());
  const [allocValidTo, setAllocValidTo] = useState("");
  const [allocScopes, setAllocScopes] = useState<AllocationScope[]>([]);
  const [newScopeKind, setNewScopeKind] = useState<ScopeKind>("Global");
  const [newScopeDistrictId, setNewScopeDistrictId] = useState("");
  const [newScopeSubDivId, setNewScopeSubDivId] = useState("");
  const [newScopeVillageId, setNewScopeVillageId] = useState("");
  const [allocSubmitting, setAllocSubmitting] = useState(false);
  const [allocFormError, setAllocFormError] = useState<string | null>(null);

  // One-Time Credential Modal (for officer creation or password reset)
  const [showCredentialModal, setShowCredentialModal] = useState(false);
  const [credentialData, setCredentialData] = useState<{
    username: string;
    temporaryCredential: string;
    expiresAt?: string;
  } | null>(null);
  const [credentialAcknowledged, setCredentialAcknowledged] = useState(false);
  const [copiedNotice, setCopiedNotice] = useState(false);

  const [actionLoading, setActionLoading] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [usersRes, optRes] = await Promise.all([
        fetch("/api/admin/users", { credentials: "include" }),
        fetch("/api/admin/account-options", { credentials: "include" }),
      ]);

      if (!usersRes.ok) {
        if (usersRes.status === 401) throw new Error("Session expired. Please log in again.");
        if (usersRes.status === 403) throw new Error("Access denied: Users.Manage permission required.");
        throw new Error(`Failed to load users (status ${usersRes.status}).`);
      }

      setUsers((await usersRes.json()) as OfficerItem[]);

      if (optRes.ok) {
        const opts = (await optRes.json()) as AccountOptionsResponse;
        setAccountOptions(opts);
        setDesignations(opts.designations || []);
        setWorkstreams((opts.workstreams || []) as Workstream[]);
        setRoles(opts.roles || []);
        setDesks(opts.desks || []);

        if (opts.districts?.length > 0 && !newScopeDistrictId) {
          setNewScopeDistrictId(opts.districts[0].id);
        }
        if (opts.subdivisions?.length > 0 && !newScopeSubDivId) {
          setNewScopeSubDivId(opts.subdivisions[0].id);
        }
        if (opts.villages?.length > 0 && !newScopeVillageId) {
          setNewScopeVillageId(opts.villages[0].id);
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load directory data.");
    } finally {
      setLoading(false);
    }
  }, [newScopeDistrictId, newScopeSubDivId, newScopeVillageId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  // Load allocations for an officer
  const loadOfficerAllocations = useCallback(async (userId: string) => {
    try {
      setAllocationsLoading(true);
      const res = await fetch(`/api/admin/users/${userId}/allocations`, { credentials: "include" });
      if (res.ok) {
        const data = (await res.json()) as Allocation[];
        setOfficerAllocations(data);
      } else {
        setOfficerAllocations([]);
      }
    } catch {
      setOfficerAllocations([]);
    } finally {
      setAllocationsLoading(false);
    }
  }, []);

  // Open Edit Officer Drawer
  const openEditDrawer = async (officer: OfficerItem) => {
    try {
      setEditDrawerLoading(true);
      setShowAllocForm(false);
      setEditingAllocation(null);
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

      // Load allocations
      await loadOfficerAllocations(officer.id);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error loading officer.");
    } finally {
      setEditDrawerLoading(false);
    }
  };

  // Save basic profile, roles, and workstreams (leaves allocations intact)
  const handleSaveOfficerProfile = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;

    try {
      setActionLoading(true);
      setActionMessage(null);

      const payload = {
        displayName: editDisplayName.trim(),
        designationId: editDesignationId || null,
        roleIds: accountOptions?.canAssignRoles ? editRoleIds : undefined,
        workstreamIds: accountOptions?.canManageAllocations ? editWorkstreamIds : undefined,
        primaryWorkstreamId: accountOptions?.canManageAllocations ? (editPrimaryWorkstreamId || null) : undefined,
      };

      const res = await fetch(`/api/admin/users/${editingOfficer.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 403) {
          throw new Error("Access denied: You do not possess the required permission to assign these roles or branches.");
        }
        const err = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to update officer profile.");
      }

      setActionMessage(`Profile & roles for ${editDisplayName.trim()} saved successfully.`);
      await loadData();
      await openEditDrawer({ ...editingOfficer, displayName: editDisplayName.trim() } as unknown as OfficerItem);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error updating officer.");
    } finally {
      setActionLoading(false);
    }
  };

  // Add scope to the currently edited allocation
  const handleAddScopeToBuilder = () => {
    let scope: AllocationScope;
    if (newScopeKind === "Global") {
      scope = { kind: "Global" };
    } else if (newScopeKind === "District") {
      scope = { kind: "District", districtId: newScopeDistrictId || null };
    } else if (newScopeKind === "Subdivision") {
      scope = { kind: "Subdivision", subDivisionId: newScopeSubDivId || null };
    } else {
      scope = { kind: "Village", villageId: newScopeVillageId || null };
    }

    setAllocScopes((prev) => [...prev, scope]);
  };

  // Open Allocation Add form
  const openNewAllocationForm = () => {
    setEditingAllocation(null);
    const worksList = accountOptions?.works || [];
    setAllocWorkDefId(worksList.length > 0 ? worksList[0].id : "");
    setAllocOrderRef("");
    setAllocReason("");
    setAllocValidFrom(getTodayLocalDateInput());
    setAllocValidTo("");
    setAllocScopes([]);
    setAllocFormError(null);
    setShowAllocForm(true);
  };

  // Open Allocation Edit form
  const openEditAllocationForm = (alloc: Allocation) => {
    setEditingAllocation(alloc);
    setAllocWorkDefId(alloc.workDefinitionId);
    setAllocOrderRef(alloc.workOrderReference || "");
    setAllocReason(alloc.reason || "");
    setAllocValidFrom(isoToLocalDateInput(alloc.validFrom));
    setAllocValidTo(isoToLocalDateInput(alloc.validTo));
    setAllocScopes(alloc.scopes ? [...alloc.scopes] : []);
    setAllocFormError(null);
    setShowAllocForm(true);
  };

  // Submit Add or Edit Allocation
  const handleSubmitAllocation = async (e: React.FormEvent) => {
    e.preventDefault();
    setAllocFormError(null);
    if (!editingOfficer || !allocWorkDefId) {
      setAllocFormError("Please select a work category.");
      return;
    }
    if (!allocOrderRef.trim()) {
      setAllocFormError("Work Order Reference is required and must be explicitly entered.");
      return;
    }
    if (allocScopes.length === 0) {
      setAllocFormError("At least one geographic scope must be consciously added. Allocations cannot have zero scopes.");
      return;
    }
    const dateVal = validateDateRange(allocValidFrom, allocValidTo || null);
    if (!dateVal.valid) {
      setAllocFormError(dateVal.error || "Invalid date range.");
      return;
    }

    try {
      setAllocSubmitting(true);
      const input: AllocationInput = {
        workDefinitionId: allocWorkDefId,
        validFrom: localDateInputToIso(allocValidFrom)!,
        validTo: allocValidTo.trim() ? localDateInputToIso(allocValidTo) : null,
        workOrderReference: allocOrderRef.trim(),
        reason: allocReason.trim() || null,
        scopes: allocScopes,
      };

      if (editingAllocation) {
        // PUT update allocation
        const res = await fetch(`/api/admin/users/${editingOfficer.id}/allocations/${editingAllocation.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            allocation: input,
            expectedRevision: editingAllocation.revision,
          }),
          credentials: "include",
        });

        if (!res.ok) {
          if (res.status === 409) {
            await loadOfficerAllocations(editingOfficer.id);
            await loadData();
            throw new Error("Revision conflict: Allocation was modified concurrently. Refreshed.");
          }
          const err = (await res.json().catch(() => null)) as { message?: string } | null;
          throw new Error(err?.message || "Failed to update allocation.");
        }
      } else {
        // POST create allocation
        const res = await fetch(`/api/admin/users/${editingOfficer.id}/allocations`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(input),
          credentials: "include",
        });

        if (!res.ok) {
          if (res.status === 409) {
            await loadOfficerAllocations(editingOfficer.id);
            await loadData();
            throw new Error("Revision or duplicate conflict on allocation creation.");
          }
          const err = (await res.json().catch(() => null)) as { message?: string } | null;
          throw new Error(err?.message || "Failed to create allocation.");
        }
      }

      setShowAllocForm(false);
      setEditingAllocation(null);
      await loadOfficerAllocations(editingOfficer.id);
      await loadData();
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Error saving allocation.";
      setAllocFormError(msg);
      alert(msg);
    } finally {
      setAllocSubmitting(false);
    }
  };

  // Revoke Allocation
  const handleRevokeAllocation = async (alloc: Allocation) => {
    if (!editingOfficer) return;
    if (!confirm(`Are you sure you want to REVOKE the work allocation "${alloc.workName}" (Order: ${alloc.workOrderReference})?\n\nRevoking this allocation will also immediately cascade to revoke any assistant allocations delegated from it.`)) {
      return;
    }

    try {
      const res = await fetch(`/api/admin/users/${editingOfficer.id}/allocations/${alloc.id}/revoke`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          expectedRevision: alloc.revision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadOfficerAllocations(editingOfficer.id);
          await loadData();
          alert("Revision conflict: Stale allocation revision. Reloaded latest records.");
          return;
        }
        throw new Error("Failed to revoke allocation.");
      }

      await loadOfficerAllocations(editingOfficer.id);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error revoking allocation.");
    }
  };

  // Desks Management
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

  // Create Officer (Generated Temporary Credential Only)
  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setActionLoading(true);
      setActionMessage(null);

      const payload = {
        username: newUsername.trim(),
        displayName: newDisplayName.trim(),
        password: null,
        designationId: newDesignationId || null,
        roleIds: accountOptions?.canAssignRoles ? selectedRoleIds : [],
        workstreamIds: accountOptions?.canManageAllocations ? selectedWorkstreamIds : [],
        primaryWorkstreamId: accountOptions?.canManageAllocations ? (primaryWorkstreamId || null) : null,
      };

      const response = await fetch("/api/admin/users", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!response.ok) {
        if (response.status === 403) {
          throw new Error("Access denied: You do not possess the required permission to assign these roles or create accounts.");
        }
        const err = (await response.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to create officer account.");
      }

      const resData = (await response.json()) as {
        id: string;
        temporaryCredential?: string;
        credentialExpiresAt?: string;
      };

      setShowCreateModal(false);
      setNewUsername("");
      setNewDisplayName("");
      setNewDesignationId("");
      setSelectedRoleIds([]);
      setSelectedWorkstreamIds([]);
      setPrimaryWorkstreamId("");
      await loadData();

      if (resData.temporaryCredential) {
        setCredentialData({
          username: payload.username,
          temporaryCredential: resData.temporaryCredential,
          expiresAt: resData.credentialExpiresAt,
        });
        setCredentialAcknowledged(false);
        setCopiedNotice(false);
        setShowCredentialModal(true);
      } else {
        setActionMessage("Officer account created successfully.");
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : "Failed to create officer.");
    } finally {
      setActionLoading(false);
    }
  };

  // Status toggle
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

  // Reset Password (now hardened with session invalidation)
  const handleResetPassword = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!resetTargetUser) return;

    try {
      setActionLoading(true);
      const res = await fetch(`/api/admin/users/${resetTargetUser.id}/reset-password`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({}),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 403) {
          throw new Error("Access denied: Password reset of administrative accounts requires Access.Manage.");
        }
        const err = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(err?.message || "Failed to reset password.");
      }

      const data = (await res.json()) as {
        message: string;
        temporaryCredential?: string;
        credentialExpiresAt?: string;
      };

      const targetUsername = resetTargetUser.username;
      setResetTargetUser(null);

      if (data.temporaryCredential) {
        setCredentialData({
          username: targetUsername,
          temporaryCredential: data.temporaryCredential,
          expiresAt: data.credentialExpiresAt,
        });
        setCredentialAcknowledged(false);
        setCopiedNotice(false);
        setShowCredentialModal(true);
      } else {
        setActionMessage(data.message || "Password reset successfully.");
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error resetting password.");
    } finally {
      setActionLoading(false);
    }
  };

  const copyCredentialToClipboard = () => {
    if (!credentialData) return;
    void navigator.clipboard.writeText(credentialData.temporaryCredential);
    setCopiedNotice(true);
    setTimeout(() => setCopiedNotice(false), 2500);
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

  // Scope label formatter
  const formatScopeLabel = (scope: AllocationScope) => {
    if (scope.kind === "Global") return "Global (All Jurisdictions)";
    if (scope.kind === "District") {
      const d = accountOptions?.districts.find((x) => x.id === scope.districtId);
      return `District: ${d?.name || scope.districtId || "Specified"}`;
    }
    if (scope.kind === "Subdivision") {
      const s = accountOptions?.subdivisions.find((x) => x.id === scope.subDivisionId);
      return `Subdivision: ${s?.name || scope.subDivisionId || "Specified"}`;
    }
    const v = accountOptions?.villages.find((x) => x.id === scope.villageId);
    return `Village: ${v?.name || scope.villageId || "Specified"}`;
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
              const initials =
                u.displayName
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
                      {u.isActive ? "Active" : "Disabled"}
                    </span>
                  </td>
                  <td>
                    <div className="role-tags-list">
                      {u.roles.map((r) => (
                        <span key={r} className="role-tag">
                          {r}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td>
                    <div className="workstream-tags-list">
                      {u.workstreams.length > 0 ? (
                        u.workstreams.map((ws) => (
                          <span key={ws} className="workstream-tag">
                            {ws}
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
                        type="button"
                        className="rbac-btn-edit"
                        onClick={() => void openEditDrawer(u)}
                        title="Edit Designation, Roles, and Work Allocations"
                      >
                        Edit Allocations
                      </button>
                      <button
                        type="button"
                        className="rbac-btn-inspect"
                        onClick={() => setInspectingOfficer(u)}
                        title="Inspect effective operational powers and scopes in plain English"
                      >
                        Access Inspector
                      </button>
                      <button
                        type="button"
                        className="rbac-btn-desk"
                        onClick={() => openManageDesksModal(u)}
                        title="Manage assigned office desks and primary seat"
                      >
                        Desks
                      </button>
                      <button
                        type="button"
                        className="secondary-button"
                        style={{ padding: "3px 8px", fontSize: "11px" }}
                        onClick={() => setResetTargetUser(u)}
                        title="Reset password with server session invalidation"
                      >
                        Reset PW
                      </button>
                      <button
                        type="button"
                        className="quiet-button"
                        style={{ padding: "3px 6px", fontSize: "11px" }}
                        onClick={() => void handleToggleStatus(u)}
                        title="Toggle active status"
                      >
                        {u.isActive ? "Disable" : "Enable"}
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
          <div className="drawer-panel" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "800px" }}>
            <div className="drawer-header">
              <div>
                <h3>Edit Officer Allocations</h3>
                <p>
                  Update civil designation, multiple roles, and specific work responsibilities for <strong>{editingOfficer.displayName}</strong> (@{editingOfficer.username})
                </p>
              </div>
              <button
                type="button"
                className="drawer-close-btn"
                onClick={() => setEditingOfficer(null)}
              >
                ✕
              </button>
            </div>

            {editDrawerLoading ? (
              <div className="state"><strong>Loading officer allocations...</strong></div>
            ) : (
              <div className="drawer-body" style={{ overflowY: "auto", padding: "16px 20px" }}>
                {/* 1. Profile, Designation, and Roles Section */}
                <form onSubmit={handleSaveOfficerProfile} style={{ marginBottom: "24px" }}>
                  <h4 style={{ margin: "0 0 12px 0", borderBottom: "1px solid #e2e8f0", paddingBottom: "6px" }}>
                    1. Civil Identity & Authority Roles
                  </h4>

                  <div className="form-group" style={{ marginBottom: "12px" }}>
                    <label>Display / Official Name *</label>
                    <input
                      type="text"
                      required
                      value={editDisplayName}
                      onChange={(e) => setEditDisplayName(e.target.value)}
                    />
                  </div>

                  <div className="form-group" style={{ marginBottom: "12px" }}>
                    <label>Civil Designation (Post / Rank)</label>
                    <select
                      value={editDesignationId}
                      onChange={(e) => setEditDesignationId(e.target.value)}
                    >
                      <option value="">-- No Official Post Assigned --</option>
                      {designations.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name} ({d.code})
                        </option>
                      ))}
                    </select>
                    <small style={{ color: "#64748b" }}>
                      Civil rank does not automatically grant system permissions. Authority requires explicit role assignment.
                    </small>
                  </div>

                  <div className="form-group" style={{ marginBottom: "12px" }}>
                    <label>Assigned Authority Roles</label>
                    {!accountOptions?.canAssignRoles && (
                      <div style={{ color: "#d97706", fontSize: "12px", marginBottom: "4px" }}>
                        Role assignment requires <code>Roles.Assign</code> or <code>Access.Manage</code> permission.
                      </div>
                    )}
                    <div className="multi-select-grid" style={{ maxHeight: "140px", opacity: accountOptions?.canAssignRoles ? 1 : 0.65 }}>
                      {roles.map((r) => (
                        <label key={r.id} className="checkbox-item">
                          <input
                            type="checkbox"
                            disabled={!accountOptions?.canAssignRoles}
                            checked={editRoleIds.includes(r.id)}
                            onChange={(e) => {
                              if (e.target.checked) setEditRoleIds((prev) => [...prev, r.id]);
                              else setEditRoleIds((prev) => prev.filter((id) => id !== r.id));
                            }}
                          />
                          <span><strong>{r.name}</strong> <code>({r.code})</code></span>
                        </label>
                      ))}
                    </div>
                  </div>

                  <div className="form-group" style={{ marginBottom: "12px" }}>
                    <label>Functional Branches (Workstreams)</label>
                    <div className="multi-select-grid" style={{ maxHeight: "110px" }}>
                      {workstreams.map((ws) => (
                        <label key={ws.id} className="checkbox-item">
                          <input
                            type="checkbox"
                            checked={editWorkstreamIds.includes(ws.id)}
                            onChange={(e) => {
                              if (e.target.checked) {
                                setEditWorkstreamIds((prev) => [...prev, ws.id]);
                                if (!editPrimaryWorkstreamId) setEditPrimaryWorkstreamId(ws.id);
                              } else {
                                setEditWorkstreamIds((prev) => prev.filter((id) => id !== ws.id));
                                if (editPrimaryWorkstreamId === ws.id) setEditPrimaryWorkstreamId("");
                              }
                            }}
                          />
                          <span>{ws.name} ({ws.code})</span>
                        </label>
                      ))}
                    </div>
                  </div>

                  <button
                    type="submit"
                    className="primary-button"
                    style={{ padding: "6px 14px", fontSize: "13px" }}
                    disabled={actionLoading}
                  >
                    {actionLoading ? "Saving..." : "Save Identity & Roles"}
                  </button>
                </form>

                {/* 2. Statutory Work Allocations Section */}
                <div>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderBottom: "1px solid #e2e8f0", paddingBottom: "6px", marginBottom: "12px" }}>
                    <h4 style={{ margin: 0 }}>
                      2. Statutory Work Allocations ({officerAllocations.filter((a) => !a.revokedAt).length} Active)
                    </h4>
                    {accountOptions?.canManageAllocations && (
                      <button
                        type="button"
                        className="secondary-button"
                        style={{ padding: "4px 10px", fontSize: "12px" }}
                        onClick={openNewAllocationForm}
                      >
                        + Add Work Allocation
                      </button>
                    )}
                  </div>

                  {/* Operational Visibility Invariant Callout */}
                  <div className="contract-notice-banner" style={{ marginBottom: "12px" }}>
                    <strong>Operational Invariant: Broad View vs. Operational Allocation</strong>
                    <span>
                      Officer broad View may remain available across all records outside allocated geography. Allocations govern operational mutation authority (approving, signing, certifying), not a visibility wall.
                    </span>
                  </div>

                  {/* Add / Edit Allocation Sub-Form */}
                  {showAllocForm && (
                    <div style={{ background: "#f8fafc", padding: "14px", borderRadius: "8px", border: "1px solid #cbd5e1", marginBottom: "16px" }}>
                      <h5 style={{ margin: "0 0 10px 0" }}>
                        {editingAllocation ? "Edit Allocation Scope & Dates" : "Assign New Statutory Responsibility"}
                      </h5>

                      {allocFormError && (
                        <div className="state error" style={{ margin: "0 0 10px 0", background: "#fef2f2", border: "1px solid #fecaca", color: "#b91c1c", padding: "8px 12px", borderRadius: "6px", fontSize: "13px" }}>
                          <strong>Validation Error:</strong> {allocFormError}
                        </div>
                      )}

                      <form onSubmit={handleSubmitAllocation} style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
                        <div className="form-group">
                          <label>Work Definition Responsibility *</label>
                          <select
                            value={allocWorkDefId}
                            onChange={(e) => setAllocWorkDefId(e.target.value)}
                            disabled={!!editingAllocation}
                          >
                            {accountOptions?.works.map((w) => (
                              <option key={w.id} value={w.id}>
                                {w.name} ({w.code} - Kind: {w.kind})
                              </option>
                            ))}
                          </select>
                          {editingAllocation && (
                            <small style={{ color: "#64748b" }}>Changing Work Category requires revoking and re-creating.</small>
                          )}
                        </div>

                        <div className="form-row" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                          <div className="form-group">
                            <label>Valid From *</label>
                            <input
                              type="date"
                              required
                              value={allocValidFrom}
                              onChange={(e) => setAllocValidFrom(e.target.value)}
                            />
                          </div>

                          <div className="form-group">
                            <label>Valid To (Exclusive - Optional)</label>
                            <input
                              type="date"
                              value={allocValidTo}
                              onChange={(e) => setAllocValidTo(e.target.value)}
                            />
                            <small style={{ color: "#64748b" }}>Exclusive upper bound. Leave blank for permanent / ongoing allocation.</small>
                          </div>
                        </div>

                        <div className="form-row" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                          <div className="form-group">
                            <label>Work Order Reference *</label>
                            <input
                              type="text"
                              required
                              placeholder="e.g. F.1(12)/LAC/S/2026/894"
                              value={allocOrderRef}
                              onChange={(e) => setAllocOrderRef(e.target.value)}
                            />
                          </div>

                          <div className="form-group">
                            <label>Reason / Statutory Ground</label>
                            <input
                              type="text"
                              placeholder="e.g. Allocation under Section 19 notification"
                              value={allocReason}
                              onChange={(e) => setAllocReason(e.target.value)}
                            />
                          </div>
                        </div>

                        {/* Geographic Scopes List */}
                        <div className="form-group">
                          <label>Geographic Scopes (Union of Targets) *</label>
                          <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginBottom: "8px" }}>
                            {allocScopes.map((sc, i) => (
                              <span
                                key={i}
                                style={{ background: "#e0f2fe", color: "#0369a1", padding: "4px 8px", borderRadius: "4px", fontSize: "12px", display: "inline-flex", alignItems: "center", gap: "6px" }}
                              >
                                {formatScopeLabel(sc)}
                                <button
                                  type="button"
                                  onClick={() => setAllocScopes((prev) => prev.filter((_, idx) => idx !== i))}
                                  style={{ background: "none", border: "none", cursor: "pointer", color: "#b91c1c" }}
                                >
                                  ✕
                                </button>
                              </span>
                            ))}
                            {allocScopes.length === 0 && (
                              <span style={{ color: "#b91c1c", fontSize: "12px" }}>At least one scope is required.</span>
                            )}
                          </div>

                          {/* Add Scope Row */}
                          <div style={{ display: "flex", gap: "8px", alignItems: "center", flexWrap: "wrap", background: "#f1f5f9", padding: "8px", borderRadius: "6px" }}>
                            <select
                              value={newScopeKind}
                              onChange={(e) => setNewScopeKind(e.target.value as ScopeKind)}
                              style={{ padding: "4px 8px" }}
                            >
                              <option value="Global">Global (All LAC)</option>
                              <option value="District">District</option>
                              <option value="Subdivision">Subdivision</option>
                              <option value="Village">Village</option>
                            </select>

                            {newScopeKind === "District" && (
                              <select
                                value={newScopeDistrictId}
                                onChange={(e) => setNewScopeDistrictId(e.target.value)}
                                style={{ padding: "4px 8px" }}
                              >
                                {accountOptions?.districts.map((d) => (
                                  <option key={d.id} value={d.id}>{d.name}</option>
                                ))}
                              </select>
                            )}

                            {newScopeKind === "Subdivision" && (
                              <select
                                value={newScopeSubDivId}
                                onChange={(e) => setNewScopeSubDivId(e.target.value)}
                                style={{ padding: "4px 8px" }}
                              >
                                {accountOptions?.subdivisions.map((s) => (
                                  <option key={s.id} value={s.id}>{s.name}</option>
                                ))}
                              </select>
                            )}

                            {newScopeKind === "Village" && (
                              <select
                                value={newScopeVillageId}
                                onChange={(e) => setNewScopeVillageId(e.target.value)}
                                style={{ padding: "4px 8px" }}
                              >
                                {accountOptions?.villages.map((v) => (
                                  <option key={v.id} value={v.id}>{v.name}</option>
                                ))}
                              </select>
                            )}

                            <button
                              type="button"
                              className="secondary-button"
                              style={{ padding: "4px 10px", fontSize: "12px" }}
                              onClick={handleAddScopeToBuilder}
                            >
                              + Add Target
                            </button>
                          </div>
                        </div>

                        <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end", marginTop: "8px" }}>
                          <button
                            type="button"
                            className="quiet-button"
                            onClick={() => setShowAllocForm(false)}
                            disabled={allocSubmitting}
                          >
                            Cancel
                          </button>
                          <button
                            type="submit"
                            className="primary-button"
                            disabled={allocSubmitting}
                          >
                            {allocSubmitting ? "Saving..." : editingAllocation ? "Update Allocation" : "Assign Allocation"}
                          </button>
                        </div>
                      </form>
                    </div>
                  )}

                  {/* Allocations Cards List */}
                  {allocationsLoading ? (
                    <div className="state"><strong>Loading officer allocations...</strong></div>
                  ) : (
                    <div style={{ display: "flex", flexDirection: "column", gap: "10px" }}>
                      {officerAllocations.map((alloc) => {
                        const isRevoked = !!alloc.revokedAt;
                        const isTemporary = !!alloc.validTo;

                        return (
                          <div
                            key={alloc.id}
                            style={{
                              background: isRevoked ? "#f8fafc" : "#ffffff",
                              border: isRevoked ? "1px dashed #cbd5e1" : "1px solid #cbd5e1",
                              borderRadius: "8px",
                              padding: "12px 14px",
                              opacity: isRevoked ? 0.7 : 1,
                            }}
                          >
                            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
                              <div>
                                <span style={{ fontWeight: 700, fontSize: "14px", color: isRevoked ? "#64748b" : "#0f172a" }}>
                                  {alloc.workName}
                                </span>
                                <span style={{ marginLeft: "8px", fontSize: "11px", background: "#f1f5f9", padding: "2px 6px", borderRadius: "4px", color: "#475569" }}>
                                  {alloc.workCode}
                                </span>
                                {isTemporary && !isRevoked && (
                                  <span style={{ marginLeft: "6px", fontSize: "11px", background: "#fef3c7", color: "#92400e", padding: "2px 6px", borderRadius: "4px", fontWeight: 600 }}>
                                    ⏱ Temporary Allocation
                                  </span>
                                )}
                                {isRevoked && (
                                  <span style={{ marginLeft: "6px", fontSize: "11px", background: "#fee2e2", color: "#991b1b", padding: "2px 6px", borderRadius: "4px", fontWeight: 600 }}>
                                    Revoked
                                  </span>
                                )}
                              </div>
                              <span style={{ fontSize: "11px", color: "#94a3b8" }}>
                                Rev {alloc.revision}
                              </span>
                            </div>

                            <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", margin: "8px 0" }}>
                              {alloc.scopes.map((sc, i) => (
                                <span
                                  key={i}
                                  style={{ background: "#f0fdf4", color: "#166534", border: "1px solid #bbf7d0", padding: "2px 6px", borderRadius: "4px", fontSize: "11px" }}
                                >
                                  📍 {formatScopeLabel(sc)}
                                </span>
                              ))}
                            </div>

                            <div style={{ display: "flex", flexWrap: "wrap", gap: "12px", fontSize: "12px", color: "#64748b" }}>
                              <span>Order: <strong>{alloc.workOrderReference}</strong></span>
                              <span>Valid: <strong>{formatLocalDate(alloc.validFrom)}</strong> to <strong>{alloc.validTo ? `${formatLocalDate(alloc.validTo)} (exclusive)` : "Ongoing"}</strong></span>
                              {alloc.reason && <span>Reason: <em>{alloc.reason}</em></span>}
                            </div>

                            {!isRevoked && accountOptions?.canManageAllocations && (
                              <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end", marginTop: "10px", borderTop: "1px solid #f1f5f9", paddingTop: "8px" }}>
                                <button
                                  type="button"
                                  className="secondary-button"
                                  style={{ padding: "3px 8px", fontSize: "11px" }}
                                  onClick={() => openEditAllocationForm(alloc)}
                                >
                                  Edit Scope/Dates
                                </button>
                                <button
                                  type="button"
                                  className="quiet-button"
                                  style={{ padding: "3px 8px", fontSize: "11px", color: "#b91c1c" }}
                                  onClick={() => void handleRevokeAllocation(alloc)}
                                >
                                  Revoke
                                </button>
                              </div>
                            )}
                          </div>
                        );
                      })}

                      {officerAllocations.length === 0 && (
                        <div style={{ padding: "20px", textAlign: "center", background: "#f8fafc", borderRadius: "6px", color: "#64748b" }}>
                          No statutory work allocations assigned to this officer.
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        </div>
      )}

      {/* Access Inspector Modal */}
      {inspectingOfficer && (
        <div className="modal-backdrop" onClick={() => setInspectingOfficer(null)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "650px" }}>
            <h3>Access Inspector: {inspectingOfficer.displayName}</h3>
            <p className="subtext">
              Plain-language overview of civil post, assigned software authority roles, and functional branch memberships.
            </p>

            <div style={{ display: "flex", flexDirection: "column", gap: "12px", margin: "16px 0" }}>
              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase", fontWeight: 600 }}>Civil Designation</span>
                <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a", marginTop: "2px" }}>
                  {inspectingOfficer.designation?.name || "No designation"}
                </div>
              </div>

              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase", fontWeight: 600 }}>Assigned Authority Roles</span>
                <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginTop: "6px" }}>
                  {inspectingOfficer.roles.map((r) => (
                    <span key={r} className="role-tag" style={{ fontSize: "12px" }}>{r}</span>
                  ))}
                </div>
              </div>

              <div style={{ background: "#f8fafc", padding: "12px", borderRadius: "6px", border: "1px solid #e2e8f0" }}>
                <span style={{ fontSize: "11px", color: "#64748b", textTransform: "uppercase", fontWeight: 600 }}>Functional Branches</span>
                <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginTop: "6px" }}>
                  {inspectingOfficer.workstreams.map((ws) => (
                    <span key={ws} className="workstream-tag" style={{ fontSize: "12px" }}>{ws}</span>
                  ))}
                </div>
              </div>
            </div>

            <div className="form-footer">
              <button type="button" className="secondary-button" onClick={() => setInspectingOfficer(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Desk Assignment Modal */}
      {deskTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "600px" }}>
            <h3>Manage Desks: {deskTargetUser.displayName}</h3>
            {userDesksLoading ? (
              <div className="state"><strong>Loading desks...</strong></div>
            ) : (
              <div>
                <h4 style={{ margin: "0 0 8px 0" }}>Active Seats ({activeMemberships.length})</h4>
                <div style={{ display: "flex", flexDirection: "column", gap: "8px", marginBottom: "16px" }}>
                  {activeMemberships.map((m) => (
                    <div key={m.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", background: "#f8fafc", padding: "8px 12px", borderRadius: "6px" }}>
                      <div>
                        <strong>{m.deskName}</strong> <code>({m.deskCode})</code>
                        {m.isPrimary && <span className="status success" style={{ marginLeft: "8px" }}>Primary Seat</span>}
                      </div>
                      <div style={{ display: "flex", gap: "6px" }}>
                        {!m.isPrimary && (
                          <button
                            type="button"
                            className="secondary-button"
                            style={{ padding: "2px 8px", fontSize: "11px" }}
                            onClick={() => void handleSetPrimaryDesk(m.id)}
                          >
                            Set Primary
                          </button>
                        )}
                        <button
                          type="button"
                          className="quiet-button"
                          style={{ padding: "2px 8px", fontSize: "11px", color: "#b91c1c" }}
                          onClick={() => void handleRemoveDesk(m.id)}
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  ))}
                  {activeMemberships.length === 0 && <span className="subtext">No active desks assigned.</span>}
                </div>

                <form onSubmit={handleAssignDesk} style={{ borderTop: "1px solid #e2e8f0", paddingTop: "12px", display: "flex", gap: "10px", alignItems: "flex-end" }}>
                  <div style={{ flex: 1 }}>
                    <label style={{ fontSize: "12px", fontWeight: 600, color: "#334155" }}>Assign New Desk</label>
                    <select
                      required
                      value={assignDeskId}
                      onChange={(e) => setAssignDeskId(e.target.value)}
                      style={{ width: "100%", marginTop: "4px" }}
                    >
                      <option value="">Choose desk...</option>
                      {desks.map((d) => (
                        <option key={d.id} value={d.id}>{d.name} ({d.code})</option>
                      ))}
                    </select>
                  </div>
                  <button type="submit" className="primary-button" disabled={actionLoading || !assignDeskId}>
                    Assign Desk
                  </button>
                </form>
              </div>
            )}

            <div className="form-footer" style={{ marginTop: "16px" }}>
              <button type="button" className="secondary-button" onClick={() => setDeskTargetUser(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create Officer Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "650px", maxHeight: "90vh", overflowY: "auto" }}>
            <h3>Create New Officer Account</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Creates an official platform identity. Role assignment and allocation management reflect server authorization capabilities.
            </p>

            <form onSubmit={handleCreateUser} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div className="form-row" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label>Official Username *</label>
                  <input
                    type="text"
                    required
                    value={newUsername}
                    onChange={(e) => setNewUsername(e.target.value.toLowerCase())}
                    placeholder="e.g. s.verma"
                  />
                </div>

                <div className="form-group">
                  <label>Full Name *</label>
                  <input
                    type="text"
                    required
                    value={newDisplayName}
                    onChange={(e) => setNewDisplayName(e.target.value)}
                    placeholder="e.g. Sh. Satish Verma"
                  />
                </div>
              </div>

              <div className="form-group">
                <label>Civil Designation</label>
                <select
                  value={newDesignationId}
                  onChange={(e) => setNewDesignationId(e.target.value)}
                >
                  <option value="">-- No Official Designation --</option>
                  {designations.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              <div className="contract-notice-banner" style={{ margin: "4px 0 8px 0" }}>
                <strong>Server-Generated Temporary Credential:</strong>
                <span>
                  The server will issue a secure 24-hour temporary credential upon creation. The officer must replace it with a personal password of at least 12 characters upon first login before accessing operational modules.
                </span>
              </div>

              {/* Roles */}
              <div className="form-group">
                <label>Assigned Authority Roles</label>
                {!accountOptions?.canAssignRoles && (
                  <div style={{ color: "#d97706", fontSize: "12px", marginBottom: "4px" }}>
                    Role assignment requires <code>Roles.Assign</code> authority.
                  </div>
                )}
                <div className="multi-select-grid" style={{ maxHeight: "120px", opacity: accountOptions?.canAssignRoles ? 1 : 0.65 }}>
                  {roles.map((r) => (
                    <label key={r.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        disabled={!accountOptions?.canAssignRoles}
                        checked={selectedRoleIds.includes(r.id)}
                        onChange={(e) => {
                          if (e.target.checked) setSelectedRoleIds((prev) => [...prev, r.id]);
                          else setSelectedRoleIds((prev) => prev.filter((id) => id !== r.id));
                        }}
                      />
                      <span><strong>{r.name}</strong> ({r.code})</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Workstreams */}
              <div className="form-group">
                <label>Functional Branches</label>
                <div className="multi-select-grid" style={{ maxHeight: "100px" }}>
                  {workstreams.map((ws) => (
                    <label key={ws.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={selectedWorkstreamIds.includes(ws.id)}
                        onChange={(e) => {
                          if (e.target.checked) {
                            setSelectedWorkstreamIds((prev) => [...prev, ws.id]);
                            if (!primaryWorkstreamId) setPrimaryWorkstreamId(ws.id);
                          } else {
                            setSelectedWorkstreamIds((prev) => prev.filter((id) => id !== ws.id));
                            if (primaryWorkstreamId === ws.id) setPrimaryWorkstreamId("");
                          }
                        }}
                      />
                      <span>{ws.name}</span>
                    </label>
                  ))}
                </div>
              </div>

              <div className="modal-actions" style={{ marginTop: "8px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setShowCreateModal(false)}
                  disabled={actionLoading}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={actionLoading}>
                  {actionLoading ? "Creating Officer..." : "Create Officer Account"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Reset Password Modal (Hardened) */}
      {resetTargetUser && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "500px" }}>
            <h3>Reset Password: {resetTargetUser.displayName}</h3>
            <p className="subtext">
              Generates a new secure temporary credential and immediately invalidates all active sessions for <strong>@{resetTargetUser.username}</strong> on the server.
            </p>

            <form onSubmit={handleResetPassword}>
              <div className="contract-warning-banner" style={{ margin: "14px 0" }}>
                <strong>Server Hardened Security Invariant:</strong>
                <span>
                  All existing sessions for this officer will be terminated immediately. The generated temporary password will be displayed once.
                </span>
              </div>

              <div className="modal-actions">
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setResetTargetUser(null)}
                  disabled={actionLoading}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={actionLoading}>
                  {actionLoading ? "Resetting..." : "Generate Temporary Credential & Invalidate Sessions"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* One-Time Temporary Credential Display Modal */}
      {showCredentialModal && credentialData && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "520px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "10px", marginBottom: "8px" }}>
              <span style={{ fontSize: "24px" }}>🔑</span>
              <h3 style={{ margin: 0 }}>One-Time Temporary Credential</h3>
            </div>
            <p className="subtext" style={{ margin: "4px 0 14px 0" }}>
              This credential has been generated securely and will <strong>NEVER</strong> be displayed again.
            </p>

            <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #cbd5e1", marginBottom: "16px" }}>
              <div style={{ marginBottom: "10px" }}>
                <span style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600 }}>Username</span>
                <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>@{credentialData.username}</div>
              </div>

              <div>
                <span style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600 }}>Temporary Password</span>
                <div style={{ display: "flex", gap: "8px", alignItems: "center", marginTop: "4px" }}>
                  <code style={{ fontSize: "16px", fontWeight: 700, background: "#e2e8f0", padding: "6px 12px", borderRadius: "6px", letterSpacing: "1px", flex: 1 }}>
                    {credentialData.temporaryCredential}
                  </code>
                  <button
                    type="button"
                    className="secondary-button"
                    onClick={copyCredentialToClipboard}
                    style={{ whiteSpace: "nowrap" }}
                  >
                    {copiedNotice ? "Copied!" : "Copy"}
                  </button>
                </div>
              </div>

              {credentialData.expiresAt && (
                <div style={{ marginTop: "10px", fontSize: "12px", color: "#64748b" }}>
                  Expires: <strong>{new Date(credentialData.expiresAt).toLocaleString("en-IN")}</strong> (24 hours)
                </div>
              )}
            </div>

            <div className="contract-warning-banner" style={{ marginBottom: "16px" }}>
              <strong>Mandatory First-Login Policy:</strong>
              <span>
                The officer must replace this temporary password with a personal password of at least 12 characters upon first login before accessing operational modules.
              </span>
            </div>

            <div style={{ marginBottom: "16px" }}>
              <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", cursor: "pointer", fontSize: "13px", color: "#1e293b" }}>
                <input
                  type="checkbox"
                  style={{ marginTop: "3px" }}
                  checked={credentialAcknowledged}
                  onChange={(e) => setCredentialAcknowledged(e.target.checked)}
                />
                <span>
                  <strong>I confirm:</strong> I have recorded this temporary credential and will communicate it securely to the officer. I understand it cannot be recovered.
                </span>
              </label>
            </div>

            <div className="modal-actions">
              <button
                type="button"
                className="primary-button"
                disabled={!credentialAcknowledged}
                onClick={() => {
                  setShowCredentialModal(false);
                  setCredentialData(null);
                  setCredentialAcknowledged(false);
                }}
              >
                Acknowledge & Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
