using LinqToLdap.Types;
using System;
using System.DirectoryServices.Protocols;

namespace LinqToLdap.Mapping.PropertyMappings
{
    internal class PwdLastSetPropertyMapping<T> : PropertyMappingGeneric<T> where T : class
    {
        public PwdLastSetPropertyMapping(PropertyMappingArguments<T> arguments)
            : base(arguments)
        {
        }

        public override object FormatValueFromDirectory(DirectoryAttribute value, string dn)
        {
            if (value != null && value.Count > 0)
            {
                try
                {
                    var fileTimeStr = value[0] as string;
                    if (long.TryParse(fileTimeStr, out var fileTime))
                    {
                        return PwdLastSetValue.FromFileTime(fileTime);
                    }
                }
                catch (Exception ex)
                {
                    ThrowMappingException(value, dn, ex);
                }
            }

            AssertNullable(dn);
            return null;
        }

        public override string FormatValueToFilter(object value)
        {
            if (value is PwdLastSetValue pwdLastSet)
            {
                return pwdLastSet.FileTime.ToString();
            }

            throw new InvalidOperationException($"Expected PwdLastSet type but got {value?.GetType().Name ?? "null"}");
        }

        public override DirectoryAttributeModification GetDirectoryAttributeModification(object instance)
        {
            var modification = new DirectoryAttributeModification
            {
                Name = AttributeName,
                Operation = DirectoryAttributeOperation.Replace
            };

            var value = GetValueForDirectory(instance);
            if (value != null)
            {
                modification.Add((string)value);
            }

            return modification;
        }

        public override object GetValueForDirectory(object instance)
        {
            var value = GetValue(instance);
            
            if (value == null)
            {
                return null;
            }

            if (value is PwdLastSetValue pwdLastSet)
            {
                return pwdLastSet.ToDirectoryValue();
            }

            throw new InvalidOperationException($"Expected PwdLastSet type but got {value.GetType().Name}");
        }
    }
}