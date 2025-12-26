using LinqToLdap.Collections;
using LinqToLdap.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.DirectoryServices.Protocols;
using System.Linq;

namespace LinqToLdap.Mapping
{
    internal abstract class ObjectMapping : IObjectMapping
    {
        private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, IPropertyMapping> _propertyMappings;
        private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, IPropertyMapping> _propertyMappingsForAdd;
        private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, IPropertyMapping> _propertyMappingsForUpdate;
        private readonly System.Collections.ObjectModel.ReadOnlyDictionary<string, IPropertyMapping> _attributePropertyMappings;
        private System.Collections.ObjectModel.ReadOnlyDictionary<string, string> _propertyNames;
        private readonly IPropertyMapping _distinguishedName;
        private readonly IPropertyMapping _catchAll;
        private ReadOnlyCollection<IObjectMapping> _readOnlySubTypeMappings;

        protected ObjectMapping(string namingContext, IEnumerable<IPropertyMapping> propertyMappings,
            string objectCategory = null, bool includeObjectCategory = true, IEnumerable<string> objectClass = null, bool includeObjectClasses = true, SecurityMasks includeSecurityMasks = SecurityMasks.None)
        {
            NamingContext = namingContext;
            ObjectCategory = objectCategory;
            ObjectClasses = objectClass;

            // Materialize once to avoid multiple enumerations
            var localPropertyMappings = propertyMappings as List<IPropertyMapping> ?? propertyMappings.ToList();

            // Build all dictionaries in a single pass
            var propertyDict = new Dictionary<string, IPropertyMapping>(localPropertyMappings.Count);
            var attributeDict = new Dictionary<string, IPropertyMapping>(localPropertyMappings.Count, StringComparer.OrdinalIgnoreCase);
            var addDict = new Dictionary<string, IPropertyMapping>(localPropertyMappings.Count);
            var updateDict = new Dictionary<string, IPropertyMapping>(localPropertyMappings.Count);

            IPropertyMapping distinguishedName = null;
            IPropertyMapping catchAll = null;

            foreach (var pm in localPropertyMappings)
            {
                propertyDict[pm.PropertyName] = pm;

                // Check for attribute name conflicts during single pass
                if (!attributeDict.TryAdd(pm.AttributeName, pm))
                {
                    throw new InvalidOperationException($"The same attribute '{pm.AttributeName}' cannot be mapped for multiple properties.");
                }

                if (pm.IsDistinguishedName)
                {
                    distinguishedName = pm;
                }
                else
                {
                    if (pm.ReadOnly == ReadOnly.OnUpdate || pm.ReadOnly == ReadOnly.Never)
                    {
                        addDict[pm.PropertyName] = pm;
                    }

                    if (pm.ReadOnly == ReadOnly.OnAdd || pm.ReadOnly == ReadOnly.Never)
                    {
                        updateDict[pm.PropertyName] = pm;
                    }
                }

                if (typeof(IDirectoryAttributes).IsAssignableFrom(pm.PropertyType))
                {
                    catchAll = pm;
                }
            }

            _propertyMappings = propertyDict.ToReadOnlyDictionary();
            _attributePropertyMappings = attributeDict.ToReadOnlyDictionary();
            _propertyMappingsForAdd = addDict.ToReadOnlyDictionary();
            _propertyMappingsForUpdate = updateDict.ToReadOnlyDictionary();
            _distinguishedName = distinguishedName;
            _catchAll = catchAll;
            _propertyNames = InitializePropertyNames();

            IncludeObjectCategory = includeObjectCategory;
            IncludeObjectClasses = includeObjectClasses;
            IncludeSecurityMasks = includeSecurityMasks;
        }

        public IDictionary<string, IObjectMapping> SubTypeMappingsObjectClassDictionary { get; } =
            new Dictionary<string, IObjectMapping>(StringComparer.OrdinalIgnoreCase);

        public IDictionary<Type, IObjectMapping> SubTypeMappingsTypeDictionary { get; } =
            new Dictionary<Type, IObjectMapping>();

        public abstract Type Type { get; }
        public abstract bool IsForAnonymousType { get; }

        public string NamingContext { get; }
        public string ObjectCategory { get; }
        public bool IncludeObjectCategory { get; }
        public IEnumerable<string> ObjectClasses { get; }
        public bool HasCatchAllMapping => _catchAll != null;
        public bool IncludeObjectClasses { get; }
        public SecurityMasks IncludeSecurityMasks { get; }
        public bool HasSubTypeMappings => SubTypeMappings != null && SubTypeMappings.Count > 0;

        public System.Collections.ObjectModel.ReadOnlyDictionary<string, string> Properties => _propertyNames ?? (_propertyNames = InitializePropertyNames());

        public ReadOnlyCollection<IObjectMapping> SubTypeMappings => _readOnlySubTypeMappings ??
            (_readOnlySubTypeMappings = new ReadOnlyCollection<IObjectMapping>(SubTypeMappingsObjectClassDictionary.Values.ToList()));

