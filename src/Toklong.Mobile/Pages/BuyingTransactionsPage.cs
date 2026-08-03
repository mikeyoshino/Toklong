using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Pages;

public sealed class BuyingTransactionsPage : TransactionsPage
{
    public BuyingTransactionsPage(
        TransactionWorkspaceViewModelFactory factory,
        IStartupMotionPreference motionPreference)
        : base(
            factory.Create(RoleFilter.Buying),
            motionPreference)
    {
    }
}
