import React, { useState, useEffect, useCallback } from "react";
import type { DeskItem, Workstream } from "./types";
import "./admin.css";

interface ConfirmationDialogState {
  title: string;
  deskName: string;
  summaryItems?: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[];
  message?: string;
  warning?: string;
  confirmLabel: string;
  confirmTone?: "primary" | "danger" | "warning";
  onConfirm: () => Promise<void> | void;
}

export const OfficeDesksAdmin: React.FC = () => {
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [confirmationDialog, setConfirmationDialog] = useState<ConfirmationDialogState | null>(null);

  // New / Edit desk modal
  const [showDeskModal, setShowDeskModal] = useState(false);
  const [editingDesk, setEditingDesk] = useState<DeskItem | null>(null);
  const [deskCode, setDeskCode] = useState("");
  const [deskName, setDeskName] = useState("");
  const [deskDesc, setDeskDesc] = useState("");
  const [deskWorkstreamId, setDeskWorkstreamId] = useState("");
  const [modalLoading, setModalLoading] = useState(false);
  const [modalError, setModalError] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);

      // Load workstreams and fallback desks from account-options
      const optRes = await fetch("/api/admin/account-options", { credentials: "include" });
      let optDesks: DeskItem[] = [];
      if (optRes.ok) {
        const optData = await optRes.json();
        setWorkstreams((optData.workstreams || []) as Workstream[]);
        optDesks = (optData.desks || []).map((d: any) => ({
          id: d.id,
          code: d.code,
          name: d.name,
          workstreamId: d.workstreamId || null,
          workstreamName: optData.workstreams?.find((w: any) => w.id === d.workstreamId)?.name || null,
          isActive: true,
          activeMembersCount: 0,
        }));
      }

      // Try loading full catalog from /api/admin/desks
      const desksRes = await fetch("/api/admin/desks", { credentials: "include" });
      if (desksRes.ok) {
        const fullDesks = (await desksRes.json()) as DeskItem[];
        setDesks(fullDesks);
      } else if (optDesks.length > 0) {
        setDesks(optDesks);
      } else {
        throw new Error("Unable to load office desks.");
      }
    } catch (err: any) {
      setError(err instanceof Error ? err.message : "Failed to load office desks.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const openNewDeskModal = () => {
    setEditingDesk(null);
    setDeskCode("");
    setDeskName("");
    setDeskDesc("");
    setDeskWorkstreamId("");
    setModalError(null);
    setShowDeskModal(true);
  };

  const openEditDeskModal = (d: DeskItem) => {
    setEditingDesk(d);
    setDeskCode(d.code);
    setDeskName(d.name);
    setDeskDesc(d.description || "");
    setDeskWorkstreamId(d.workstreamId || "");
    setModalError(null);
    setShowDeskModal(true);
  };

  const handleSaveDesk = (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);

    const name = deskName.trim();
    if (!name) {
      setModalError("Desk Name is required.");
      return;
    }

    if (!editingDesk) {
      const code = deskCode.trim().toUpperCase();
      if (!code) {
        setModalError("Desk Code is required.");
        return;
      }

      const wsName = workstreams.find((w) => w.id === deskWorkstreamId)?.name || "General / Multi-branch";
      const summaryItems = [
        { label: "Desk Code", value: code, isAddition: true },
        { label: "Desk Name", value: name, isAddition: true },
        { label: "Branch / Workstream", value: wsName },
        { label: "Description", value: deskDesc.trim() || "(None)" },
      ];

      setConfirmationDialog({
        title: "Review & Create Office Desk",
        deskName: `${name} (${code})`,
        summaryItems,
        message: `Create new office desk seat "${name}"?`,
        confirmLabel: "Confirm & Create Desk",
        confirmTone: "primary",
        onConfirm: async () => {
          setModalLoading(true);
          try {
            const response = await fetch("/api/admin/desks", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({
                code,
                name,
                description: deskDesc.trim() || null,
                workstreamId: deskWorkstreamId || null,
                purpose: "General",
              }),
              credentials: "include",
            });
            if (!response.ok) {
              const err = await response.json().catch(() => ({}));
              throw new Error(err?.message || "Failed to create office desk.");
            }
            setConfirmationDialog(null);
            setShowDeskModal(false);
            setActionMessage(`Created office desk "${name}".`);
            setTimeout(() => setActionMessage(null), 5000);
            await loadData();
          } catch (err: any) {
            setConfirmationDialog(null);
            setModalError(err instanceof Error ? err.message : "Error saving office desk.");
          } finally {
            setModalLoading(false);
          }
        },
      });
    } else {
      // Editing existing desk
      const diffItems: { label: string; value: string; isAddition?: boolean; isRemoval?: boolean }[] = [];
      if (name !== editingDesk.name) {
        diffItems.push({ label: "Desk Name", value: `${editingDesk.name} → ${name}` });
      }
      const oldWs = workstreams.find((w) => w.id === editingDesk.workstreamId)?.name || "General / Multi-branch";
      const newWs = workstreams.find((w) => w.id === deskWorkstreamId)?.name || "General / Multi-branch";
      if (oldWs !== newWs) {
        diffItems.push({ label: "Branch / Workstream", value: `${oldWs} → ${newWs}` });
      }
      const oldDesc = editingDesk.description || "(None)";
      const newDesc = deskDesc.trim() || "(None)";
      if (oldDesc !== newDesc) {
        diffItems.push({ label: "Description", value: `${oldDesc} → ${newDesc}` });
      }
      if (diffItems.length === 0) {
        diffItems.push({ label: "Notice", value: "No field modifications detected." });
      }

      setConfirmationDialog({
        title: "Confirm Desk Changes",
        deskName: `${editingDesk.name} (${editingDesk.code})`,
        summaryItems: diffItems,
        message: `Apply updates to office desk "${editingDesk.name}"?`,
        confirmLabel: "Confirm Changes",
        confirmTone: "primary",
        onConfirm: async () => {
          setModalLoading(true);
          try {
            const response = await fetch(`/api/admin/desks/${editingDesk.id}`, {
              method: "PUT",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify({
                name,
                description: deskDesc.trim() || null,
                workstreamId: deskWorkstreamId || null,
              }),
              credentials: "include",
            });
            if (!response.ok) {
              const err = await response.json().catch(() => ({}));
              throw new Error(err?.message || "Failed to update office desk.");
            }
            setConfirmationDialog(null);
            setShowDeskModal(false);
            setActionMessage(`Updated office desk "${name}".`);
            setTimeout(() => setActionMessage(null), 5000);
            await loadData();
          } catch (err: any) {
            setConfirmationDialog(null);
            setModalError(err instanceof Error ? err.message : "Error saving office desk.");
          } finally {
            setModalLoading(false);
          }
        },
      });
    }
  };

  const handleToggleDeskStatus = (d: DeskItem) => {
    const isDeactivating = d.isActive;
    setConfirmationDialog({
      title: isDeactivating ? "Confirm Desk Deactivation" : "Confirm Desk Activation",
      deskName: `${d.name} (${d.code})`,
      message: isDeactivating
        ? `Are you sure you want to deactivate desk "${d.name}"? Active officers assigned to this seat will retain their account, but this seat will be marked inactive.`
        : `Are you sure you want to activate desk "${d.name}"? This seat will become available for officer assignments.`,
      warning: isDeactivating ? "Officers currently seated here may need alternative seat assignment." : undefined,
      confirmLabel: isDeactivating ? "Confirm Deactivation" : "Confirm Activation",
      confirmTone: isDeactivating ? "danger" : "primary",
      onConfirm: async () => {
        try {
          const response = await fetch(`/api/admin/desks/${d.id}/toggle-status`, {
            method: "POST",
            credentials: "include",
          });
          if (!response.ok) {
            const err = await response.json().catch(() => ({}));
            throw new Error(err?.message || "Failed to toggle desk status.");
          }
          setConfirmationDialog(null);
          setActionMessage(`Desk "${d.name}" ${d.isActive ? "deactivated" : "activated"}.`);
          setTimeout(() => setActionMessage(null), 5000);
          await loadData();
        } catch (err: any) {
          alert(err instanceof Error ? err.message : "Error toggling desk status.");
          setConfirmationDialog(null);
        }
      },
    });
  };

  return (
    <div className="rbac-admin-container">
      <div className="rbac-admin-header">
        <div>
          <h1>Office & Desk Configuration</h1>
          <p>
            Configure operational posts, seating allocations, and branch affiliations.
          </p>
        </div>
        <div>
          <button className="rbac-btn-primary" onClick={openNewDeskModal}>
            + Create Office Desk (Seat)
          </button>
        </div>
      </div>

      {actionMessage && (
        <div className="rbac-banner-success" style={{ marginBottom: "16px", padding: "10px 14px", background: "#f0fdf4", color: "#166534", borderRadius: "6px", border: "1px solid #bbf7d0" }}>
          ✓ {actionMessage}
        </div>
      )}

      {error && (
        <div className="rbac-banner-error" style={{ marginBottom: "16px", padding: "10px 14px", background: "#fef2f2", color: "#991b1b", borderRadius: "6px", border: "1px solid #fecaca" }}>
          ⚠️ {error}
        </div>
      )}

      {loading ? (
        <div className="rbac-loading">Loading office desks…</div>
      ) : (
        <div className="rbac-table-container">
          <table className="rbac-table">
            <thead>
              <tr>
                <th>Desk Code</th>
                <th>Desk Name (Post / Seat Title)</th>
                <th>Description</th>
                <th>Branch / Workstream</th>
                <th>Assigned Officers</th>
                <th>Status</th>
                <th style={{ textAlign: "right" }}>Actions</th>
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
                        <span className="workstream-tag primary">{d.workstreamName}</span>
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
                    <td style={{ textAlign: "right" }}>
                      <div className="rbac-action-buttons" style={{ justifyContent: "flex-end", gap: "6px" }}>
                        <button
                          className="rbac-btn-sm rbac-btn-outline"
                          onClick={() => openEditDeskModal(d)}
                        >
                          Edit
                        </button>
                        <button
                          className={`rbac-btn-sm ${d.isActive ? "rbac-btn-danger" : "rbac-btn-outline"}`}
                          onClick={() => void handleToggleDeskStatus(d)}
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

      {/* Desk Edit/Create Modal */}
      {showDeskModal && (
        <div className="rbac-modal-backdrop">
          <div className="rbac-modal-shell" style={{ maxWidth: "520px" }}>
            <div className="rbac-modal-header">
              <div>
                <h2>{editingDesk ? `Edit Office Desk: ${editingDesk.name}` : "Create New Office Desk"}</h2>
                <p>An Office Desk represents an operational seat, dealing post, or branch workstation.</p>
              </div>
              <button
                type="button"
                className="rbac-modal-close-btn"
                onClick={() => setShowDeskModal(false)}
                aria-label="Close"
              >
                &times;
              </button>
            </div>

            {modalError && (
              <div style={{ padding: "10px 14px", margin: "14px 20px 0", borderRadius: "8px", background: "#fef2f2", color: "#991b1b", fontSize: "13px", border: "1px solid #fecaca" }}>
                ⚠️ {modalError}
              </div>
            )}

            <form onSubmit={handleSaveDesk} style={{ display: "flex", flexDirection: "column" }}>
              <div className="rbac-modal-body" style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <div className="rbac-form-group">
                  <label>Desk Code *</label>
                  <input
                    type="text"
                    required
                    disabled={Boolean(editingDesk)}
                    value={deskCode}
                    onChange={(e) => setDeskCode(e.target.value)}
                    placeholder="e.g. DESK_NORTH_DA_1"
                    className="rbac-input"
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Desk Name (Post / Seat Title) *</label>
                  <input
                    type="text"
                    required
                    value={deskName}
                    onChange={(e) => setDeskName(e.target.value)}
                    placeholder="e.g. Dealing Assistant Desk - North District"
                    className="rbac-input"
                  />
                </div>

                <div className="rbac-form-group">
                  <label>Branch / Workstream Affiliation</label>
                  <select
                    value={deskWorkstreamId}
                    onChange={(e) => setDeskWorkstreamId(e.target.value)}
                    className="rbac-input"
                  >
                    <option value="">-- General / Multi-branch --</option>
                    {workstreams.map((ws) => (
                      <option key={ws.id} value={ws.id}>
                        {ws.name} ({ws.code})
                      </option>
                    ))}
                  </select>
                </div>

                <div className="rbac-form-group">
                  <label>Description</label>
                  <textarea
                    value={deskDesc}
                    onChange={(e) => setDeskDesc(e.target.value)}
                    placeholder="Notes on seat responsibilities…"
                    rows={2}
                    className="rbac-input"
                    style={{ minHeight: "60px", resize: "vertical" }}
                  />
                </div>
              </div>

              <div className="rbac-modal-footer">
                <button
                  type="button"
                  className="rbac-btn-outline"
                  disabled={modalLoading}
                  onClick={() => setShowDeskModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="rbac-btn-primary"
                  disabled={modalLoading}
                >
                  {modalLoading ? "Saving…" : "Save Office Desk"}
                </button>
              </div>
            </form>
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
                <span style={{ color: "#64748b", fontSize: "12px", textTransform: "uppercase", fontWeight: 700 }}>Desk / Seat</span>
                <div style={{ fontSize: "15px", fontWeight: 700, color: "#0f172a" }}>
                  {confirmationDialog.deskName}
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
              >
                Cancel
              </button>
              <button
                type="button"
                className={`rbac-btn-primary ${confirmationDialog.confirmTone === "danger" ? "rbac-btn-danger" : ""}`}
                onClick={() => void confirmationDialog.onConfirm()}
              >
                {confirmationDialog.confirmLabel}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
