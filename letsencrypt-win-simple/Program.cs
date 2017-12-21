﻿using ACMESharp;
using Autofac;
using LetsEncrypt.ACME.Simple.Clients;
using LetsEncrypt.ACME.Simple.Extensions;
using LetsEncrypt.ACME.Simple.Plugins.Interfaces;
using LetsEncrypt.ACME.Simple.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading;

namespace LetsEncrypt.ACME.Simple
{
    partial class Program
    {
        private const string _clientName = "letsencrypt-win-simple";
        private static IInputService _input;
        private static RenewalService _renewalService;
        private static IOptionsService _optionsService;
        private static Options _options;
        private static ILogService _log;
        private static IContainer _container;

        static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        private static void Main(string[] args)
        {
            // Setup DI
            _container = AutofacBuilder.Global(args, _clientName, new PluginService());

            // Basic services
            _log = _container.Resolve<ILogService>();
            _optionsService = _container.Resolve<IOptionsService>();
            _options = _optionsService.Options;
            if (_options == null) return;
            _input = _container.Resolve<IInputService>();

            // .NET Framework check
            var dn = _container.Resolve<DotNetVersionService>();
            if (!dn.Check())
            {
                return;
            }

            // Show version information
            _input.ShowBanner();

            // Advanced services
            _renewalService = _container.Resolve<RenewalService>();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            // Main loop
            do
            {
                try
                {
                    if (_options.Renew)
                    {
                        CheckRenewals();
                        CloseDefault();
                    }
                    else if (!string.IsNullOrEmpty(_options.Plugin))
                    {
                        if (_options.Cancel)
                        {
                            CancelRenewal();
                        }
                        else
                        {
                            CreateNewCertificate(true);
                        }
                        CloseDefault();
                    }
                    else
                    {
                        MainMenu();
                    }
                }
                catch (Exception e)
                {
                    HandleException(e);
                    Environment.ExitCode = e.HResult;
                }
                if (!_options.CloseOnFinish)
                {
                    _options.Plugin = null;
                    _options.Renew = false;
                    _options.ForceRenewal = false;
                    Environment.ExitCode = 0;
                }
            } while (!_options.CloseOnFinish);
        }

        /// <summary>
        /// Handle exceptions
        /// </summary>
        /// <param name="ex"></param>
        private static void HandleException(Exception ex)
        {
            _log.Debug($"{ex.GetType().Name}: {{@e}}", ex);
            _log.Error($"{ex.GetType().Name}: {{e}}", ex.Message);
        }

        /// <summary>
        /// Present user with the option to close the program
        /// Useful to keep the console output visible when testing
        /// unattended commands
        /// </summary>
        private static void CloseDefault()
        {
            if (_options.Test && !_options.CloseOnFinish)
            {
                _options.CloseOnFinish = _input.PromptYesNo("Quit?");
            }
            else
            {
                _options.CloseOnFinish = true;
            }
        }

        /// <summary>
        /// Create new ScheduledRenewal from the options
        /// </summary>
        /// <returns></returns>
        private static ScheduledRenewal CreateRenewal(Options options)
        {
            return new ScheduledRenewal
            {
                Binding = new Target
                {
                    TargetPluginName = options.Plugin,
                    ValidationPluginName = $"{options.ValidationMode}.{options.Validation}"
                },
                New = true,
                Test = options.Test,
                CentralSslStore = options.CentralSslStore,
                CertificateStore = options.CertificateStore,
                KeepExisting = options.KeepExisting,
                InstallationPluginNames = options.Installation.Count() > 0 ? options.Installation.ToList() : null,
                Warmup = options.Warmup
            };
        }

        /// <summary>
        /// If renewal is already Scheduled, replace it with the new options
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private static ScheduledRenewal CreateRenewal(ScheduledRenewal temp)
        {
            var renewal = _renewalService.Find(temp.Binding);
            if (renewal == null)
            {
                renewal = temp;
            }
            renewal.New = true;
            renewal.Test = temp.Test;
            renewal.Binding = temp.Binding;
            renewal.CentralSslStore = temp.CentralSslStore;
            renewal.KeepExisting = temp.KeepExisting;
            renewal.Script = temp.Script;
            renewal.ScriptParameters = temp.ScriptParameters;
            renewal.Warmup = temp.Warmup;
            return renewal;
        }

