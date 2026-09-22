import React, { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../auth/AuthProvider";
import { ExportMenu } from "../components/ExportMenu";
import { IconPlus, IconSearch, IconChevronRight, IconClose } from "../components/Icons";
import "./land.css";

const api = "/api";

type Page<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};

type KhasraRow = {
  khasraNumber: string;
  bigha: string;
  biswa: string;
  biswansi: string;
  awardNumber: string;
  awardDate: string;
};

const blankKhasra = (): KhasraRow => ({
  khasraNumber: "",
  bigha: "",
  biswa: "",
  biswansi: "",
  awardNumber: "",
  awardDate: "",
});

function date(value?: string | null) {
  if (!value) return "—";
  const raw = String(value);
  const parsed = new Date(/^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw + "T00:00:00" : raw);
  if (Number.isNaN(parsed.getTime())) return "—";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

export interface VillageKhasrasTabProps {
  villageId: string;
}

export const VillageKhasrasTab: React.FC<VillageKhasrasTabProps> = ({ villageId }) => {
  const { hasPermission } = useAuth();
  const canEditKhasra = hasPermission("Khasra.Edit");

  const [page, setPage] = useState(0);
  const [query, setQuery] = useState("");
  const [refresh, setRefresh] = useState(0);
  const [panel, setPanel] = useState(false);
  const [quickId, setQuickId] = useState("");
  const [importFile, setImportFile] = useState<File | null>(null);
  const [edit, setEdit] = useState<any>(null);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<Page<any> | null>(null);

  useEffect(() => {
    if (!villageId) return;
    let active = true;
    setLoading(true);
    setError(null);

    const searchParams = new URLSearchParams({
      page: page.toString(),
      pageSize: "25",
      r: refresh.toString(),
    });
    if (query) searchParams.set("q", query);

    fetch(`${api}/villages/${villageId}/khasras?${searchParams.toString()}`, { credentials: "include" })
      .then(async (res) => {
        if (!res.ok) {
          if (res.status === 403) throw new Error("Access denied: You do not have permission to view Khasras.");
          throw new Error("Could not load Khasras.");
        }
        return res.json() as Promise<Page<any>>;
      })
      .then((resData) => {
        if (active) {
          setData(resData);
          setLoading(false);
        }
      })
      .catch((err) => {
        if (active) {
          setError(err?.message || "Failed to load Khasras.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [villageId, page, query, refresh]);

  const changed = () => {
    setRefresh((v) => v + 1);
  };

  const openAddModal = () => {
    setEdit(null);
    setPanel(true);
  };

  const openEditModal = (item: any) => {
    setEdit(item);
    setPanel(true);
  };

  const lastPage = data ? Math.max(0, Math.ceil(data.totalCount / data.pageSize) - 1) : 0;

  // Group items by rectangle
  const rectangleGroups: { [key: string]: any[] } = {};
  if (data?.items) {
    data.items.forEach((item) => {
      const rect = item.rectangleNumber || "Other";
      if (!rectangleGroups[rect]) rectangleGroups[rect] = [];
      rectangleGroups[rect].push(item);
    });
  }

  return (
    <div className="village-khasras-tab">
      {/* Header Toolbar */}
      <div className="section-heading" style={{ marginBottom: "20px" }}>
        <div>
          <h2>Canonical Khasras Directory</h2>
          <span>Master land records and rectangle groupings for this village.</span>
        </div>
        <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
          {canEditKhasra && (
            <>
              <button className="primary-button" onClick={openAddModal}>
                <IconPlus size={16} /> Add Khasra
              </button>
              <label className="secondary-button" style={{ cursor: "pointer", margin: 0 }}>
                Import Excel
                <input
                  type="file"
                  style={{ display: "none" }}
                  accept=".xlsx"
                  onChange={(e) => e.target.files?.[0] && setImportFile(e.target.files[0])}
                />
              </label>
            </>
          )}

          <a className="secondary-button" href={`${api}/villages/${villageId}/khasras/import-template`} download>
            Download Template
          </a>
          <ExportMenu baseUrl={`${api}/villages/${villageId}/khasras/export`} query={query} />

          <div className="lac-header-search-box" style={{ width: "220px" }}>
            <IconSearch size={14} className="lac-search-icon" />
            <input
              value={query}
              onChange={(e) => {
                setPage(0);
                setQuery(e.target.value);
              }}
              placeholder="Search Khasras…"
            />
          </div>
        </div>
      </div>

      {importFile && (
        <KhasraImportModal
          villageId={villageId}
          file={importFile}
          onClose={() => setImportFile(null)}
          onSaved={changed}
        />
      )}

      {panel && (
        <KhasraEntryPanelModal
          villageId={villageId}
          edit={edit}
          onClose={() => {
            setPanel(false);
            setEdit(null);
          }}
          onSaved={changed}
        />
      )}

      {loading ? (
        <div className="state loading">Loading Khasras…</div>
      ) : error ? (
        <div className="state error">{error}</div>
      ) : !data || data.items.length === 0 ? (
        <div className="state empty">
          <strong>No Khasra records found</strong>
          <span>{query ? "No khasras match your search query." : "Add khasras to initialize this village master."}</span>
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: "24px" }}>
          {Object.entries(rectangleGroups).map(([rect, items]) => (
            <div key={rect} className="section" style={{ padding: "16px" }}>
              <h3 style={{ margin: "0 0 14px 0", fontSize: "16px", fontWeight: 700 }}>
                {rect !== "Other" ? `Rectangle ${rect}` : "Unclassified / Other Rectangles"} ({items.length})
              </h3>

              <div className="table-wrap">
                <table>
                  <thead>
                    <tr>
                      <th scope="col">Khasra Number</th>
                      <th scope="col">Canonical Area</th>
                      <th scope="col">Linked Awards</th>
                      <th scope="col">Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((k) => (
                      <tr key={k.id}>
                        <td>
                          <button
                            className="text-action"
                            style={{
                              fontWeight: 700,
                              background: "none",
                              border: "none",
                              padding: 0,
                              cursor: "pointer",
                            }}
                            onClick={() => setQuickId(k.id)}
                          >
                            {k.displayNumber}
                          </button>
                        </td>
                        <td>
                          {k.areaBigha != null ? `${k.areaBigha} B ${k.areaBiswa ?? 0} Bis ${k.areaBiswansi ?? 0} Bisw` : "—"}
                        </td>
                        <td>
                          {k.awards?.length ? (
                            <span>{k.awards.map((a: any) => `Award #${a.awardNumber}`).join(", ")}</span>
                          ) : (
                            <span style={{ color: "#94a3b8" }}>None</span>
                          )}
                        </td>
                        <td>
                          <div style={{ display: "flex", gap: "10px", alignItems: "center" }}>
                            <button
                              className="text-action"
                              style={{ background: "none", border: "none", padding: 0, cursor: "pointer" }}
                              onClick={() => setQuickId(k.id)}
                            >
                              Quick View
                            </button>
                            {canEditKhasra && (
                              <button
                                className="secondary-button"
                                style={{ padding: "4px 8px", fontSize: "12px" }}
                                onClick={() => openEditModal(k)}
                              >
                                Edit
                              </button>
                            )}
                          </div>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          ))}

          {data.totalCount > data.pageSize && (
            <div className="pagination">
              <span>
                Showing {data.page * data.pageSize + 1}–
                {Math.min((data.page + 1) * data.pageSize, data.totalCount)} of {data.totalCount} Khasras
              </span>
              <div>
                <button disabled={page === 0} onClick={() => setPage(page - 1)}>
                  Previous
                </button>
                <button disabled={page >= lastPage} onClick={() => setPage(page + 1)}>
                  Next
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      {/* Quick View Drawer */}
      {quickId && <KhasraQuickViewDrawer id={quickId} onClose={() => setQuickId("")} />}
    </div>
  );
};

// Modal Components
function KhasraEntryPanelModal({
  villageId,
  edit,
  onClose,
  onSaved,
}: {
  villageId: string;
  edit: any;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [row, setRow] = useState<KhasraRow>(
    edit
      ? {
          khasraNumber: edit.displayNumber || "",
          bigha: edit.areaBigha?.toString() || "",
          biswa: edit.areaBiswa?.toString() || "",
          biswansi: edit.areaBiswansi?.toString() || "",
          awardNumber: edit.awards?.[0]?.awardNumber || "",
          awardDate: edit.awards?.[0]?.awardDate || "",
        }
      : blankKhasra()
  );
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    if (!row.khasraNumber.trim()) {
      setError("Khasra number is required.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      if (edit) {
        const res = await fetch(`${api}/khasras/${edit.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            displayNumber: row.khasraNumber.trim(),
            bigha: row.bigha === "" ? null : Number(row.bigha),
            biswa: row.biswa === "" ? null : Number(row.biswa),
            biswansi: row.biswansi === "" ? null : Number(row.biswansi),
          }),
          credentials: "include",
        });
        if (!res.ok) throw new Error("Failed to update Khasra.");
      } else {
        const res = await fetch(`${api}/villages/${villageId}/khasras/batch`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ rows: [row].map((r) => ({
            khasraNumber: r.khasraNumber,
            bigha: r.bigha === "" ? null : Number(r.bigha),
            biswa: r.biswa === "" ? null : Number(r.biswa),
            biswansi: r.biswansi === "" ? null : Number(r.biswansi),
            awardNumber: r.awardNumber || null,
            awardDate: r.awardDate || null,
          })) }),
          credentials: "include",
        });
        if (!res.ok) throw new Error("Failed to add Khasra.");
      }
      onSaved();
      onClose();
    } catch (e: any) {
      setError(e?.message || "Could not save Khasra.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "560px" }}>
        <div className="modal-header">
          <h3>{edit ? "Edit Khasra Record" : "Add Khasra Record"}</h3>
          <button className="icon-button" onClick={onClose}>
            <IconClose size={18} />
          </button>
        </div>

        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {error && <div className="state error">{error}</div>}

          <div className="form-group">
            <label className="form-label required">Khasra Number</label>
            <input
              type="text"
              className="form-input"
              placeholder="e.g. 12//14/2"
              value={row.khasraNumber}
              onChange={(e) => setRow({ ...row, khasraNumber: e.target.value })}
              required
            />
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
            <div className="form-group">
              <label className="form-label">Bigha</label>
              <input
                type="number"
                className="form-input"
                value={row.bigha}
                onChange={(e) => setRow({ ...row, bigha: e.target.value })}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Biswa</label>
              <input
                type="number"
                className="form-input"
                value={row.biswa}
                onChange={(e) => setRow({ ...row, biswa: e.target.value })}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Biswansi</label>
              <input
                type="number"
                className="form-input"
                value={row.biswansi}
                onChange={(e) => setRow({ ...row, biswansi: e.target.value })}
              />
            </div>
          </div>
        </div>

        <div className="modal-footer">
          <button type="button" className="secondary-button" onClick={onClose}>
            Cancel
          </button>
          <button type="button" className="primary-button" disabled={busy} onClick={() => void save()}>
            {busy ? "Saving…" : edit ? "Save Changes" : "Add Khasra"}
          </button>
        </div>
      </div>
    </div>
  );
}

function KhasraImportModal({
  villageId,
  file,
  onClose,
  onSaved,
}: {
  villageId: string;
  file: File;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState("");
  const [preview, setPreview] = useState<any>(null);

  useEffect(() => {
    let active = true;
    const form = new FormData();
    form.append("file", file);

    fetch(`${api}/villages/${villageId}/khasras/import-preview`, {
      method: "POST",
      body: form,
      credentials: "include",
    })
      .then(async (r) => {
        if (!r.ok) throw new Error("Could not parse Excel template.");
        return r.json();
      })
      .then((d) => {
        if (active) {
          setPreview(d);
          setBusy(false);
        }
      })
      .catch((e) => {
        if (active) {
          setError(e?.message || "Failed to preview import.");
          setBusy(false);
        }
      });
    return () => {
      active = false;
    };
  }, [file, villageId]);

  const commit = async () => {
    if (!preview?.validRows?.length) return;
    setBusy(true);
    setError("");
    try {
      const res = await fetch(`${api}/villages/${villageId}/khasras/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rows: preview.validRows }),
        credentials: "include",
      });
      if (!res.ok) throw new Error("Failed to import Khasras.");
      onSaved();
      onClose();
    } catch (e: any) {
      setError(e?.message || "Import failed.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "700px" }}>
        <div className="modal-header">
          <h3>Import Khasras Preview</h3>
          <button className="icon-button" onClick={onClose}>
            <IconClose size={18} />
          </button>
        </div>

        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {error && <div className="state error">{error}</div>}

          {busy ? (
            <div className="state loading">Parsing spreadsheet file…</div>
          ) : preview ? (
            <div>
              <div className="summary-strip" style={{ marginBottom: "16px" }}>
                <div className="metric">
                  <strong>{preview.validRows?.length ?? 0}</strong>
                  <span>Valid Rows</span>
                </div>
                <div className="metric">
                  <strong style={{ color: "#e11d48" }}>{preview.invalidRows?.length ?? 0}</strong>
                  <span>Invalid Rows</span>
                </div>
              </div>

              {preview.validRows?.length > 0 && (
                <div className="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th>Khasra Number</th>
                        <th>Bigha</th>
                        <th>Biswa</th>
                        <th>Biswansi</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.validRows.slice(0, 10).map((r: any, idx: number) => (
                        <tr key={idx}>
                          <td>{r.khasraNumber}</td>
                          <td>{r.bigha ?? 0}</td>
                          <td>{r.biswa ?? 0}</td>
                          <td>{r.biswansi ?? 0}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>
          ) : null}
        </div>

        <div className="modal-footer">
          <button type="button" className="secondary-button" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="primary-button"
            disabled={busy || !preview?.validRows?.length}
            onClick={() => void commit()}
          >
            {busy ? "Importing…" : "Commit Valid Rows"}
          </button>
        </div>
      </div>
    </div>
  );
}

// Quick View Drawer with exact backend fields and permission-aware ownership state
function KhasraQuickViewDrawer({ id, onClose }: { id: string; onClose: () => void }) {
  const [detail, setDetail] = useState<any>(null);
  const [ownership, setOwnership] = useState<{ data?: any; forbidden?: boolean }>({});
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);

    Promise.all([
      fetch(`${api}/khasras/${id}`, { credentials: "include" }).then((r) => (r.ok ? r.json() : null)),
      fetch(`${api}/khasras/${id}/ownership`, { credentials: "include" }).then((r) => {
        if (r.status === 403) return { forbidden: true };
        return r.ok ? r.json() : null;
      }),
    ]).then(([d, o]) => {
      if (active) {
        setDetail(d);
        if (o?.forbidden) {
          setOwnership({ forbidden: true });
        } else {
          setOwnership({ data: o });
        }
        setLoading(false);
      }
    });

    return () => {
      active = false;
    };
  }, [id]);

  if (loading) {
    return (
      <aside className="modal-overlay" onClick={onClose}>
        <div className="modal-container" style={{ maxWidth: "450px" }} onClick={(e) => e.stopPropagation()}>
          <div className="state loading">Loading Khasra details…</div>
        </div>
      </aside>
    );
  }

  const k = detail || {};
  const notifications: any[] = k.notifications || [];
  const awards: any[] = k.awards || [];
  const lrEntries: any[] = k.lrEntries || [];
  const owners: any[] = ownership.data?.owners || [];

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "480px" }}>
        <div className="modal-header">
          <div>
            <h3 style={{ margin: 0 }}>Khasra #{k.displayNumber || id}</h3>
            <span style={{ fontSize: "12px", color: "#64748b" }}>
              {k.village?.name} • Sub-division: {k.village?.subDivision?.name || "—"}
            </span>
          </div>
          <button className="icon-button" onClick={onClose}>
            <IconClose size={18} />
          </button>
        </div>

        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
          {/* Canonical Area */}
          <div style={{ background: "#f8fafc", padding: "12px 16px", borderRadius: "8px" }}>
            <h4 style={{ margin: "0 0 4px 0", fontSize: "13px", textTransform: "uppercase", color: "#64748b" }}>
              Canonical Master Area
            </h4>
            <div style={{ fontSize: "18px", fontWeight: 700, color: "#0f172a" }}>
              {k.areaBigha != null ? `${k.areaBigha} Bigha ${k.areaBiswa ?? 0} Biswa ${k.areaBiswansi ?? 0} Biswansi` : k.totalArea != null ? `${k.totalArea} ${k.areaUnit || ""}` : "Not recorded"}
            </div>
          </div>

          {/* Recorded Owners */}
          <div>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", fontWeight: 700 }}>Recorded Owners</h4>
            {ownership.forbidden ? (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b", italic: "true" }}>
                Ownership details not available with current access
              </p>
            ) : owners.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {owners.map((o: any, idx: number) => (
                  <div key={idx} style={{ fontSize: "13px", borderBottom: "1px solid #f1f5f9", paddingBottom: "4px" }}>
                    <span style={{ fontWeight: 650 }}>{o.displayName || o.name}</span>
                    {o.shareText && <span style={{ color: "#64748b", marginLeft: "8px" }}>({o.shareText})</span>}
                  </div>
                ))}
              </div>
            ) : (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>Not recorded</p>
            )}
          </div>

          {/* Linked Awards */}
          <div>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", fontWeight: 700 }}>Linked Acquisition Awards</h4>
            {awards.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {awards.map((a: any) => (
                  <Link key={a.id} to={`/awards/${a.id}`} className="entity-link" style={{ fontSize: "13px" }}>
                    Award #{a.awardNumber} ({date(a.awardDate)})
                  </Link>
                ))}
              </div>
            ) : (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>No linked awards</p>
            )}
          </div>

          {/* Relevant Notifications */}
          <div>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", fontWeight: 700 }}>Relevant Notifications</h4>
            {notifications.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {notifications.map((n: any) => (
                  <Link key={n.id} to={`/notifications/${n.id}`} className="entity-link" style={{ fontSize: "13px" }}>
                    {n.notificationType || "Notification"} #{n.notificationNumber || n.id}
                  </Link>
                ))}
              </div>
            ) : (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>No linked notifications</p>
            )}
          </div>

          {/* LR Source Entries */}
          <div>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", fontWeight: 700 }}>LR Source Entries</h4>
            {lrEntries.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {lrEntries.map((e: any, idx: number) => (
                  <div key={idx} style={{ fontSize: "13px", color: "#334155" }}>
                    <span style={{ fontWeight: 600 }}>{e.registerReference || "LR Register"}:</span> {e.rawKhasraText || e.status}
                  </div>
                ))}
              </div>
            ) : (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>No linked LR entries</p>
            )}
          </div>
        </div>

        <div className="modal-footer" style={{ justifyContent: "space-between" }}>
          <Link to={`/khasras/${id}`} className="primary-button" style={{ textDecoration: "none" }}>
            Open Full Khasra Record →
          </Link>
          <button type="button" className="secondary-button" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
