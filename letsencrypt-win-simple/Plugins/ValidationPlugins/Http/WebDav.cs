﻿using LetsEncrypt.ACME.Simple.Client;
using LetsEncrypt.ACME.Simple.Configuration;
using LetsEncrypt.ACME.Simple.Services;
using System.Linq;

namespace LetsEncrypt.ACME.Simple.Plugins.ValidationPlugins.Http
{
    /// <summary>
    /// WebDav validation
    /// </summary>
    class WebDavFactory : BaseHttpValidationFactory<WebDav>
    {
        public WebDavFactory(ILogService log) : base(log, nameof(WebDav), "Upload verification file to WebDav path") { }
        public override bool CanValidate(Target target) => string.IsNullOrEmpty(target.WebRootPath) || ValidateWebroot(target);
        public override bool ValidateWebroot(Target target) => target.WebRootPath.StartsWith("\\\\");

        public override string[] WebrootHint()
        {
            return new[] {
                "Enter a webdav path that leads to the web root of the host for http authentication",
                " Example, \\\\domain.com:80\\",
                " Example, \\\\domain.com:443\\"
            };
        }

        public override void Default(Target target, IOptionsService optionsService)
        {
            base.Default(target, optionsService);
            target.HttpWebDavOptions = new WebDavOptions(optionsService);
        }

        public override void Aquire(Target target, IOptionsService optionsService, IInputService inputService)
        {
            base.Aquire(target, optionsService, inputService);
            target.HttpWebDavOptions = new WebDavOptions(optionsService, inputService);
        }
    }

    class WebDav : BaseHttpValidation
    {
        private WebDavClient _webdavClient;

        public WebDav(ScheduledRenewal renewal, Target target, ILogService log, IInputService input, IOptionsService options, ProxyService proxy, string identifier) : 
            base(log, input, proxy, renewal, target, identifier)
        {
            _webdavClient = new WebDavClient(target.HttpWebDavOptions, log);
        }

        protected override void DeleteFile(string path)
        {
            _webdavClient.Delete(path);
        }

        protected override void DeleteFolder(string path)
        {
            _webdavClient.Delete(path);
        }

        protected override bool IsEmpty(string path)
        {
            return _webdavClient.GetFiles(path).Count() == 0;
        }

        protected override void WriteFile(string path, string content)
        {
            _webdavClient.Upload(path, content);
        }

        public override void CleanUp()
        {
            _webdavClient = null;
            base.CleanUp();
        }
    }
}
