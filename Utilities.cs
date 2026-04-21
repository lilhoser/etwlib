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
    public static partial class MSStoreAppPackageHelper
    {
        [LibraryImport("kernel32.dll", EntryPoint = "GetPackageFullNameW", SetLastError = true)]
        private static unsafe partial int GetPackageFullName(
                nint hProcess,
                ref uint packageFullNameLength,
                char* fullName);

        [LibraryImport("kernel32.dll", EntryPoint = "GetApplicationUserModelIdW", SetLastError = true)]
        private static unsafe partial int GetApplicationUserModelId(
            nint hProcess,
            ref uint applicationUserModelIdLength,
            char* applicationUserModelId);

        [LibraryImport("kernel32.dll", EntryPoint = "ParseApplicationUserModelIdW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static unsafe partial int ParseApplicationUserModelId(
            string applicationUserModelId,
            ref uint packageFamilyNameLength,
            char* packageFamilyName,
            ref uint packageRelativeApplicationIdLength,
            char* packageRelativeApplicationId);

        private static string BufferToString(char[] buffer, uint length)
        {
            // length includes the null terminator; trim it.
            int actual = length > 0 ? (int)length - 1 : 0;
            return new string(buffer, 0, Math.Min(actual, buffer.Length));
        }

        public static unsafe Tuple<string, string>? GetPackage(nint ProcessHandle)
        {
            uint bufferLength = 1024;

            //
            // The package full name is a serialized form of the Package ID, which
            // consists of identifying info like: name, publisher, architecture, etc.
            // This is what the ETW filter type EVENT_FILTER_TYPE_PACKAGE_ID  wants.
            //
            var packageIdBuffer = new char[bufferLength];
            var packageIdLen = bufferLength;
            int result;
            fixed (char* pPackageId = packageIdBuffer)
            {
                result = GetPackageFullName(ProcessHandle, ref packageIdLen, pPackageId);
            }
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            var userModelIdBuffer = new char[bufferLength];
            var userModelIdLen = bufferLength;
            fixed (char* pUserModelId = userModelIdBuffer)
            {
                result = GetApplicationUserModelId(ProcessHandle, ref userModelIdLen, pUserModelId);
            }
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            //
            // The ETW filter type EVENT_FILTER_TYPE_PACKAGE_APP_ID wants the
            // "package-relative APP ID (PRAID)" which must be parsed from
            // the "application user model ID".
            //
            var packageFamilyBuffer = new char[bufferLength];
            var packageFamilyLength = bufferLength;
            var relativeAppIdBuffer = new char[bufferLength];
            var relativeAppIdLength = bufferLength;
            var userModelIdStr = BufferToString(userModelIdBuffer, userModelIdLen);
            fixed (char* pFamily = packageFamilyBuffer)
            fixed (char* pRelative = relativeAppIdBuffer)
            {
                result = ParseApplicationUserModelId(userModelIdStr,
                    ref packageFamilyLength,
                    pFamily,
                    ref relativeAppIdLength,
                    pRelative);
            }
            if (result != ERROR_SUCCESS)
            {
                return null;
            }

            return new Tuple<string, string>(
                BufferToString(packageIdBuffer, packageIdLen),
                BufferToString(relativeAppIdBuffer, relativeAppIdLength));
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

        /// <summary>
        /// Checks if a buffer size is too large to safely allocate.
        /// Returns true if the buffer is too large, false otherwise.
        /// </summary>
        /// <param name="bufferSize">The buffer size in bytes.</param>
        /// <param name="maxSize">Optional: The maximum allowed buffer size in bytes. Defaults to 128MB.</param>
        public static bool IsBufferSizeTooLarge(uint bufferSize, uint maxSize = 128 * 1024 * 1024)
        {
            // Prevent allocations that are suspiciously large or negative (overflow)
            return bufferSize == 0 || bufferSize > maxSize;
        }
    }

}
