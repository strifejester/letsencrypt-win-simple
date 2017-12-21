﻿using LetsEncrypt.ACME.Simple.Plugins.Interfaces;
using LetsEncrypt.ACME.Simple.Services;
using System;

namespace LetsEncrypt.ACME.Simple.Plugins.Base
{
    /// <summary>
    /// Null implementation
    /// </summary>
    class NullInstallationFactory : IInstallationPluginFactory, INull
    {
        string IHasName.Name => "None";
        string IHasName.Description => "Do not run any installation steps";
        Type IHasType.Instance => typeof(NullInstallation);
        bool IInstallationPluginFactory.CanInstall(ScheduledRenewal renewal) => true;
        bool IHasName.Match(string name) => string.Equals("None", name, StringComparison.InvariantCultureIgnoreCase);
        void IInstallationPluginFactory.Aquire(ScheduledRenewal renewal, IOptionsService optionsService, IInputService inputService) { }
        void IInstallationPluginFactory.Default(ScheduledRenewal renewal, IOptionsService optionsService) { }
    }

    class NullInstallation : IInstallationPlugin
    {
        void IInstallationPlugin.Install(CertificateInfo newCertificateInfo, CertificateInfo oldCertificateInfo) { }
    }
}
