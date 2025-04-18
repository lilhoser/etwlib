﻿/* 
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
    using static NativeTraceConsumer;

    public class ParsedEtwTemplateItem : IEquatable<ParsedEtwTemplateItem>, IComparable<ParsedEtwTemplateItem>
    {
        public class Backreference
        {
            public Backreference() { } // for serialization
            public Backreference(int Index, bool IsCounted)
            {
                FieldIndex = Index;
                IsCountedField = IsCounted;
            }
            public string? FieldName { get; set; }    // must be resolved
            public int FieldIndex { get; set; }      // match to ParsedEtwTemplateItem.Index
            public bool IsCountedField { get; set; } // if false, size field
        }

        public string Name { get; set; }
        public TdhInputType InType { get; set; }
        public TdhOutputType OutType { get; set; }
        public int Length { get; set; }
        public string Value { get; set; }
        public Backreference? FieldBackreference { get; set; }
        public int Index { get; set; } // the index when it was parsed

        private ParsedEtwTemplateItem() { } // For XML serialization

        public ParsedEtwTemplateItem(
            string name,
            TdhInputType inType,
            TdhOutputType outType,
            int length,
            string value,
            int index)
        {
            Name = name;
            InType = inType;
            OutType = outType;
            Length = length;
            Value = value;
            FieldBackreference = null;
            Index = index;
        }

        public override bool Equals(object? Other)
        {
            if (Other == null)
            {
                return false;
            }
            var field = Other as ParsedEtwTemplateItem;
            return Equals(field);
        }

        public bool Equals(ParsedEtwTemplateItem? Other)
        {
            if (Other == null)
            {
                return false;
            }
            return Name.ToLower() == Other.Name.ToLower() &&
                InType == Other.InType &&
                OutType == Other.OutType;
        }

        public static bool operator ==(ParsedEtwTemplateItem? Template1, ParsedEtwTemplateItem? Template2)
        {
            if ((object)Template1 == null || (object)Template2 == null)
                return Equals(Template1, Template2);
            return Template1.Equals(Template2);
        }

        public static bool operator !=(ParsedEtwTemplateItem? Template1, ParsedEtwTemplateItem? Template2)
        {
            if ((object)Template1 == null || (object)Template2 == null)
                return !Equals(Template1, Template2);
            return !(Template1.Equals(Template2));
        }

        public override int GetHashCode()
        {
            return (Name, InType, OutType).GetHashCode();
        }

        public int CompareTo(ParsedEtwTemplateItem? Other)
        {
            if (Other == null)
            {
                return 1;
            }
            return Name.CompareTo(Other.Name);
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(Value))
            {
                return $"{Name} = {Value}";
            }
            return string.Empty;
        }
    }
}
