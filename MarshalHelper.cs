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
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace etwlib
{
    public static class MarshalHelper
    {
        public
        static
        object?
        MarshalArbitraryType<T>(nint Pointer)
        {
            try
            {
                return Convert.ChangeType(Marshal.PtrToStructure(Pointer, typeof(T)), typeof(T));
            }
            catch (Exception ex)
            {
                throw new Exception($"Could not marshal pointer 0x{Pointer.ToInt64():X}" +
                    $" to destination type {typeof(T).ToString()}"+
                    ex.Message);
            }
        }

        public
        static
        List<T>
        MarshalArray<T>(nint ArrayAddress, uint ElementCount)
        {
            nint entry = ArrayAddress;
            var result = new List<T>();

            for (int i = 0; i < ElementCount; i++)
            {
                Debug.Assert(entry != nint.Zero);

                result.Add((T)MarshalArbitraryType<T>(entry)!);

                //
                // Advance to the next structure.
                //
                unsafe
                {
                    entry = (nint)((byte*)entry.ToPointer() + Marshal.SizeOf(typeof(T)));
                }
            }
            return result;
        }
    }
}
