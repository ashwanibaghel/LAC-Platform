import React, { useState, useEffect, useMemo } from "react";
import type { OfficerItem, OfficerAssistantDelegation } from "./types";
import "./admin.css";

// Staged canonical delegations demonstrating the real Delhi office pattern
const INITIAL_DELEGATIONS: OfficerAssistantDelegation[] = [
  {
    id: "del-1",
    assistantUserId: "user-deo-rajesh",
    assistantUsername: "deo.rajesh",
    assistantDisplayName: "Rajesh Kumar (DEO)",
    supervisingOfficerId: "officer-sdm-verma",
    supervisingOfficerName: "Sh. R. K. Verma",
    supervisingOfficerDesignation: "SDM / Land Acquisition Collector",
    delegatedWorkCodes: ["WORK_STMT_A", "WORK_LR_VERIF", "WORK_DAK_INTAKE"],
    delegatedPermissionCodes: ["Dak.View", "Dak.Register", "Award.View", "Khasra.Verify"],
    notes: "Attached to SDM/LAC for Statement-A verification and dak digitization.",
    isActive: true,
    delegatedAt: "2026-09-15T10:30:00Z",
  },
  {
    id: "del-2",
    assistantUserId: "user-deo-sunita",
    assistantUsername: "deo.sunita",
    assistantDisplayName: "Sunita Sharma (DEO)",
    supervisingOfficerId: "officer-sdm-verma",
    supervisingOfficerName: "Sh. R. K. Verma",
    supervisingOfficerDesignation: "SDM / Land Acquisition Collector",
    delegatedWorkCodes: ["WORK_COMP_DISBUR", "WORK_COURT_LITIG"],
    delegatedPermissionCodes: ["Matter.View", "Court.View"],
    notes: "Attached to SDM/LAC for compensation voucher drafting and court hearing diary.",
    isActive: true,
    delegatedAt: "2026-09-20T14:15:00Z",
  },
  {
    id: "del-3",
    assistantUserId: "user-deo-amit",
    assistantUsername: "deo.amit",
    assistantDisplayName: "Amit Yadav (DEO)",
    supervisingOfficerId: "officer-tehsildar-singh",
    supervisingOfficerName: "Sh. Vikram Singh",
    supervisingOfficerDesignation: "Tehsildar (LAC)",
    delegatedWorkCodes: ["WORK_LR_VERIF", "WORK_POSS_DEMARC"],
    delegatedPermissionCodes: ["Khasra.Verify", "Award.View"],
    notes: "Attached to Tehsildar for field demarcation and khatauni tallying.",
    isActive: true,
    delegatedAt: "2026-09-25T09:45:00Z",
  },
];

const AVAILABLE_PERMISSIONS = [
  { code: "Dak.View", label: "View Inward Dak & Desks", category: "Correspondence" },
  { code: "Dak.Register", label: "Register New Inward Dak", category: "Correspondence" },
  { code: "Outward.View", label: "View Outward Dispatch", category: "Correspondence" },
  { code: "Outward.Create", label: "Draft Outward Letter", category: "Correspondence" },
  { code: "Award.View", label: "Inspect Awards & Land Records", category: "Land Records" },
  { code: "Award.Edit", label: "Edit Draft Award Schedule", category: "Land Records" },
  { code: "Khasra.Verify", label: "Verify Khasra & Ownership", category: "Land Records" },
  { code: "Matter.View", label: "View Matters & Note Sheets", category: "Matters" },
  { code: "Matter.Edit", label: "Draft Note Sheets", category: "Matters" },
  { code: "Matter.Sign", label: "Approve & E-Sign Note Sheet", category: "Matters" },
  { code: "Court.View", label: "View Court Cases & Hearings", category: "Court" },
  { code: "Court.Create", label: "Register New Court Reference", category: "Court" },
];

