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

namespace etwlib
{
    public class ParsedEtwManifestField : IEquatable<ParsedEtwManifestField>, IComparable<ParsedEtwManifestField>
    {
        public string Name;
        public string Description;
        public ulong Value;

        public ParsedEtwManifestField(string name, string description, ulong value)
        {
            Name = name;
            Description = description;
            Value = value;
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwManifestField;
            if (field == null)
            {
                return false;
            }
            return Equals(Other);
        }

        public bool Equals(ParsedEtwManifestField? Other)
        {
            if (Other == null)
            {
                return false;
            }
            return Value == Other.Value && Other.Name == Name;
        }

        public static bool operator ==(ParsedEtwManifestField? Field1, ParsedEtwManifestField? Field2)
        {
            if ((object)Field1 == null || (object)Field2 == null)
                return Equals(Field1, Field2);
            return Field1.Equals(Field2);
        }

        public static bool operator !=(ParsedEtwManifestField? Field1, ParsedEtwManifestField? Field2)
        {
            if ((object)Field1 == null || (object)Field2 == null)
                return !Equals(Field1, Field2);
            return !(Field1.Equals(Field2));
        }

        public override int GetHashCode()
        {
            return (Name, Description, Value).GetHashCode();
        }

        public int CompareTo(ParsedEtwManifestField? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return Name.CompareTo(Other.Name);
        }

        public override string ToString()
        {
            var description = !string.IsNullOrEmpty(Description) ?
                $", {Description}" : "";
            return $"   {Name}{description}, Mask/Value=0x{Value:X}";
        }
    }
}
