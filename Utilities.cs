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
using System.Runtime.InteropServices;
using System.Text;
using static etwlib.NativeTraceConsumer;

namespace etwlib
{
    public static class MSStoreAppPackageHelper
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetPackageFullName(
                [In] nint hProcess,
                [In, Out] ref uint packageFullNameLength,
                [Out] StringBuilder fullName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int GetApplicationUserModelId(
            [In] nint hProcess,
            [In, Out] ref uint applicationUserModelIdLength,
            [Out] StringBuilder applicationUserModelId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern int ParseApplicationUserModelId(
            [In] string applicationUserModelId,
            [In, Out] ref uint packageFamilyNameLength,
            [In] StringBuilder packageFamilyName,
            [In, Out] ref uint packageRelativeApplicationIdLength,
            [Out] StringBuilder packageRelativeApplicationId);

        public static Tuple<string, string>? GetPackage(nint ProcessHandle)
        {
            uint bufferLength = 1024;

            //
            // The package full name is a serialized form of the Package ID, which
            // consists of identifying info like: name, publisher, architecture, etc.
            // This is what the ETW filter type EVENT_FILTER_TYPE_PACKAGE_ID  wants.
            //
            var packageId = new StringBuilder((int)bufferLength);
            var result = GetPackageFullName(ProcessHandle, ref bufferLength, packageId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            var userModelId = new StringBuilder((int)bufferLength);
            result = GetApplicationUserModelId(ProcessHandle, ref bufferLength, userModelId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            //
            // The ETW filter type EVENT_FILTER_TYPE_PACKAGE_APP_ID wants the 
            // "package-relative APP ID (PRAID)" which must be parsed from
            // the "application user model ID".
            //
            var packageFamilyLength = bufferLength;
            var packageFamily = new StringBuilder((int)packageFamilyLength);
            var relativeAppIdLength = bufferLength;
            var relativeAppId = new StringBuilder((int)relativeAppIdLength);
            result = ParseApplicationUserModelId(userModelId.ToString(),
                ref packageFamilyLength,
                packageFamily,
                ref relativeAppIdLength,
                relativeAppId);
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            return new Tuple<string, string>(
                packageId.ToString(), relativeAppId.ToString());
        }
    }

    public static class Utilities
    {
        public static int StringToInteger(string Value)
        {
            if (Value.StartsWith("0x") || Value.Any(char.IsAsciiLetter))
            {
                return Convert.ToInt32(Value.Replace("0x", ""), 16);
            }
            else
            {
                if (!int.TryParse(Value, out int value))
                {
                    throw new Exception($"Failed to parse integer from value {Value}");
                }
                return value;
            }
        }

        public static (int, int) GetBetweenArguments(string Value)
        {
            var loc = Value.IndexOf(",");
            if (loc < 0)
            {
                throw new Exception("Between operator requires two integers " +
                    "separated by a comma");
            }
            var values = Value.Split(',');
            if (values.Length != 2)
            {
                throw new Exception("Between operator requires two integers " +
                    "separated by a comma");
            }

            var first = Utilities.StringToInteger(values[0]);
            var second = Utilities.StringToInteger(values[1]);
            return (first, second);
        }
    }

}