        private static void CancelRenewal()
        {
            var tempRenewal = CreateRenewal(_options);
            using (var scope = AutofacBuilder.Renewal(_container, tempRenewal, false))
            {
                // Choose target plugin
                var targetPluginFactory = scope.Resolve<ITargetPluginFactory>();
                if (targetPluginFactory is INull)
                {
                    return; // User cancelled or unable to resolve
                }

                // Aquire target
                var targetPlugin = scope.Resolve<ITargetPlugin>();
                var target = targetPlugin.Default(_optionsService);
                if (target == null)
                {
                    _log.Error("Plugin {name} was unable to generate a target", targetPluginFactory.Name);
                    return;
                }

                // Find renewal
                var renewal = _renewalService.Find(target);
                if (renewal == null)
                {
                    _log.Warning("No renewal scheduled for {target}, this run has no effect", target);
                    return;
                }

                // Cancel renewal
                _renewalService.Cancel(renewal);
            }
        }

        private static void CreateNewCertificate(bool unattended)
        {
            if (unattended)
            {
                _log.Information(true, "Running in unattended mode.");
            }
            var tempRenewal = CreateRenewal(_options);
            using (var scope = AutofacBuilder.Renewal(_container, tempRenewal, !unattended))
            {
                // Choose target plugin
                var targetPluginFactory = scope.Resolve<ITargetPluginFactory>();
                if (targetPluginFactory is INull)
                {
                    return; // User cancelled or unable to resolve
                }

                // Aquire target
                var targetPlugin = scope.Resolve<ITargetPlugin>();
                var target = unattended ? targetPlugin.Default(_optionsService) : targetPlugin.Aquire(_optionsService, _input);
                var originalTarget = tempRenewal.Binding;
                tempRenewal.Binding = target;
                if (target == null)
                {
                    _log.Error("Plugin {name} was unable to generate a target", targetPluginFactory.Name);
                    return;
                }
                tempRenewal.Binding.TargetPluginName = targetPluginFactory.Name;
                tempRenewal.Binding.SSLPort = _options.SSLPort;
                tempRenewal.Binding.ValidationPluginName = originalTarget.ValidationPluginName;
                _log.Information("Plugin {name} generated target {target}", targetPluginFactory.Name, tempRenewal.Binding);
 
                // Choose validation plugin
                var validationPluginFactory = scope.Resolve<IValidationPluginFactory>();
                if (validationPluginFactory is INull)
                {

                    return; // User cancelled
                }
                else if (!validationPluginFactory.CanValidate(target))
                {
                    // Might happen in unattended mode
                    _log.Error("Validation plugin {name} is unable to validate target", validationPluginFactory.Name);
                    return;
                }

                // Configure validation
                try
                {
                    if (unattended)
                    {
                        validationPluginFactory.Default(target, _optionsService);
                    }
                    else
                    {
                        validationPluginFactory.Aquire(target, _optionsService, _input);
                    }
                    tempRenewal.Binding.ValidationPluginName = $"{validationPluginFactory.ChallengeType}.{validationPluginFactory.Name}";
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Invalid validation input");
                    return;
                }

                // Choose and configure installation plugins
                try
                {
                    var installFactories = scope.Resolve<List<IInstallationPluginFactory>>();
                    if (installFactories.Count == 0)
                    {
                        // User cancelled, otherwise we would at least have the Null-installer
                        return;
                    }
                    foreach (var installFactory in installFactories)
                    {
                        if (unattended)
                        {
                            installFactory.Default(tempRenewal, _optionsService);
                        }
                        else
                        {
                            installFactory.Aquire(tempRenewal, _optionsService, _input);
                        }
                    }
                    tempRenewal.InstallationPluginNames = installFactories.Select(f => f.Name).ToList();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Invalid installation input");
                    return;
                }

                var result = Renew(scope, CreateRenewal(tempRenewal));
                if (!result.Success)
                {
                    _log.Error("Create certificate failed");
                }
            }
        }

        private static RenewResult Renew(ScheduledRenewal renewal)
        {
            using (var scope = AutofacBuilder.Renewal(_container, renewal, false))
            {
                return Renew(scope, renewal);
            }
        }

