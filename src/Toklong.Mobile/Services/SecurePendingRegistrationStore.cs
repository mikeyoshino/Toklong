using Microsoft.Maui.Storage;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class SecurePendingRegistrationStore
    : IPendingRegistrationStore
{
    private const string Prefix =
        "toklong.auth.pending-registration.";
    private const string TicketKey = $"{Prefix}ticket";
    private const string ExpiryKey = $"{Prefix}expiry";
    private const string MaskedPhoneKey = $"{Prefix}masked-phone";
    private const string InstallationIdKey =
        $"{Prefix}installation-id";
    private const string IdempotencyKey =
        $"{Prefix}completion-idempotency-key";

    public async Task<PendingMobileRegistration?> GetValidAsync(
        DateTimeOffset now)
    {
        try
        {
            var ticket = await SecureStorage.Default.GetAsync(
                TicketKey);
            var rawExpiry = await SecureStorage.Default.GetAsync(
                ExpiryKey);
            var maskedPhone = await SecureStorage.Default.GetAsync(
                MaskedPhoneKey);
            var installationId =
                await SecureStorage.Default.GetAsync(
                    InstallationIdKey);
            var idempotencyKey =
                await SecureStorage.Default.GetAsync(
                    IdempotencyKey);
            if (!DateTimeOffset.TryParse(
                    rawExpiry,
                    out var expiresAt))
            {
                Clear();
                return null;
            }

            var pending = new PendingMobileRegistration(
                ticket ?? "",
                expiresAt,
                maskedPhone ?? "",
                installationId ?? "",
                idempotencyKey ?? "");
            if (pending.ExpiresAt <= now)
            {
                Clear();
                return null;
            }
            return pending;
        }
        catch (ArgumentException)
        {
            Clear();
            return null;
        }
    }

    public async Task SaveAsync(PendingMobileRegistration pending)
    {
        ArgumentNullException.ThrowIfNull(pending);
        await SecureStorage.Default.SetAsync(
            TicketKey,
            pending.RegistrationTicket);
        await SecureStorage.Default.SetAsync(
            ExpiryKey,
            pending.ExpiresAt.ToString("O"));
        await SecureStorage.Default.SetAsync(
            MaskedPhoneKey,
            pending.MaskedPhoneNumber);
        await SecureStorage.Default.SetAsync(
            InstallationIdKey,
            pending.InstallationId);
        await SecureStorage.Default.SetAsync(
            IdempotencyKey,
            pending.CompletionIdempotencyKey);
    }

    public void Clear()
    {
        SecureStorage.Default.Remove(TicketKey);
        SecureStorage.Default.Remove(ExpiryKey);
        SecureStorage.Default.Remove(MaskedPhoneKey);
        SecureStorage.Default.Remove(InstallationIdKey);
        SecureStorage.Default.Remove(IdempotencyKey);
    }
}
