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

export interface KhasraWorkspaceRow {
  khasraNumber: string;
  bigha?: number | null;
  biswa?: number | null;
  biswansi?: number | null;
  awardNumber?: string | null;
  awardDate?: string | null;
  rectangleNumber?: string | null;
  qualifier?: string | null;
}

export interface KhasraImportProblem {
  rowNumber: number;
  khasraNumber?: string | null;
  message: string;
}

export interface KhasraImportRowPreview {
  rowNumber: number;
  khasraNumber?: string | null;
  khasraStatus: string;
  areaStatus: string;
  awardStatus: string;
  awardLinkStatus: string;
  result: string;
  message?: string | null;
  canImport: boolean;
  row?: KhasraWorkspaceRow | null;
}

export interface KhasraImportPreview {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  newKhasras: number;
  existingKhasras: number;
  newAwards: number;
  existingAwards: number;
  ambiguousAwards: number;
  newAwardLinks: number;
  existingAwardLinks: number;
  skippedRows: number;
  problems: KhasraImportProblem[];
  importableRows: KhasraWorkspaceRow[];
  rows: KhasraImportRowPreview[];
}

export interface NotificationLinkItem {
  id: string;
  notificationNumber?: string | null;
  sectionType?: string | null;
  notificationDate?: string | null;
  area?: number | null;
  areaUnit?: string | null;
}

export interface AwardLinkItem {
  id: string;
  awardNumber: string;
  acquisitionStatus?: string | null;
  acquiredArea?: number | null;
  areaUnit?: string | null;
}

export interface LrEntryItem {
  id: string;
  villageLrId: string;
  rawKhasraText?: string | null;
  rawAreaText?: string | null;
  rawRemarks?: string | null;
  verificationStatus: string;
}

export interface RecordedOwner {
  partyId: string;
  displayName: string;
  rawShareText?: string | null;
  numerator?: number | null;
  denominator?: number | null;
}

export interface RecordedOwnershipResult {
  found: boolean;
  isAmbiguous: boolean;
  message?: string | null;
  khatauniRecordId?: string | null;
  khataId?: string | null;
  owners: RecordedOwner[];
}

export interface KhasraDetail {
  id: string;
  displayNumber: string;
  normalizedNumber: string;
  rectangleNumber?: string | null;
  killaNumber?: string | null;
  subdivisionNumber?: string | null;
  totalArea?: number | null;
  areaUnit?: string | null;
  areaBigha?: number | null;
  areaBiswa?: number | null;
  areaBiswansi?: number | null;
  remarks?: string | null;
  village?: {
    id: string;
    name: string;
    subDivision?: {
      id: string;
      name: string;
    };
  };
  notifications?: NotificationLinkItem[];
  awards?: AwardLinkItem[];
  lrEntries?: LrEntryItem[];
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
      .catch((err: any) => {
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
      {quickId && <KhasraQuickViewDrawer id={quickId} villageId={villageId} onClose={() => setQuickId("")} />}
    </div>
  );
};

// Add/Edit Khasra Modal
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
  const [khasraNumber, setKhasraNumber] = useState(edit?.displayNumber || edit?.khasraNumber || "");
  const [bigha, setBigha] = useState(edit?.areaBigha?.toString() ?? edit?.bigha?.toString() ?? "");
  const [biswa, setBiswa] = useState(edit?.areaBiswa?.toString() ?? edit?.biswa?.toString() ?? "");
  const [biswansi, setBiswansi] = useState(edit?.areaBiswansi?.toString() ?? edit?.biswansi?.toString() ?? "");
  const [awardNumber, setAwardNumber] = useState(edit?.awards?.[0]?.awardNumber || edit?.awardNumber || "");
  const [awardDate, setAwardDate] = useState(edit?.awards?.[0]?.awardDate || edit?.awardDate || "");
  const [rectangleNumber, setRectangleNumber] = useState(edit?.rectangleNumber || "");
  const [qualifier, setQualifier] = useState(edit?.qualifier || "");

  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  const save = async () => {
    if (!khasraNumber.trim()) {
      setError("Khasra number is required.");
      return;
    }
    setBusy(true);
    setError("");

    const payload: KhasraWorkspaceRow = {
      khasraNumber: khasraNumber.trim(),
      bigha: bigha === "" ? null : Number(bigha),
      biswa: biswa === "" ? null : Number(biswa),
      biswansi: biswansi === "" ? null : Number(biswansi),
      awardNumber: awardNumber.trim() || null,
      awardDate: awardDate || null,
      rectangleNumber: rectangleNumber.trim() || null,
      qualifier: qualifier.trim() || null,
    };

    try {
      if (edit?.id) {
        const res = await fetch(`${api}/khasras/${edit.id}`, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
          credentials: "include",
        });
        if (!res.ok) throw new Error("Failed to update Khasra.");
      } else {
        const res = await fetch(`${api}/villages/${villageId}/khasras/batch`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ rows: [payload] }),
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

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
            <div className="form-group">
              <label className="form-label required">Khasra Number</label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. 12//14/2"
                value={khasraNumber}
                onChange={(e) => setKhasraNumber(e.target.value)}
                required
              />
            </div>
            <div className="form-group">
              <label className="form-label">Rectangle Number</label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. 12"
                value={rectangleNumber}
                onChange={(e) => setRectangleNumber(e.target.value)}
              />
            </div>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: "12px" }}>
            <div className="form-group">
              <label className="form-label">Bigha</label>
              <input
                type="number"
                className="form-input"
                value={bigha}
                onChange={(e) => setBigha(e.target.value)}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Biswa</label>
              <input
                type="number"
                className="form-input"
                value={biswa}
                onChange={(e) => setBiswa(e.target.value)}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Biswansi</label>
              <input
                type="number"
                className="form-input"
                value={biswansi}
                onChange={(e) => setBiswansi(e.target.value)}
              />
            </div>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "12px" }}>
            <div className="form-group">
              <label className="form-label">Award Number</label>
              <input
                type="text"
                className="form-input"
                placeholder="e.g. 15/2021-22"
                value={awardNumber}
                onChange={(e) => setAwardNumber(e.target.value)}
              />
            </div>
            <div className="form-group">
              <label className="form-label">Award Date</label>
              <input
                type="date"
                className="form-input"
                value={awardDate}
                onChange={(e) => setAwardDate(e.target.value)}
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

