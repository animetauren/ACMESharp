﻿using ACMESharp.POSH.Vault;
using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using ACMESharp.PKI;

namespace ACMESharp.POSH
{
    [Cmdlet(VerbsCommon.Get, "Certificate", DefaultParameterSetName = PSET_LIST)]
    [OutputType(typeof(CertificateInfo))]
    public class GetCertificate : Cmdlet
    {
        public const string PSET_LIST = "List";
        public const string PSET_GET = "Get";

        [Parameter(ParameterSetName = PSET_GET)]
        public string Ref
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string ExportKeyPEM
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string ExportCsrPEM
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string ExportCertificatePEM
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string ExportCertificateDER
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string ExportPkcs12
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public SwitchParameter Overwrite
        { get; set; }

        [Parameter(ParameterSetName = PSET_GET)]
        public string VaultProfile
        { get; set; }

        protected override void ProcessRecord()
        {
            using (var vp = InitializeVault.GetVaultProvider(VaultProfile))
            {
                vp.OpenStorage();
                var v = vp.LoadVault();

                if (v.Registrations == null || v.Registrations.Count < 1)
                    throw new InvalidOperationException("No registrations found");

                var ri = v.Registrations[0];
                var r = ri.Registration;

                if (string.IsNullOrEmpty(Ref))
                {
                    int seq = 0;
                    WriteObject(v.Certificates.Values.Select(x => new
                    {
                        Seq = seq++,
                        Certificate = x,
                        //Id = x.Id,
                        //Alias = x.Alias,
                        //Label = x.Label,
                        //StatusCode = x.CertificateRequest.StatusCode
                    }), true);
                }
                else
                {
                    if (v.Certificates == null || v.Certificates.Count < 1)
                        throw new InvalidOperationException("No certificates found");

                    var ci = v.Certificates.GetByRef(Ref);
                    if (ci == null)
                        throw new ItemNotFoundException("Unable to find a Certificate for the given reference");

                    var mode = Overwrite ? FileMode.Create : FileMode.CreateNew;

                    if (!string.IsNullOrEmpty(ExportKeyPEM))
                    {
                        if (string.IsNullOrEmpty(ci.KeyPemFile))
                            throw new InvalidOperationException("Cannot export private key; it hasn't been imported or generated");
                        CopyTo(vp, VaultAssetType.KeyPem, ci.KeyPemFile, ExportKeyPEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCsrPEM))
                    {
                        if (string.IsNullOrEmpty(ci.CsrPemFile))
                            throw new InvalidOperationException("Cannot export CSR; it hasn't been imported or generated");
                        CopyTo(vp, VaultAssetType.CsrPem, ci.CsrPemFile, ExportCsrPEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCertificatePEM))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtPemFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        CopyTo(vp, VaultAssetType.CrtPem, ci.CrtPemFile, ExportCertificatePEM, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportCertificateDER))
                    {
                        if (ci.CertificateRequest == null || string.IsNullOrEmpty(ci.CrtDerFile))
                            throw new InvalidOperationException("Cannot export CRT; CSR hasn't been submitted or CRT hasn't been retrieved");
                        CopyTo(vp, VaultAssetType.CrtDer, ci.CrtDerFile, ExportCertificateDER, mode);
                    }

                    if (!string.IsNullOrEmpty(ExportPkcs12))
                    {
                        if (string.IsNullOrEmpty(ci.KeyPemFile))
                            throw new InvalidOperationException("Cannot export PKCS12; private hasn't been imported or generated");
                        if (string.IsNullOrEmpty(ci.CrtPemFile))
                            throw new InvalidOperationException("Cannot export PKCS12; CSR hasn't been submitted or CRT hasn't been retrieved");
                        if (string.IsNullOrEmpty(ci.IssuerSerialNumber) || !v.IssuerCertificates.ContainsKey(ci.IssuerSerialNumber))
                            throw new InvalidOperationException("Cannot export PKCS12; Issuer certificate hasn't been resolved");

                        var keyPemAsset = vp.GetAsset(VaultAssetType.KeyPem, ci.KeyPemFile);
                        var crtPemAsset = vp.GetAsset(VaultAssetType.CrtPem, ci.CrtPemFile);
                        var isuPemAsset = vp.GetAsset(VaultAssetType.IssuerPem,
                                v.IssuerCertificates[ci.IssuerSerialNumber].CrtPemFile);

                        using (var cp = CertificateProvider.GetProvider())
                        {

                            using (Stream keyStream = vp.LoadAsset(keyPemAsset),
                                crtStream = vp.LoadAsset(crtPemAsset),
                                isuStream = vp.LoadAsset(isuPemAsset),
                                fs = new FileStream(ExportPkcs12, mode))
                            {
                                var pk = cp.ImportPrivateKey<RsaPrivateKey>(EncodingFormat.PEM, keyStream);
                                var crt = cp.ImportCertificate(EncodingFormat.PEM, crtStream);
                                var isu = cp.ImportCertificate(EncodingFormat.PEM, isuStream);

                                var certs = new[] { crt, isu };

                                cp.ExportArchive(pk, certs, ArchiveFormat.PKCS12, fs);
                            }
                        }
                    }

                    WriteObject(ci);
                }
            }
        }

        public static void CopyTo(IVaultProvider vp, VaultAssetType vat, string van, string target, FileMode mode)
        {
            var asset = vp.GetAsset(vat, van);
            using (Stream s = vp.LoadAsset(asset),
                    fs = new FileStream(target, mode))
            {
                s.CopyTo(fs);
            }
        }
    }
}
