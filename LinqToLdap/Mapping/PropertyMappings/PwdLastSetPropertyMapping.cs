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
                // Only allow 0 or -1 to be sent to AD
                // Any other value would be rejected by Active Directory
                if (pwdLastSet.FileTime == 0 || pwdLastSet.FileTime == -1)
                {
                    return pwdLastSet.ToDirectoryValue();
                }

                // If it's any other value, don't send it - this prevents updates from failing
                return null;
            }

            throw new InvalidOperationException($"Expected PwdLastSet type but got {value.GetType().Name}");
        }

        public override bool IsEqual(object instance, object value, out DirectoryAttributeModification modification)
        {
            var currentValue = GetValue(instance);

            // If both are null, they're equal
            if (value == null && currentValue == null)
            {
                modification = null;
                return true;
            }

            // Compare current property value with the original value from directory
            if (currentValue is PwdLastSetValue currentPwdLastSet && 
                value is PwdLastSetValue originalPwdLastSet)
            {
                // If values are equal, no modification needed
                if (currentPwdLastSet.FileTime == originalPwdLastSet.FileTime)
                {
                    modification = null;
                    return true;
                }

                // Values are different - only send modification if the new value is 0 or -1
                // (Active Directory only accepts these special values for writes)
                if (currentPwdLastSet.FileTime == 0 || currentPwdLastSet.FileTime == -1)
                {
                    modification = GetDirectoryAttributeModification(instance);
                    return false;
                }

                // Value changed to something other than 0/-1, which AD won't accept
                // Treat as equal to prevent sending invalid modification
                modification = null;
                return true;
            }

            // If original value is null but current is not, check if we should send it
            if (currentValue is PwdLastSetValue pwdLastSet)
            {
                if (pwdLastSet.FileTime == 0 || pwdLastSet.FileTime == -1)
                {
                    modification = GetDirectoryAttributeModification(instance);
                    return false;
                }
            }

            // Default case: treat as equal to prevent any modification
            modification = null;
            return true;
        }
    }
}