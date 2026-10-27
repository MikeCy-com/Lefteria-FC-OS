using System.Security.Cryptography;
using System.Text;
using LefteriaFc.Api.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LefteriaFc.Identity.IntegrationTests;

internal sealed partial class IdentityHttpChecks
{
    private static Task LegacyBridgeConfigurationAsync()
    {
        var signing=RandomNumberGenerator.GetBytes(32);var otp=RandomNumberGenerator.GetBytes(32);
        var fingerprint=RandomNumberGenerator.GetBytes(32);
        // ASCII literal source key makes byte-identical reuse with each decoded
        // rewrite key representable without altering UTF-8 signing semantics.
        var source=Encoding.UTF8.GetBytes("synthetic-legacy-secret-32-bytes!!");
        Validate(true,signing,otp,fingerprint,source,true);
        Validate(true,signing,signing,fingerprint,source,false);
        Validate(true,signing,otp,signing,source,false);
        Validate(true,signing,otp,otp,source,false);
        Validate(true,source,otp,fingerprint,source,false);
        Validate(true,signing,source,fingerprint,source,false);
        Validate(true,signing,otp,source,source,false);
        Validate(false,signing,otp,[],[],true);
        return Task.CompletedTask;

        static void Validate(bool enabled,byte[] signing,byte[] otp,byte[] fingerprint,byte[] source,bool expected)
        {
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["MobileAuth:SigningKey"]=Convert.ToBase64String(signing),["MobileAuth:OtpKey"]=Convert.ToBase64String(otp),
                ["LegacyMobileBridge:Enabled"]=enabled.ToString(),
                ["LegacyMobileBridge:SigningSecret"]=Encoding.UTF8.GetString(source),
                ["LegacyMobileBridge:FingerprintKey"]=Convert.ToBase64String(fingerprint),
                ["LegacyMobileBridge:StartsAt"]="2026-10-27T00:00:00Z",["LegacyMobileBridge:EndsAt"]="2026-10-28T00:00:00Z"
            }).Build();
            var services=new ServiceCollection();services.AddLogging();services.AddMobileIdentity(configuration);
            using var provider=services.BuildServiceProvider();var accepted=true;
            try{_ = provider.GetRequiredService<IOptions<LegacyMobileBridgeOptions>>().Value;}
            catch(OptionsValidationException){accepted=false;}
            Check.Equal(expected,accepted,"Real bridge DI configuration failed to enforce independent keys or disabled defaults.");
        }
    }

    public static async Task<int> RunLegacyPolicyChecksAsync()
    {
        var failures=0;
        foreach(var (name,check) in new (string,Func<Task>)[]
        {
            ("Strict legacy token validation",LegacyMobileTokenValidationAsync),
            ("Legacy bridge DI configuration and key separation",LegacyBridgeConfigurationAsync)
        })
        {
            try{await check();Console.WriteLine("PASS "+name);}
            catch(Exception error){failures++;Console.Error.WriteLine("FAIL "+name+": "+error.GetType().Name+": "+error.Message);}
        }
        return failures==0?0:1;
    }
}
