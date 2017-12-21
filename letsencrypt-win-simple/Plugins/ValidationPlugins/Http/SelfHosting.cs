﻿using ACMESharp.ACME;
using LetsEncrypt.ACME.Simple.Services;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace LetsEncrypt.ACME.Simple.Plugins.ValidationPlugins.Http
{
    /// <summary>
    /// Self-host the validation files
    /// </summary>
    class SelfHostingFactory : BaseHttpValidationFactory<SelfHosting>
    {
        public SelfHostingFactory(ILogService log) :  base(log, nameof(SelfHosting), "Self-host verification files (recommended)") { }
        public override void Default(Target target, IOptionsService optionsService) { }
        public override void Aquire(Target target, IOptionsService optionsService, IInputService inputService) { }
    }

    class SelfHosting : BaseHttpValidation
    {
        private HttpListener _listener;
        public Dictionary<string, string> _files;
        private Task _listeningTask;
        protected override char PathSeparator => '/';

        public SelfHosting(ScheduledRenewal renewal, Target target, string identifier, ILogService log, IInputService input, ProxyService proxy) : 
            base(log, input, proxy, renewal, target, identifier)
        {
            _files = new Dictionary<string, string>();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{new IdnMapping().GetUnicode(identifier)}:80/.well-known/acme-challenge/");
            _listener.Start();
            _listeningTask = Task.Run(RecieveRequests);
        }

        public async Task RecieveRequests()
        {
            while (_listener.IsListening)
            {
                var ctx = await _listener.GetContextAsync();
                string response = null;
                var path = ctx.Request.Url.LocalPath;
                if (_files.TryGetValue(path, out response))
                {
                    _log.Verbose("SelfHosting plugin serving file {name}", path);
                    using (var writer = new StreamWriter(ctx.Response.OutputStream))
                    {
                        writer.Write(response);
                    }
                }
                else
                {
                    _log.Warning("SelfHosting plugin couldn't serve file {name}", path);
                    ctx.Response.StatusCode = 404;
                }
            }
        }

        protected override void DeleteFile(string path) {}
        protected override void DeleteFolder(string path) {}
        protected override bool IsEmpty(string path) => true;
        protected override void WriteFile(string path, string content) => _files.Add(path, content);
        protected override string CombinePath(string root, string path) => PathSeparator + path;

        public override void CleanUp()
        {
            _listener.Stop();
            _listener.Close();
            _listener = null;
            base.CleanUp();
        }
    }
}
