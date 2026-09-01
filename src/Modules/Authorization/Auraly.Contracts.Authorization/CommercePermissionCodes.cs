namespace Auraly.Contracts.Authorization;

public static class CommercePermissionCodes
{
    public const string CatalogRead = "catalog.read";
    public const string CatalogWrite = "catalog.write";
    public const string EnrolledDevicesEnroll = "pos.devices.enroll";
    public const string SalesCreate = "sales.create";
    public const string SalesDiscount = "sales.discount";
    public const string SalesChangePrice = "sales.change-price";
    public const string SalesReprint = "sales.reprint";
    public const string SalesRemoveLine = "sales.lines.remove";
    public const string SalesRestartDraft = "sales.drafts.restart";
    public const string PosApprovalsAuthorize = "pos.approvals.authorize";
    public const string PosApprovalsRead = "pos.approvals.read";
    public const string PosApprovalsReceiveNotifications = "pos.approvals.receive_notifications";
    public const string PosApprovalsManageCredential = "pos.approvals.manage_credential";
    public const string PosWorkspaceChange = "pos.workspace.change";
    public const string SalesReturn = "sales.return";
    public const string CashCount = "cash.count";
    public const string CashRead = "cash.read";
    public const string CashOpen = "cash.open";
    public const string CashHandoffApprove = "cash.handoff.approve";
    public const string CashClose = "cash.close";
}