        public bool WithoutSubTypeMapping { get; set; }

        public IEnumerable<IPropertyMapping> GetPropertyMappings()
        {
            return _propertyMappings.Values;
        }

        public IEnumerable<IPropertyMapping> GetPropertyMappingsForAdd()
        {
            return _propertyMappingsForAdd.Values;
        }

        public IEnumerable<IPropertyMapping> GetPropertyMappingsForUpdate()
        {
            return _propertyMappingsForUpdate.Values;
        }

        public IPropertyMapping GetPropertyMapping(string name, Type owningType = null)
        {
            if (owningType == null || owningType == Type)
            {
                if (_propertyMappings.TryGetValue(name, out IPropertyMapping mapping))
                {
                    return mapping;
                }
                if (HasSubTypeMappings)
                {
                    return null;
                }
            }
            else if (HasSubTypeMappings)
            {
                if (SubTypeMappingsTypeDictionary.TryGetValue(owningType, out IObjectMapping subTypeMapping))
                {
                    return subTypeMapping.GetPropertyMapping(name);
                }
            }

            throw new MappingException($"Property mapping with name '{name}' was not found for '{Type.FullName}'");
        }

        public IPropertyMapping GetPropertyMappingByAttribute(string name, Type owningType = null)
        {
            if (owningType == null || owningType == Type)
            {
                if (_attributePropertyMappings.TryGetValue(name, out IPropertyMapping mapping))
                {
                    return mapping;
                }
            }
            else if (HasSubTypeMappings)
            {
                if (SubTypeMappingsTypeDictionary.TryGetValue(owningType, out IObjectMapping subTypeMapping))
                {
                    return subTypeMapping.GetPropertyMappingByAttribute(name);
                }
            }

            return null;
        }

        public IPropertyMapping GetDistinguishedNameMapping()
        {
            return _distinguishedName;
        }

        public IPropertyMapping GetCatchAllMapping()
        {
            return _catchAll;
        }

        public abstract object Create(object[] parameters = null, object[] objectClasses = null);

        public virtual void AddSubTypeMapping(IObjectMapping mapping)
        {
            if (WithoutSubTypeMapping || SubTypeMappingsObjectClassDictionary.Values.Contains(mapping)) return;

            var currentMappings = SortByInheritanceDescending(SubTypeMappingsObjectClassDictionary.Values.Union(new[] { mapping }));

            SubTypeMappingsObjectClassDictionary.Clear();
            SubTypeMappingsTypeDictionary.Clear();

            foreach (var currentMapping in currentMappings)
            {
                var objectClasses = currentMapping.ObjectClasses.ToList();

                //find direct ancestor object classes or default to this class' object classes if a direct ancestor hasn't been mapped yet.
                var parentObjectClasses = currentMappings
                    .Where(x => currentMapping.Type.IsSubclassOf(x.Type))
                    .Select(x => x.ObjectClasses)
                    .FirstOrDefault() ?? ObjectClasses;

                objectClasses = objectClasses.Except(parentObjectClasses, StringComparer.OrdinalIgnoreCase).ToList();

                if (objectClasses.Count == 0)
                    throw new InvalidOperationException("Unable to identify distinct object class based on mapped inheritance");

                SubTypeMappingsObjectClassDictionary.Add(objectClasses[0], currentMapping);
                SubTypeMappingsTypeDictionary.Add(currentMapping.Type, currentMapping);
            }

            _readOnlySubTypeMappings = null;
            _propertyNames = InitializePropertyNames();
        }

        private System.Collections.ObjectModel.ReadOnlyDictionary<string, string> InitializePropertyNames()
        {
            var properties = _propertyMappings.ToDictionary(x => x.Key, x => x.Value.AttributeName, StringComparer.OrdinalIgnoreCase);

            if (HasSubTypeMappings)
            {
                foreach (var subTypeMapping in SubTypeMappings)
                {
                    foreach (var subTypeProperty in subTypeMapping.Properties)
                    {
                        if (!properties.ContainsKey(subTypeProperty.Key))
                        {
                            properties.Add(subTypeProperty.Key, subTypeProperty.Value);
                        }
                    }
                }
            }

            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(properties);
        }

        private List<IObjectMapping> SortByInheritanceDescending(IEnumerable<IObjectMapping> mappings)
        {
            Dictionary<int, List<IObjectMapping>> hiearchy = new Dictionary<int, List<IObjectMapping>>();

            foreach (var objectMapping in mappings)
            {
                int count = 0;
                var baseType = objectMapping.Type.BaseType;
                while (baseType != Type && baseType != null)
                {
                    count++;
                    baseType = baseType.BaseType;
                }

                if (hiearchy.TryGetValue(count, out List<IObjectMapping> list))
                {
                    list.Add(objectMapping);
                }
                else
                {
                    hiearchy[count] = new List<IObjectMapping> { objectMapping };
                }
            }

            return hiearchy.OrderByDescending(x => x.Key).SelectMany(x => x.Value).ToList();
        }
    }
}