import React, { useState, useEffect, useCallback, useMemo } from "react";
import type {
  OfficerAssistantSummary,
  OfficerAssistantDetail,
  DelegationOptionsResponse,
  AssistantInput,
  AllocationInput,
  Designation,
} from "./types";
import "./admin.css";

export const OfficerAssistantAdmin: React.FC = () => {
  const [assistants, setAssistants] = useState<OfficerAssistantSummary[]>([]);
  const [delegationOptions, setDelegationOptions] = useState<DelegationOptionsResponse | null>(null);
  const [designations, setDesignations] = useState<Designation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  // Search & Filters
  const [searchQuery, setSearchQuery] = useState("");
  const [filterStatus, setFilterStatus] = useState<"ALL" | "ACTIVE" | "INACTIVE">("ALL");

  // Create Modal
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [createUsername, setCreateUsername] = useState("");
  const [createDisplayName, setCreateDisplayName] = useState("");
  const [createDesignationId, setCreateDesignationId] = useState("");
  const [createRoleIds, setCreateRoleIds] = useState<string[]>([]);
  const [createPermCodes, setCreatePermCodes] = useState<string[]>([]);
  const [createDeskIds, setCreateDeskIds] = useState<string[]>([]);
  const [createAllocations, setCreateAllocations] = useState<AllocationInput[]>([]);
  const [createLoading, setCreateLoading] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

  // Edit Modal
  const [editingAssistant, setEditingAssistant] = useState<OfficerAssistantDetail | null>(null);
  const [editDisplayName, setEditDisplayName] = useState("");
  const [editDesignationId, setEditDesignationId] = useState("");
  const [editRoleIds, setEditRoleIds] = useState<string[]>([]);
  const [editPermCodes, setEditPermCodes] = useState<string[]>([]);
  const [editDeskIds, setEditDeskIds] = useState<string[]>([]);
  const [editAllocations, setEditAllocations] = useState<AllocationInput[]>([]);
  const [editLoading, setEditLoading] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  // Temporary Credential Modal (One-time Display)
  const [showCredentialModal, setShowCredentialModal] = useState(false);
  const [credentialData, setCredentialData] = useState<{
    username: string;
    temporaryCredential: string;
    expiresAt?: string;
  } | null>(null);
  const [credentialAcknowledged, setCredentialAcknowledged] = useState(false);
  const [copiedNotice, setCopiedNotice] = useState(false);

  // Child Allocation Builder State
  const [builderParentAllocationId, setBuilderParentAllocationId] = useState("");

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [asstRes, optRes, desigRes] = await Promise.all([
        fetch("/api/officers/me/assistants", { credentials: "include" }),
        fetch("/api/officers/me/assistants/delegation-options", { credentials: "include" }),
        fetch("/api/admin/designations", { credentials: "include" }),
      ]);

      if (!asstRes.ok) {
        if (asstRes.status === 401) throw new Error("Session expired. Please log in again.");
        if (asstRes.status === 403) throw new Error("Access denied: Assistants.Manage permission required.");
        throw new Error(`Failed to load assistants list (status ${asstRes.status}).`);
      }

      const asstData = (await asstRes.json()) as OfficerAssistantSummary[];
      setAssistants(asstData);

      if (optRes.ok) {
        const optData = (await optRes.json()) as DelegationOptionsResponse;
        setDelegationOptions(optData);
        if (optData.allocations.length > 0 && !builderParentAllocationId) {
          setBuilderParentAllocationId(optData.allocations[0].id);
        }
      }

      if (desigRes.ok) {
        const desigData = (await desigRes.json()) as Designation[];
        setDesignations(desigData);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load assistant data.");
    } finally {
      setLoading(false);
    }
  }, [builderParentAllocationId]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const filteredAssistants = useMemo(() => {
    return assistants.filter((a) => {
      const matchesSearch =
        searchQuery.trim() === "" ||
        a.displayName.toLowerCase().includes(searchQuery.toLowerCase()) ||
        a.username.toLowerCase().includes(searchQuery.toLowerCase());

      const matchesStatus =
        filterStatus === "ALL" ||
        (filterStatus === "ACTIVE" && a.isActive) ||
        (filterStatus === "INACTIVE" && !a.isActive);

      return matchesSearch && matchesStatus;
    });
  }, [assistants, searchQuery, filterStatus]);

  const availableSupervisorPermissions = useMemo(() => {
    if (!delegationOptions) return [];
    return delegationOptions.permissions;
  }, [delegationOptions]);

  const openCreateModal = () => {
    setCreateUsername("");
    setCreateDisplayName("");
    setCreateDesignationId(designations.find((d) => d.code === "DEO")?.id || "");
    setCreateRoleIds([]);
    setCreatePermCodes([]);
    setCreateDeskIds([]);
    setCreateAllocations([]);
    setCreateError(null);
    setShowCreateModal(true);
  };

  const handleAddChildAllocation = (
    isEdit: boolean,
    parentAllocationId: string
  ) => {
    if (!delegationOptions || !parentAllocationId) return;
    const parent = delegationOptions.allocations.find((a) => a.id === parentAllocationId);
    if (!parent) return;

    const childInput: AllocationInput = {
      workDefinitionId: parent.workDefinitionId,
      validFrom: parent.validFrom,
      validTo: parent.validTo,
      workOrderReference: parent.workOrderReference,
      reason: `Delegated from ${parent.workName} (${parent.workOrderReference})`,
      scopes: parent.scopes,
      delegatedFromAllocationId: parent.id,
    };

    if (isEdit) {
      setEditAllocations((prev) => [...prev, childInput]);
    } else {
      setCreateAllocations((prev) => [...prev, childInput]);
    }
  };

  const handleCreateAssistant = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!createUsername.trim() || !createDisplayName.trim()) return;

    try {
      setCreateLoading(true);
      setCreateError(null);

      const assistantPayload: AssistantInput = {
        displayName: createDisplayName.trim(),
        designationId: createDesignationId || null,
        roleIds: createRoleIds,
        permissionCodes: createPermCodes,
        allocations: createAllocations,
        deskIds: createDeskIds,
      };

      const res = await fetch("/api/officers/me/assistants", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          username: createUsername.trim().toLowerCase(),
          assistant: assistantPayload,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          throw new Error("Username already exists or allocation conflict detected.");
        }
        if (res.status === 403) {
          throw new Error("Access denied: Ceiling violation or insufficient delegation permissions.");
        }
        const errJson = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to create assistant (status ${res.status}).`);
      }

      const data = (await res.json()) as {
        id: string;
        assistantRevision: number;
        temporaryCredential?: string;
        credentialExpiresAt?: string;
      };

      setShowCreateModal(false);
      await loadData();

      if (data.temporaryCredential) {
        setCredentialData({
          username: createUsername.trim().toLowerCase(),
          temporaryCredential: data.temporaryCredential,
          expiresAt: data.credentialExpiresAt,
        });
        setCredentialAcknowledged(false);
        setCopiedNotice(false);
        setShowCredentialModal(true);
      } else {
        setActionMessage(`Assistant ${createDisplayName.trim()} created successfully.`);
      }
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : "Error creating assistant.");
    } finally {
      setCreateLoading(false);
    }
  };

  const openEditModal = async (asst: OfficerAssistantSummary) => {
    try {
      setEditLoading(true);
      setEditError(null);
      const res = await fetch(`/api/officers/me/assistants/${asst.id}`, { credentials: "include" });
      if (!res.ok) {
        throw new Error(`Failed to load assistant details (status ${res.status}).`);
      }
      const detail = (await res.json()) as OfficerAssistantDetail;
      setEditingAssistant(detail);
      setEditDisplayName(detail.displayName);
      setEditDesignationId(detail.designationId || "");
      setEditRoleIds(detail.roleIds || []);
      setEditPermCodes(detail.permissionCodes || []);
      setEditDeskIds(detail.deskIds || []);
      const allocInputs: AllocationInput[] = (detail.allocations || []).map((a) => ({
        workDefinitionId: a.workDefinitionId,
        validFrom: a.validFrom,
        validTo: a.validTo,
        workOrderReference: a.workOrderReference,
        reason: a.reason,
        scopes: a.scopes,
        delegatedFromAllocationId: a.delegatedFromAllocationId || null,
      }));
      setEditAllocations(allocInputs);
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error opening assistant.");
    } finally {
      setEditLoading(false);
    }
  };

  const handleUpdateAssistant = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingAssistant || !editDisplayName.trim()) return;

    try {
      setEditLoading(true);
      setEditError(null);

      const assistantPayload: AssistantInput = {
        displayName: editDisplayName.trim(),
        designationId: editDesignationId || null,
        roleIds: editRoleIds,
        permissionCodes: editPermCodes,
        allocations: editAllocations,
        deskIds: editDeskIds,
      };

      const res = await fetch(`/api/officers/me/assistants/${editingAssistant.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          assistant: assistantPayload,
          expectedRevision: editingAssistant.assistantRevision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadData();
          throw new Error("Revision conflict: Assistant was modified concurrently. Data reloaded.");
        }
        if (res.status === 403) {
          throw new Error("Access denied: Delegation ceiling exceeded.");
        }
        const errJson = (await res.json().catch(() => null)) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to update assistant (status ${res.status}).`);
      }

      setEditingAssistant(null);
      setActionMessage(`Assistant ${editDisplayName.trim()} delegation updated successfully.`);
      await loadData();
    } catch (err) {
      setEditError(err instanceof Error ? err.message : "Error updating assistant delegation.");
    } finally {
      setEditLoading(false);
    }
  };

  const handleResetCredential = async (asst: OfficerAssistantSummary) => {
    if (!confirm(`Generate a new temporary credential for assistant ${asst.displayName} (@${asst.username})?\n\nThis will immediately invalidate all active sessions for this assistant.`)) {
      return;
    }

    try {
      setActionMessage(null);
      const res = await fetch(`/api/officers/me/assistants/${asst.id}/reset-credential`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          expectedRevision: asst.assistantRevision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadData();
          alert("Revision conflict: Assistant record changed concurrently. Directory refreshed.");
          return;
        }
        throw new Error(`Failed to reset credential (status ${res.status}).`);
      }

      const data = (await res.json()) as {
        id: string;
        assistantRevision: number;
        temporaryCredential?: string;
        credentialExpiresAt?: string;
      };

      await loadData();

      if (data.temporaryCredential) {
        setCredentialData({
          username: asst.username,
          temporaryCredential: data.temporaryCredential,
          expiresAt: data.credentialExpiresAt,
        });
        setCredentialAcknowledged(false);
        setCopiedNotice(false);
        setShowCredentialModal(true);
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error resetting assistant credential.");
    }
  };

  const handleRevokeAssistant = async (asst: OfficerAssistantSummary) => {
    if (!confirm(`Are you sure you want to REVOKE assistant ${asst.displayName}?\n\nRevocation permanently disables this assistant account and revokes all active child delegations. Old sessions will be invalidated immediately.`)) {
      return;
    }

    try {
      setActionMessage(null);
      const res = await fetch(`/api/officers/me/assistants/${asst.id}/revoke`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          expectedRevision: asst.assistantRevision,
        }),
        credentials: "include",
      });

      if (!res.ok) {
        if (res.status === 409) {
          await loadData();
          alert("Revision conflict: Stale assistant revision. Data refreshed.");
          return;
        }
        throw new Error(`Failed to revoke assistant (status ${res.status}).`);
      }

      setActionMessage(`Assistant ${asst.displayName} revoked and disabled.`);
      await loadData();
    } catch (err) {
      alert(err instanceof Error ? err.message : "Error revoking assistant.");
    }
  };

  const copyCredentialToClipboard = () => {
    if (!credentialData) return;
    void navigator.clipboard.writeText(credentialData.temporaryCredential);
    setCopiedNotice(true);
    setTimeout(() => setCopiedNotice(false), 2500);
  };

  if (loading) {
    return (
      <div className="rbac-admin-root">
        <div className="state"><strong>Loading officer assistant directory...</strong></div>
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
            Attached Data Entry Operators (DEOs) and Dealing Assistants executing delegated statutory work on behalf of the supervising officer with strict containment and live audit attribution.
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="primary-button" onClick={openCreateModal}>
            + Attach Assistant / DEO
          </button>
        </div>
      </div>

      {actionMessage && <div className="form-message">{actionMessage}</div>}
      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      {/* Audit Attribution & Containment Banner */}
      <div className="contract-notice-banner">
        <strong>Office Security & Delegation Invariants:</strong>
        <span>
          • <strong>Audit Attribution:</strong> Every operation performed by an assistant is permanently recorded as <em>"DEO X on behalf of Officer Y"</em>.<br />
          • <strong>Permission Ceiling:</strong> An assistant can <em>never</em> receive permissions or roles that the supervising officer does not currently hold.<br />
          • <strong>Work Allocation Containment:</strong> Child allocations must be derived from an active parent officer allocation.<br />
          • <strong>Temporary Credentials:</strong> Plaintext passwords are not stored. Temporary credentials are shown exactly once.
        </span>
      </div>

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search assistant display name or username..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={filterStatus}
          onChange={(e) => setFilterStatus(e.target.value as any)}
        >
          <option value="ALL">All Statuses</option>
          <option value="ACTIVE">Active Only</option>
          <option value="INACTIVE">Revoked / Inactive</option>
        </select>

        <span style={{ fontSize: "13px", color: "#64748b", marginLeft: "auto" }}>
          Showing <strong>{filteredAssistants.length}</strong> of {assistants.length} Attached Assistants
        </span>
      </div>

      {/* Assistant Cards List */}
      <div className="officer-grid">
        {filteredAssistants.map((asst) => (
          <div key={asst.id} className="officer-card">
            <div className="officer-card-header">
              <div>
                <span className="officer-name">{asst.displayName}</span>
                <span className="officer-username">@{asst.username}</span>
              </div>
              <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: "4px" }}>
                <span className={`status ${asst.isActive ? "success" : "warning"}`}>
                  {asst.isActive ? "Active Attached" : "Revoked"}
                </span>
                <span style={{ fontSize: "11px", color: "#94a3b8" }}>
                  Rev {asst.assistantRevision}
                </span>
              </div>
            </div>

            <div className="officer-meta-row">
              <span className="meta-label">Civil Designation:</span>
              <span className="meta-value">
                {designations.find((d) => d.id === asst.designationId)?.name || "Data Entry Operator (DEO)"}
              </span>
            </div>

            <div className="officer-meta-row">
              <span className="meta-label">Credential Status:</span>
              <span className="meta-value">
                {asst.mustChangePassword ? (
                  <span style={{ color: "#d97706", fontWeight: 600 }}>Temporary (Must Change Password)</span>
                ) : (
                  <span style={{ color: "#059669", fontWeight: 600 }}>Operational</span>
                )}
              </span>
            </div>

            <div className="officer-card-footer">
              <button
                type="button"
                className="secondary-button"
                style={{ padding: "4px 10px", fontSize: "12px" }}
                onClick={() => void openEditModal(asst)}
                disabled={!asst.isActive}
              >
                Edit Delegation
              </button>
              <button
                type="button"
                className="secondary-button"
                style={{ padding: "4px 10px", fontSize: "12px" }}
                onClick={() => void handleResetCredential(asst)}
                disabled={!asst.isActive}
              >
                Reset Credential
              </button>
              {asst.isActive && (
                <button
                  type="button"
                  className="quiet-button"
                  style={{ padding: "4px 8px", fontSize: "12px", color: "#b91c1c" }}
                  onClick={() => void handleRevokeAssistant(asst)}
                >
                  Revoke
                </button>
              )}
            </div>
          </div>
        ))}

        {filteredAssistants.length === 0 && (
          <div style={{ gridColumn: "1 / -1", textAlign: "center", padding: "40px", background: "#f8fafc", borderRadius: "8px", border: "1px dashed #cbd5e1" }}>
            <p style={{ color: "#64748b", margin: 0 }}>No attached assistants found matching query.</p>
          </div>
        )}
      </div>

      {/* Create Assistant Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "700px", maxHeight: "90vh", overflowY: "auto" }}>
            <h3>Attach New Assistant / DEO</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Provisions a dedicated login for an assistant operating on your behalf. Permissions and works must be bounded by your active allocations.
            </p>

            {createError && (
              <div className="state error" style={{ marginBottom: "12px" }}>
                <strong>Error:</strong> {createError}
              </div>
            )}

            <form onSubmit={handleCreateAssistant} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div className="form-row" style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
                <div className="form-group">
                  <label>Assistant Username *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. deo.rajesh"
                    value={createUsername}
                    onChange={(e) => setCreateUsername(e.target.value.toLowerCase())}
                  />
                  <small style={{ color: "#64748b" }}>Unique login handle for assistant.</small>
                </div>

                <div className="form-group">
                  <label>Full Display Name *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. Rajesh Kumar (DEO)"
                    value={createDisplayName}
                    onChange={(e) => setCreateDisplayName(e.target.value)}
                  />
                </div>
              </div>

              <div className="form-group">
                <label>Designation</label>
                <select
                  value={createDesignationId}
                  onChange={(e) => setCreateDesignationId(e.target.value)}
                >
                  <option value="">Select Designation...</option>
                  {designations.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              {/* Roles Selection */}
              <div className="form-group">
                <label>Delegated Role Bundle(s)</label>
                <div style={{ fontSize: "12px", color: "#64748b", marginBottom: "6px" }}>
                  Select from roles you currently hold. System Admin and privileged management roles cannot be delegated.
                </div>
                <div className="multi-select-grid" style={{ maxHeight: "130px" }}>
                  {delegationOptions?.roles.map((r) => (
                    <label key={r.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={createRoleIds.includes(r.id)}
                        onChange={(e) => {
                          if (e.target.checked) setCreateRoleIds((prev) => [...prev, r.id]);
                          else setCreateRoleIds((prev) => prev.filter((id) => id !== r.id));
                        }}
                      />
                      <span><strong>{r.name}</strong> ({r.code})</span>
                    </label>
                  ))}
                  {(!delegationOptions || delegationOptions.roles.length === 0) && (
                    <div style={{ color: "#94a3b8", fontSize: "12px", padding: "6px" }}>No delegable roles available.</div>
                  )}
                </div>
              </div>

              {/* Permission Ceiling Selection */}
              <div className="form-group">
                <label>Permission Ceiling (Restrictive Boundary)</label>
                <div style={{ fontSize: "12px", color: "#64748b", marginBottom: "6px" }}>
                  Must exist in chosen role and your own active permissions.
                </div>
                <div className="multi-select-grid" style={{ maxHeight: "130px" }}>
                  {availableSupervisorPermissions.map((p) => (
                    <label key={p.code} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={createPermCodes.includes(p.code)}
                        onChange={(e) => {
                          if (e.target.checked) setCreatePermCodes((prev) => [...prev, p.code]);
                          else setCreatePermCodes((prev) => prev.filter((c) => c !== p.code));
                        }}
                      />
                      <span><code>{p.code}</code> ({p.scopeMode})</span>
                    </label>
                  ))}
                  {availableSupervisorPermissions.length === 0 && (
                    <div style={{ color: "#94a3b8", fontSize: "12px", padding: "6px" }}>No delegable permissions found.</div>
                  )}
                </div>
              </div>

              {/* Desks Selection */}
              <div className="form-group">
                <label>Operational Desks (Subset of Supervising Officer Desks)</label>
                <div className="multi-select-grid" style={{ maxHeight: "100px" }}>
                  {delegationOptions?.desks.map((d) => (
                    <label key={d.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={createDeskIds.includes(d.id)}
                        onChange={(e) => {
                          if (e.target.checked) setCreateDeskIds((prev) => [...prev, d.id]);
                          else setCreateDeskIds((prev) => prev.filter((id) => id !== d.id));
                        }}
                      />
                      <span>{d.name} ({d.code})</span>
                    </label>
                  ))}
                  {(!delegationOptions || delegationOptions.desks.length === 0) && (
                    <div style={{ color: "#94a3b8", fontSize: "12px", padding: "6px" }}>No operational desks held.</div>
                  )}
                </div>
              </div>

              {/* Delegated Child Allocations */}
              <div className="form-group">
                <label>Delegated Work Allocations (Child Grants)</label>
                <div style={{ fontSize: "12px", color: "#64748b", marginBottom: "6px" }}>
                  Every child allocation must reference one of your active parent allocations.
                </div>

                {createAllocations.length > 0 && (
                  <div style={{ display: "flex", flexDirection: "column", gap: "6px", marginBottom: "10px" }}>
                    {createAllocations.map((alloc, idx) => {
                      const parent = delegationOptions?.allocations.find((a) => a.id === alloc.delegatedFromAllocationId);
                      return (
                        <div key={idx} style={{ background: "#f8fafc", padding: "8px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                          <div>
                            <strong>{parent?.workName || "Work Allocation"}</strong>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>
                              Order: {alloc.workOrderReference} | Scopes: {alloc.scopes.map((s) => s.kind).join(", ")}
                            </div>
                          </div>
                          <button
                            type="button"
                            className="quiet-button"
                            style={{ color: "#b91c1c", fontSize: "12px" }}
                            onClick={() => setCreateAllocations((prev) => prev.filter((_, i) => i !== idx))}
                          >
                            Remove
                          </button>
                        </div>
                      );
                    })}
                  </div>
                )}

                {delegationOptions && delegationOptions.allocations.length > 0 && (
                  <div style={{ background: "#f1f5f9", padding: "10px", borderRadius: "6px", display: "flex", flexDirection: "column", gap: "8px" }}>
                    <div style={{ fontWeight: 600, fontSize: "12px", color: "#334155" }}>Add Delegated Work Allocation:</div>
                    <select
                      value={builderParentAllocationId}
                      onChange={(e) => setBuilderParentAllocationId(e.target.value)}
                    >
                      {delegationOptions.allocations.map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.workName} (Ref: {a.workOrderReference}) - {a.scopes.map((s) => s.kind).join(", ")}
                        </option>
                      ))}
                    </select>

                    <button
                      type="button"
                      className="secondary-button"
                      style={{ alignSelf: "flex-start", padding: "4px 10px", fontSize: "12px" }}
                      onClick={() =>
                        handleAddChildAllocation(
                          false,
                          builderParentAllocationId
                        )
                      }
                    >
                      + Delegate Selected Allocation
                    </button>
                  </div>
                )}
              </div>

              <div className="contract-notice-banner" style={{ margin: "4px 0" }}>
                <strong>One-Time Credential Issuance:</strong>
                <span>
                  The backend will issue a 24-hour temporary credential upon creation. You must convey this credential to the assistant.
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
                  {createLoading ? "Attaching Assistant..." : "Attach Assistant & Issue Credential"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit Delegation Modal */}
      {editingAssistant && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "700px", maxHeight: "90vh", overflowY: "auto" }}>
            <h3>Edit Assistant Delegation</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Update display name, role ceiling, and delegated allocations for <strong>{editingAssistant.displayName}</strong> (@{editingAssistant.username}).
            </p>

            {editError && (
              <div className="state error" style={{ marginBottom: "12px" }}>
                <strong>Error:</strong> {editError}
              </div>
            )}

            <form onSubmit={handleUpdateAssistant} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div className="form-group">
                <label>Assistant Display Name *</label>
                <input
                  type="text"
                  required
                  value={editDisplayName}
                  onChange={(e) => setEditDisplayName(e.target.value)}
                />
              </div>

              <div className="form-group">
                <label>Designation</label>
                <select
                  value={editDesignationId}
                  onChange={(e) => setEditDesignationId(e.target.value)}
                >
                  <option value="">Select Designation...</option>
                  {designations.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} ({d.code})
                    </option>
                  ))}
                </select>
              </div>

              {/* Roles */}
              <div className="form-group">
                <label>Delegated Role Bundle(s)</label>
                <div className="multi-select-grid" style={{ maxHeight: "120px" }}>
                  {delegationOptions?.roles.map((r) => (
                    <label key={r.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={editRoleIds.includes(r.id)}
                        onChange={(e) => {
                          if (e.target.checked) setEditRoleIds((prev) => [...prev, r.id]);
                          else setEditRoleIds((prev) => prev.filter((id) => id !== r.id));
                        }}
                      />
                      <span><strong>{r.name}</strong> ({r.code})</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Permissions */}
              <div className="form-group">
                <label>Permission Ceiling</label>
                <div className="multi-select-grid" style={{ maxHeight: "120px" }}>
                  {availableSupervisorPermissions.map((p) => (
                    <label key={p.code} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={editPermCodes.includes(p.code)}
                        onChange={(e) => {
                          if (e.target.checked) setEditPermCodes((prev) => [...prev, p.code]);
                          else setEditPermCodes((prev) => prev.filter((c) => c !== p.code));
                        }}
                      />
                      <span><code>{p.code}</code> ({p.scopeMode})</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Desks */}
              <div className="form-group">
                <label>Operational Desks</label>
                <div className="multi-select-grid" style={{ maxHeight: "100px" }}>
                  {delegationOptions?.desks.map((d) => (
                    <label key={d.id} className="checkbox-item">
                      <input
                        type="checkbox"
                        checked={editDeskIds.includes(d.id)}
                        onChange={(e) => {
                          if (e.target.checked) setEditDeskIds((prev) => [...prev, d.id]);
                          else setEditDeskIds((prev) => prev.filter((id) => id !== d.id));
                        }}
                      />
                      <span>{d.name} ({d.code})</span>
                    </label>
                  ))}
                </div>
              </div>

              {/* Child Allocations */}
              <div className="form-group">
                <label>Delegated Work Allocations</label>
                {editAllocations.length > 0 && (
                  <div style={{ display: "flex", flexDirection: "column", gap: "6px", marginBottom: "10px" }}>
                    {editAllocations.map((alloc, idx) => {
                      const parent = delegationOptions?.allocations.find((a) => a.id === alloc.delegatedFromAllocationId);
                      return (
                        <div key={idx} style={{ background: "#f8fafc", padding: "8px 12px", borderRadius: "6px", border: "1px solid #cbd5e1", display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                          <div>
                            <strong>{parent?.workName || "Work Allocation"}</strong>
                            <div style={{ fontSize: "11px", color: "#64748b" }}>
                              Order: {alloc.workOrderReference} | Scopes: {alloc.scopes.map((s) => s.kind).join(", ")}
                            </div>
                          </div>
                          <button
                            type="button"
                            className="quiet-button"
                            style={{ color: "#b91c1c", fontSize: "12px" }}
                            onClick={() => setEditAllocations((prev) => prev.filter((_, i) => i !== idx))}
                          >
                            Remove
                          </button>
                        </div>
                      );
                    })}
                  </div>
                )}

                {delegationOptions && delegationOptions.allocations.length > 0 && (
                  <div style={{ background: "#f1f5f9", padding: "10px", borderRadius: "6px", display: "flex", flexDirection: "column", gap: "8px" }}>
                    <div style={{ fontWeight: 600, fontSize: "12px", color: "#334155" }}>Add Delegated Work Allocation:</div>
                    <select
                      value={builderParentAllocationId}
                      onChange={(e) => setBuilderParentAllocationId(e.target.value)}
                    >
                      {delegationOptions.allocations.map((a) => (
                        <option key={a.id} value={a.id}>
                          {a.workName} (Ref: {a.workOrderReference}) - {a.scopes.map((s) => s.kind).join(", ")}
                        </option>
                      ))}
                    </select>

                    <button
                      type="button"
                      className="secondary-button"
                      style={{ alignSelf: "flex-start", padding: "4px 10px", fontSize: "12px" }}
                      onClick={() =>
                        handleAddChildAllocation(
                          true,
                          builderParentAllocationId
                        )
                      }
                    >
                      + Delegate Selected Allocation
                    </button>
                  </div>
                )}
              </div>

              <div className="modal-actions" style={{ marginTop: "10px" }}>
                <button
                  type="button"
                  className="quiet-button"
                  onClick={() => setEditingAssistant(null)}
                  disabled={editLoading}
                >
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={editLoading}>
                  {editLoading ? "Updating..." : "Save Delegation Changes"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* One-time Temporary Credential Modal */}
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
                The assistant must replace this temporary password with a personal password of at least 12 characters upon first login before accessing operational modules.
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
                  <strong>I confirm:</strong> I have recorded this temporary credential and will communicate it securely to the assistant. I understand it cannot be recovered.
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
