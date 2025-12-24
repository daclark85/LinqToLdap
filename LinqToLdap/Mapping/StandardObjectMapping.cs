using LinqToLdap.Helpers;
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;

namespace LinqToLdap.Mapping
{
    internal class StandardObjectMapping<T> : ObjectMapping where T : class
    {
        private readonly Ctor<T> _constructor;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IObjectMapping> _fullObjectClassMappings =
            new System.Collections.Concurrent.ConcurrentDictionary<string, IObjectMapping>(StringComparer.OrdinalIgnoreCase);


        public StandardObjectMapping(string namingContext,
            IEnumerable<IPropertyMapping> propertyMappings, string objectCategory, bool includeObjectCategory, IEnumerable<string> objectClass, bool includeObjectClasses, SecurityMasks includeSecurityMasks = SecurityMasks.None)
            : base(namingContext, propertyMappings, objectCategory, includeObjectCategory, objectClass, includeObjectClasses, includeSecurityMasks)
        {
            _constructor = DelegateBuilder.BuildCtor<T>();
        }

        public override bool IsForAnonymousType => false;
        public override Type Type => typeof(T);

        public override object Create(object[] parameters = null, object[] objectClasses = null)
        {
            if (HasSubTypeMappings && objectClasses != null)
            {
                string joinedObjectClasses = string.Join(" ", objectClasses);

                if (_fullObjectClassMappings.TryGetValue(joinedObjectClasses, out IObjectMapping mapping))
                {
                    return mapping.Create();
                }
                //Reverse the object classes in order of most specific to least specific.
                //AD and openLDAP seem to retrieve the classes in order of least to most specific so I assume it's the standard.
                if (objectClasses.Cast<string>().Reverse().Any(objectClass => SubTypeMappingsObjectClassDictionary.TryGetValue(objectClass, out mapping)))
                {
                    _fullObjectClassMappings.TryAdd(joinedObjectClasses, mapping);
                    return mapping.Create();
                }
            }
            return _constructor();
        }
    }
}