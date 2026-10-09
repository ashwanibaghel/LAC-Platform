import React, { useState, useEffect, useCallback } from "react";
import type { DeskItem, Workstream } from "./types";
import "./admin.css";

export const OfficeDesksAdmin: React.FC = () => {
  const [desks, setDesks] = useState<DeskItem[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

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

  const handleSaveDesk = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalError(null);
    setModalLoading(true);

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
        if (!response.ok) {
          const err = await response.json().catch(() => ({}));
          throw new Error(err?.message || "Failed to update office desk.");
        }
        setActionMessage(`Updated office desk "${deskName.trim()}".`);
      } else {
        const response = await fetch("/api/admin/desks", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            code: deskCode.trim().toUpperCase(),
            name: deskName.trim(),
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
        setActionMessage(`Created office desk "${deskName.trim()}".`);
      }

      setShowDeskModal(false);
      setTimeout(() => setActionMessage(null), 5000);
      await loadData();
    } catch (err: any) {
      setModalError(err instanceof Error ? err.message : "Error saving office desk.");
    } finally {
      setModalLoading(false);
    }
  };

  const handleToggleDeskStatus = async (d: DeskItem) => {
    try {
      const response = await fetch(`/api/admin/desks/${d.id}/toggle-status`, {
        method: "POST",
        credentials: "include",
      });
      if (!response.ok) {
        const err = await response.json().catch(() => ({}));
        throw new Error(err?.message || "Failed to toggle desk status.");
      }
      setActionMessage(`Desk "${d.name}" ${d.isActive ? "deactivated" : "activated"}.`);
      setTimeout(() => setActionMessage(null), 5000);
      await loadData();
    } catch (err: any) {
      alert(err instanceof Error ? err.message : "Error toggling desk status.");
    }
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
          <button className="primary-button" onClick={openNewDeskModal}>
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
                          className="rbac-btn-edit"
                          onClick={() => openEditDeskModal(d)}
                        >
                          Edit
                        </button>
                        <button
                          className="rbac-btn-edit"
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
        <div className="modal-overlay">
          <div className="modal-content" style={{ maxWidth: "520px" }}>
            <h3>{editingDesk ? `Edit Office Desk: ${editingDesk.name}` : "Create New Office Desk"}</h3>
            <p className="subtext" style={{ marginBottom: "16px" }}>
              An Office Desk represents a specific post, dealing assistant seat, or operational desk within the office.
            </p>

            {modalError && (
              <div className="rbac-banner-error" style={{ marginBottom: "12px", padding: "8px 12px", background: "#fef2f2", color: "#991b1b", borderRadius: "6px" }}>
                ⚠️ {modalError}
              </div>
            )}

            <form onSubmit={handleSaveDesk} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
              <div>
                <label>Desk Code *</label>
                <input
                  type="text"
                  required
                  disabled={Boolean(editingDesk)}
                  value={deskCode}
                  onChange={(e) => setDeskCode(e.target.value)}
                  placeholder="e.g. DESK_NORTH_DA_1"
                />
              </div>

              <div>
                <label>Desk Name (Post / Seat Title) *</label>
                <input
                  type="text"
                  required
                  value={deskName}
                  onChange={(e) => setDeskName(e.target.value)}
                  placeholder="e.g. Dealing Assistant Desk - North District"
                />
              </div>

              <div>
                <label>Branch / Workstream Affiliation</label>
                <select
                  value={deskWorkstreamId}
                  onChange={(e) => setDeskWorkstreamId(e.target.value)}
                >
                  <option value="">-- General / Multi-branch --</option>
                  {workstreams.map((ws) => (
                    <option key={ws.id} value={ws.id}>
                      {ws.name} ({ws.code})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label>Description</label>
                <textarea
                  value={deskDesc}
                  onChange={(e) => setDeskDesc(e.target.value)}
                  placeholder="Notes on seat responsibilities…"
                  rows={2}
                />
              </div>

              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "12px" }}>
                <button
                  type="button"
                  className="secondary-button"
                  disabled={modalLoading}
                  onClick={() => setShowDeskModal(false)}
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="primary-button"
                  disabled={modalLoading}
                >
                  {modalLoading ? "Saving…" : "Save Office Desk"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
