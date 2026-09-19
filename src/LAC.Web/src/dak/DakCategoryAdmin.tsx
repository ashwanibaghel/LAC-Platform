import React, { useState, useEffect, useCallback } from "react";
import type { DakCategory } from "./types";
import type { Workstream } from "../auth/types";

export const DakCategoryAdmin: React.FC = () => {
  const [categories, setCategories] = useState<DakCategory[]>([]);
  const [workstreams, setWorkstreams] = useState<Workstream[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal state
  const [showModal, setShowModal] = useState(false);
  const [editingCategory, setEditingCategory] = useState<DakCategory | null>(null);
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [defaultPriority, setDefaultPriority] = useState<"Routine" | "Urgent" | "Immediate">("Routine");
  const [defaultWorkstreamId, setDefaultWorkstreamId] = useState("");
  const [saving, setSaving] = useState(false);

  const loadData = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const [catRes, wsRes] = await Promise.all([
        fetch("/api/admin/dak-categories", { credentials: "include" }),
        fetch("/api/admin/workstreams", { credentials: "include" }),
      ]);

      if (!catRes.ok || !wsRes.ok) throw new Error("Failed to load category or workstream configurations.");

      const cats = (await catRes.json()) as DakCategory[];
      const wss = (await wsRes.json()) as Workstream[];

      setCategories(cats);
      setWorkstreams(wss);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load categories.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  const openNewModal = () => {
    setEditingCategory(null);
    setCode("");
    setName("");
    setDescription("");
    setDefaultPriority("Routine");
    setDefaultWorkstreamId("");
    setShowModal(true);
  };

  const openEditModal = (cat: DakCategory) => {
    setEditingCategory(cat);
    setCode(cat.code);
    setName(cat.name);
    setDescription(cat.description || "");
    setDefaultPriority(cat.defaultPriority);
    setDefaultWorkstreamId(cat.defaultWorkstreamId || "");
    setShowModal(true);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setSaving(true);
      if (editingCategory) {
        // Update
        const res = await fetch(`/api/admin/dak-categories/${editingCategory.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            name: name.trim(),
            description: description.trim() || null,
            defaultPriority,
            defaultWorkstreamId: defaultWorkstreamId || null,
          }),
        });
        if (!res.ok) {
          const d = await res.json().catch(() => null);
          throw new Error(d?.detail || d?.message || "Failed to update category.");
        }
      } else {
        // Create
        const res = await fetch("/api/admin/dak-categories", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          credentials: "include",
          body: JSON.stringify({
            code: code.trim(),
            name: name.trim(),
            description: description.trim() || null,
            defaultPriority,
            defaultWorkstreamId: defaultWorkstreamId || null,
          }),
        });
        if (!res.ok) {
          const d = await res.json().catch(() => null);
          throw new Error(d?.detail || d?.message || "Failed to create category.");
        }
      }

      setShowModal(false);
      await loadData();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error saving category.");
    } finally {
      setSaving(false);
    }
  };

  const handleToggleStatus = async (cat: DakCategory) => {
    try {
      const res = await fetch(`/api/admin/dak-categories/${cat.id}/toggle-status`, {
        method: "POST",
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to toggle category status.");
      await loadData();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Error toggling category status.");
    }
  };

  if (loading) return <div className="state"><strong>Loading Dak categories...</strong></div>;

  return (
    <div className="dak-category-admin">
      <div className="section-heading">
        <div>
          <h3>Inward Dak Categories</h3>
          <span>Manage classification codes, default routing priorities, and workstream bindings.</span>
        </div>
        <button className="primary-button" onClick={openNewModal}>
          + Create New Category
        </button>
      </div>

      {error && <div className="state error"><strong>Error:</strong> {error}</div>}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th>Code</th>
              <th>Category Name</th>
              <th>Description</th>
              <th>Default Priority</th>
              <th>Default Workstream</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            {categories.length === 0 ? (
              <tr>
                <td colSpan={7} style={{ textAlign: "center", color: "#64748b" }}>
                  No Dak categories defined yet. Create your first category above.
                </td>
              </tr>
            ) : (
              categories.map((c) => (
                <tr key={c.id}>
                  <td><code>{c.code}</code></td>
                  <td><strong>{c.name}</strong></td>
                  <td>{c.description || "—"}</td>
                  <td>
                    <span className={`priority-pill priority-${c.defaultPriority.toLowerCase()}`}>
                      {c.defaultPriority}
                    </span>
                  </td>
                  <td>{c.defaultWorkstreamName || "—"}</td>
                  <td>
                    <span className={`status ${c.isActive ? "success" : "warning"}`}>
                      {c.isActive ? "Active" : "Inactive"}
                    </span>
                  </td>
                  <td>
                    <div style={{ display: "flex", gap: "8px" }}>
                      <button className="text-action" onClick={() => openEditModal(c)}>
                        Edit
                      </button>
                      <button
                        className="text-action"
                        style={{ color: c.isActive ? "#b91c1c" : "#15803d" }}
                        onClick={() => void handleToggleStatus(c)}
                      >
                        {c.isActive ? "Deactivate" : "Activate"}
                      </button>
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Create / Edit Modal */}
      {showModal && (
        <div className="modal-backdrop">
          <div className="modal-card">
            <h3>{editingCategory ? "Edit Dak Category" : "Create New Dak Category"}</h3>
            <form onSubmit={handleSave}>
              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Category Code *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. JUDICIAL_NOTICE"
                  value={code}
                  onChange={(e) => setCode(e.target.value.toUpperCase())}
                  disabled={editingCategory !== null || saving}
                />
                <span className="hint">Unique identifier code (cannot be changed once created).</span>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Category Name *</label>
                <input
                  type="text"
                  required
                  placeholder="e.g. Judicial Notice / High Court Order"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  disabled={saving}
                />
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Description</label>
                <textarea
                  placeholder="Brief description of the types of documents falling under this category..."
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  disabled={saving}
                  rows={2}
                />
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Default Priority *</label>
                <select
                  value={defaultPriority}
                  onChange={(e) => setDefaultPriority(e.target.value as "Routine" | "Urgent" | "Immediate")}
                  disabled={saving}
                >
                  <option value="Routine">Routine</option>
                  <option value="Urgent">Urgent</option>
                  <option value="Immediate">Immediate / Top Priority</option>
                </select>
              </div>

              <div className="field-group" style={{ marginBottom: "14px" }}>
                <label>Default Workstream (Optional)</label>
                <select
                  value={defaultWorkstreamId}
                  onChange={(e) => setDefaultWorkstreamId(e.target.value)}
                  disabled={saving}
                >
                  <option value="">-- No Default Workstream --</option>
                  {workstreams.map((w) => (
                    <option key={w.id} value={w.id}>
                      {w.name} ({w.code})
                    </option>
                  ))}
                </select>
              </div>

              <div className="modal-actions" style={{ display: "flex", justifyContent: "flex-end", gap: "10px", marginTop: "20px" }}>
                <button type="button" className="secondary-button" onClick={() => setShowModal(false)} disabled={saving}>
                  Cancel
                </button>
                <button type="submit" className="primary-button" disabled={saving || !name.trim() || (!editingCategory && !code.trim())}>
                  {saving ? "Saving..." : editingCategory ? "Update Category" : "Create Category"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
