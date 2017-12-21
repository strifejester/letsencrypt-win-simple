﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LetsEncrypt.ACME.Simple
{
    public class CertificateInfo
    {
        public X509Certificate2 Certificate { get; set; }
        public X509Store Store { get; set; }
        public FileInfo PfxFile { get; set; }

        public string SubjectName
        {
            get
            {
                return Certificate.Subject.Replace("CN=", "").Trim();
            }
        }

        public List<string> HostNames
        {
            get
            {
                var ret = new List<string>();
                foreach (var x in Certificate.Extensions)
                {
                    if (x.Oid.Value.Equals("2.5.29.17"))
                    {
                        AsnEncodedData asndata = new AsnEncodedData(x.Oid, x.RawData);
                        var parts = asndata.Format(true).Trim().Split('\n');
                        foreach (var part in parts)
                        {
                            var domainString = part.Replace("DNS Name=", "").Trim();
                            // IDN
                            var idnIndex = domainString.IndexOf('(');
                            if (idnIndex > -1)
                            {
                                domainString = domainString.Substring(0, idnIndex).Trim();
                            }
                            ret.Add(domainString);
                        }
                    }
                }
                return ret;
            }
        }
    }
}
