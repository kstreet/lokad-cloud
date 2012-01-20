﻿#region Copyright (c) Lokad 2009-2010
// This code is released under the terms of the new BSD licence.
// URL: http://www.lokad.com/
#endregion

using System;
using System.Text;
using Lokad.Cloud.Runtime;
using Lokad.Cloud.Storage;

namespace Lokad.Cloud.Management
{
    /// <summary>
    /// Management facade for cloud configuration.
    /// </summary>
    public class CloudConfiguration
    {
        /// <summary>Name of the container used to store the assembly package.</summary>
        public const string ContainerName = "lokad-cloud-assemblies";

        /// <summary>Name of the blob used to store the optional dependency injection configuration.</summary>
        public const string ConfigurationBlobName = "config";

        readonly IBlobStorageProvider _blobProvider;
        readonly UTF8Encoding _encoding = new UTF8Encoding();

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudConfiguration"/> class.
        /// </summary>
        public CloudConfiguration(RuntimeProviders runtimeProviders)
        {
            _blobProvider = runtimeProviders.BlobStorage;
        }

        /// <summary>
        /// Get the cloud configuration file.
        /// </summary>
        public string GetConfigurationString()
        {
            var buffer = _blobProvider.GetBlob<byte[]>(ContainerName, ConfigurationBlobName);
            return buffer.Convert(bytes => _encoding.GetString(bytes), String.Empty);
        }

        /// <summary>
        /// Set or update the cloud configuration file.
        /// </summary>
        public void SetConfiguration(string configuration)
        {
            if(configuration == null)
            {
                RemoveConfiguration();
                return;
            }

            configuration = configuration.Trim();
            if(String.IsNullOrEmpty(configuration))
            {
                RemoveConfiguration();
                return;
            }

            _blobProvider.PutBlob(ContainerName, ConfigurationBlobName, _encoding.GetBytes(configuration));
        }

        /// <summary>
        /// Remove the cloud configuration file.
        /// </summary>
        public void RemoveConfiguration()
        {
            _blobProvider.DeleteBlobIfExist(ContainerName, ConfigurationBlobName);
        }
    }
}
