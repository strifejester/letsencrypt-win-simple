﻿using Autofac;
using LetsEncrypt.ACME.Simple.Extensions;
using LetsEncrypt.ACME.Simple.Plugins.TargetPlugins;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LetsEncrypt.ACME.Simple.Services
{
    class RenewalService
    {
        private ILogService _log;
        private IOptionsService _optionsService;
        private ISettingsService _settings;
        private string _configPath;
        public float RenewalPeriod { get; set; } = 60;
        private List<ScheduledRenewal> _renewalsCache = null;

        public RenewalService(ISettingsService settings, IInputService input, 
            IOptionsService options, ILogService log,  string clientName)
        {
            _log = log;
            _optionsService = options;
            _settings = settings;
            _configPath = settings.ConfigPath;
            ParseRenewalPeriod();
            // Trigger init of renewals cache
            var x = Renewals;
        }

        private void ParseRenewalPeriod()
        {
            try
            {
                RenewalPeriod = Properties.Settings.Default.RenewalDays;
                _log.Debug("Renewal period: {RenewalPeriod}", RenewalPeriod);
            }
            catch (Exception ex)
            {
                _log.Warning("Error reading RenewalDays from app config, defaulting to {RenewalPeriod} Error: {@ex}", RenewalPeriod.ToString(), ex);
            }
        }

        public IEnumerable<ScheduledRenewal> Renewals
        {
            get
            {
                if (_renewalsCache == null)
                {
                    if (_settings.RenewalStore != null)
                    {
                        _renewalsCache = _settings.RenewalStore.Select(x => Load(x, _configPath)).Where(x => x != null).ToList();
                    }
                    else
                    {
                        _renewalsCache = new List<ScheduledRenewal>();
                    }
                }
                return _renewalsCache;
            }
            set
            {
                _renewalsCache = value.ToList();
                _renewalsCache.ForEach(r =>
                {
                    if (r.Updated)
                    {
                        File.WriteAllText(HistoryFile(r, _configPath).FullName, JsonConvert.SerializeObject(r.History));
                        r.Updated = false;
                    }
                });
                _settings.RenewalStore = _renewalsCache.Select(x => JsonConvert.SerializeObject(x, 
                    new JsonSerializerSettings {
                        NullValueHandling = NullValueHandling.Ignore
                    })).ToArray();
            }
        }
  
        public ScheduledRenewal Find(Target target)
        {
            return Renewals.Where(r => string.Equals(r.Binding.Host, target.Host)).FirstOrDefault();
        }

        public void Save(ScheduledRenewal renewal, RenewResult result)
        {
            var renewals = Renewals.ToList();
            if (renewal.New)
            {
                renewal.History = new List<RenewResult>();
                renewals.Add(renewal);
                _log.Information(true, "Adding renewal for {target}", renewal.Binding.Host);

            }
            else if (result.Success)
            {
                _log.Information(true, "Renewal for {host} succeeded", renewal.Binding.Host);
            }
            else
            {
                _log.Error("Renewal for {host} failed, will retry on next run", renewal.Binding.Host);
            }

            // Set next date
            if (result.Success)
            {
                renewal.Date = DateTime.UtcNow.AddDays(RenewalPeriod);
                _log.Information(true, "Next renewal scheduled at {date}", renewal.Date.ToUserString());
            }
            renewal.Updated = true;
            renewal.History.Add(result);
            Renewals = renewals;
        }

        /// <summary>
        /// Determine location and name of the history file
        /// </summary>
        /// <param name="target"></param>
        /// <param name="configPath"></param>
        /// <returns></returns>
        private FileInfo HistoryFile(ScheduledRenewal renewal, string configPath)
        {
            return new FileInfo(Path.Combine(configPath, $"{renewal.Binding.Host}.history.json"));
        }

        private ScheduledRenewal Load(string renewal, string path)
        {
            var result = JsonConvert.DeserializeObject<ScheduledRenewal>(renewal);

            if (result == null || result.Binding == null)
            {
                _log.Error("Unable to deserialize renewal {renewal}", renewal);
                return null;
            }

            if (result.History == null)
            {
                result.History = new List<RenewResult>();
                var historyFile = HistoryFile(result, path);
                if (historyFile.Exists)
                {
                    try
                    {
                        result.History = JsonConvert.DeserializeObject<List<RenewResult>>(File.ReadAllText(historyFile.FullName));
                    }
                    catch
                    {
                        _log.Warning("Unable to read history file {path}", historyFile.Name);
                    }
                }
            }

            if (result.Binding.AlternativeNames == null)
            {
                result.Binding.AlternativeNames = new List<string>();
            }

            if (result.Binding.HostIsDns == null)
            {
                result.Binding.HostIsDns = !result.San;
            }

            if (result.Binding.IIS == null)
            {
                result.Binding.IIS = !(result.Binding.PluginName == nameof(Manual));
            }

            if (result.Binding.TargetSiteId == null)
            {
                result.Binding.TargetSiteId = result.Binding.SiteId;
            }

            return result;
        }

        internal void Cancel(ScheduledRenewal renewal)
        {
            Renewals = Renewals.Except(new[] { renewal });
            _log.Warning("Renewal {target} cancelled", renewal);
        }
    }
}
