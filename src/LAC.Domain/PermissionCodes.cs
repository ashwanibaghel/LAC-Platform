namespace LAC.Domain;

public static class PermissionCodes
{
    public const string VillageView = "Village.View";
    public const string KhasraView = "Khasra.View";
    public const string KhasraEdit = "Khasra.Edit";
    public const string LrView = "LR.View";
    public const string LrEdit = "LR.Edit";
    public const string LrVerify = "LR.Verify";
    public const string LrCommit = "LR.Commit";
    public const string AwardView = "Award.View";
    public const string AwardCreate = "Award.Create";
    public const string AwardEdit = "Award.Edit";
    public const string AwardCoreDocumentUpload = "Award.CoreDocument.Upload";
    public const string MatterView = "Matter.View";
    public const string MatterCreate = "Matter.Create";
    public const string MatterEdit = "Matter.Edit";
    public const string MatterDocumentManage = "Matter.Document.Manage";
    public const string DraftView = "Draft.View";
    public const string DraftCreate = "Draft.Create";
    public const string DraftEdit = "Draft.Edit";
    public const string UsersManage = "Users.Manage";
    public const string AccessManage = "Access.Manage";
    public const string AuditView = "Audit.View";
    public const string DakView = "Dak.View";
    public const string DakRegister = "Dak.Register";
    public const string DakEdit = "Dak.Edit";
    public const string DakMove = "Dak.Move";
    public const string DakDispose = "Dak.Dispose";
    public const string DakCancel = "Dak.Cancel";

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(VillageView, "View Villages", "View village records and basic details", "Village"),
        new(KhasraView, "View Khasras", "View khasra records and workspace", "Khasra"),
        new(KhasraEdit, "Edit Khasras", "Create or edit khasra attributes", "Khasra"),
        new(LrView, "View LR", "View Land Records and entries", "LandRecords"),
        new(LrEdit, "Edit LR", "Edit Land Record entries and notifications", "LandRecords"),
        new(LrVerify, "Verify LR", "Verify Land Record entries", "LandRecords"),
        new(LrCommit, "Commit LR", "Commit Land Record entries to canonical state", "LandRecords"),
        new(AwardView, "View Awards", "View awards and award candidate records", "Award"),
        new(AwardCreate, "Create Awards", "Create new award instances", "Award"),
        new(AwardEdit, "Edit Awards", "Edit award metadata and details", "Award"),
        new(AwardCoreDocumentUpload, "Upload Award Core Documents", "Upload and extract core award documents", "Award"),
        new(MatterView, "View Matters", "View legal and administrative matters", "Matter"),
        new(MatterCreate, "Create Matters", "Create new matters", "Matter"),
        new(MatterEdit, "Edit Matters", "Edit existing matters", "Matter"),
        new(MatterDocumentManage, "Manage Matter Documents", "Attach or remove documents from matters", "Matter"),
        new(DraftView, "View Drafts", "View matter drafts and noting", "Draft"),
        new(DraftCreate, "Create Drafts", "Create new matter drafts", "Draft"),
        new(DraftEdit, "Edit Drafts", "Edit and revise matter drafts", "Draft"),
        new(UsersManage, "Manage Users", "Create and manage system user accounts and credentials", "Administration"),
        new(AccessManage, "Manage Access & Roles", "Manage roles, permissions, workstreams, and designations", "Administration"),
        new(AuditView, "View Audit Logs", "View system audit trail and activity history", "Administration"),
        new(DakView, "View Dak", "View inward correspondence, movement history, and attached documents", "Dak"),
        new(DakRegister, "Register Dak", "Register new inward correspondence in the official register", "Dak"),
        new(DakEdit, "Edit Dak", "Edit Dak metadata, classification, attachments, and domain links", "Dak"),
        new(DakMove, "Move Dak", "Mark, forward, or return Dak across desks and officers", "Dak"),
        new(DakDispose, "Dispose Dak", "Mark Dak as completed or disposed and close active custody", "Dak"),
        new(DakCancel, "Cancel Dak", "Void errant Dak registration with mandatory audit justification", "Dak"),
    ];
}

public sealed record PermissionDefinition(string Code, string Name, string Description, string Category);
