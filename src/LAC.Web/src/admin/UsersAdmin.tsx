import React, { useEffect, useState, useCallback, useMemo } from "react";
import type {
  OfficeAuthority,
  OfficeModule,
  LandAccessLevel,
  HelperAccessLevel,
  OfficeAccountDetail,
  OfficeAccountOptions,
} from "./officeV3Types";
import "./admin.css";

const ALL_MODULES: { code: OfficeModule; label: string; description: string }[] = [
  {
    code: "DakMatters",
    label: "Dak & Matters",
    description: "Work on Dak/files that reach your desk, noting, drafts and outward action.",
  },
  {
    code: "Court",
    label: "Court",
    description: "Manage Court cases, orders, hearings and Court documents.",
  },
  {
    code: "Rti",
    label: "RTI",
    description: "Handle RTI applications and responses.",
  },
  {
    code: "Accounts",
    label: "Accounts",
    description: "Handle Accounts and compensation-support work.",
  },
  {
    code: "RecordRoom",
    label: "Record Room",
    description: "Handle physical record/file custody.",
  },
];

export const UsersAdmin: React.FC = () => {
  const [accounts, setAccounts] = useState<OfficeAccountDetail[]>([]);
  const [options, setOptions] = useState<OfficeAccountOptions | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters & Search
  const [searchQuery, setSearchQuery] = useState("");
  const [filterAuthority, setFilterAuthority] = useState<string>("ALL");
  const [filterStatus, setFilterStatus] = useState<string>("ALL");

  // Modals & Drawers state
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [showTechAdminModal, setShowTechAdminModal] = useState(false);
  const [editingOfficer, setEditingOfficer] = useState<OfficeAccountDetail | null>(null);
  const [inspectingOfficer, setInspectingOfficer] = useState<OfficeAccountDetail | null>(null);

  // Form states - Create Officer Modal
  const [newUsername, setNewUsername] = useState("");
  const [newFullName, setNewFullName] = useState("");
  const [newDesignationId, setNewDesignationId] = useState("");
  const [newCustomDesignation, setNewCustomDesignation] = useState("");
  const [newIsSupervisor, setNewIsSupervisor] = useState(false);
  const [newModules, setNewModules] = useState<OfficeModule[]>(["DakMatters"]);
  const [newCanRegisterInwardDak, setNewCanRegisterInwardDak] = useState(false);
  const [newLandAccess, setNewLandAccess] = useState<LandAccessLevel>("None");
  const [newPrimaryDeskId, setNewPrimaryDeskId] = useState("");
  const [newAdditionalDeskIds, setNewAdditionalDeskIds] = useState<string[]>([]);
  const [createFormError, setCreateFormError] = useState<string | null>(null);

  // Form states - Create Technical System Admin Modal
  const [techUsername, setTechUsername] = useState("");
  const [techFullName, setTechFullName] = useState("");
  const [techDesignationId, setTechDesignationId] = useState("");
  const [techFormError, setTechFormError] = useState<string | null>(null);

  // Form states - Edit Officer Drawer
  const [editFullName, setEditFullName] = useState("");
  const [editDesignationId, setEditDesignationId] = useState("");
  const [editCustomDesignation, setEditCustomDesignation] = useState("");
  const [editIsSupervisor, setEditIsSupervisor] = useState(false);
  const [editModules, setEditModules] = useState<OfficeModule[]>([]);
  const [editCanRegisterInwardDak, setEditCanRegisterInwardDak] = useState(false);
  const [editLandAccess, setEditLandAccess] = useState<LandAccessLevel>("None");
  const [editPrimaryDeskId, setEditPrimaryDeskId] = useState("");
  const [editAdditionalDeskIds, setEditAdditionalDeskIds] = useState<string[]>([]);
  const [editDrawerError, setEditDrawerError] = useState<string | null>(null);
  const [editDrawerLoading, setEditDrawerLoading] = useState(false);

  // Senior Management Helpers sub-form in Edit Drawer
  const [showAddHelperForOfficer, setShowAddHelperForOfficer] = useState(false);
  const [helperUsername, setHelperUsername] = useState("");
  const [helperFullName, setHelperFullName] = useState("");
  const [helperDesignationChoice, setHelperDesignationChoice] = useState<"DEO" | "OTHER">("DEO");
  const [helperCustomDesignation, setHelperCustomDesignation] = useState("");
  const [helperDeskId, setHelperDeskId] = useState("");
  const [helperAccess, setHelperAccess] = useState<HelperAccessLevel>("ReadOnly");
  const [helperFormError, setHelperFormError] = useState<string | null>(null);

  // One-Time Credential Modal
  const [showCredentialModal, setShowCredentialModal] = useState(false);
  const [credentialData, setCredentialData] = useState<{
    username: string;
    temporaryCredential: string;
    expiresAt?: string | null;
  } | null>(null);
  const [credentialAcknowledged, setCredentialAcknowledged] = useState(false);
  const [copiedNotice, setCopiedNotice] = useState(false);

  const [actionLoading, setActionLoading] = useState(false);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [accountsRes, optionsRes] = await Promise.all([
        fetch("/api/office/accounts", { credentials: "include" }),
        fetch("/api/office/accounts/options", { credentials: "include" }),
      ]);

      if (accountsRes.status === 403 || optionsRes.status === 403) {
        setError("You do not have administrative authority to manage office accounts.");
        setLoading(false);
        return;
      }

      if (!accountsRes.ok || !optionsRes.ok) {
        throw new Error("Unable to load accounts directory.");
      }

      const accData = (await accountsRes.json()) as OfficeAccountDetail[];
      const optData = (await optionsRes.json()) as OfficeAccountOptions;

      setAccounts(accData);
      setOptions(optData);
    } catch (err: any) {
      setError(err.message || "Failed to load directory data.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  // Derived: Is selected designation ADM?
  const isSelectedDesignationAdm = useMemo(() => {
    if (!options?.designations || !newDesignationId) return false;
    const d = options.designations.find((x) => x.id === newDesignationId);
    return d?.code === "ADM";
  }, [options, newDesignationId]);

  const isEditDesignationAdm = useMemo(() => {
    if (!options?.designations || !editDesignationId) return false;
    const d = options.designations.find((x) => x.id === editDesignationId);
    return d?.code === "ADM";
  }, [options, editDesignationId]);

  const openCreateOfficerModal = () => {
    setNewUsername("");
    setNewFullName("");
    setNewDesignationId(options?.designations[0]?.id || "");
    setNewCustomDesignation("");
    setNewIsSupervisor(false);
    setNewModules(["DakMatters"]);
    setNewCanRegisterInwardDak(false);
    setNewLandAccess("None");
    setNewPrimaryDeskId(options?.desks[0]?.id || "");
    setNewAdditionalDeskIds([]);
    setCreateFormError(null);
    setShowCreateModal(true);
  };

  const handleCreateOfficerSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreateFormError(null);

    const username = newUsername.trim();
    const fullName = newFullName.trim();

    if (!username) {
      setCreateFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setCreateFormError("Full Name is required.");
      return;
    }

    const isOther = newDesignationId === "__OTHER__";
    if (isOther && !newCustomDesignation.trim()) {
      setCreateFormError("Please enter a custom designation title.");
      return;
    }

    let authority: OfficeAuthority = "STANDARD_OFFICER";
    if (newIsSupervisor) {
      authority = "OFFICE_SUPERVISOR";
    }

    const allDeskIds = [newPrimaryDeskId, ...newAdditionalDeskIds].filter(Boolean);
    const payload = {
      username,
      account: {
        fullName,
        designationId: isOther ? null : newDesignationId || null,
        customDesignation: isOther ? newCustomDesignation.trim() : null,
        authority,
        modules: newIsSupervisor ? [] : newModules,
        canRegisterInwardDak: newIsSupervisor ? true : newCanRegisterInwardDak,
        landAccess: newIsSupervisor ? ("ViewWrite" as LandAccessLevel) : newLandAccess,
        deskIds: Array.from(new Set(allDeskIds)),
      },
    };

    setActionLoading(true);
    try {
      const res = await fetch("/api/office/accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `Account creation failed (HTTP ${res.status})`);
      }

      const created = await res.json();
      setShowCreateModal(false);
      setCredentialData({
        username: created.account?.username || username,
        temporaryCredential: created.temporaryCredential,
        expiresAt: created.credentialExpiresAt,
      });
      setCredentialAcknowledged(false);
      setShowCredentialModal(true);
      void loadData();
    } catch (err: any) {
      setCreateFormError(err.message || "Failed to create officer account.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleCreateTechAdminSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setTechFormError(null);

    const username = techUsername.trim();
    const fullName = techFullName.trim();

    if (!username) {
      setTechFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setTechFormError("Full Name is required.");
      return;
    }

    const payload = {
      username,
      account: {
        fullName,
        designationId: techDesignationId || null,
        customDesignation: null,
        authority: "SYSTEM_ADMIN" as OfficeAuthority,
        modules: [],
        canRegisterInwardDak: true,
        landAccess: "ViewWrite" as LandAccessLevel,
        deskIds: [],
      },
    };

    setActionLoading(true);
    try {
      const res = await fetch("/api/office/accounts", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `System Admin creation failed (HTTP ${res.status})`);
      }

      const created = await res.json();
      setShowTechAdminModal(false);
      setCredentialData({
        username: created.account?.username || username,
        temporaryCredential: created.temporaryCredential,
        expiresAt: created.credentialExpiresAt,
      });
      setCredentialAcknowledged(false);
      setShowCredentialModal(true);
      void loadData();
    } catch (err: any) {
      setTechFormError(err.message || "Failed to create System Administrator.");
    } finally {
      setActionLoading(false);
    }
  };

  const openEditDrawer = async (officer: OfficeAccountDetail) => {
    setEditDrawerLoading(true);
    setEditDrawerError(null);
    setShowAddHelperForOfficer(false);
    try {
      const res = await fetch(`/api/office/accounts/${officer.id}`, { credentials: "include" });
      if (!res.ok) throw new Error("Could not reload officer details.");
      const fresh = (await res.json()) as OfficeAccountDetail;
      setEditingOfficer(fresh);
      setEditFullName(fresh.fullName || "");
      if (fresh.customDesignation) {
        setEditDesignationId("__OTHER__");
        setEditCustomDesignation(fresh.customDesignation);
      } else {
        setEditDesignationId(fresh.designationId || "");
        setEditCustomDesignation("");
      }
      setEditIsSupervisor(fresh.authority === "OFFICE_SUPERVISOR");
      setEditModules(fresh.modules || []);
      setEditCanRegisterInwardDak(fresh.canRegisterInwardDak);
      setEditLandAccess(fresh.landAccess || "None");
      setEditPrimaryDeskId(fresh.deskIds[0] || "");
      setEditAdditionalDeskIds(fresh.deskIds.slice(1) || []);
    } catch (err: any) {
      alert(err.message || "Failed to load officer details.");
    } finally {
      setEditDrawerLoading(false);
    }
  };

  const handleEditOfficerSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;
    setEditDrawerError(null);

    const fullName = editFullName.trim();
    if (!fullName) {
      setEditDrawerError("Full Name is required.");
      return;
    }

    const isOther = editDesignationId === "__OTHER__";
    if (isOther && !editCustomDesignation.trim()) {
      setEditDrawerError("Please enter a custom designation title.");
      return;
    }

    let authority: OfficeAuthority = editingOfficer.authority;
    if (editingOfficer.authority === "STANDARD_OFFICER" || editingOfficer.authority === "OFFICE_SUPERVISOR") {
      authority = editIsSupervisor ? "OFFICE_SUPERVISOR" : "STANDARD_OFFICER";
    }

    const allEditDeskIds = [editPrimaryDeskId, ...editAdditionalDeskIds].filter(Boolean);
    const payload = {
      account: {
        fullName,
        designationId: isOther ? null : editDesignationId || null,
        customDesignation: isOther ? editCustomDesignation.trim() : null,
        authority,
        modules: editIsSupervisor || authority === "OFFICE_ADMIN" || authority === "SYSTEM_ADMIN" ? [] : editModules,
        canRegisterInwardDak: editIsSupervisor ? true : editCanRegisterInwardDak,
        landAccess: editIsSupervisor ? ("ViewWrite" as LandAccessLevel) : editLandAccess,
        deskIds: Array.from(new Set(allEditDeskIds)),
      },
      expectedRevision: editingOfficer.revision,
    };

    setActionLoading(true);
    try {
      const res = await fetch(`/api/office/accounts/${editingOfficer.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (res.status === 409) {
        setEditDrawerError("This account changed elsewhere. Latest details have been reloaded.");
        await loadData();
        const refetch = await fetch(`/api/office/accounts/${editingOfficer.id}`, { credentials: "include" });
        if (refetch.ok) setEditingOfficer(await refetch.json());
        return;
      }

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `Failed to update officer (HTTP ${res.status})`);
      }

      setEditingOfficer(null);
      setActionMessage("Access updated. Existing login sessions for this employee were ended.");
      setTimeout(() => setActionMessage(null), 6000);
      void loadData();
    } catch (err: any) {
      setEditDrawerError(err.message || "Failed to update officer.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleSeniorAddHelperSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingOfficer) return;
    setHelperFormError(null);

    const username = helperUsername.trim();
    const fullName = helperFullName.trim();

    if (!username) {
      setHelperFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setHelperFormError("Full Name is required.");
      return;
    }

    const deoId = options?.designations.find((d) => d.code === "DEO")?.id || null;

    const payload = {
      username,
      helper: {
        fullName,
        designationId: helperDesignationChoice === "DEO" ? deoId : null,
        customDesignation: helperDesignationChoice === "OTHER" ? helperCustomDesignation.trim() : null,
        deskId: helperDeskId || null,
        access: helperAccess,
      },
    };

    setActionLoading(true);
    try {
      const res = await fetch(`/api/office/accounts/${editingOfficer.id}/helpers`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
        credentials: "include",
      });

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `Failed to add helper (HTTP ${res.status})`);
      }

      const created = await res.json();
      setShowAddHelperForOfficer(false);
      setCredentialData({
        username: created.account?.username || username,
        temporaryCredential: created.temporaryCredential,
        expiresAt: created.credentialExpiresAt,
      });
      setCredentialAcknowledged(false);
      setShowCredentialModal(true);
      void loadData();
    } catch (err: any) {
      setHelperFormError(err.message || "Failed to attach helper.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleResetCredential = async (officer: OfficeAccountDetail) => {
    if (!window.confirm(`Issue a new 24-hour temporary credential for ${officer.username}?`)) {
      return;
    }

    setActionLoading(true);
    try {
      const res = await fetch(`/api/office/accounts/${officer.id}/reset-credential`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ expectedRevision: officer.revision }),
        credentials: "include",
      });

      if (res.status === 409) {
        alert("This account was updated elsewhere. Reloading latest records.");
        await loadData();
        return;
      }

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `Reset failed (HTTP ${res.status})`);
      }

      const data = await res.json();
      setCredentialData({
        username: officer.username,
        temporaryCredential: data.temporaryCredential,
        expiresAt: data.credentialExpiresAt,
      });
      setCredentialAcknowledged(false);
      setShowCredentialModal(true);
      void loadData();
    } catch (err: any) {
      alert(err.message || "Credential reset failed.");
    } finally {
      setActionLoading(false);
    }
  };

  const handleToggleStatus = async (officer: OfficeAccountDetail) => {
    const action = officer.isActive ? "deactivate" : "activate";
    if (!window.confirm(`Are you sure you want to ${action} ${officer.username}?`)) {
      return;
    }

    setActionLoading(true);
    try {
      const res = await fetch(`/api/office/accounts/${officer.id}/toggle-status`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ expectedRevision: officer.revision }),
        credentials: "include",
      });

      if (res.status === 409) {
        alert("This account was updated elsewhere. Reloading latest records.");
        await loadData();
        return;
      }

      if (!res.ok) {
        const err = await res.json().catch(() => ({}));
        throw new Error(err.message || `Status update failed (HTTP ${res.status})`);
      }

      setActionMessage(`Officer ${officer.username} is now ${officer.isActive ? "inactive" : "active"}.`);
      setTimeout(() => setActionMessage(null), 4000);
      void loadData();
    } catch (err: any) {
      alert(err.message || "Failed to update officer status.");
    } finally {
      setActionLoading(false);
    }
  };

  const toggleModuleSelection = (mod: OfficeModule, currentList: OfficeModule[], setter: (m: OfficeModule[]) => void) => {
    if (currentList.includes(mod)) {
      setter(currentList.filter((m) => m !== mod));
    } else {
      setter([...currentList, mod]);
    }
  };

  const copyCredentialToClipboard = () => {
    if (!credentialData?.temporaryCredential) return;
    navigator.clipboard.writeText(credentialData.temporaryCredential);
    setCopiedNotice(true);
    setTimeout(() => setCopiedNotice(false), 2500);
  };

  const closeCredentialModal = () => {
    setShowCredentialModal(false);
    setCredentialData(null);
    setCredentialAcknowledged(false);
  };

  // Filtered accounts
  const filteredAccounts = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    return accounts.filter((acc) => {
      if (filterAuthority !== "ALL" && acc.authority !== filterAuthority) return false;
      if (filterStatus === "ACTIVE" && !acc.isActive) return false;
      if (filterStatus === "INACTIVE" && acc.isActive) return false;
      if (!q) return true;
      return (
        acc.username.toLowerCase().includes(q) ||
        (acc.fullName && acc.fullName.toLowerCase().includes(q)) ||
        (acc.effectiveDesignation && acc.effectiveDesignation.toLowerCase().includes(q))
      );
    });
  }, [accounts, searchQuery, filterAuthority, filterStatus]);

  // Attached helpers map
  const attachedHelpersMap = useMemo(() => {
    const map = new Map<string, OfficeAccountDetail[]>();
    for (const acc of accounts) {
      if (acc.supervisingOfficerId) {
        const list = map.get(acc.supervisingOfficerId) || [];
        list.push(acc);
        map.set(acc.supervisingOfficerId, list);
      }
    }
    return map;
  }, [accounts]);

  const getAuthorityBadgeLabel = (auth: OfficeAuthority) => {
    switch (auth) {
      case "SYSTEM_ADMIN": return "System Administrator";
      case "OFFICE_ADMIN": return "Office Administrator";
      case "OFFICE_SUPERVISOR": return "Office Supervisor";
      case "STANDARD_OFFICER": return "Officer / Staff";
      case "HELPER": return "Helper";
    }
  };

  const getAuthorityBadgeTone = (auth: OfficeAuthority) => {
    switch (auth) {
      case "SYSTEM_ADMIN": return "rbac-badge-purple";
      case "OFFICE_ADMIN": return "rbac-badge-primary";
      case "OFFICE_SUPERVISOR": return "rbac-badge-blue";
      case "STANDARD_OFFICER": return "rbac-badge-teal";
      case "HELPER": return "rbac-badge-neutral";
    }
  };

  return (
    <div className="rbac-admin-root">
      {/* Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h1>Officers &amp; Staff</h1>
          <p>
            Official directory of administrative officers, operational staff, and attached helpers.
          </p>
        </div>
        <div className="rbac-actions-group">
          {options?.canAssignSystemAdmin && (
            <button
              className="rbac-btn-secondary"
              onClick={() => {
                setTechUsername("");
                setTechFullName("");
                setTechDesignationId("");
                setTechFormError(null);
                setShowTechAdminModal(true);
              }}
              disabled={loading}
              title="Create dedicated security/system administrator"
            >
              + Create Technical System Administrator
            </button>
          )}
          <button className="rbac-btn-primary" onClick={openCreateOfficerModal} disabled={loading}>
            + Create Officer
          </button>
        </div>
      </div>

      {actionMessage && (
        <div className="rbac-banner-success" style={{ padding: "12px 16px", borderRadius: "8px", background: "#f0fdf4", color: "#166534", border: "1px solid #bbf7d0" }}>
          <span>✓</span> <div>{actionMessage}</div>
        </div>
      )}

      {error && (
        <div className="rbac-banner-error" style={{ padding: "12px 16px", borderRadius: "8px", background: "#fef2f2", color: "#991b1b", border: "1px solid #fecaca" }}>
          <span>⚠️</span> <div>{error}</div>
        </div>
      )}

      {/* Filter and Search Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <input
            type="text"
            placeholder="Search by officer name, username, or designation…"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={filterAuthority}
          onChange={(e) => setFilterAuthority(e.target.value)}
        >
          <option value="ALL">All Authorities</option>
          <option value="SYSTEM_ADMIN">System Administrator</option>
          <option value="OFFICE_ADMIN">Office Administrator</option>
          <option value="OFFICE_SUPERVISOR">Office Supervisor</option>
          <option value="STANDARD_OFFICER">Officer / Staff</option>
          <option value="HELPER">Helper</option>
        </select>

        <select
          className="rbac-filter-select"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value)}
        >
          <option value="ALL">All Statuses</option>
          <option value="ACTIVE">Active</option>
          <option value="INACTIVE">Inactive</option>
        </select>
      </div>

      {/* Directory Table */}
      {loading ? (
        <div style={{ padding: "40px", textAlign: "center", color: "#64748b" }}>
          Loading official directory…
        </div>
      ) : filteredAccounts.length === 0 ? (
        <div style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "48px 24px", textAlign: "center" }}>
          <h3 style={{ margin: "0 0 8px 0", color: "#1e293b", fontSize: "18px" }}>No Officers Found</h3>
          <p style={{ margin: 0, color: "#64748b", fontSize: "14px" }}>
            No employee records matched the selected filters.
          </p>
        </div>
      ) : (
        <div className="rbac-table-container" style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Officer Name &amp; Username</th>
                <th>Civil Designation</th>
                <th>Authority</th>
                <th>Work Modules</th>
                <th>Land Records</th>
                <th>Assigned Seat / Desk</th>
                <th>Status</th>
                <th style={{ textAlign: "right" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredAccounts.map((u) => {
                const assignedDesks = options?.desks.filter((d) => u.deskIds.includes(d.id)) || [];
                const deskName = assignedDesks.length > 0
                  ? assignedDesks.length > 1
                    ? `${assignedDesks[0].name} (+${assignedDesks.length - 1} more)`
                    : assignedDesks[0].name
                  : (u.deskIds.length > 0 ? "Assigned Desk" : "Unassigned");
                const attachedHelpers = attachedHelpersMap.get(u.id) || [];
                const isTechAdmin = u.authority === "SYSTEM_ADMIN" && !u.designationId && !u.customDesignation;

                return (
                  <tr key={u.id}>
                    <td>
                      <div style={{ fontWeight: 600, color: "#0f172a", cursor: "pointer" }} onClick={() => setInspectingOfficer(u)}>
                        {u.fullName}
                      </div>
                      <div style={{ fontSize: "12px", color: "#64748b", fontFamily: "monospace" }}>
                        {u.username}
                      </div>
                      {attachedHelpers.length > 0 && (
                        <div style={{ fontSize: "11px", color: "#0369a1", marginTop: "2px" }}>
                          {attachedHelpers.length} attached helper{attachedHelpers.length > 1 ? "s" : ""}
                        </div>
                      )}
                      {u.supervisingOfficerId && (
                        <div style={{ fontSize: "11px", color: "#854d0e", background: "#fef9c3", padding: "2px 6px", borderRadius: "4px", display: "inline-block", marginTop: "4px" }}>
                          Attached Helper
                        </div>
                      )}
                    </td>
                    <td>
                      {isTechAdmin ? (
                        <div>
                          <span className="rbac-badge rbac-badge-purple">Technical Account</span>
                          <div className="subtext" style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                            No civil designation
                          </div>
                        </div>
                      ) : u.effectiveDesignation ? (
                        <span style={{ fontSize: "13.5px", color: "#1e293b", fontWeight: 500 }}>
                          {u.effectiveDesignation}
                        </span>
                      ) : (
                        <span className="subtext" style={{ fontSize: "12px", color: "#94a3b8" }}>
                          Unassigned
                        </span>
                      )}
                    </td>
                    <td>
                      <span className={`rbac-badge ${getAuthorityBadgeTone(u.authority)}`}>
                        {getAuthorityBadgeLabel(u.authority)}
                      </span>
                    </td>
                    <td>
                      {u.authority === "SYSTEM_ADMIN" || u.authority === "OFFICE_ADMIN" || u.authority === "OFFICE_SUPERVISOR" ? (
                        <span className="rbac-badge rbac-badge-primary" style={{ background: "#e0e7ff", color: "#3730a3" }}>
                          Full Office Access
                        </span>
                      ) : u.authority === "HELPER" ? (
                        <span style={{ fontSize: "12px", color: "#64748b" }}>
                          {u.helperPermissionCodes && u.helperPermissionCodes.some((c) => !c.endsWith(".View"))
                            ? "Read + Write"
                            : "Read Only"}
                        </span>
                      ) : (
                        <div style={{ display: "flex", flexWrap: "wrap", gap: "4px", maxWidth: "260px" }}>
                          {u.modules.map((m) => {
                            const modInfo = ALL_MODULES.find((x) => x.code === m);
                            return (
                              <span key={m} className="rbac-badge rbac-badge-outline" style={{ fontSize: "11px", padding: "2px 6px" }}>
                                {modInfo?.label || m}
                              </span>
                            );
                          })}
                          {u.canRegisterInwardDak && (
                            <span className="rbac-badge rbac-badge-outline" style={{ fontSize: "11px", padding: "2px 6px", borderColor: "#0284c7", color: "#0369a1" }}>
                              + Inward Dak
                            </span>
                          )}
                          {u.modules.length === 0 && !u.canRegisterInwardDak && (
                            <span style={{ fontSize: "12px", color: "#94a3b8" }}>None</span>
                          )}
                        </div>
                      )}
                    </td>
                    <td>
                      {u.authority === "SYSTEM_ADMIN" || u.authority === "OFFICE_ADMIN" || u.authority === "OFFICE_SUPERVISOR" ? (
                        <span className="rbac-badge rbac-badge-teal" style={{ fontSize: "11px" }}>
                          View + Write
                        </span>
                      ) : (
                        <span className={`rbac-badge ${u.landAccess === "ViewWrite" ? "rbac-badge-teal" : u.landAccess === "ViewOnly" ? "rbac-badge-outline" : "rbac-badge-neutral"}`} style={{ fontSize: "11px" }}>
                          {u.landAccess === "ViewWrite" ? "View + Write" : u.landAccess === "ViewOnly" ? "View Only" : "None"}
                        </span>
                      )}
                    </td>
                    <td>
                      <span style={{ fontSize: "13px", color: "#334155" }}>{deskName}</span>
                    </td>
                    <td>
                      <span className={`rbac-status-dot ${u.isActive ? "active" : "inactive"}`} />
                      <span style={{ fontSize: "13px", color: u.isActive ? "#166534" : "#94a3b8" }}>
                        {u.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <div className="rbac-table-actions">
                        <button className="rbac-btn-sm rbac-btn-outline" onClick={() => void openEditDrawer(u)} title="Edit Officer">
                          Edit
                        </button>
                        <button className="rbac-btn-sm rbac-btn-outline" onClick={() => void handleResetCredential(u)} title="Generate New Password">
                          Reset Credential
                        </button>
                        <button
                          className={`rbac-btn-sm ${u.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`}
                          onClick={() => void handleToggleStatus(u)}
                          title={u.isActive ? "Deactivate Officer" : "Activate Officer"}
                        >
                          {u.isActive ? "Deactivate" : "Activate"}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {/* Inspector Modal */}
      {inspectingOfficer && (
        <div className="rbac-modal-backdrop" onClick={() => setInspectingOfficer(null)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "540px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Officer Inspector</h2>
                <p>{inspectingOfficer.fullName} ({inspectingOfficer.username})</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setInspectingOfficer(null)}>&times;</button>
            </div>
            <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
              <div style={{ display: "grid", gridTemplateColumns: "140px 1fr", gap: "8px", fontSize: "13px" }}>
                <span style={{ color: "#64748b" }}>Civil Designation:</span>
                <span style={{ fontWeight: 600, color: "#0f172a" }}>
                  {inspectingOfficer.authority === "SYSTEM_ADMIN" && !inspectingOfficer.designationId && !inspectingOfficer.customDesignation
                    ? "No civil designation (Technical System Administrator)"
                    : inspectingOfficer.effectiveDesignation || "Unassigned"}
                </span>

                <span style={{ color: "#64748b" }}>Authority:</span>
                <span>
                  <span className={`rbac-badge ${getAuthorityBadgeTone(inspectingOfficer.authority)}`}>
                    {getAuthorityBadgeLabel(inspectingOfficer.authority)}
                  </span>
                </span>

                <span style={{ color: "#64748b" }}>Status:</span>
                <span style={{ color: inspectingOfficer.isActive ? "#166534" : "#94a3b8", fontWeight: 600 }}>
                  {inspectingOfficer.isActive ? "Active" : "Inactive"}
                </span>

                <span style={{ color: "#64748b" }}>Land Records:</span>
                <span>{inspectingOfficer.landAccess}</span>

                <span style={{ color: "#64748b" }}>Inward Dak:</span>
                <span>{inspectingOfficer.canRegisterInwardDak ? "Can Register Inward Dak" : "No Registry Rights"}</span>

                <span style={{ color: "#64748b" }}>Work Modules:</span>
                <span>
                  {inspectingOfficer.modules.length > 0
                    ? inspectingOfficer.modules.join(", ")
                    : inspectingOfficer.authority === "OFFICE_SUPERVISOR" || inspectingOfficer.authority === "OFFICE_ADMIN" || inspectingOfficer.authority === "SYSTEM_ADMIN"
                    ? "Full Office Access"
                    : "None"}
                </span>

                <span style={{ color: "#64748b" }}>Assigned Desks:</span>
                <span>
                  {inspectingOfficer.deskIds.length > 0
                    ? options?.desks.filter(d => inspectingOfficer.deskIds.includes(d.id)).map(d => `${d.name} (${d.code})`).join(", ") || "Assigned Desks"
                    : "None"}
                </span>
              </div>
            </div>
            <div className="rbac-modal-footer">
              <button className="rbac-btn-outline" onClick={() => setInspectingOfficer(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Create Officer Modal (V3) */}
      {showCreateModal && (
        <div className="rbac-modal-backdrop" onClick={() => setShowCreateModal(false)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "620px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Create Officer Account</h2>
                <p>Assign official civil identity, operational responsibilities, and primary seat.</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setShowCreateModal(false)}>&times;</button>
            </div>

            <form onSubmit={handleCreateOfficerSubmit}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px", maxHeight: "68vh", overflowY: "auto" }}>
                {createFormError && (
                  <div className="rbac-banner-error" style={{ padding: "8px 12px", borderRadius: "6px", background: "#fef2f2", color: "#991b1b", fontSize: "13px" }}>
                    ⚠️ {createFormError}
                  </div>
                )}

                {/* Section A: Official Identity */}
                <div style={{ background: "#f8fafc", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                  <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "12px", letterSpacing: "0.05em" }}>
                    A. Official Identity
                  </div>

                  <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                    <div className="rbac-form-group">
                      <label>Official Username *</label>
                      <input
                        type="text"
                        required
                        placeholder="e.g. rajesh.sharma"
                        value={newUsername}
                        onChange={(e) => setNewUsername(e.target.value)}
                        autoFocus
                      />
                    </div>

                    <div className="rbac-form-group">
                      <label>Full Name *</label>
                      <input
                        type="text"
                        required
                        placeholder="e.g. Rajesh Sharma"
                        value={newFullName}
                        onChange={(e) => setNewFullName(e.target.value)}
                      />
                    </div>
                  </div>

                  <div className="rbac-form-group" style={{ marginTop: "12px" }}>
                    <label>Civil Designation *</label>
                    <select
                      value={newDesignationId}
                      onChange={(e) => setNewDesignationId(e.target.value)}
                    >
                      <option value="">-- Select Civil Designation --</option>
                      {options?.designations.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name}
                        </option>
                      ))}
                      <option value="__OTHER__">Other / Other Designation</option>
                    </select>
                  </div>

                  {newDesignationId === "__OTHER__" && (
                    <div className="rbac-form-group" style={{ marginTop: "10px" }}>
                      <label>Enter Designation *</label>
                      <input
                        type="text"
                        required
                        placeholder="e.g. Officer on Special Duty"
                        value={newCustomDesignation}
                        onChange={(e) => setNewCustomDesignation(e.target.value)}
                      />
                    </div>
                  )}

                  {isSelectedDesignationAdm && options?.authority === "SYSTEM_ADMIN" && (
                    <div style={{ marginTop: "10px", padding: "10px 12px", borderRadius: "6px", background: "#e0e7ff", border: "1px solid #c7d2fe", color: "#3730a3", fontSize: "13px" }}>
                      🛡️ <strong>Office Administrator — Full Office Access</strong>
                      <div style={{ fontSize: "12px", marginTop: "2px" }}>
                        Assigning the canonical ADM designation configures this account as the Office Administrator with full operational office authority.
                      </div>
                    </div>
                  )}
                </div>

                {/* Section B: Authority / Supervision */}
                {options?.canAssignOfficeSupervisor && !isSelectedDesignationAdm && (
                  <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                    <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px", letterSpacing: "0.05em" }}>
                      B. Authority &amp; Supervision
                    </div>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", padding: "10px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", background: newIsSupervisor ? "#eff6ff" : "#f8fafc" }}>
                      <input
                        type="checkbox"
                        checked={newIsSupervisor}
                        onChange={(e) => setNewIsSupervisor(e.target.checked)}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13.5px", color: "#0f172a" }}>Office Supervisor</strong>
                        <div style={{ fontSize: "12.5px", color: "#64748b", marginTop: "2px" }}>
                          Supervisor receives full operational access and can create/manage ordinary officers and helpers.
                        </div>
                      </div>
                    </label>

                    {newIsSupervisor && (
                      <div style={{ marginTop: "10px", padding: "10px 12px", borderRadius: "6px", background: "#f0fdf4", border: "1px solid #bbf7d0", color: "#166534", fontSize: "13px" }}>
                        ✓ <strong>Full operational office access will be assigned automatically.</strong> Module, Land, and Inward Dak permissions are pre-configured.
                      </div>
                    )}
                  </div>
                )}

                {/* Standard Officer Configuration (Work Access, Inward Dak, Land) */}
                {!newIsSupervisor && !isSelectedDesignationAdm && (
                  <>
                    {/* Section C: Work Access */}
                    <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                      <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "10px", letterSpacing: "0.05em" }}>
                        C. Work Access
                      </div>
                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
                        {ALL_MODULES.map((mod) => {
                          const isChecked = newModules.includes(mod.code);
                          return (
                            <label
                              key={mod.code}
                              style={{
                                display: "flex",
                                alignItems: "flex-start",
                                gap: "8px",
                                padding: "8px 10px",
                                border: "1px solid",
                                borderColor: isChecked ? "#2563eb" : "#e2e8f0",
                                borderRadius: "6px",
                                background: isChecked ? "#eff6ff" : "#ffffff",
                                cursor: "pointer",
                              }}
                            >
                              <input
                                type="checkbox"
                                checked={isChecked}
                                onChange={() => toggleModuleSelection(mod.code, newModules, setNewModules)}
                                style={{ marginTop: "3px" }}
                              />
                              <div>
                                <strong style={{ fontSize: "13px", color: "#0f172a" }}>{mod.label}</strong>
                                <div style={{ fontSize: "11.5px", color: "#64748b", marginTop: "2px" }}>
                                  {mod.description}
                                </div>
                              </div>
                            </label>
                          );
                        })}
                      </div>
                    </div>

                    {/* Section D: Inward Dak Registration */}
                    <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                      <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px", letterSpacing: "0.05em" }}>
                        D. Inward Dak Registration
                      </div>
                      <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", padding: "8px 12px", borderRadius: "6px", border: "1px solid #e2e8f0", background: newCanRegisterInwardDak ? "#eff6ff" : "#ffffff" }}>
                        <input
                          type="checkbox"
                          checked={newCanRegisterInwardDak}
                          onChange={(e) => setNewCanRegisterInwardDak(e.target.checked)}
                          style={{ marginTop: "3px" }}
                        />
                        <div>
                          <strong style={{ fontSize: "13px", color: "#0f172a" }}>Can Register Inward Dak</strong>
                          <div style={{ fontSize: "12px", color: "#64748b" }}>
                            Can enter new incoming Dak and make the first marking to an officer.
                          </div>
                        </div>
                      </label>
                    </div>

                    {/* Section E: Land Records */}
                    <div style={{ background: "#ffffff", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                      <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px", letterSpacing: "0.05em" }}>
                        E. Land Records Access
                      </div>
                      <div style={{ display: "flex", flexDirection: "column", gap: "8px" }}>
                        <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: newLandAccess === "None" ? "#f8fafc" : "#ffffff" }}>
                          <input
                            type="radio"
                            name="newLandAccessRadio"
                            value="None"
                            checked={newLandAccess === "None"}
                            onChange={() => setNewLandAccess("None")}
                            style={{ marginTop: "3px" }}
                          />
                          <div>
                            <strong style={{ fontSize: "13px", color: "#0f172a" }}>None</strong>
                            <div style={{ fontSize: "12px", color: "#64748b" }}>No Land Records access.</div>
                          </div>
                        </label>

                        <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: newLandAccess === "ViewOnly" ? "#f0f9ff" : "#ffffff" }}>
                          <input
                            type="radio"
                            name="newLandAccessRadio"
                            value="ViewOnly"
                            checked={newLandAccess === "ViewOnly"}
                            onChange={() => setNewLandAccess("ViewOnly")}
                            style={{ marginTop: "3px" }}
                          />
                          <div>
                            <strong style={{ fontSize: "13px", color: "#0f172a" }}>View Only</strong>
                            <div style={{ fontSize: "12px", color: "#64748b" }}>Can view village, khasra, LR, Award, NM and related Land Records.</div>
                          </div>
                        </label>

                        <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: newLandAccess === "ViewWrite" ? "#f0fdf4" : "#ffffff" }}>
                          <input
                            type="radio"
                            name="newLandAccessRadio"
                            value="ViewWrite"
                            checked={newLandAccess === "ViewWrite"}
                            onChange={() => setNewLandAccess("ViewWrite")}
                            style={{ marginTop: "3px" }}
                          />
                          <div>
                            <strong style={{ fontSize: "13px", color: "#0f172a" }}>View + Write</strong>
                            <div style={{ fontSize: "12px", color: "#64748b" }}>Can maintain Land Records including village/khasra/Award/NM related records.</div>
                          </div>
                        </label>
                      </div>
                    </div>
                  </>
                )}

                {/* Section F: Desk / Seat */}
                <div style={{ background: "#f8fafc", padding: "14px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                  <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px", letterSpacing: "0.05em" }}>
                    F. Operational Seat / Desk
                  </div>
                  <div className="rbac-form-group">
                    <label>Primary Desk / Seat</label>
                    <select
                      value={newPrimaryDeskId}
                      onChange={(e) => {
                        const val = e.target.value;
                        setNewPrimaryDeskId(val);
                        if (val && newAdditionalDeskIds.includes(val)) {
                          setNewAdditionalDeskIds(newAdditionalDeskIds.filter((id) => id !== val));
                        }
                      }}
                    >
                      <option value="">-- No Specific Seat Assigned --</option>
                      {options?.desks.map((d) => (
                        <option key={d.id} value={d.id}>
                          {d.name} ({d.code})
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="rbac-form-group" style={{ marginTop: "10px" }}>
                    <label>Additional Active Desks</label>
                    {newAdditionalDeskIds.length > 0 && (
                      <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginBottom: "8px" }}>
                        {newAdditionalDeskIds.map((deskId) => {
                          const desk = options?.desks.find((d) => d.id === deskId);
                          return (
                            <span
                              key={deskId}
                              className="rbac-badge rbac-badge-outline"
                              style={{ display: "inline-flex", alignItems: "center", gap: "6px", fontSize: "12px", padding: "4px 8px" }}
                            >
                              <span>{desk?.name || deskId}</span>
                              <button
                                type="button"
                                onClick={() => setNewAdditionalDeskIds(newAdditionalDeskIds.filter((id) => id !== deskId))}
                                style={{ background: "none", border: "none", cursor: "pointer", color: "#64748b", fontWeight: "bold", padding: 0 }}
                                title="Remove desk"
                              >
                                &times;
                              </button>
                            </span>
                          );
                        })}
                      </div>
                    )}
                    <select
                      value=""
                      onChange={(e) => {
                        const val = e.target.value;
                        if (val && !newAdditionalDeskIds.includes(val) && val !== newPrimaryDeskId) {
                          setNewAdditionalDeskIds([...newAdditionalDeskIds, val]);
                        }
                      }}
                    >
                      <option value="">+ Add another desk…</option>
                      {options?.desks
                        ?.filter((d) => d.id !== newPrimaryDeskId && !newAdditionalDeskIds.includes(d.id))
                        .map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name} ({d.code})
                          </option>
                        ))}
                    </select>
                    <small style={{ color: "#64748b", fontSize: "12px", display: "block", marginTop: "4px" }}>
                      Allows this officer to operate across multiple posts or dealing seats when required.
                    </small>
                  </div>
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button type="button" className="rbac-btn-outline" onClick={() => setShowCreateModal(false)} disabled={actionLoading}>
                  Cancel
                </button>
                <button type="submit" className="rbac-btn-primary" disabled={actionLoading}>
                  {actionLoading ? "Creating…" : "Create Official Account"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Create Technical System Administrator Modal */}
      {showTechAdminModal && (
        <div className="rbac-modal-backdrop" onClick={() => setShowTechAdminModal(false)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "520px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Create Technical System Administrator</h2>
                <p>Security and platform administrative identity with system-level access.</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setShowTechAdminModal(false)}>&times;</button>
            </div>

            <form onSubmit={handleCreateTechAdminSubmit}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {techFormError && (
                  <div className="rbac-banner-error" style={{ padding: "8px 12px", borderRadius: "6px", background: "#fef2f2", color: "#991b1b", fontSize: "13px" }}>
                    ⚠️ {techFormError}
                  </div>
                )}

                <div className="rbac-form-group">
                  <label>Official Username *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. sysadmin.sec"
                    value={techUsername}
                    onChange={(e) => setTechUsername(e.target.value)}
                    autoFocus
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Full Name *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. Platform Administrator"
                    value={techFullName}
                    onChange={(e) => setTechFullName(e.target.value)}
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Civil Designation</label>
                  <select
                    value={techDesignationId}
                    onChange={(e) => setTechDesignationId(e.target.value)}
                  >
                    <option value="">-- No Official Designation --</option>
                    {options?.designations.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div style={{ padding: "10px 12px", borderRadius: "6px", background: "#f8fafc", border: "1px solid #cbd5e1", fontSize: "13px", color: "#475569" }}>
                  ℹ️ <strong>System Administrator is a technical security role and does not require a civil designation.</strong>
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button type="button" className="rbac-btn-outline" onClick={() => setShowTechAdminModal(false)} disabled={actionLoading}>
                  Cancel
                </button>
                <button type="submit" className="rbac-btn-primary" disabled={actionLoading}>
                  {actionLoading ? "Creating…" : "Create System Administrator"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit Officer Drawer */}
      {editingOfficer && (
        <div className="rbac-modal-backdrop" onClick={() => setEditingOfficer(null)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "660px", maxHeight: "90vh", overflowY: "auto" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Edit Account — {editingOfficer.username}</h2>
                <p>Update designation, operational access, seat, or attached assistants.</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setEditingOfficer(null)}>&times;</button>
            </div>

            <form onSubmit={handleEditOfficerSubmit}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {editDrawerLoading && (
                  <div style={{ padding: "8px 12px", borderRadius: "6px", background: "#f8fafc", color: "#475569", fontSize: "13px" }}>
                    Loading latest account details…
                  </div>
                )}
                {editDrawerError && (
                  <div className="rbac-banner-error" style={{ padding: "8px 12px", borderRadius: "6px", background: "#fef2f2", color: "#991b1b", fontSize: "13px" }}>
                    ⚠️ {editDrawerError}
                  </div>
                )}

                <div className="rbac-form-group">
                  <label>Full Name *</label>
                  <input
                    type="text"
                    required
                    value={editFullName}
                    onChange={(e) => setEditFullName(e.target.value)}
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Civil Designation</label>
                  <select
                    value={editDesignationId}
                    onChange={(e) => setEditDesignationId(e.target.value)}
                  >
                    <option value="">-- No Official Designation --</option>
                    {options?.designations.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                    <option value="__OTHER__">Other / Other Designation</option>
                  </select>
                </div>

                {editDesignationId === "__OTHER__" && (
                  <div className="rbac-form-group">
                    <label>Enter Designation *</label>
                    <input
                      type="text"
                      required
                      placeholder="e.g. Officer on Special Duty"
                      value={editCustomDesignation}
                      onChange={(e) => setEditCustomDesignation(e.target.value)}
                    />
                  </div>
                )}

                {isEditDesignationAdm && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                  <div style={{ fontSize: "12px", color: "#64748b", background: "#f8fafc", padding: "8px 12px", borderRadius: "6px", border: "1px solid #e2e8f0", marginBottom: "1rem" }}>
                    ℹ️ Note: Changing an existing account's designation to ADM does not automatically promote it to Office Admin authority.
                  </div>
                )}

                {/* Supervisor toggle */}
                {options?.canAssignOfficeSupervisor && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                  <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", padding: "10px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", background: editIsSupervisor ? "#eff6ff" : "#f8fafc" }}>
                    <input
                      type="checkbox"
                      checked={editIsSupervisor}
                      onChange={(e) => setEditIsSupervisor(e.target.checked)}
                      style={{ marginTop: "3px" }}
                    />
                    <div>
                      <strong style={{ fontSize: "13.5px", color: "#0f172a" }}>Office Supervisor</strong>
                      <div style={{ fontSize: "12.5px", color: "#64748b", marginTop: "2px" }}>
                        Supervisor receives full operational access and can create/manage ordinary officers and helpers.
                      </div>
                    </div>
                  </label>
                )}

                {/* Work Access, Inward Dak, Land when not supervisor or admin */}
                {!editIsSupervisor && editingOfficer.authority !== "OFFICE_ADMIN" && editingOfficer.authority !== "SYSTEM_ADMIN" && (
                  <>
                    <div style={{ background: "#ffffff", padding: "12px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                      <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px" }}>
                        Work Access
                      </div>
                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px" }}>
                        {ALL_MODULES.map((mod) => {
                          const isChecked = editModules.includes(mod.code);
                          return (
                            <label key={mod.code} style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px", border: "1px solid", borderColor: isChecked ? "#2563eb" : "#e2e8f0", borderRadius: "6px", background: isChecked ? "#eff6ff" : "#ffffff", cursor: "pointer" }}>
                              <input
                                type="checkbox"
                                checked={isChecked}
                                onChange={() => toggleModuleSelection(mod.code, editModules, setEditModules)}
                                style={{ marginTop: "3px" }}
                              />
                              <div>
                                <strong style={{ fontSize: "13px", color: "#0f172a" }}>{mod.label}</strong>
                                <div style={{ fontSize: "11px", color: "#64748b" }}>{mod.description}</div>
                              </div>
                            </label>
                          );
                        })}
                      </div>
                    </div>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", cursor: "pointer", padding: "8px 12px", borderRadius: "6px", border: "1px solid #e2e8f0", background: editCanRegisterInwardDak ? "#eff6ff" : "#ffffff" }}>
                      <input
                        type="checkbox"
                        checked={editCanRegisterInwardDak}
                        onChange={(e) => setEditCanRegisterInwardDak(e.target.checked)}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>Can Register Inward Dak</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Can enter new incoming Dak and make the first marking to an officer.</div>
                      </div>
                    </label>

                    <div style={{ background: "#ffffff", padding: "12px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                      <div style={{ fontSize: "12px", fontWeight: 700, textTransform: "uppercase", color: "#475569", marginBottom: "8px" }}>
                        Land Records Access
                      </div>
                      <div style={{ display: "flex", gap: "16px" }}>
                        <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                          <input type="radio" name="editLandRadio" value="None" checked={editLandAccess === "None"} onChange={() => setEditLandAccess("None")} />
                          None
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                          <input type="radio" name="editLandRadio" value="ViewOnly" checked={editLandAccess === "ViewOnly"} onChange={() => setEditLandAccess("ViewOnly")} />
                          View Only
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                          <input type="radio" name="editLandRadio" value="ViewWrite" checked={editLandAccess === "ViewWrite"} onChange={() => setEditLandAccess("ViewWrite")} />
                          View + Write
                        </label>
                      </div>
                    </div>
                  </>
                )}

                <div className="rbac-form-group">
                  <label>Primary Seat / Desk</label>
                  <select value={editPrimaryDeskId} onChange={(e) => setEditPrimaryDeskId(e.target.value)}>
                    <option value="">-- No Specific Seat Assigned --</option>
                    {options?.desks.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name} ({d.code})
                      </option>
                    ))}
                  </select>

                  <div style={{ marginTop: "10px" }}>
                    <label style={{ fontSize: "12px", color: "#475569", fontWeight: 600, display: "block", marginBottom: "4px" }}>
                      Additional Active Desks / Seats
                    </label>
                    {editAdditionalDeskIds.length > 0 && (
                      <div style={{ display: "flex", flexWrap: "wrap", gap: "6px", marginBottom: "8px" }}>
                        {editAdditionalDeskIds.map((deskId) => {
                          const desk = options?.desks.find((d) => d.id === deskId);
                          return (
                            <span
                              key={deskId}
                              className="rbac-badge rbac-badge-outline"
                              style={{ display: "inline-flex", alignItems: "center", gap: "6px", fontSize: "12px", padding: "4px 8px" }}
                            >
                              <span>{desk?.name || deskId}</span>
                              <button
                                type="button"
                                onClick={() => setEditAdditionalDeskIds(editAdditionalDeskIds.filter((id) => id !== deskId))}
                                style={{ background: "none", border: "none", cursor: "pointer", color: "#64748b", fontWeight: "bold", padding: 0 }}
                                title="Remove desk"
                              >
                                &times;
                              </button>
                            </span>
                          );
                        })}
                      </div>
                    )}
                    <select
                      value=""
                      onChange={(e) => {
                        const val = e.target.value;
                        if (val && !editAdditionalDeskIds.includes(val) && val !== editPrimaryDeskId) {
                          setEditAdditionalDeskIds([...editAdditionalDeskIds, val]);
                        }
                      }}
                    >
                      <option value="">+ Add another desk…</option>
                      {options?.desks
                        ?.filter((d) => d.id !== editPrimaryDeskId && !editAdditionalDeskIds.includes(d.id))
                        .map((d) => (
                          <option key={d.id} value={d.id}>
                            {d.name} ({d.code})
                          </option>
                        ))}
                    </select>
                    <small style={{ color: "#64748b", fontSize: "12px", display: "block", marginTop: "4px" }}>
                      Allows this officer to operate across multiple posts or dealing seats when required.
                    </small>
                  </div>
                </div>

                {/* Attached Helpers Section */}
                <div style={{ marginTop: "8px", borderTop: "1px solid #e2e8f0", paddingTop: "14px" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "10px" }}>
                    <div style={{ fontSize: "13px", fontWeight: 700, color: "#1e293b" }}>
                      Attached Helpers ({attachedHelpersMap.get(editingOfficer.id)?.length || 0})
                    </div>
                    <button
                      type="button"
                      className="rbac-btn-sm rbac-btn-outline"
                      onClick={() => {
                        setHelperUsername("");
                        setHelperFullName("");
                        setHelperDesignationChoice("DEO");
                        setHelperCustomDesignation("");
                        setHelperDeskId(editingOfficer.deskIds[0] || "");
                        setHelperAccess("ReadOnly");
                        setHelperFormError(null);
                        setShowAddHelperForOfficer(true);
                      }}
                    >
                      + Add Helper for this Officer
                    </button>
                  </div>

                  {attachedHelpersMap.get(editingOfficer.id)?.length ? (
                    <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                      {attachedHelpersMap.get(editingOfficer.id)!.map((h) => (
                        <div key={h.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "8px 10px", background: "#f8fafc", borderRadius: "6px", border: "1px solid #e2e8f0", fontSize: "13px" }}>
                          <div>
                            <strong>{h.fullName}</strong> <span style={{ color: "#64748b", fontFamily: "monospace", fontSize: "12px" }}>({h.username})</span>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>{h.effectiveDesignation || "DEO"} · {h.isActive ? "Active" : "Inactive"}</div>
                          </div>
                          <div style={{ display: "flex", gap: "6px" }}>
                            <button type="button" className="rbac-btn-sm rbac-btn-outline" onClick={() => void handleResetCredential(h)}>
                              Reset
                            </button>
                            <button type="button" className={`rbac-btn-sm ${h.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`} onClick={() => void handleToggleStatus(h)}>
                              {h.isActive ? "Deactivate" : "Activate"}
                            </button>
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div style={{ fontSize: "12px", color: "#94a3b8", fontStyle: "italic" }}>
                      No helpers currently attached to this officer.
                    </div>
                  )}

                  {/* Add Helper Sub-Form */}
                  {showAddHelperForOfficer && (
                    <div style={{ marginTop: "12px", padding: "12px", background: "#f1f5f9", borderRadius: "6px", border: "1px solid #cbd5e1" }}>
                      <div style={{ fontWeight: 600, fontSize: "13px", marginBottom: "8px", color: "#0f172a" }}>
                        New Attached Helper
                      </div>

                      {helperFormError && (
                        <div style={{ color: "#991b1b", fontSize: "12px", marginBottom: "8px" }}>
                          ⚠️ {helperFormError}
                        </div>
                      )}

                      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "8px", marginBottom: "8px" }}>
                        <div>
                          <label style={{ fontSize: "11px", fontWeight: 600, color: "#475569" }}>Username *</label>
                          <input
                            type="text"
                            required
                            placeholder="e.g. deo.officer"
                            value={helperUsername}
                            onChange={(e) => setHelperUsername(e.target.value)}
                            style={{ width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #cbd5e1", fontSize: "12px", boxSizing: "border-box" }}
                          />
                        </div>
                        <div>
                          <label style={{ fontSize: "11px", fontWeight: 600, color: "#475569" }}>Full Name *</label>
                          <input
                            type="text"
                            required
                            placeholder="e.g. Assistant Name"
                            value={helperFullName}
                            onChange={(e) => setHelperFullName(e.target.value)}
                            style={{ width: "100%", padding: "6px", borderRadius: "4px", border: "1px solid #cbd5e1", fontSize: "12px", boxSizing: "border-box" }}
                          />
                        </div>
                      </div>

                      <div style={{ display: "flex", gap: "12px", alignItems: "center", marginBottom: "8px" }}>
                        <label style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "12px" }}>
                          <input
                            type="radio"
                            name="seniorHelperDesignation"
                            checked={helperDesignationChoice === "DEO"}
                            onChange={() => setHelperDesignationChoice("DEO")}
                          />
                          DEO
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "4px", fontSize: "12px" }}>
                          <input
                            type="radio"
                            name="seniorHelperDesignation"
                            checked={helperDesignationChoice === "OTHER"}
                            onChange={() => setHelperDesignationChoice("OTHER")}
                          />
                          Other
                        </label>
                        {helperDesignationChoice === "OTHER" && (
                          <input
                            type="text"
                            placeholder="Custom title"
                            value={helperCustomDesignation}
                            onChange={(e) => setHelperCustomDesignation(e.target.value)}
                            style={{ flex: 1, padding: "4px 6px", borderRadius: "4px", border: "1px solid #cbd5e1", fontSize: "12px" }}
                          />
                        )}
                      </div>

                      <div style={{ display: "flex", gap: "12px", marginBottom: "8px", fontSize: "12px" }}>
                        <label style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                          <input type="radio" name="seniorHelperAccess" value="None" checked={helperAccess === "None"} onChange={() => setHelperAccess("None")} />
                          None
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                          <input type="radio" name="seniorHelperAccess" value="ReadOnly" checked={helperAccess === "ReadOnly"} onChange={() => setHelperAccess("ReadOnly")} />
                          Read Only
                        </label>
                        <label style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                          <input type="radio" name="seniorHelperAccess" value="ReadWrite" checked={helperAccess === "ReadWrite"} onChange={() => setHelperAccess("ReadWrite")} />
                          Read + Write
                        </label>
                      </div>

                      <div style={{ display: "flex", gap: "8px", justifyContent: "flex-end" }}>
                        <button type="button" className="rbac-btn-sm rbac-btn-outline" onClick={() => setShowAddHelperForOfficer(false)}>
                          Cancel
                        </button>
                        <button type="button" className="rbac-btn-sm rbac-btn-primary" onClick={handleSeniorAddHelperSubmit} disabled={actionLoading}>
                          {actionLoading ? "Adding…" : "Attach Helper"}
                        </button>
                      </div>
                    </div>
                  )}
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button type="button" className="rbac-btn-outline" onClick={() => setEditingOfficer(null)} disabled={actionLoading}>
                  Cancel
                </button>
                <button type="submit" className="rbac-btn-primary" disabled={actionLoading}>
                  {actionLoading ? "Saving…" : "Save Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* One-Time Credential Modal */}
      {showCredentialModal && credentialData && (
        <div className="rbac-modal-backdrop">
          <div className="rbac-modal-dialog" style={{ maxWidth: "480px" }}>
            <div className="rbac-modal-header">
              <div>
                <h2>One-Time Temporary Credential</h2>
                <p>Share securely with the employee. This password will not be shown again.</p>
              </div>
            </div>

            <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
              <div style={{ background: "#f8fafc", padding: "16px", borderRadius: "8px", border: "1px solid #e2e8f0" }}>
                <div style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600, letterSpacing: "0.05em" }}>
                  Official Username
                </div>
                <div style={{ fontSize: "16px", fontWeight: 700, color: "#0f172a", marginTop: "4px" }}>
                  {credentialData.username}
                </div>

                <div style={{ fontSize: "12px", color: "#64748b", textTransform: "uppercase", fontWeight: 600, letterSpacing: "0.05em", marginTop: "16px" }}>
                  Temporary Password
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", marginTop: "6px" }}>
                  <code style={{ fontSize: "18px", fontWeight: 700, color: "#1e3a8a", background: "#ffffff", padding: "8px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", flex: 1, letterSpacing: "0.05em" }}>
                    {credentialData.temporaryCredential}
                  </code>
                  <button type="button" className="rbac-btn-outline" onClick={copyCredentialToClipboard} style={{ padding: "8px 12px" }}>
                    {copiedNotice ? "Copied! ✓" : "Copy"}
                  </button>
                </div>

                <div style={{ marginTop: "16px", fontSize: "12.5px", color: "#b45309", background: "#fffbeb", padding: "8px 12px", borderRadius: "6px", border: "1px solid #fef3c7" }}>
                  ⏱ <strong>Valid for 24 hours.</strong> The officer must replace it with a personal password on first login.
                </div>
              </div>

              <label style={{ display: "flex", alignItems: "flex-start", gap: "10px", fontSize: "13px", color: "#334155", cursor: "pointer", background: "#f1f5f9", padding: "12px", borderRadius: "6px" }}>
                <input
                  type="checkbox"
                  checked={credentialAcknowledged}
                  onChange={(e) => setCredentialAcknowledged(e.target.checked)}
                  style={{ marginTop: "2px" }}
                />
                <span>
                  I have copied or securely communicated this temporary credential to the employee. I acknowledge that it cannot be retrieved again.
                </span>
              </label>
            </div>

            <div className="rbac-modal-footer">
              <button
                type="button"
                className="rbac-btn-primary"
                disabled={!credentialAcknowledged}
                onClick={closeCredentialModal}
              >
                Close &amp; Finish
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
