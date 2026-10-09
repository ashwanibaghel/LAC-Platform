import React, { useState, useEffect, useCallback } from "react";
import { Link } from "react-router-dom";
import type {
  OfficeAccountDetail,
  HelperOptionsResponse,
  OfficeHelperInput,
  HelperAccessLevel,
} from "../admin/officeV3Types";
import "../admin/admin.css";

interface ConfirmationDialogState {
  title: string;
  employeeName: string;
  summaryItems?: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[];
  message?: string;
  warning?: string;
  confirmLabel: string;
  confirmTone?: "primary" | "danger" | "warning";
  onConfirm: () => Promise<void> | void;
}

export const MyHelpersView: React.FC = () => {
  const [helpers, setHelpers] = useState<OfficeAccountDetail[]>([]);
  const [options, setOptions] = useState<HelperOptionsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [actionLoading, setActionLoading] = useState(false);
  const [confirmationDialog, setConfirmationDialog] = useState<ConfirmationDialogState | null>(null);

  // Modals state
  const [showAddModal, setShowAddModal] = useState(false);
  const [editingHelper, setEditingHelper] = useState<OfficeAccountDetail | null>(null);

  // Form states - Add Helper
  const [newUsername, setNewUsername] = useState("");
  const [newFullName, setNewFullName] = useState("");
  const [designationChoice, setDesignationChoice] = useState<"DEO" | "OTHER">("DEO");
  const [customDesignation, setCustomDesignation] = useState("");
  const [selectedDeskId, setSelectedDeskId] = useState("");
  const [selectedAccess, setSelectedAccess] = useState<HelperAccessLevel>("ReadOnly");
  const [formError, setFormError] = useState<string | null>(null);

  // Form states - Edit Helper
  const [editFullName, setEditFullName] = useState("");
  const [editDesignationChoice, setEditDesignationChoice] = useState<"DEO" | "OTHER">("DEO");
  const [editCustomDesignation, setEditCustomDesignation] = useState("");
  const [editDeskId, setEditDeskId] = useState("");
  const [editAccess, setEditAccess] = useState<HelperAccessLevel>("ReadOnly");
  const [editError, setEditError] = useState<string | null>(null);

  // Credential Modal State
  const [showCredentialModal, setShowCredentialModal] = useState(false);
  const [credentialData, setCredentialData] = useState<{
    username: string;
    temporaryCredential: string;
    expiresAt?: string | null;
  } | null>(null);
  const [credentialAcknowledged, setCredentialAcknowledged] = useState(false);
  const [copiedNotice, setCopiedNotice] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [helpersRes, optionsRes] = await Promise.all([
        fetch("/api/office/me/helpers", { credentials: "include" }),
        fetch("/api/office/me/helpers/options", { credentials: "include" }),
      ]);

      if (helpersRes.status === 403) {
        setError("You do not have permission to manage helpers.");
        setLoading(false);
        return;
      }

      if (!helpersRes.ok || !optionsRes.ok) {
        throw new Error("Unable to load assistants data.");
      }

      const helpersData = (await helpersRes.json()) as OfficeAccountDetail[];
      const optionsData = (await optionsRes.json()) as HelperOptionsResponse;

      setHelpers(helpersData);
      setOptions(optionsData);
    } catch (err: any) {
      setError(err.message || "Failed to load assistants.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const openAddHelperModal = () => {
    setNewUsername("");
    setNewFullName("");
    setDesignationChoice("DEO");
    setCustomDesignation("");
    setSelectedDeskId(options?.desks[0]?.id || "");
    setSelectedAccess("ReadOnly");
    setFormError(null);
    setShowAddModal(true);
  };

  const handleAddHelperSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const username = newUsername.trim();
    const fullName = newFullName.trim();

    if (!username) {
      setFormError("Official Username is required.");
      return;
    }
    if (!fullName) {
      setFormError("Full Name is required.");
      return;
    }

    if (designationChoice === "OTHER" && !customDesignation.trim()) {
      setFormError("Please enter a custom designation title.");
      return;
    }

    const payload: { username: string; helper: OfficeHelperInput } = {
      username,
      helper: {
        fullName,
        designationId: designationChoice === "DEO" ? options?.designation.id || null : null,
        customDesignation: designationChoice === "OTHER" ? customDesignation.trim() : null,
        deskId: selectedDeskId || null,
        access: selectedAccess,
      },
    };

    const desigLabel = designationChoice === "DEO" ? "Data Entry Operator (DEO)" : `${customDesignation.trim()} (Custom)`;
    const deskLabel = options?.desks.find((d) => d.id === selectedDeskId)?.name || "No Specific Seat";
    const accessLabel = selectedAccess === "ReadWrite" ? "Read + Write" : selectedAccess === "ReadOnly" ? "Read Only" : "None";

    setConfirmationDialog({
      title: "Confirm Attached Assistant",
      employeeName: `${fullName} (${username})`,
      summaryItems: [
        { label: "Designation", value: desigLabel },
        { label: "Access Level", value: accessLabel },
        { label: "Assigned Seat", value: deskLabel },
      ],
      confirmLabel: "Confirm & Create Assistant",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch("/api/office/me/helpers", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Failed to create helper (HTTP ${res.status})`);
          }

          const created = await res.json();
          setShowAddModal(false);
          setConfirmationDialog(null);
          setCredentialData({
            username: created.account?.username || username,
            temporaryCredential: created.temporaryCredential,
            expiresAt: created.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          setFormError(err.message || "Failed to create helper.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  const openEditHelperModal = (helper: OfficeAccountDetail) => {
    setEditingHelper(helper);
    setEditFullName(helper.fullName || "");
    if (helper.customDesignation) {
      setEditDesignationChoice("OTHER");
      setEditCustomDesignation(helper.customDesignation);
    } else {
      setEditDesignationChoice("DEO");
      setEditCustomDesignation("");
    }
    setEditDeskId(helper.deskIds[0] || "");

    // Infer access level
    const codes = helper.helperPermissionCodes || [];
    const isReadWrite = codes.some((c) => !c.endsWith(".View"));
    setEditAccess(codes.length === 0 ? "None" : isReadWrite ? "ReadWrite" : "ReadOnly");
    setEditError(null);
  };

  const handleEditHelperSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingHelper) return;
    setEditError(null);

    const fullName = editFullName.trim();
    if (!fullName) {
      setEditError("Full Name is required.");
      return;
    }
    if (editDesignationChoice === "OTHER" && !editCustomDesignation.trim()) {
      setEditError("Please enter a custom designation title.");
      return;
    }

    const payload = {
      helper: {
        fullName,
        designationId: editDesignationChoice === "DEO" ? options?.designation.id || null : null,
        customDesignation: editDesignationChoice === "OTHER" ? editCustomDesignation.trim() : null,
        deskId: editDeskId || null,
        access: editAccess,
      },
      expectedRevision: editingHelper.assistantRevision,
    };

    const oldCodes = editingHelper.helperPermissionCodes || [];
    const oldIsWrite = oldCodes.some((c) => !c.endsWith(".View"));
    const oldAccessLabel = oldCodes.length === 0 ? "None" : oldIsWrite ? "Read + Write" : "Read Only";
    const newAccessLabel = editAccess === "ReadWrite" ? "Read + Write" : editAccess === "ReadOnly" ? "Read Only" : "None";

    const oldDesk = options?.desks.find((d) => d.id === editingHelper.deskIds[0])?.name || "None";
    const newDesk = options?.desks.find((d) => d.id === editDeskId)?.name || "None";

    const diffItems: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[] = [];
    if (fullName !== editingHelper.fullName) {
      diffItems.push({ label: "Name", value: `${editingHelper.fullName} → ${fullName}` });
    }
    const oldDesig = editingHelper.customDesignation
      ? `${editingHelper.customDesignation} (Custom)`
      : editingHelper.effectiveDesignation || "Data Entry Operator (DEO)";
    const newDesig = editDesignationChoice === "OTHER"
      ? `${editCustomDesignation.trim()} (Custom)`
      : options?.designation?.name || "Data Entry Operator (DEO)";
    if (oldDesig !== newDesig) {
      diffItems.push({ label: "Designation", value: `${oldDesig} → ${newDesig}` });
    }
    if (oldAccessLabel !== newAccessLabel) {
      diffItems.push({ label: "Access Level", value: `${oldAccessLabel} → ${newAccessLabel}` });
    }
    if (oldDesk !== newDesk) {
      diffItems.push({ label: "Seat / Desk", value: `${oldDesk} → ${newDesk}` });
    }
    if (diffItems.length === 0) {
      diffItems.push({ label: "Notice", value: "No operational access changes detected." });
    }

    setConfirmationDialog({
      title: "Confirm Helper Permission Changes",
      employeeName: `${fullName} (${editingHelper.username})`,
      summaryItems: diffItems,
      warning: "Saving changes will invalidate current active sessions for this assistant.",
      confirmLabel: "Confirm Changes",
      confirmTone: "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/me/helpers/${editingHelper.id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
            credentials: "include",
          });

          if (res.status === 409) {
            setEditError("This helper account changed elsewhere. Latest details have been reloaded.");
            setConfirmationDialog(null);
            await loadData();
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Failed to update helper (HTTP ${res.status})`);
          }

          setEditingHelper(null);
          setConfirmationDialog(null);
          setActionMessage("Helper access updated. Active login sessions for this assistant were rotated.");
          setTimeout(() => setActionMessage(null), 6000);
          void loadData();
        } catch (err: any) {
          setEditError(err.message || "Failed to update helper.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  const handleResetCredential = (helper: OfficeAccountDetail) => {
    setConfirmationDialog({
      title: "Confirm Password Reset",
      employeeName: `${helper.fullName} (${helper.username})`,
      message: `Reset password for assistant ${helper.fullName}? This will invalidate existing sessions and issue a new 24-hour temporary credential.`,
      warning: "The assistant will be required to set a permanent password upon first login with the new credential.",
      confirmLabel: "Confirm Password Reset",
      confirmTone: "warning",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/me/helpers/${helper.id}/reset-credential`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ expectedRevision: helper.revision }),
            credentials: "include",
          });

          if (res.status === 409) {
            alert("This account was updated concurrently. Reloading latest records.");
            setConfirmationDialog(null);
            await loadData();
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Reset failed (HTTP ${res.status})`);
          }

          const data = await res.json();
          setConfirmationDialog(null);
          setCredentialData({
            username: helper.username,
            temporaryCredential: data.temporaryCredential,
            expiresAt: data.credentialExpiresAt,
          });
          setCredentialAcknowledged(false);
          setShowCredentialModal(true);
          void loadData();
        } catch (err: any) {
          alert(err.message || "Credential reset failed.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
  };

  const handleToggleStatus = (helper: OfficeAccountDetail) => {
    const isDeactivating = helper.isActive;
    setConfirmationDialog({
      title: isDeactivating ? "Confirm Account Deactivation" : "Confirm Account Activation",
      employeeName: `${helper.fullName} (${helper.username})`,
      message: isDeactivating
        ? `Disable assistant ${helper.fullName}? They will immediately lose access to the LAC Platform.`
        : `Enable assistant ${helper.fullName}? They will regain access to the LAC Platform.`,
      warning: isDeactivating ? "All active sessions will be terminated immediately." : undefined,
      confirmLabel: isDeactivating ? "Confirm Deactivation" : "Confirm Activation",
      confirmTone: isDeactivating ? "danger" : "primary",
      onConfirm: async () => {
        setActionLoading(true);
        try {
          const res = await fetch(`/api/office/me/helpers/${helper.id}/toggle-status`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ expectedRevision: helper.revision }),
            credentials: "include",
          });

          if (res.status === 409) {
            alert("This account was updated concurrently. Reloading latest records.");
            setConfirmationDialog(null);
            await loadData();
            return;
          }

          if (!res.ok) {
            const err = await res.json().catch(() => ({}));
            throw new Error(err.message || `Status update failed (HTTP ${res.status})`);
          }

          setConfirmationDialog(null);
          setActionMessage(`Assistant ${helper.username} is now ${helper.isActive ? "inactive" : "active"}.`);
          setTimeout(() => setActionMessage(null), 4000);
          void loadData();
        } catch (err: any) {
          alert(err.message || "Failed to update assistant status.");
          setConfirmationDialog(null);
        } finally {
          setActionLoading(false);
        }
      },
    });
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

  return (
    <div className="rbac-admin-root" style={{ maxWidth: "1200px" }}>
      {/* Breadcrumb Hierarchy */}
      <nav aria-label="Breadcrumb" className="breadcrumbs">
        <Link to="/">Home</Link>
        <i>/</i>
        <span>My Helpers</span>
      </nav>

      {/* Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h1>My Attached Helpers</h1>
          <p>
            Delegate operational duties and record processing to Data Entry Operators (DEO) or personal assistants working under your authority.
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="rbac-btn-primary" onClick={openAddHelperModal} disabled={loading}>
            + Add Helper
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

      {/* Content */}
      {loading ? (
        <div style={{ padding: "40px", textAlign: "center", color: "#64748b" }}>
          Loading attached assistants…
        </div>
      ) : helpers.length === 0 ? (
        <div style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", padding: "48px 24px", textAlign: "center" }}>
          <h3 style={{ margin: "0 0 8px 0", color: "#1e293b", fontSize: "18px" }}>No Helpers Attached</h3>
          <p style={{ margin: "0 0 20px 0", color: "#64748b", fontSize: "14px", maxWidth: "480px", marginInline: "auto" }}>
            You haven't added any assistants yet. Attached helpers operate strictly within your own designated seat, records, and authority.
          </p>
          <button className="rbac-btn-primary" onClick={openAddHelperModal}>
            + Add First Helper
          </button>
        </div>
      ) : (
        <div className="rbac-table-container" style={{ background: "#ffffff", border: "1px solid #e2e8f0", borderRadius: "8px", overflow: "hidden" }}>
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Assistant Name & Username</th>
                <th>Designation</th>
                <th>Seat / Desk</th>
                <th>Operational Access</th>
                <th>Status</th>
                <th style={{ textAlign: "right" }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {helpers.map((h) => {
                const deskName = options?.desks.find((d) => h.deskIds.includes(d.id))?.name || (h.deskIds.length > 0 ? "Assigned Desk" : "No Seat");
                const codes = h.helperPermissionCodes || [];
                const isReadWrite = codes.some((c) => !c.endsWith(".View"));
                const accessLabel = codes.length === 0 ? "None" : isReadWrite ? "Read + Write" : "Read Only";

                return (
                  <tr key={h.id}>
                    <td>
                      <div style={{ fontWeight: 600, color: "#0f172a" }}>{h.fullName}</div>
                      <div style={{ fontSize: "12px", color: "#64748b", fontFamily: "monospace" }}>{h.username}</div>
                    </td>
                    <td>
                      <span className="rbac-badge rbac-badge-neutral">
                        {h.effectiveDesignation || "Data Entry Operator"}
                      </span>
                    </td>
                    <td>
                      <span style={{ fontSize: "13px", color: "#334155" }}>{deskName}</span>
                    </td>
                    <td>
                      <span className={`rbac-badge ${accessLabel === "Read + Write" ? "rbac-badge-primary" : accessLabel === "Read Only" ? "rbac-badge-outline" : "rbac-badge-neutral"}`}>
                        {accessLabel}
                      </span>
                      <div style={{ fontSize: "11px", color: "#64748b", marginTop: "2px" }}>
                        {accessLabel === "Read + Write" ? "Can update delegated records" : accessLabel === "Read Only" ? "View delegated records only" : "No operational grants"}
                      </div>
                    </td>
                    <td>
                      <span className={`rbac-status-dot ${h.isActive ? "active" : "inactive"}`} />
                      <span style={{ fontSize: "13px", color: h.isActive ? "#166534" : "#94a3b8" }}>
                        {h.isActive ? "Active" : "Inactive"}
                      </span>
                    </td>
                    <td style={{ textAlign: "right" }}>
                      <div className="rbac-table-actions">
                        <button className="rbac-btn-sm rbac-btn-outline" onClick={() => openEditHelperModal(h)} title="Edit Assistant Access">
                          Edit
                        </button>
                        <button className="rbac-btn-sm rbac-btn-outline" onClick={() => void handleResetCredential(h)} title="Generate New Password">
                          Reset Password
                        </button>
                        <button
                          className={`rbac-btn-sm ${h.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`}
                          onClick={() => void handleToggleStatus(h)}
                          title={h.isActive ? "Deactivate Assistant" : "Activate Assistant"}
                        >
                          {h.isActive ? "Deactivate" : "Activate"}
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

      {/* Add Helper Modal */}
      {showAddModal && (
        <div className="rbac-modal-backdrop" onClick={() => setShowAddModal(false)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "560px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Add Attached Helper</h2>
                <p>Helper will be bound to your active desk and operational ceiling.</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setShowAddModal(false)}>&times;</button>
            </div>

            <form onSubmit={handleAddHelperSubmit}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {formError && (
                  <div className="rbac-banner-error" style={{ padding: "8px 12px", borderRadius: "6px", background: "#fef2f2", color: "#991b1b", fontSize: "13px" }}>
                    ⚠️ {formError}
                  </div>
                )}

                <div className="rbac-form-group">
                  <label>Official Username *</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. deo.rajesh"
                    value={newUsername}
                    onChange={(e) => setNewUsername(e.target.value)}
                    autoFocus
                  />
                  <small style={{ color: "#64748b", fontSize: "12px" }}>Must be unique across the office platform.</small>
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

                <div className="rbac-form-group">
                  <label>Designation *</label>
                  <div style={{ display: "flex", gap: "16px", marginBottom: "8px" }}>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                      <input
                        type="radio"
                        name="addDesignationChoice"
                        checked={designationChoice === "DEO"}
                        onChange={() => setDesignationChoice("DEO")}
                      />
                      Data Entry Operator
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                      <input
                        type="radio"
                        name="addDesignationChoice"
                        checked={designationChoice === "OTHER"}
                        onChange={() => setDesignationChoice("OTHER")}
                      />
                      Other / Custom Title
                    </label>
                  </div>
                  {designationChoice === "OTHER" && (
                    <input
                      type="text"
                      required
                      placeholder="Enter designation (e.g. Personal Assistant)"
                      value={customDesignation}
                      onChange={(e) => setCustomDesignation(e.target.value)}
                    />
                  )}
                </div>

                <div className="rbac-form-group">
                  <label>Assigned Seat / Desk</label>
                  <select value={selectedDeskId} onChange={(e) => setSelectedDeskId(e.target.value)}>
                    <option value="">-- No Specific Seat --</option>
                    {options?.desks.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </select>
                  <small style={{ color: "#64748b", fontSize: "12px" }}>Only desks assigned to you are available for helpers.</small>
                </div>

                <div className="rbac-form-group">
                  <label>Operational Access Level *</label>
                  <div style={{ display: "flex", flexDirection: "column", gap: "8px", marginTop: "4px" }}>
                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: selectedAccess === "ReadOnly" ? "#f0f9ff" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="addAccess"
                        value="ReadOnly"
                        checked={selectedAccess === "ReadOnly"}
                        onChange={() => setSelectedAccess("ReadOnly")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>Read Only</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Helper may read delegated work but cannot change records.</div>
                      </div>
                    </label>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: selectedAccess === "ReadWrite" ? "#f0fdf4" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="addAccess"
                        value="ReadWrite"
                        checked={selectedAccess === "ReadWrite"}
                        onChange={() => setSelectedAccess("ReadWrite")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>Read + Write</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Helper may work on delegated records within your own authority.</div>
                      </div>
                    </label>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: selectedAccess === "None" ? "#f8fafc" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="addAccess"
                        value="None"
                        checked={selectedAccess === "None"}
                        onChange={() => setSelectedAccess("None")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>None</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Account only; no operational permissions granted.</div>
                      </div>
                    </label>
                  </div>
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button type="button" className="rbac-btn-outline" onClick={() => setShowAddModal(false)} disabled={actionLoading}>
                  Cancel
                </button>
                <button type="submit" className="rbac-btn-primary" disabled={actionLoading}>
                  {actionLoading ? "Creating…" : "Create Helper Account"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Edit Helper Modal */}
      {editingHelper && (
        <div className="rbac-modal-backdrop" onClick={() => setEditingHelper(null)}>
          <div className="rbac-modal-dialog" style={{ maxWidth: "560px" }} onClick={(e) => e.stopPropagation()}>
            <div className="rbac-modal-header">
              <div>
                <h2>Edit Helper — {editingHelper.username}</h2>
                <p>Update assigned desk, title, or operational authority ceiling.</p>
              </div>
              <button className="rbac-modal-close-btn" onClick={() => setEditingHelper(null)}>&times;</button>
            </div>

            <form onSubmit={handleEditHelperSubmit}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
                {editError && (
                  <div className="rbac-banner-error" style={{ padding: "8px 12px", borderRadius: "6px", background: "#fef2f2", color: "#991b1b", fontSize: "13px" }}>
                    ⚠️ {editError}
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
                  <label>Designation *</label>
                  <div style={{ display: "flex", gap: "16px", marginBottom: "8px" }}>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                      <input
                        type="radio"
                        name="editDesignationChoice"
                        checked={editDesignationChoice === "DEO"}
                        onChange={() => setEditDesignationChoice("DEO")}
                      />
                      Data Entry Operator
                    </label>
                    <label style={{ display: "flex", alignItems: "center", gap: "6px", cursor: "pointer", fontSize: "13px" }}>
                      <input
                        type="radio"
                        name="editDesignationChoice"
                        checked={editDesignationChoice === "OTHER"}
                        onChange={() => setEditDesignationChoice("OTHER")}
                      />
                      Other / Custom Title
                    </label>
                  </div>
                  {editDesignationChoice === "OTHER" && (
                    <input
                      type="text"
                      required
                      placeholder="Enter designation (e.g. Personal Assistant)"
                      value={editCustomDesignation}
                      onChange={(e) => setEditCustomDesignation(e.target.value)}
                    />
                  )}
                </div>

                <div className="rbac-form-group">
                  <label>Assigned Seat / Desk</label>
                  <select value={editDeskId} onChange={(e) => setEditDeskId(e.target.value)}>
                    <option value="">-- No Specific Seat --</option>
                    {options?.desks.map((d) => (
                      <option key={d.id} value={d.id}>
                        {d.name}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="rbac-form-group">
                  <label>Operational Access Level *</label>
                  <div style={{ display: "flex", flexDirection: "column", gap: "8px", marginTop: "4px" }}>
                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: editAccess === "ReadOnly" ? "#f0f9ff" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="editAccess"
                        value="ReadOnly"
                        checked={editAccess === "ReadOnly"}
                        onChange={() => setEditAccess("ReadOnly")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>Read Only</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Helper may read delegated work but cannot change records.</div>
                      </div>
                    </label>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: editAccess === "ReadWrite" ? "#f0fdf4" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="editAccess"
                        value="ReadWrite"
                        checked={editAccess === "ReadWrite"}
                        onChange={() => setEditAccess("ReadWrite")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>Read + Write</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>Helper may work on delegated records within your own authority.</div>
                      </div>
                    </label>

                    <label style={{ display: "flex", alignItems: "flex-start", gap: "8px", padding: "8px 12px", border: "1px solid #e2e8f0", borderRadius: "6px", cursor: "pointer", background: editAccess === "None" ? "#f8fafc" : "#ffffff" }}>
                      <input
                        type="radio"
                        name="editAccess"
                        value="None"
                        checked={editAccess === "None"}
                        onChange={() => setEditAccess("None")}
                        style={{ marginTop: "3px" }}
                      />
                      <div>
                        <strong style={{ fontSize: "13px", color: "#0f172a" }}>None</strong>
                        <div style={{ fontSize: "12px", color: "#64748b" }}>No operational permissions granted.</div>
                      </div>
                    </label>
                  </div>
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button type="button" className="rbac-btn-outline" onClick={() => setEditingHelper(null)} disabled={actionLoading}>
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

      {/* Confirmation Dialog Modal */}
      {confirmationDialog && (
        <div className="rbac-modal-backdrop" style={{ zIndex: 1200 }}>
          <div className="rbac-confirm-shell" onClick={(e) => e.stopPropagation()}>
            <div className="rbac-confirm-header">
              <h3>{confirmationDialog.title}</h3>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setConfirmationDialog(null)}
                aria-label="Cancel"
              >
                &times;
              </button>
            </div>

            <div className="rbac-confirm-body">
              <div>
                <span style={{ color: "#64748b", fontSize: "12px", textTransform: "uppercase", fontWeight: 700 }}>Employee</span>
                <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                  {confirmationDialog.employeeName}
                </div>
              </div>

              {confirmationDialog.message && (
                <div style={{ lineHeight: 1.45 }}>
                  {confirmationDialog.message}
                </div>
              )}

              {confirmationDialog.summaryItems && confirmationDialog.summaryItems.length > 0 && (
                <div className="rbac-confirm-diff-list">
                  {confirmationDialog.summaryItems.map((item, idx) => (
                    <div key={idx} className="rbac-diff-row">
                      <span style={{ width: "140px", color: "#64748b", fontWeight: 600, flexShrink: 0 }}>
                        {item.label}:
                      </span>
                      <span className={item.isAddition ? "rbac-diff-add" : item.isRemoval ? "rbac-diff-remove" : ""} style={{ flex: 1 }}>
                        {item.value}
                      </span>
                    </div>
                  ))}
                </div>
              )}

              {confirmationDialog.warning && (
                <div style={{ padding: "10px 14px", borderRadius: "8px", background: "#fffbeb", border: "1px solid #fef3c7", color: "#92400e", fontSize: "12.5px" }}>
                  ⚠️ {confirmationDialog.warning}
                </div>
              )}
            </div>

            <div className="rbac-confirm-footer">
              <button
                type="button"
                className="rbac-btn-outline"
                onClick={() => setConfirmationDialog(null)}
                disabled={actionLoading}
              >
                Cancel
              </button>
              <button
                type="button"
                className={`rbac-btn-primary ${confirmationDialog.confirmTone === "danger" ? "rbac-btn-danger" : ""}`}
                style={confirmationDialog.confirmTone === "warning" ? { background: "#d97706", borderColor: "#b45309" } : {}}
                onClick={() => void confirmationDialog.onConfirm()}
                disabled={actionLoading}
              >
                {actionLoading ? "Processing…" : confirmationDialog.confirmLabel}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
