using Toklong.Mobile.Core;
using Toklong.Mobile.ViewModels;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerOfferNavigationTests
{
    [Fact]
    public async Task Successful_accept_replaces_offer_with_created_detail()
    {
        Shell.Current = new Shell();
        var transaction = Transaction();
        var service = new SellerOfferServiceStub(transaction);
        var viewModel = new SellerOfferViewModel(
            service,
            new AddressServiceStub());
        await viewModel.LoadAsync("public-token");
        viewModel.TransferRightsAttested = true;
        viewModel.AcceptedTerms = true;
        var navigated = RoutesCompleted(2);

        viewModel.AcceptCommand.Execute(null);
        await navigated.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            ["..", "TransactionDetailPage"],
            Shell.Current.Routes);
        var detail = Assert.Single(Shell.Current.ParameterizedRoutes);
        Assert.Equal(transaction.Id, detail.Parameters["TransactionId"]);
        Assert.Equal(1, service.AcceptCalls);
        Assert.False(viewModel.HasMessage);
    }

    [Fact]
    public async Task Successful_decline_returns_to_the_originating_page()
    {
        Shell.Current = new Shell();
        var service = new SellerOfferServiceStub(Transaction());
        var viewModel = new SellerOfferViewModel(
            service,
            new AddressServiceStub());
        await viewModel.LoadAsync("public-token");
        var navigated = RoutesCompleted(1);

        viewModel.DeclineCommand.Execute(null);
        await navigated.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([".."], Shell.Current.Routes);
        Assert.Equal(1, service.DeclineCalls);
        Assert.False(viewModel.HasMessage);
    }

    private static Task RoutesCompleted(int expected)
    {
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        Shell.Current.Navigate = _ =>
        {
            if (++count == expected)
                completed.TrySetResult();
            return Task.CompletedTask;
        };
        return completed.Task;
    }

    private static AppTransaction Transaction() => new(
        Guid.Parse("00000000-0000-0000-0000-000000000951"),
        "สิทธิ์ดิจิทัล",
        2_500_00,
        "THB",
        AppTransactionRole.Seller,
        AppFulfillmentType.Digital,
        "AwaitingSellerAcceptance",
        DateTimeOffset.Parse("2026-08-01T10:00:00+07:00"),
        DateTimeOffset.Parse("2026-08-02T10:00:00+07:00"),
        "ผู้ซื้อ");

    private sealed class SellerOfferServiceStub(AppTransaction transaction)
        : ISellerOfferService
    {
        private readonly MobilePayoutAccount payout = new(
            Guid.Parse("00000000-0000-0000-0000-000000000952"),
            "KBANK",
            "ผู้ขาย",
            "xxx-x-x1234-x",
            true);

        public int AcceptCalls { get; private set; }
        public int DeclineCalls { get; private set; }

        public Task<SellerOfferInvitation> GetAsync(
            string publicToken,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SellerOfferInvitation(
                transaction,
                2_400_00,
                [payout],
                null));

        public Task<AppTransaction> AcceptAsync(
            string publicToken,
            Guid payoutAccountId,
            bool transferRightsAttested,
            bool sellerAcceptedTerms,
            SellerShippingSelection? shipping,
            CancellationToken cancellationToken = default)
        {
            AcceptCalls++;
            return Task.FromResult(transaction);
        }

        public Task<AppTransaction> DeclineAsync(
            string publicToken,
            CancellationToken cancellationToken = default)
        {
            DeclineCalls++;
            return Task.FromResult(transaction);
        }

        public Task<IReadOnlyList<MobilePayoutAccount>>
            GetPayoutAccountsAsync(
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MobileShippingQuote>>
            GetShippingQuotesAsync(
                string publicToken,
                SellerShippingQuoteRequest request,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MobilePayoutAccount>>
            SavePayoutAccountAsync(
                Guid? accountId,
                string bankCode,
                string accountName,
                string accountNumber,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AddressServiceStub : IAddressService
    {
        public Task<IReadOnlyList<AddressOption>> GetProvincesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AddressOption>> GetDistrictsAsync(
            int provinceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SubdistrictOption>> GetSubdistrictsAsync(
            int districtId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