export const OfficerAssistantAdmin: React.FC = () => {
  const [delegations, setDelegations] = useState<OfficerAssistantDelegation[]>(INITIAL_DELEGATIONS);
  const [officers, setOfficers] = useState<OfficerItem[]>([]);
  const [loadingOfficers, setLoadingOfficers] = useState(true);
  const [searchQuery, setSearchQuery] = useState("");
  const [filterOfficerId, setFilterOfficerId] = useState("ALL");

  // Attach Assistant Modal
  const [showAttachModal, setShowAttachModal] = useState(false);
  const [selectedSupervisorId, setSelectedSupervisorId] = useState("");
  const [assistantUsername, setAssistantUsername] = useState("");
  const [assistantDisplayName, setAssistantDisplayName] = useState("");
  const [tempPassword, setTempPassword] = useState("");
  const [selectedWorkCodes, setSelectedWorkCodes] = useState<string[]>([]);
  const [selectedPermCodes, setSelectedPermCodes] = useState<string[]>([]);
  const [delegationNotes, setDelegationNotes] = useState("");
  const [stagedNotification, setStagedNotification] = useState<string | null>(null);

  // Load actual officers from backend
  useEffect(() => {
    fetch("/api/admin/users", { credentials: "include" })
      .then((res) => (res.ok ? res.json() : []))
      .then((data: OfficerItem[]) => {
        setOfficers(data);
        if (data.length > 0 && !selectedSupervisorId) {
          // Default to first active officer with a designation
          const firstOfficer = data.find((u) => u.isActive && u.designation) || data[0];
          setSelectedSupervisorId(firstOfficer.id);
        }
      })
      .catch(() => setOfficers([]))
      .finally(() => setLoadingOfficers(false));
  }, []);

  // Determine selected supervising officer and held powers
  const currentSupervisor = useMemo(() => {
    return officers.find((o) => o.id === selectedSupervisorId) || null;
  }, [officers, selectedSupervisorId]);

  // Determine what permissions the supervisor possesses
  // In Delhi LAC model, if supervisor is SYSTEM_ADMIN or has full roles, they possess all powers.
  // Otherwise, their permissions are constrained to their roles.
  const supervisorHeldPermissions = useMemo(() => {
    if (!currentSupervisor) return new Set<string>();
    const roles = currentSupervisor.roles || [];
    if (roles.includes("SYSTEM_ADMIN")) {
      return new Set(AVAILABLE_PERMISSIONS.map((p) => p.code));
    }
    const held = new Set<string>();
    if (roles.includes("LAC_OFFICER") || roles.includes("BRANCH_INCHARGE")) {
      AVAILABLE_PERMISSIONS.forEach((p) => held.add(p.code));
    } else {
      if (roles.includes("INTAKE_OFFICER")) {
        held.add("Dak.View");
        held.add("Dak.Register");
        held.add("Outward.View");
      }
      if (roles.includes("RECORD_VERIFIER")) {
        held.add("Award.View");
        held.add("Khasra.Verify");
      }
      if (roles.includes("DEALING_ASSISTANT")) {
        held.add("Matter.View");
        held.add("Matter.Edit");
        held.add("Dak.View");
        held.add("Court.View");
      }
      // If none matched, grant base read permissions
      held.add("Dak.View");
      held.add("Award.View");
      held.add("Matter.View");
      held.add("Court.View");
    }
    return held;
  }, [currentSupervisor]);

  // Filter delegations
  const filteredDelegations = useMemo(() => {
    return delegations.filter((d) => {
      const matchesSearch =
        searchQuery.trim() === "" ||
        d.assistantDisplayName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        d.assistantUsername.toLowerCase().includes(searchQuery.toLowerCase()) ||
        d.supervisingOfficerName.toLowerCase().includes(searchQuery.toLowerCase());

      const matchesOfficer = filterOfficerId === "ALL" || d.supervisingOfficerId === filterOfficerId;

      return matchesSearch && matchesOfficer;
    });
  }, [delegations, searchQuery, filterOfficerId]);

  const handleToggleWork = (code: string) => {
    setSelectedWorkCodes((prev) =>
      prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]
    );
  };

  const handleTogglePerm = (code: string) => {
    if (!supervisorHeldPermissions.has(code)) return; // Strictly locked
    setSelectedPermCodes((prev) =>
      prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]
    );
  };

  const handleAttachAssistant = (e: React.FormEvent) => {
    e.preventDefault();
    if (!currentSupervisor || !assistantUsername.trim() || !assistantDisplayName.trim()) return;

    // Filter delegated permissions to strictly ensure containment
    const sanitizedPerms = selectedPermCodes.filter((c) => supervisorHeldPermissions.has(c));

    const newDelegation: OfficerAssistantDelegation = {
      id: `del-staged-${Date.now()}`,
      assistantUserId: `user-${assistantUsername.trim().toLowerCase()}`,
      assistantUsername: assistantUsername.trim().toLowerCase(),
      assistantDisplayName: assistantDisplayName.trim(),
      supervisingOfficerId: currentSupervisor.id,
      supervisingOfficerName: currentSupervisor.displayName,
      supervisingOfficerDesignation: currentSupervisor.designation?.name || "Gazetted Officer",
      delegatedWorkCodes: selectedWorkCodes,
      delegatedPermissionCodes: sanitizedPerms,
      notes: delegationNotes.trim() || `Delegated assistant operating on behalf of ${currentSupervisor.displayName}.`,
      isActive: true,
      delegatedAt: new Date().toISOString(),
    };

    setDelegations((prev) => [newDelegation, ...prev]);
    setStagedNotification(
      `Staged delegation policy saved: DEO ${assistantDisplayName} attached to ${currentSupervisor.displayName}. Notice: Real account creation & backend persistence awaits Codex RBAC's delegation contract.`
    );

    setTimeout(() => {
      setShowAttachModal(false);
      setStagedNotification(null);
      setAssistantUsername("");
      setAssistantDisplayName("");
      setTempPassword("");
      setSelectedWorkCodes([]);
      setSelectedPermCodes([]);
      setDelegationNotes("");
    }, 2000);
  };

  const handleRevokeDelegation = (id: string, name: string) => {
    if (!confirm(`Are you sure you want to revoke delegation for ${name}?`)) return;
    setDelegations((prev) =>
      prev.map((d) => (d.id === id ? { ...d, isActive: false, revokedAt: new Date().toISOString() } : d))
    );
  };

  if (loadingOfficers) {
    return (
      <div className="rbac-admin-root">
        <div className="state"><strong>Loading assistant directory...</strong></div>
      </div>
    );
  }

  return (
    <div className="rbac-admin-root">
      {/* Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Officer Assistants & DEO Delegations</h2>
          <p>
            Supervising officers can attach Data Entry Operators (DEOs) and Dealing Assistants to execute specific responsibilities on their behalf with strict audit attribution.
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="primary-button" onClick={() => setShowAttachModal(true)}>
            + Attach Assistant / DEO
          </button>
        </div>
      </div>

      {/* Audit Attribution & Security Banner */}
      <div className="contract-notice-banner">
        <strong>Office Security & Audit Invariant:</strong>
        <span>
          • <strong>Audit Attribution:</strong> Every operation performed by an assistant is recorded as <em>"DEO X on behalf of Officer Y"</em>.<br />
          • <strong>Containment Rule:</strong> An assistant can <em>never</em> receive permissions that the supervising officer does not possess.<br />
          • <strong>Credential Policy:</strong> Plaintext passwords are never shown or stored. Only temporary initial passwords or reset links are issued.
        </span>
      </div>

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search assistant name, username, or supervising officer..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={filterOfficerId}
          onChange={(e) => setFilterOfficerId(e.target.value)}
        >
          <option value="ALL">All Supervising Officers</option>
          {officers.map((off) => (
            <option key={off.id} value={off.id}>
              {off.displayName} ({off.designation?.name || "Officer"})
            </option>
          ))}
        </select>

        <span style={{ fontSize: "13px", color: "#64748b", marginLeft: "auto" }}>
          Showing <strong>{filteredDelegations.length}</strong> Delegations
        </span>
      </div>

      {/* Delegations Table */}
      <div className="rbac-table-container">
        <table className="rbac-table">
          <thead>
            <tr>
              <th>Assistant / DEO</th>
              <th>Audit Attribution</th>
              <th>Delegated Work Categories</th>
              <th>Delegated Permissions</th>
              <th>Status</th>
              <th>Attached Date</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {filteredDelegations.map((d) => (
              <tr key={d.id}>
                <td>
                  <div className="officer-cell">
                    <div className="officer-avatar assistant">DEO</div>
                    <div className="officer-info">
                      <span className="officer-name">{d.assistantDisplayName}</span>
                      <span className="officer-user">@{d.assistantUsername}</span>
                    </div>
                  </div>
                </td>
                <td>
                  <div className="attribution-tag">
                    <span className="deo-name">{d.assistantDisplayName.split(" ")[0]}</span>
                    <span>on behalf of</span>
                    <span className="officer-name">{d.supervisingOfficerName}</span>
                  </div>
                  <div style={{ fontSize: "11px", color: "#64748b", marginTop: "4px" }}>
                    {d.supervisingOfficerDesignation}
                  </div>
                </td>
                <td>
                  <div className="role-tags-container">
                    {d.delegatedWorkCodes.map((code) => (
                      <span key={code} className="workstream-tag primary">
                        {code.replace("WORK_", "")}
                      </span>
                    ))}
                  </div>
                </td>
                <td>
                  <div className="role-tags-container">
                    {d.delegatedPermissionCodes.map((code) => (
                      <span key={code} className="role-tag">
                        {code}
                      </span>
                    ))}
                  </div>
                </td>
                <td>
                  <span className={`status ${d.isActive ? "success" : "neutral"}`}>
                    {d.isActive ? "Active Delegation" : "Revoked"}
                  </span>
                </td>
                <td>
                  <span style={{ fontSize: "12px", color: "#64748b" }}>
                    {new Date(d.delegatedAt).toLocaleDateString()}
                  </span>
                </td>
                <td>
                  <div className="rbac-action-buttons">
                    {d.isActive ? (
                      <button
                        className="rbac-btn-edit"
                        style={{ color: "#b91c1c" }}
                        onClick={() => handleRevokeDelegation(d.id, d.assistantDisplayName)}
                      >
                        Revoke Delegation
                      </button>
                    ) : (
                      <span style={{ fontSize: "12px", color: "#94a3b8" }}>Revoked</span>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Attach Assistant Modal */}
      {showAttachModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "680px" }}>
            <h3>Attach Assistant / DEO Account</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Configure a dedicated login account for an assistant and delegate a subset of your official authority.
            </p>

            {stagedNotification ? (
              <div className="form-message" style={{ background: "#ecfdf5", color: "#065f46", border: "1px solid #a7f3d0" }}>
                {stagedNotification}
              </div>
            ) : (
              <form onSubmit={handleAttachAssistant} style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {/* Supervisor Selection */}
                <div className="form-group">
                  <label>Supervising Officer</label>
                  <select
                    value={selectedSupervisorId}
                    onChange={(e) => setSelectedSupervisorId(e.target.value)}
                    required
                  >
                    {officers.map((off) => (
                      <option key={off.id} value={off.id}>
                        {off.displayName} ({off.designation?.name || "Officer"}) — Roles: {off.roles.join(", ") || "Standard"}
                      </option>
                    ))}
                  </select>
                  <small style={{ color: "#64748b" }}>
                    The assistant will operate under this officer's constitutional authority.
                  </small>
                </div>

                {/* Assistant Details */}
                <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                  <div className="form-group">
                    <label>Assistant Full Name</label>
                    <input
                      type="text"
                      required
                      placeholder="e.g. Manisha Rawat (DEO)"
                      value={assistantDisplayName}
                      onChange={(e) => setAssistantDisplayName(e.target.value)}
                    />
                  </div>
                  <div className="form-group">
                    <label>Assistant Username</label>
                    <input
                      type="text"
                      required
                      placeholder="e.g. deo.manisha"
                      value={assistantUsername}
                      onChange={(e) => setAssistantUsername(e.target.value)}
                    />
                  </div>
                </div>

                {/* Temporary Password */}
                <div className="form-group">
                  <label>Initial Temporary Password</label>
                  <input
                    type="password"
                    required
                    placeholder="Set temporary initial password"
                    value={tempPassword}
                    onChange={(e) => setTempPassword(e.target.value)}
                  />
                  <small style={{ color: "#64748b" }}>
                    Assistant will be prompted to reset password on first login. Plaintext is never stored.
                  </small>
                </div>

                {/* Delegated Work Categories */}
                <div className="form-group">
                  <label>Delegated Work Categories</label>
                  <div className="multi-select-grid">
                    {[
                      { code: "WORK_STMT_A", name: "Statement-A Verification" },
                      { code: "WORK_LR_VERIF", name: "Land Record Verification" },
                      { code: "WORK_AWARD_FORM", name: "Award Formulation Assistance" },
                      { code: "WORK_COMP_DISBUR", name: "Compensation Calculation" },
                      { code: "WORK_DAK_INTAKE", name: "Inward Dak Digitization" },
                      { code: "WORK_OUTWARD_DISP", name: "Outward Dispatch Preparation" },
                      { code: "WORK_COURT_LITIG", name: "Court Hearing Diary Collation" },
                    ].map((w) => (
                      <label key={w.code} className="multi-select-item">
                        <input
                          type="checkbox"
                          checked={selectedWorkCodes.includes(w.code)}
                          onChange={() => handleToggleWork(w.code)}
                        />
                        <span>{w.name}</span>
                      </label>
                    ))}
                  </div>
                </div>

                {/* Delegated Permissions with Strict Containment */}
                <div className="form-group">
                  <label>
                    Delegated Operational Powers (Subset of Supervisor's Authority)
                  </label>
                  <div className="multi-select-grid" style={{ maxHeight: "220px" }}>
                    {AVAILABLE_PERMISSIONS.map((p) => {
                      const supervisorHasIt = supervisorHeldPermissions.has(p.code);
                      return (
                        <label
                          key={p.code}
                          className="multi-select-item"
                          style={{
                            opacity: supervisorHasIt ? 1 : 0.5,
                            cursor: supervisorHasIt ? "pointer" : "not-allowed",
                          }}
                        >
                          <input
                            type="checkbox"
                            disabled={!supervisorHasIt}
                            checked={supervisorHasIt && selectedPermCodes.includes(p.code)}
                            onChange={() => handleTogglePerm(p.code)}
                          />
                          <div>
                            <div>{p.label}</div>
                            <small style={{ color: "#64748b" }}>{p.code}</small>
                            {!supervisorHasIt && (
                              <div className="delegation-lock-notice">
                                🔒 Supervisor lacks this power
                              </div>
                            )}
                          </div>
                        </label>
                      );
                    })}
                  </div>
                </div>

                {/* Delegation Notes */}
                <div className="form-group">
                  <label>Official Allocation Order / Scope Remarks</label>
                  <textarea
                    rows={2}
                    placeholder="Specify office order number or tenure bounds..."
                    value={delegationNotes}
                    onChange={(e) => setDelegationNotes(e.target.value)}
                  />
                </div>

                {/* Staged Contract Notice */}
                <div className="contract-warning-banner">
                  <strong>Codex RBAC Contract Dependency:</strong>
                  <span>
                    Delegations configured here are staged in the UI. Live persistence will activate once Codex RBAC's assistant delegation endpoints (<code>POST /api/admin/assistants/delegations</code>) are released.
                  </span>
                </div>

                <div className="modal-actions" style={{ marginTop: "10px" }}>
                  <button type="button" className="quiet-button" onClick={() => setShowAttachModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="primary-button">
                    Save Assistant & Delegation
                  </button>
                </div>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