// Excel Import Modal
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
  const [preview, setPreview] = useState<KhasraImportPreview | null>(null);

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
        return r.json() as Promise<KhasraImportPreview>;
      })
      .then((d) => {
        if (active) {
          setPreview(d);
          setBusy(false);
        }
      })
      .catch((e: any) => {
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
    if (!preview?.importableRows?.length) return;
    setBusy(true);
    setError("");
    try {
      const res = await fetch(`${api}/villages/${villageId}/khasras/import`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rows: preview.importableRows }),
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
      <div className="modal-container" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "780px" }}>
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
                  <strong>{preview.totalRows}</strong>
                  <span>Total Rows</span>
                </div>
                <div className="metric">
                  <strong style={{ color: "#16a34a" }}>{preview.validRows}</strong>
                  <span>Valid Rows</span>
                </div>
                <div className="metric">
                  <strong style={{ color: "#e11d48" }}>{preview.invalidRows}</strong>
                  <span>Invalid Rows</span>
                </div>
                <div className="metric">
                  <strong>{preview.newKhasras}</strong>
                  <span>New Khasras</span>
                </div>
                <div className="metric">
                  <strong>{preview.existingKhasras}</strong>
                  <span>Existing Khasras</span>
                </div>
              </div>

              {preview.rows?.length > 0 && (
                <div className="table-wrap" style={{ maxHeight: "300px", overflowY: "auto" }}>
                  <table>
                    <thead>
                      <tr>
                        <th>Row #</th>
                        <th>Khasra</th>
                        <th>Bigha/Biswa</th>
                        <th>Award Ref</th>
                        <th>Khasra Status</th>
                        <th>Award/Link Status</th>
                        <th>Result</th>
                      </tr>
                    </thead>
                    <tbody>
                      {preview.rows.map((r, idx) => (
                        <tr key={idx}>
                          <td>{r.rowNumber}</td>
                          <td style={{ fontWeight: 650 }}>{r.khasraNumber || "—"}</td>
                          <td>
                            {r.row?.bigha != null ? `${r.row.bigha}B ${r.row.biswa ?? 0}Bis` : "—"}
                          </td>
                          <td>{r.row?.awardNumber || "—"}</td>
                          <td>{r.khasraStatus}</td>
                          <td>{r.awardLinkStatus || r.awardStatus}</td>
                          <td>
                            <span
                              style={{
                                color: r.canImport ? "#16a34a" : "#e11d48",
                                fontWeight: 600,
                                fontSize: "12px",
                              }}
                            >
                              {r.result} {r.message ? `(${r.message})` : ""}
                            </span>
                          </td>
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
            disabled={busy || !preview?.importableRows?.length}
            onClick={() => void commit()}
          >
            {busy ? "Importing…" : `Commit ${preview?.importableRows?.length ?? 0} Valid Rows`}
          </button>
        </div>
      </div>
    </div>
  );
}

// Khasra Quick View Drawer
function KhasraQuickViewDrawer({ id, villageId, onClose }: { id: string; villageId: string; onClose: () => void }) {
  const { hasPermission } = useAuth();
  const canViewAward = hasPermission("Award.View");
  const canViewLr = hasPermission("LR.View");

  const [detail, setDetail] = useState<KhasraDetail | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);
  const [ownership, setOwnership] = useState<{ data?: RecordedOwnershipResult; forbidden?: boolean; error?: boolean }>({});
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setDetailError(null);

    Promise.all([
      fetch(`${api}/khasras/${id}`, { credentials: "include" }).then(async (r) => {
        if (!r.ok) throw new Error("Could not load Khasra details.");
        return r.json() as Promise<KhasraDetail>;
      }),
      fetch(`${api}/khasras/${id}/ownership`, { credentials: "include" }).then(async (r) => {
        if (r.status === 403) return { forbidden: true };
        if (!r.ok) return { error: true };
        return r.json() as Promise<RecordedOwnershipResult>;
      }),
    ])
      .then(([d, o]) => {
        if (active) {
          setDetail(d);
          if (o && "forbidden" in o && o.forbidden) {
            setOwnership({ forbidden: true });
          } else if (o && "error" in o && o.error) {
            setOwnership({ error: true });
          } else {
            setOwnership({ data: o as RecordedOwnershipResult });
          }
          setLoading(false);
        }
      })
      .catch((e: any) => {
        if (active) {
          setDetailError(e?.message || "Khasra record unavailable.");
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, [id]);

  if (loading) {
    return (
      <div className="modal-overlay" onClick={onClose}>
        <div className="modal-container" style={{ maxWidth: "450px" }} onClick={(e) => e.stopPropagation()}>
          <div className="state loading">Loading Khasra details…</div>
        </div>
      </div>
    );
  }

  if (detailError || !detail) {
    return (
      <div className="modal-overlay" onClick={onClose}>
        <div className="modal-container" style={{ maxWidth: "450px" }} onClick={(e) => e.stopPropagation()}>
          <div className="modal-header">
            <h3>Khasra Record</h3>
            <button className="icon-button" onClick={onClose}>
              <IconClose size={18} />
            </button>
          </div>
          <div className="modal-body">
            <div className="state error">{detailError || "Khasra record unavailable."}</div>
          </div>
        </div>
      </div>
    );
  }

  const k = detail;
  const notifications = k.notifications || [];
  const awards = k.awards || [];
  const lrEntries = k.lrEntries || [];
  const oData = ownership.data;

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
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b", fontStyle: "italic" }}>
                Ownership details not available with current access
              </p>
            ) : ownership.error ? (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>Ownership status unavailable</p>
            ) : oData?.isAmbiguous ? (
              <p style={{ margin: 0, fontSize: "13px", color: "#d97706", fontWeight: 600 }}>
                {oData.message || "Recorded ownership is ambiguous and pending verification."}
              </p>
            ) : !oData?.found ? (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>
                {oData?.message || "No verified recorded ownership is available for this context."}
              </p>
            ) : oData.owners?.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {oData.owners.map((owner, idx) => {
                  const shareStr = owner.rawShareText
                    ? owner.rawShareText
                    : owner.numerator != null && owner.denominator != null
                    ? `${owner.numerator}/${owner.denominator}`
                    : null;

                  return (
                    <div key={idx} style={{ fontSize: "13px", borderBottom: "1px solid #f1f5f9", paddingBottom: "4px" }}>
                      <span style={{ fontWeight: 650 }}>{owner.displayName}</span>
                      {shareStr && <span style={{ color: "#64748b", marginLeft: "8px" }}>({shareStr})</span>}
                    </div>
                  );
                })}
              </div>
            ) : (
              <p style={{ margin: 0, fontSize: "13px", color: "#64748b" }}>
                {oData?.message || "No verified recorded ownership is available for this context."}
              </p>
            )}
          </div>

          {/* Linked Acquisition Awards */}
          <div>
            <h4 style={{ margin: "0 0 8px 0", fontSize: "14px", fontWeight: 700 }}>Linked Acquisition Awards</h4>
            {awards.length > 0 ? (
              <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                {awards.map((a) => (
                  <div key={a.id} style={{ fontSize: "13px" }}>
                    {canViewAward ? (
                      <Link to={`/awards/${a.id}`} className="entity-link">
                        Award #{a.awardNumber} {a.acquisitionStatus ? `(${a.acquisitionStatus})` : ""}
                      </Link>
                    ) : (
                      <span style={{ fontWeight: 600 }}>
                        Award #{a.awardNumber} {a.acquisitionStatus ? `(${a.acquisitionStatus})` : ""}
                      </span>
                    )}
                  </div>
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
                {notifications.map((n) => (
                  <div key={n.id} style={{ fontSize: "13px" }}>
                    {canViewAward ? (
                      <Link to={`/notifications/${n.id}`} className="entity-link">
                        {n.sectionType || "Notification"} #{n.notificationNumber || n.id}
                      </Link>
                    ) : (
                      <span style={{ fontWeight: 600 }}>
                        {n.sectionType || "Notification"} #{n.notificationNumber || n.id}
                      </span>
                    )}
                  </div>
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
                {lrEntries.map((e) => (
                  <div key={e.id} style={{ fontSize: "13px", color: "#334155", display: "flex", justifyContent: "space-between" }}>
                    {canViewLr ? (
                      <Link to={`/villages/${k.village?.id || villageId}/lr/${e.villageLrId}`} className="entity-link">
                        Source Entry ({e.rawKhasraText || "Khasra"})
                      </Link>
                    ) : (
                      <span>Source Entry ({e.rawKhasraText || "Khasra"})</span>
                    )}
                    <span className="status-badge status-draft" style={{ fontSize: "11px" }}>
                      {e.verificationStatus}
                    </span>
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
