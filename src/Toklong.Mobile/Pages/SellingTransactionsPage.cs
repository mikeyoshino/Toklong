using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public sealed class SellingTransactionsPage : TransactionsPage
{
    public SellingTransactionsPage(
        TransactionWorkspaceViewModelFactory factory,
        IStartupMotionPreference motionPreference)
        : base(
            factory.Create(RoleFilter.Selling),
            motionPreference)
    {
    }
}
