using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentValidation;
using FluentValidation.Validators;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;

namespace Sonarr.Api.V3.Config
{
    public static class CertificateValidation
    {
        public static IRuleBuilderOptions<T, string> IsValidCertificate<T>(this IRuleBuilder<T, string> ruleBuilder)
        {
            return ruleBuilder.SetValidator(new CertificateValidator());
        }
    }

    public class CertificateValidator : PropertyValidator
    {
        protected override string GetDefaultMessageTemplate() => "Invalid SSL certificate file or {passwordOrKey}. {message}";

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(CertificateValidator));

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return false;
            }

            if (context.InstanceToValidate is not HostConfigResource resource)
            {
                return true;
            }

            var certPath = resource.SslCertPath;
            var keyPath = resource.SslKeyPath;
            var certPassword = resource.SslCertPassword;
            var isPem = certPath.GetPathExtension() == ".pem" || keyPath.IsNotNullOrWhiteSpace();

            try
            {
                if (isPem)
                {
                    X509Certificate2.CreateFromPemFile(certPath, keyPath.IsNullOrWhiteSpace() ? null : keyPath);
                }
                else
                {
                    new X509Certificate2(certPath, certPassword, X509KeyStorageFlags.DefaultKeySet);
                }

                return true;
            }
            catch (CryptographicException ex)
            {
                var passwordOrKey = isPem ? "key" : "password";

                Logger.Debug(ex, "Invalid SSL certificate file or {0}. {1}", passwordOrKey, ex.Message);

                context.MessageFormatter.AppendArgument("passwordOrKey", passwordOrKey);
                context.MessageFormatter.AppendArgument("message", ex.Message);

                return false;
            }
        }
    }
}