        private static RenewResult Renew(ILifetimeScope renewalScope, ScheduledRenewal renewal)
        {
            var targetPlugin = renewalScope.Resolve<ITargetPlugin>();
            renewal.Binding = targetPlugin.Refresh(renewal.Binding);
            if (renewal.Binding == null)
            {
                _log.Error("Renewal target not found");
                return new RenewResult(new Exception("Renewal target not found"));
            }
            foreach (var target in targetPlugin.Split(renewal.Binding))
            {
                var auth = Authorize(renewalScope, target);
                if (auth.Status != _authorizationValid)
                {
                    return OnRenewFail(auth);
                }
            }
            return OnRenewSuccess(renewalScope, renewal);
        }

        /// <summary>
        /// Steps to take on authorization failed
        /// </summary>
        /// <param name="auth"></param>
        /// <returns></returns>
        public static RenewResult OnRenewFail(AuthorizationState auth)
        {
            var errors = auth.Challenges?.
                Select(c => c.ChallengePart).
                Where(cp => cp.Status == _authorizationInvalid).
                SelectMany(cp => cp.Error);

            if (errors?.Count() > 0)
            {
                _log.Error("ACME server reported:");
                foreach (var error in errors)
                {
                    _log.Error("[{_key}] {@value}", error.Key, error.Value);
                }
            }

            return new RenewResult(new AuthorizationFailedException(auth, errors?.Select(x => x.Value)));
        }

