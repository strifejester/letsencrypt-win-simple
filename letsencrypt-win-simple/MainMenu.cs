﻿using ACMESharp.JOSE;
using Autofac;
using LetsEncrypt.ACME.Simple.Clients;
using LetsEncrypt.ACME.Simple.Extensions;
using LetsEncrypt.ACME.Simple.Plugins;
using LetsEncrypt.ACME.Simple.Plugins.Resolvers;
using LetsEncrypt.ACME.Simple.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LetsEncrypt.ACME.Simple
{
    partial class Program
    {
        /// <summary>
        /// Main user experience
        /// </summary>
        private static void MainMenu()
        {
            var options = new List<Choice<Action>>
            {
                Choice.Create<Action>(() => CreateNewCertificate(false), "Create new certificate", "N"),
                Choice.Create<Action>(() => ShowCertificates(), "List scheduled renewals", "L"),
                Choice.Create<Action>(() => CheckRenewals(), "Renew scheduled", "R"),
                Choice.Create<Action>(() => RenewSpecific(), "Renew specific", "S"),
                Choice.Create<Action>(() => { _options.ForceRenewal = true; CheckRenewals(); _options.ForceRenewal = false; }, "Renew *all*", "A"),
                Choice.Create<Action>(() => RevokeCertificate(), "Revoke certificate", "V"),
                Choice.Create<Action>(() => CancelSingleRenewal(), "Cancel scheduled renewal", "C"),
                Choice.Create<Action>(() => CancelAllRenewals(), "Cancel *all* scheduled renewals", "X"),
                Choice.Create<Action>(() => { _options.CloseOnFinish = true; _options.Test = false; }, "Quit", "Q")
            };
            _input.ChooseFromList("Please choose from the menu", options, false).Invoke();
        }

        /// <summary>
        /// Show certificate details
        /// </summary>
        private static void ShowCertificates()
        {
            var target = _input.ChooseFromList("Show details for renewal?",
                _renewalService.Renewals.OrderBy(x => x.Date),
                x => Choice.Create(x),
                true);
            if (target != null)
            {
                try
                {
                    using (var scope = AutofacBuilder.Renewal(_container, target, false))
                    {
                        var resolver = scope.Resolve<UnattendedResolver>();
                        _input.Show("Name", target.Binding.Host, true);
                        _input.Show("AlternativeNames", string.Join(", ", target.Binding.AlternativeNames));
                        _input.Show("ExcludeBindings", target.Binding.ExcludeBindings);
                        _input.Show("Target plugin", resolver.GetTargetPlugin(scope).Description);
                        _input.Show("Validation plugin", resolver.GetValidationPlugin(scope).Description);
                        _input.Show("Store plugin", resolver.GetStorePlugin(scope).Description);
                        _input.Show("Install plugin(s)", string.Join(", ", resolver.GetInstallationPlugins(scope).Select(x => x.Description)));
                        _input.Show("Renewal due", target.Date.ToUserString());
                        _input.Show("Script", target.Script);
                        _input.Show("ScriptParameters", target.ScriptParameters);
                        _input.Show("CentralSslStore", target.CentralSslStore);
                        _input.Show("KeepExisting", target.KeepExisting.ToString());
                        _input.Show("Warmup", target.Warmup.ToString());
                        _input.Show("Renewed", $"{target.History.Count} times");
                        _input.WritePagedList(target.History.Select(x => Choice.Create(x)));
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Unable to list details for target");
                }
            }
        }

        /// <summary>
        /// Renew specific certificate
        /// </summary>
        private static void RenewSpecific()
        {
            var target = _input.ChooseFromList("Which renewal would you like to run?",
                _renewalService.Renewals,
                x => Choice.Create(x),
                true);
            if (target != null)
            {
                ProcessRenewal(target);
            }
        }

        /// <summary>
        /// Revoke certificate
        /// </summary>
        private static void RevokeCertificate()
        {
            var target = _input.ChooseFromList("Which certificate would you like to revoke?",
                _renewalService.Renewals,
                x => Choice.Create(x),
                true);
            if (target != null)
            {
                if (_input.PromptYesNo($"Are you sure you want to revoke the most recently issued certificate for {target.Binding}?"))
                {
                    using (var scope = AutofacBuilder.Renewal(_container, target, false))
                    {
                        var cs = scope.Resolve<CertificateService>();
                        try
                        {
                            cs.RevokeCertificate(target.Binding);
                            target.History.Add(new RenewResult(new Exception($"Certificate revoked")));
                        }
                        catch (Exception ex)
                        {
                            HandleException(ex);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Cancel a renewal
        /// </summary>
        private static void CancelSingleRenewal()
        {
            var renewal = _input.ChooseFromList("Which renewal would you like to cancel?",
                _renewalService.Renewals,
                x => Choice.Create(x),
                true);

            if (renewal != null)
            {
                if (_input.PromptYesNo($"Are you sure you want to cancel the renewal for {renewal.Binding}"))
                {
                    _renewalService.Cancel(renewal);
                }
            }
        }

        /// <summary>
        /// Cancel all renewals
        /// </summary>
        private static void CancelAllRenewals()
        {
            _input.WritePagedList(_renewalService.Renewals.Select(x => Choice.Create(x)));
            if (_input.PromptYesNo("Are you sure you want to delete all of these?"))
            {
                _renewalService.Renewals = new List<ScheduledRenewal>();
                _log.Warning("All scheduled renewals cancelled at user request");
            }
        }
    }
}
