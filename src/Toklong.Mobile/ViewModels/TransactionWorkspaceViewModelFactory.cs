using Toklong.Mobile.Core;

namespace Toklong.Mobile.ViewModels;

public sealed class TransactionWorkspaceViewModelFactory(
    ITransactionService transactions,
    IDeepLinkCoordinator deepLinks,
    IMobileAnalytics analytics,
    AuthenticatedSessionBoundary session)
{
    public TransactionsViewModel Create(RoleFilter role) =>
        new(transactions, deepLinks, analytics, session, role);
}
