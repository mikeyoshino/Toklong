using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace Toklong.Infrastructure.Services;

public static class DataProtectionCertificateLoader
{
    public const string CertificatePathConfigurationKey =
        "DataProtection:CertificatePath";
    public const string CertificatePasswordFileConfigurationKey =
        "DataProtection:CertificatePasswordFile";

    public static X509Certificate2? Load(
        IConfiguration configuration)
    {
        var certificatePath =
            configuration[CertificatePathConfigurationKey]?.Trim();
        var passwordFile =
            configuration[
                CertificatePasswordFileConfigurationKey]?.Trim();
        if (string.IsNullOrWhiteSpace(certificatePath) &&
            string.IsNullOrWhiteSpace(passwordFile))
            return null;
        if (string.IsNullOrWhiteSpace(certificatePath) ||
            string.IsNullOrWhiteSpace(passwordFile))
            throw new InvalidOperationException(
                "Data Protection certificate path and password file must be configured together");

        var password = File.ReadAllText(passwordFile)
            .TrimEnd('\r', '\n');
        if (password.Length < 16)
            throw new InvalidOperationException(
                "Data Protection certificate password must be at least 16 characters");

        return X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
    }
}
