/* 
Licensed to the Apache Software Foundation (ASF) under one
or more contributor license agreements.  See the NOTICE file
distributed with this work for additional information
regarding copyright ownership.  The ASF licenses this file
to you under the Apache License, Version 2.0 (the
"License"); you may not use this file except in compliance
with the License.  You may obtain a copy of the License at

  http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing,
software distributed under the License is distributed on an
"AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
KIND, either express or implied.  See the License for the
specific language governing permissions and limitations
under the License.
*/
using Microsoft.VisualStudio.TestTools.UnitTesting;
using etwlib;
using System;
using System.IO;

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class ProviderTests
    {
        [TestMethod]
        public void SingleProviderByName()
        {
            ConfigureLoggers();

            try
            {
                var provider = ProviderParser.GetProvider("Microsoft-Windows-Kernel-Registry");
                Assert.IsNotNull(provider);
                Assert.IsTrue(provider.Id == s_WinKernelRegistryGuid);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [TestMethod]
        public void SingleProviderById()
        {
            ConfigureLoggers();

            try
            {
                var provider = ProviderParser.GetProvider(s_WinKernelRegistryGuid);
                Assert.IsNotNull(provider);
                Assert.AreEqual("Microsoft-Windows-Kernel-Registry", provider.Name);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [TestMethod]
        public void AllProviders()
        {
            ConfigureLoggers();

            try
            {
                var providers = ProviderParser.GetProviders();
                Assert.IsNotNull(providers);
                Assert.IsGreaterThan(0, providers.Count);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [TestMethod]
        public void SingleManifest()
        {
            ConfigureLoggers();

            try
            {
                var manifest = ProviderParser.GetManifest(s_WinKernelRegistryGuid);
                Assert.IsNotNull(manifest);
                var str = manifest.ToString();
                Assert.IsNotEmpty(str);
                var xml = manifest.ToXml();
                Assert.IsNotEmpty(xml);
                //
                // Now ask TDH to parse the XML manifest we just created
                //
                var dummyGuid = Guid.NewGuid();
                xml = xml.Replace(s_WinKernelRegistryGuid.ToString(), dummyGuid.ToString());
                var dummyName = "HelloWorld";
                xml = xml.Replace("Microsoft-Windows-Kernel-Registry", dummyName);
                var target = Path.Combine(new string[] {
                    Path.GetTempPath(), "etwlib", "Debug" });
                Directory.CreateDirectory(target);
                target = Path.Combine(target, $"{dummyGuid}.xml");
                File.WriteAllText(target, xml);
                var manifest2 = ProviderParser.GetManifest(dummyGuid, target);
                Assert.IsNotNull(manifest2);
                str = manifest2.ToString();
                Assert.IsNotEmpty(str);
                xml = manifest2.ToXml();
                Assert.IsNotEmpty(xml);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [Ignore] // Takes a lot of memory + 90 seconds
        [TestMethod]
        public void AllManifests()
        {
            ConfigureLoggers();

            try
            {
                var all = ProviderParser.GetManifests();
                Assert.IsNotNull(all);
                foreach (var kvp in all)
                {
                    var manifest = kvp.Value.ToXml();
                    Assert.IsNotNull(manifest);
                    //
                    // It's a little ambitious to think all of these will load cleanly
                    // with TDH, so I'm not even going to try!
                    //
                    var filename = $"{kvp.Key.Name} {{{kvp.Key.Id}}}.xml";
                    filename = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
                    var target = Path.Combine(@"C:\projects\etwmanifests\manifests\22621.2134", filename);
                    File.WriteAllText(target, manifest);
                }
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }
    }
}
