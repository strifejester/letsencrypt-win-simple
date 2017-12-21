﻿using Autofac;
using LetsEncrypt.ACME.Simple.Configuration;
using LetsEncrypt.ACME.Simple.Services;
using System;
using System.IO;
using System.Linq;
using System.Net;

namespace LetsEncrypt.ACME.Simple.Client
{
    class WebDavClient
    {
        private NetworkCredential _credential { get; set; }
        private ILogService _log;

        public WebDavClient(WebDavOptions options, ILogService log)
        {
            _log = log;
            _credential = options.GetCredential();
        }

        private WebDAVClient.Client GetClient(string webDavPath)
        {
            Uri webDavUri = new Uri(webDavPath);
            var scheme = webDavUri.Scheme;
            string webDavConnection = scheme + "://" + webDavUri.Host + ":" + webDavUri.Port;
            var client = new WebDAVClient.Client(_credential);
            client.Server = webDavConnection;
            client.BasePath = webDavUri.AbsolutePath;
            return client;
        }

        public void Upload(string webDavPath, string content)
        {
            try
            {
                using (MemoryStream stream = new MemoryStream())
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.Write(content);
                    writer.Flush();
                    stream.Position = 0;
                    int pathLastSlash = webDavPath.LastIndexOf("/") + 1;
                    var file = webDavPath.Substring(pathLastSlash);
                    var path = webDavPath.Remove(pathLastSlash);
                    var client = GetClient(path);
                    var fileUploaded = client.Upload("/", stream, file).Result;
                    _log.Verbose("Upload status {StatusDescription}", fileUploaded);
                }
            }
            catch (Exception ex)
            {
                _log.Verbose("WebDav error {@ex}", ex);
                _log.Warning("Error uploading file {webDavPath} {Message}", webDavPath, ex.Message);
            }

        }

        public async void Delete(string webDavPath)
        {
            var client = GetClient(webDavPath);
            try
            {
                await client.DeleteFile(webDavPath);
            }
            catch (Exception ex)
            {
                _log.Verbose("WebDav error {@ex}", ex);
                _log.Warning("Error deleting file/folder {webDavPath} {Message}", webDavPath, ex.Message);
            }
        }

        public string GetFiles(string webDavPath)
        {
            try
            {
                var client = GetClient(webDavPath);
                var folderFiles = client.List().Result;
                var names = string.Join(",", folderFiles.Select(x => x.DisplayName.Trim()).ToArray());
                _log.Verbose("Files in path {webDavPath}: {@names}", webDavPath, names);
                return names;
            }
            catch (Exception ex)
            {
                _log.Verbose("WebDav error {@ex}", ex);
                return string.Empty;
            }
        }
    }
}