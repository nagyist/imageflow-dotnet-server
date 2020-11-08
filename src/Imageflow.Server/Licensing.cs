using System;
using System.Collections.Generic;
using System.Linq;
using Imazen.Common.Licensing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;

namespace Imageflow.Server
{
    internal class Licensing : ILicenseConfig
    {
        private ImageflowMiddlewareOptions options;

        private readonly Func<Uri> getCurrentRequestUrl;

        private readonly LicenseManagerSingleton mgr;
        
        Computation cachedResult;
        internal Licensing(LicenseManagerSingleton mgr, Func<Uri> getCurrentRequestUrl = null)
        {
            this.mgr = mgr;
            this.getCurrentRequestUrl = getCurrentRequestUrl;
        }

        internal void Initialize(ImageflowMiddlewareOptions options)
        {
            this.options = options;
            mgr.MonitorLicenses(this);
            mgr.MonitorHeartbeat(this);

            // Ensure our cache is appropriately invalidated
            cachedResult = null;
            mgr.AddLicenseChangeHandler(this, (me, manager) => me.cachedResult = null);

            // And repopulated, so that errors show up.
            if (Result == null) {
                throw new ApplicationException("Failed to populate license result");
            }
        }

        public bool EnforcementEnabled()
        {
            return !string.IsNullOrEmpty(options.LicenseKey)
                || string.IsNullOrEmpty(options.MyOpenSourceProjectUrl);
        }
        public IEnumerable<KeyValuePair<string, string>> GetDomainMappings()
        {
            return Enumerable.Empty<KeyValuePair<string, string>>();
        }

        public IEnumerable<IEnumerable<string>> GetFeaturesUsed()
        {
            return new [] {new [] {"Imageflow"}};
        }

        public IEnumerable<string> GetLicenses()
        {
            if (!string.IsNullOrEmpty(options?.LicenseKey))
            {
                return Enumerable.Repeat(options.LicenseKey, 1);
            }
            else
            {
                return Enumerable.Empty<string>();
            }
        }

        public LicenseAccess LicenseScope => LicenseAccess.Local;

        public LicenseErrorAction LicenseEnforcement
        {
            get
            {
                return options.EnforcementMethod switch
                {
                    Server.EnforceLicenseWith.RedDotWatermark => LicenseErrorAction.Watermark,
                    Server.EnforceLicenseWith.Http422Error => LicenseErrorAction.Http422,
                    Server.EnforceLicenseWith.Http402Error => LicenseErrorAction.Http402,
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }

        public string EnforcementMethodMessage
        {
            get
            {
                return options.EnforcementMethod switch
                {
                    Server.EnforceLicenseWith.RedDotWatermark =>
                        "You are using EnforceLicenseWith.RedDotWatermark. If there is a licensing error, an red dot will be drawn on the bottom-right corner of each image. This can be set to EnforceLicenseWith.Http402Error instead (valuable if you are externally caching or storing result images.)",
                    Server.EnforceLicenseWith.Http422Error =>
                        "You are using EnforceLicenseWith.Http422Error. If there is a licensing error, HTTP status code 422 will be returned instead of serving the image. This can also be set to EnforceLicenseWith.RedDotWatermark.",
                    Server.EnforceLicenseWith.Http402Error =>
                        "You are using EnforceLicenseWith.Http402Error. If there is a licensing error, HTTP status code 402 will be returned instead of serving the image. This can also be set to EnforceLicenseWith.RedDotWatermark.",
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }

        public event LicenseConfigEvent LicensingChange;
        
        public event LicenseConfigEvent Heartbeat;
        public bool IsImageflow => true;
        public bool IsImageResizer => false;
        public string LicensePurchaseUrl => "https://imageresizing.net/licenses";

        public string AGPLCompliantMessage
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(options.MyOpenSourceProjectUrl))
                {
                    return "You have certified that you are complying with the AGPLv3 and have open-sourced your project at the following url:\r\n"
                        + options.MyOpenSourceProjectUrl;
                }
                else
                {
                    return "";
                }
            }
        }

        internal Computation Result
        {
            get {
                if (cachedResult?.ComputationExpires != null &&
                    cachedResult.ComputationExpires.Value < mgr.Clock.GetUtcNow()) {
                    cachedResult = null;
                }
                return cachedResult ??= new Computation(this, mgr.TrustedKeys,mgr, mgr,
                    mgr.Clock, EnforcementEnabled());
            }
        }

        public string InvalidLicenseMessage =>
            "Imageflow cannot validate your license; visit /imageflow.debug or /imageflow.license to troubleshoot.";

        internal bool RequestNeedsEnforcementAction(HttpRequest request)
        {
            if (!EnforcementEnabled()) {
                return false;
            }
            
            var requestUrl = getCurrentRequestUrl != null ? getCurrentRequestUrl() : 
                new Uri(request.GetEncodedUrl());

            var isLicensed = Result.LicensedForRequestUrl(requestUrl);
            if (isLicensed) {
                return false;
            }

            if (requestUrl == null && Result.LicensedForSomething()) {
                return false;
            }

            return true;
        }


        public string GetLicensePageContents()
        {
            return Result.ProvidePublicLicensesPage();
        }
    }
}