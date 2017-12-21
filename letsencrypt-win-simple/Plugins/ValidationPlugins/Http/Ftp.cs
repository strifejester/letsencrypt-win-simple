﻿using LetsEncrypt.ACME.Simple.Clients;
using LetsEncrypt.ACME.Simple.Configuration;
using LetsEncrypt.ACME.Simple.Services;
using System.Linq;

namespace LetsEncrypt.ACME.Simple.Plugins.ValidationPlugins.Http
{
    /// <summary>
    /// Ftp validation
    /// </summary>
    class FtpFactory : BaseHttpValidationFactory<Ftp>
    {
        public FtpFactory(ILogService log) : base(log, nameof(Ftp), "Upload verification file to FTP(S) server") {}

        public override bool CanValidate(Target target) => string.IsNullOrEmpty(target.WebRootPath) || ValidateWebroot(target);

        public override bool ValidateWebroot(Target target)
        {
            return target.WebRootPath.StartsWith("ftp");
        }

        public override string[] WebrootHint()
        {
            return new[] {
                "Enter an ftp path that leads to the web root of the host for http authentication",
                    " Example, ftp://domain.com:21/site/wwwroot/",
                    " Example, ftps://domain.com:990/site/wwwroot/"
                };
        }

        public override void Default(Target target, IOptionsService optionsService)
        {
            base.Default(target, optionsService);
            target.HttpFtpOptions = new FtpOptions(optionsService);
        }

        public override void Aquire(Target target, IOptionsService optionsService, IInputService inputService)
        {
            base.Aquire(target, optionsService, inputService);
            target.HttpFtpOptions = new FtpOptions(optionsService, inputService);
        }
    }

    class Ftp : BaseHttpValidation
    {
        private FtpClient _ftpClient;

        public Ftp(ScheduledRenewal renewal, Target target, ILogService log, IInputService input, ProxyService proxy, string identifier) : 
            base(log, input, proxy, renewal, target, identifier)
        {
            _ftpClient = new FtpClient(target.HttpFtpOptions, log);
        }

        protected override char PathSeparator => '/';

        protected override void DeleteFile(string path)
        {
            _ftpClient.Delete(path, FtpClient.FileType.File);
        }

        protected override void DeleteFolder(string path)
        {
            _ftpClient.Delete(path, FtpClient.FileType.Directory);
        }

        protected override bool IsEmpty(string path)
        {
            return _ftpClient.GetFiles(path).Count() == 0;
        }

        protected override void WriteFile(string path, string content)
        {
            _ftpClient.Upload(path, content);
        }

        public override void CleanUp()
        {
            _ftpClient = null;
            base.CleanUp();
        }
    }
}