        /// <summary>
        /// Steps to take on succesful (re)authorization
        /// </summary>
        /// <param name="target"></param>
        private static RenewResult OnRenewSuccess(ILifetimeScope renewalScope, ScheduledRenewal renewal)
        {
            RenewResult result = null;
            try
            {
                var certificateService = renewalScope.Resolve<CertificateService>();
                var storePlugin = renewalScope.Resolve<IStorePlugin>();
                var oldCertificate = renewal.Certificate(storePlugin);
                var newCertificate = certificateService.RequestCertificate(renewal.Binding);
                if (newCertificate == null)
                {
                    return new RenewResult(new Exception("No certificate generated"));
                }
                else
                {
                    result = new RenewResult(newCertificate);
                }

                // Early escape for testing validation only
                if (_options.Test &&
                    renewal.New &&
                    !_input.PromptYesNo($"Do you want to save the certificate?"))
                    return result;

                // Save to store
                storePlugin.Save(newCertificate);

                // Run installation plugin(s)
                try
                {
                    var installFactories = renewalScope.Resolve<List<IInstallationPluginFactory>>();
                    var steps = installFactories.Count();
                    for (var i = 0; i < steps; i++)
                    {
                        var installFactory = installFactories[i];
                        if (!(installFactory is INull))
                        {
                            var installInstance = (IInstallationPlugin)renewalScope.Resolve(installFactory.Instance);
                            if (steps > 1)
                            {
                                _log.Information("Installation step {n}/{m}: {name}...", i + 1, steps, installFactory.Description);
                            }
                            else
                            {
                                _log.Information("Installing with {name}...", installFactory.Description);
                            }
                            installInstance.Install(newCertificate, oldCertificate);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Unable to install certificate");
                    result.Success = false;
                    result.ErrorMessage = $"Install failed: {ex.Message}";
                }

                // Delete the old certificate if specified and found
                if (!renewal.KeepExisting && oldCertificate != null)
                {
                    try
                    {
                        storePlugin.Delete(oldCertificate);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Unable to delete previous certificate");
                        //result.Success = false; // not a show-stopper, consider the renewal a success
                        result.ErrorMessage = $"Delete failed: {ex.Message}";
                    }
                }

                // Add or update renewal
                if (renewal.New &&
                    !_options.NoTaskScheduler &&
                    (!_options.Test ||
                    _input.PromptYesNo($"Do you want to automatically renew this certificate in {_renewalService.RenewalPeriod} days? This will add a task scheduler task.")))
                {
                    var taskScheduler = _container.Resolve<TaskSchedulerService>();
                    taskScheduler.EnsureTaskScheduler();
                    _renewalService.Save(renewal, result);
                }
                return result;
            }
            catch (Exception ex)
            {
                // Result might still contain the Thumbprint of the certificate 
                // that was requested and (partially? installed, which might help
                // with debugging
                HandleException(ex);
                if (result == null)
                {
                    result = new RenewResult(ex);
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
            }
            return result;
        }

        /// <summary>
        /// Loop through the store renewals and run those which are
        /// due to be run
        /// </summary>
        private static void CheckRenewals()
        {
            _log.Verbose("Checking renewals");

            var renewals = _renewalService.Renewals.ToList();
            if (renewals.Count == 0)
                _log.Warning("No scheduled renewals found.");

            var now = DateTime.UtcNow;
            foreach (var renewal in renewals)
            {
                if (_options.ForceRenewal)
                {
                    ProcessRenewal(renewal);
                }
                else
                {
                    _log.Verbose("Checking {renewal}", renewal.Binding.Host);
                    if (renewal.Date >= now)
                    {
                        _log.Information("Renewal for certificate {renewal} not scheduled, due after {date}", renewal.Binding.Host, renewal.Date.ToUserString());
                    }
                    else
                    {
                        ProcessRenewal(renewal);
                    }
                }
            }
        }

        /// <summary>
        /// Process a single renewal
        /// </summary>
        /// <param name="renewal"></param>
        private static void ProcessRenewal(ScheduledRenewal renewal)
        {
            _log.Information(true, "Renewing certificate for {renewal}", renewal.Binding.Host);
            try
            {
                // Let the plugin run
                var result = Renew(renewal);
                _renewalService.Save(renewal, result);
            }
            catch (Exception ex)
            {
                HandleException(ex);
                _log.Error("Renewal for {host} failed, will retry on next run", renewal.Binding.Host);
            }
        }

        private const string _authorizationValid = "valid";
        private const string _authorizationPending = "pending";
        private const string _authorizationInvalid = "invalid";

        /// <summary>
        /// Make sure we have authorization for every host in target
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private static AuthorizationState Authorize(ILifetimeScope renewalScope, Target target)
        {
            List<string> identifiers = target.GetHosts(false);
            List<AuthorizationState> authStatus = new List<AuthorizationState>();
            var client = renewalScope.Resolve<LetsEncryptClient>();
            foreach (var identifier in identifiers)
            {
                var authzState = client.Acme.AuthorizeIdentifier(identifier);
                if (authzState.Status == _authorizationValid && !_options.Test)
                {
                    _log.Information("Cached authorization result: {Status}", authzState.Status);
                    authStatus.Add(authzState);
                }
                else
                {
                    using (var identifierScope = AutofacBuilder.Identifier(renewalScope, target, identifier))
                    {
                        IValidationPluginFactory validationPluginFactory = null;
                        IValidationPlugin validationPlugin = null;
                        try
                        {
                            validationPluginFactory = identifierScope.Resolve<IValidationPluginFactory>();
                            validationPlugin = identifierScope.Resolve<IValidationPlugin>();
                        }
                        catch { }
                        if (validationPluginFactory == null || validationPluginFactory is INull || validationPlugin == null)
                        {
                            return new AuthorizationState { Status = _authorizationInvalid };
                        }
                        _log.Information("Authorizing {dnsIdentifier} using {challengeType} validation ({name})", identifier, validationPluginFactory.ChallengeType, validationPluginFactory.Name);
                        var challenge = client.Acme.DecodeChallenge(authzState, validationPluginFactory.ChallengeType);
                        validationPlugin.PrepareChallenge(challenge);
                        _log.Debug("Submitting answer");
                        authzState.Challenges = new AuthorizeChallenge[] { challenge };
                        client.Acme.SubmitChallengeAnswer(authzState, validationPluginFactory.ChallengeType, true);

                        // have to loop to wait for server to stop being pending.
                        // TODO: put timeout/retry limit in this loop
                        while (authzState.Status == _authorizationPending)
                        {
                            _log.Debug("Refreshing authorization");
                            Thread.Sleep(4000); // this has to be here to give ACME server a chance to think
                            var newAuthzState = client.Acme.RefreshIdentifierAuthorization(authzState);
                            if (newAuthzState.Status != _authorizationPending)
                            {
                                authzState = newAuthzState;
                            }
                        }

                        if (authzState.Status != _authorizationValid)
                        {
                            _log.Information("Authorization result: {Status}", authzState.Status);
                        }
                        else
                        {
                            _log.Error("Authorization result: {Status}", authzState.Status);
                        }
                        authStatus.Add(authzState);
                    }
                }
            }
            foreach (var authState in authStatus)
            {
                if (authState.Status != _authorizationValid)
                {
                    return authState;
                }
            }
            return new AuthorizationState { Status = _authorizationValid };
        }

    }
}