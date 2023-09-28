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
    public class ParsedEtwProvider : IEquatable<ParsedEtwProvider>, IComparable<ParsedEtwProvider>
    {
        public Guid Id;
        public string? Name;
        public string? Source;

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwProvider;
            if (field == null)
            {
                return false;
            }
            return Equals(Other);
        }

        public bool Equals(ParsedEtwProvider? Other)
        {
            if (Other == null)
            {
                return false;
            }
            return Id == Other.Id;
        }

        public static bool operator ==(ParsedEtwProvider? Provider1, ParsedEtwProvider? Provider2)
        {
            if ((object)Provider1 == null || (object)Provider2 == null)
                return Equals(Provider1, Provider2);
            return Provider1.Equals(Provider2);
        }

        public static bool operator !=(ParsedEtwProvider? Provider1, ParsedEtwProvider? Provider2)
        {
            if ((object)Provider1 == null || (object)Provider2 == null)
                return !Equals(Provider1, Provider2);
            return !(Provider1.Equals(Provider2));
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public int CompareTo(ParsedEtwProvider? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            if (string.IsNullOrEmpty(Name))
            {
                return Id.CompareTo(Other.Id);
            }
            return Name.CompareTo(Other.Name);
        }

        public override string ToString()
        {
            var name = string.IsNullOrEmpty(Name) ? Id.ToString() : Name;
            return $"{name} : ({Id}/{Source})";
        }
    }
}
