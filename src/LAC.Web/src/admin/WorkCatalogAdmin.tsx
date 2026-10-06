import React, { useState, useMemo } from "react";
import type { WorkCatalogItem } from "./types";
import "./admin.css";

const STANDARD_WORK_CATALOG: WorkCatalogItem[] = [
  {
    id: "work-1",
    code: "WORK_STMT_A",
    name: "Statement-A Verification & Formulation",
    workstreamCode: "LAND_RECORDS",
    workstreamName: "Land Records",
    description: "Preparation, revenue verification, and joint inspection of Statement-A land schedule prior to declaration.",
    statutoryBasis: "RFCTLARR 2013 Sec 19 / LAA 1894 Sec 6",
    associatedPermissions: ["Award.View", "Award.Edit", "Khasra.Verify"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-2",
    code: "WORK_LR_VERIF",
    name: "Land Record (Khatauni/Khasra) Verification",
    workstreamCode: "LAND_RECORDS",
    workstreamName: "Land Records",
    description: "Verification of ownership entries, khatauni shares, and unrecorded co-sharers from Delhi revenue registers.",
    statutoryBasis: "Delhi Land Revenue Act 1954",
    associatedPermissions: ["Khasra.Verify", "Award.View"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-3",
    code: "WORK_AWARD_FORM",
    name: "Award Inquiry & Formulation",
    workstreamCode: "LAND_RECORDS",
    workstreamName: "Land Records",
    description: "Conduct of Section 11/23 inquiry, market value assessment, solatium calculation, and award pronouncement.",
    statutoryBasis: "RFCTLARR 2013 Sec 23-30 / LAA 1894 Sec 11",
    associatedPermissions: ["Award.View", "Award.Edit", "Award.Sign"],
    defaultScopeMode: "All",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-4",
    code: "WORK_COMP_DISBUR",
    name: "Compensation Calculation & Disbursement",
    workstreamCode: "MATTERS",
    workstreamName: "Matters & Notes",
    description: "Preparation of payment vouchers, apportionment among lawful tenure holders, and treasury escrow management.",
    statutoryBasis: "RFCTLARR 2013 Sec 77 / LAA 1894 Sec 31",
    associatedPermissions: ["Matter.View", "Matter.Edit", "Matter.Sign"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-5",
    code: "WORK_POSS_DEMARC",
    name: "Possession Handover & Demarcation",
    workstreamCode: "LAND_RECORDS",
    workstreamName: "Land Records",
    description: "Execution of Kabza Karwayi (taking over physical possession) and delivery to the Requisitioning Body.",
    statutoryBasis: "RFCTLARR 2013 Sec 38 / LAA 1894 Sec 16",
    associatedPermissions: ["Award.View", "Khasra.Verify"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-6",
    code: "WORK_COURT_LITIG",
    name: "Reference Court & High Court Litigation",
    workstreamCode: "COURT",
    workstreamName: "Court & Litigation",
    description: "Processing Section 18/64 references, filing counter-affidavits, monitoring stay orders, and compliance reports.",
    statutoryBasis: "RFCTLARR 2013 Sec 64 / LAA 1894 Sec 18 / Delhi HC Rules",
    associatedPermissions: ["Court.View", "Court.Create", "Court.Edit"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-7",
    code: "WORK_DAK_INTAKE",
    name: "Inward Dak & Communication Intake",
    workstreamCode: "DAK",
    workstreamName: "Correspondence",
    description: "Receipt, digital stamping, metadata extraction, and initial desk assignment of incoming office letters.",
    statutoryBasis: "Manual of Office Procedure (MOP) Para 14-22",
    associatedPermissions: ["Dak.View", "Dak.Register"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-8",
    code: "WORK_OUTWARD_DISP",
    name: "Outward Dispatch & Inter-Departmental Communication",
    workstreamCode: "DAK",
    workstreamName: "Correspondence",
    description: "Dispatch registration, speed post / email delivery tracking, and acknowledgment verification.",
    statutoryBasis: "Manual of Office Procedure (MOP) Para 85-98",
    associatedPermissions: ["Outward.View", "Outward.Create"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-9",
    code: "WORK_RTI_GRIEV",
    name: "RTI & Public Grievance Disposal",
    workstreamCode: "ADMIN",
    workstreamName: "Administration",
    description: "Time-bound reply formulation, record collation, and appellate reply management for RTI requests.",
    statutoryBasis: "Right to Information Act 2005 Sec 6 & 7",
    associatedPermissions: ["Dak.View", "Matter.View"],
    defaultScopeMode: "Assigned",
    isStandard: true,
    isActive: true,
  },
  {
    id: "work-10",
    code: "WORK_RECORD_ARCH",
    name: "Revenue Record Room Archival & Inspection",
    workstreamCode: "ADMIN",
    workstreamName: "Administration",
    description: "Custody, digitization, inspection requests, and certified copy issuance from legacy revenue basta.",
    statutoryBasis: "Punjab Land Records Manual Ch. 4 / Delhi Rules",
    associatedPermissions: ["Award.View", "Audit.View"],
    defaultScopeMode: "Workstream",
    isStandard: true,
    isActive: true,
  },
];

export const WorkCatalogAdmin: React.FC = () => {
  const [items, setItems] = useState<WorkCatalogItem[]>(STANDARD_WORK_CATALOG);
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedBranch, setSelectedBranch] = useState("ALL");
  const [selectedScope, setSelectedScope] = useState("ALL");

  // Create Modal
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [newCode, setNewCode] = useState("");
  const [newName, setNewName] = useState("");
  const [newBranch, setNewBranch] = useState("LAND_RECORDS");
  const [newBasis, setNewBasis] = useState("");
  const [newDesc, setNewDesc] = useState("");
  const [newDefaultScope, setNewDefaultScope] = useState<"All" | "Workstream" | "Assigned" | "Own">("Workstream");
  const [modalNotice, setModalNotice] = useState<string | null>(null);

  const filteredItems = useMemo(() => {
    return items.filter((item) => {
      const matchesSearch =
        searchQuery.trim() === "" ||
        item.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
        item.code.toLowerCase().includes(searchQuery.toLowerCase()) ||
        (item.statutoryBasis && item.statutoryBasis.toLowerCase().includes(searchQuery.toLowerCase())) ||
        item.description.toLowerCase().includes(searchQuery.toLowerCase());

      const matchesBranch = selectedBranch === "ALL" || item.workstreamCode === selectedBranch;
      const matchesScope = selectedScope === "ALL" || item.defaultScopeMode === selectedScope;

      return matchesSearch && matchesBranch && matchesScope;
    });
  }, [items, searchQuery, selectedBranch, selectedScope]);

  const handleCreateWork = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newCode.trim() || !newName.trim()) return;

    const branchNameMap: Record<string, string> = {
      LAND_RECORDS: "Land Records",
      MATTERS: "Matters & Notes",
      COURT: "Court & Litigation",
      DAK: "Correspondence",
      ADMIN: "Administration",
    };

    const newItem: WorkCatalogItem = {
      id: `work-custom-${Date.now()}`,
      code: newCode.trim().toUpperCase(),
      name: newName.trim(),
      workstreamCode: newBranch,
      workstreamName: branchNameMap[newBranch] || newBranch,
      description: newDesc.trim() || "Custom statutory work responsibility.",
      statutoryBasis: newBasis.trim() || "Office Order Allocation",
      associatedPermissions: ["Matter.View"],
      defaultScopeMode: newDefaultScope,
      isStandard: false,
      isActive: true,
    };

    setItems((prev) => [newItem, ...prev]);
    setModalNotice("Staged custom work created in catalog. Note: Backend persistence contract for custom work definitions is awaiting Codex RBAC's work catalog API.");
    setTimeout(() => {
      setShowCreateModal(false);
      setModalNotice(null);
      setNewCode("");
      setNewName("");
      setNewBasis("");
      setNewDesc("");
    }, 1500);
  };

  return (
    <div className="rbac-admin-root">
      {/* Page Header */}
      <div className="rbac-header-row">
        <div className="rbac-header-title">
          <h2>Statutory Work Catalog</h2>
          <p>
            Specific statutory responsibilities and operational task categories, distinct from physical/digital seats (OfficeDesks) and functional branches (Workstreams).
          </p>
        </div>
        <div className="rbac-actions-group">
          <button className="primary-button" onClick={() => setShowCreateModal(true)}>
            + Define New Work Category
          </button>
        </div>
      </div>

      {/* Model Distinction Banner */}
      <div className="contract-notice-banner">
        <strong>Model Architecture Notice: Work vs. Desk vs. Role</strong>
        <span>
          • <strong>Work</strong> = Specific statutory responsibility (e.g. Statement-A, Award Formulation, Compensation Disbursement).<br />
          • <strong>OfficeDesk</strong> = Physical or digital operational seat/post (e.g. Dealing Assistant Desk North, Branch Incharge Seat).<br />
          • <strong>Role</strong> = Reusable permission bundle (e.g. LAC_OFFICER, DEALING_ASSISTANT). Officers may hold multiple roles simultaneously.
        </span>
      </div>

      {/* Filter Bar */}
      <div className="rbac-filter-bar">
        <div className="rbac-search-box">
          <span className="rbac-search-icon">🔍</span>
          <input
            type="text"
            placeholder="Search work category, statutory section, or keyword..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
          />
        </div>

        <select
          className="rbac-filter-select"
          value={selectedBranch}
          onChange={(e) => setSelectedBranch(e.target.value)}
        >
          <option value="ALL">All Functional Branches</option>
          <option value="LAND_RECORDS">Land Records Branch</option>
          <option value="MATTERS">Matters & Notes Branch</option>
          <option value="COURT">Court & Litigation Branch</option>
          <option value="DAK">Correspondence Branch</option>
          <option value="ADMIN">General Administration</option>
        </select>

        <select
          className="rbac-filter-select"
          value={selectedScope}
          onChange={(e) => setSelectedScope(e.target.value)}
        >
          <option value="ALL">All Default Scopes</option>
          <option value="All">All LAC (Platform-wide)</option>
          <option value="Workstream">Branch-wide (Workstream)</option>
          <option value="Assigned">Assigned Matters Only</option>
          <option value="Own">Own Submissions Only</option>
        </select>

        <span style={{ fontSize: "13px", color: "#64748b", marginLeft: "auto" }}>
          Showing <strong>{filteredItems.length}</strong> of {items.length} Work Categories
        </span>
      </div>

      {/* Work Catalog Cards Grid */}
      <div className="work-catalog-grid">
        {filteredItems.map((work) => (
          <div key={work.id} className="work-card">
            <div>
              <div className="work-card-header">
                <div>
                  <span className="work-card-title">{work.name}</span>
                  <div style={{ marginTop: "4px", display: "flex", gap: "6px", alignItems: "center" }}>
                    <span className="work-card-code">{work.code}</span>
                    <span className="workstream-tag primary">{work.workstreamName}</span>
                  </div>
                </div>
                <span className={`scope-indicator ${work.defaultScopeMode.toLowerCase()}`}>
                  {work.defaultScopeMode} Scope
                </span>
              </div>

              <p className="work-card-desc" style={{ marginTop: "10px" }}>
                {work.description}
              </p>
            </div>

            <div>
              {work.statutoryBasis && (
                <div style={{ fontSize: "12px", color: "#475569", marginBottom: "8px" }}>
                  <strong>Statutory Basis:</strong> <span>{work.statutoryBasis}</span>
                </div>
              )}

              <div className="work-card-meta">
                <span>
                  <strong>Permissions:</strong> {work.associatedPermissions.join(", ")}
                </span>
                <span style={{ color: work.isStandard ? "#059669" : "#d97706", fontWeight: 600 }}>
                  {work.isStandard ? "Canonical Standard" : "Custom Defined"}
                </span>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Create Work Modal */}
      {showCreateModal && (
        <div className="modal-backdrop">
          <div className="modal-card" style={{ maxWidth: "600px" }}>
            <h3>Define New Statutory Work Category</h3>
            <p className="subtext" style={{ margin: "4px 0 16px 0" }}>
              Add a new statutory responsibility or operational task definition to the platform work catalog.
            </p>

            {modalNotice ? (
              <div className="form-message" style={{ background: "#ecfdf5", color: "#065f46", border: "1px solid #a7f3d0" }}>
                {modalNotice}
              </div>
            ) : (
              <form onSubmit={handleCreateWork} style={{ display: "flex", flexDirection: "column", gap: "14px" }}>
                <div className="form-group">
                  <label>Work Code (Identifier)</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. WORK_ENCROACH_EVICT"
                    value={newCode}
                    onChange={(e) => setNewCode(e.target.value)}
                  />
                  <small style={{ color: "#64748b" }}>Unique upper-case code for task allocation and audit records.</small>
                </div>

                <div className="form-group">
                  <label>Work Name / Title</label>
                  <input
                    type="text"
                    required
                    placeholder="e.g. Encroachment Eviction & Demolition"
                    value={newName}
                    onChange={(e) => setNewName(e.target.value)}
                  />
                </div>

                <div className="form-group">
                  <label>Functional Branch (Workstream)</label>
                  <select
                    value={newBranch}
                    onChange={(e) => setNewBranch(e.target.value)}
                  >
                    <option value="LAND_RECORDS">Land Records Branch</option>
                    <option value="MATTERS">Matters & Notes Branch</option>
                    <option value="COURT">Court & Litigation Branch</option>
                    <option value="DAK">Correspondence Branch</option>
                    <option value="ADMIN">General Administration</option>
                  </select>
                </div>

                <div className="form-group">
                  <label>Statutory Basis / Reference</label>
                  <input
                    type="text"
                    placeholder="e.g. Delhi Land Reforms Act 1954 Sec 86A / High Court Order"
                    value={newBasis}
                    onChange={(e) => setNewBasis(e.target.value)}
                  />
                </div>

                <div className="form-group">
                  <label>Description of Responsibility</label>
                  <textarea
                    rows={3}
                    placeholder="Describe the operational scope and statutory tasks involved in this work."
                    value={newDesc}
                    onChange={(e) => setNewDesc(e.target.value)}
                  />
                </div>

                <div className="form-group">
                  <label>Default Authority Scope</label>
                  <select
                    value={newDefaultScope}
                    onChange={(e) => setNewDefaultScope(e.target.value as any)}
                  >
                    <option value="Workstream">Workstream (Branch-wide)</option>
                    <option value="All">All LAC (Platform-wide)</option>
                    <option value="Assigned">Assigned Matters Only</option>
                    <option value="Own">Own Submissions Only</option>
                  </select>
                </div>

                <div className="contract-warning-banner">
                  <strong>Backend Contract Notice:</strong>
                  <span>
                    This custom definition is staged in the UI. Backend persistence will be activated when Codex RBAC releases the Work Catalog API endpoint.
                  </span>
                </div>

                <div className="modal-actions" style={{ marginTop: "10px" }}>
                  <button type="button" className="quiet-button" onClick={() => setShowCreateModal(false)}>
                    Cancel
                  </button>
                  <button type="submit" className="primary-button">
                    Save Work Category
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
