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

namespace UnitTests
{
    using static Shared;

    [TestClass]
    public class SessionTests
    {
        [TestMethod]
        public void SessionsByProviderName()
        {
            ConfigureLoggers();

            try
            {
                var sessions = SessionParser.GetSessions();
                Assert.IsNotNull(sessions);
                Assert.IsTrue(sessions.Count > 0);
                foreach (var session in sessions)
                {
                    var provider = ProviderParser.GetProvider(
                        session.EnabledProviders[0].ProviderId);
                    if (provider != null && !string.IsNullOrEmpty(provider.Name))
                    {
                        var results = SessionParser.GetSessions(provider.Name);
                        Assert.IsNotNull(results);
                        Assert.IsTrue(session == results[0]);
                        return;
                    }
                }
                Assert.Fail();
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
                var sessions = SessionParser.GetSessions();
                Assert.IsNotNull(sessions);
                Assert.IsTrue(sessions.Count > 0);
                var session = sessions[0];
                var results = SessionParser.GetSessions(session.EnabledProviders[0].ProviderId);
                Assert.IsNotNull(results);
                Assert.IsTrue(session == results[0]);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }

        [TestMethod]
        public void AllSessions()
        {
            ConfigureLoggers();

            try
            {
                var sessions = SessionParser.GetSessions();
                Assert.IsNotNull(sessions);
                Assert.IsTrue(sessions.Count > 0);
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.Message);
            }
        }
    }
}
