using System.DirectoryServices.Protocols;
using System.Security.AccessControl;

namespace LinqToLdap.Mapping.PropertyMappings
{
    internal class RawSecurityDescriptorPropertyMapping<T> : PropertyMappingGeneric<T> where T : class
    {
        public RawSecurityDescriptorPropertyMapping(PropertyMappingArguments<T> arguments) : base(arguments)
        {
        }

        public override string FormatValueToFilter(object value)
        {
            if (value != null)
            {
                var descriptor = value as RawSecurityDescriptor;
                var binary = new byte[descriptor.BinaryLength];
                descriptor.GetBinaryForm(binary, 0);
                return binary.ToStringOctet();
            }

            return null;
        }

        public override DirectoryAttributeModification GetDirectoryAttributeModification(object instance)
        {
            var modification = new DirectoryAttributeModification
            {
                Name = AttributeName,
                Operation = DirectoryAttributeOperation.Replace
            };
            var value = (byte[])GetValueForDirectory(instance);

            if (value != null)
            {
                modification.Add(value);
            }

            return modification;
        }

        public override object GetValueForDirectory(object instance)
        {
            var value = GetValue(instance);
            if (value == null) return value;

            var descriptor = value as RawSecurityDescriptor;
            var binary = new byte[descriptor.BinaryLength];
            descriptor.GetBinaryForm(binary, 0);

            return binary;
        }

        public override object FormatValueFromDirectory(DirectoryAttribute value, string dn)
        {
            if (value != null)
            {
                var bytes = value.GetValues(typeof(byte[]))[0] as byte[];
                if (bytes == null) ThrowMappingException(dn);

                return new RawSecurityDescriptor(bytes, 0);
            }

            AssertNullable(dn);

            return null;
        }
    }
}