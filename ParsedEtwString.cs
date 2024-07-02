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
    public class ParsedEtwString : IEquatable<ParsedEtwString>, IComparable<ParsedEtwString>
    {
        public string Name { get; set; }
        public ulong Value { get; set; }

        private ParsedEtwString() { } // For XML serialization

        public ParsedEtwString(string name, ulong value)
        {
            Name = name;
            Value = value;
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwString;
            return Equals(field);
        }

        public bool Equals(ParsedEtwString? Other)
        {
            if ((object)Other == null)
            {
                return false;
            }
            return Value == Other.Value && Other.Name == Name;
        }

        public static bool operator ==(ParsedEtwString? Str1, ParsedEtwString? Str2)
        {
            if ((object)Str1 == null || (object)Str2 == null)
                return Equals(Str1, Str2);
            return Str1.Equals(Str2);
        }

        public static bool operator !=(ParsedEtwString? Str1, ParsedEtwString? Str2)
        {
            if ((object)Str1 == null || (object)Str2 == null)
                return !Equals(Str1, Str2);
            return !(Str1.Equals(Str2));
        }

        public override int GetHashCode()
        {
            return (Name, Value).GetHashCode();
        }

        public int CompareTo(ParsedEtwString? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return Name.CompareTo(Other.Name);
        }

        public override string ToString()
        {
            return $"{Name} = {Value}";
        }
    }
}
