using LinqToLdap.Exceptions;
using System;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Linq;
using System.Reflection;

namespace LinqToLdap.Mapping
{
    /// <summary>
    /// Class for storing a retrieving object mappings.
    /// </summary>
    public class DirectoryMapper : IDirectoryMapper
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IObjectMapping> _mappings = new System.Collections.Concurrent.ConcurrentDictionary<Type, IObjectMapping>();
        private Func<Type, IClassMap> _autoClassMapper;
        private Func<Type, IClassMap> _attributeClassMapper;

        /// <summary>
        /// Returns all mappings tracked by this object.
        /// </summary>
        /// <returns></returns>

        public System.Collections.ObjectModel.ReadOnlyDictionary<Type, IObjectMapping> GetMappings()
        {
            return new System.Collections.ObjectModel.ReadOnlyDictionary<Type, IObjectMapping>(_mappings);
        }


        /// <summary>
        /// Provide a delegate that takes an object type and returns the class map for it.
        /// </summary>
        /// <param name="autoClassMapBuilder">The delegate.</param>
        /// <returns></returns>
        public IDirectoryMapper AutoMapWith(Func<Type, IClassMap> autoClassMapBuilder)
        {
            _autoClassMapper = autoClassMapBuilder;
            return this;
        }

        /// <summary>
        /// Indicates if a custom AutoMapping delegate has been provided
        /// </summary>
        public bool HasCustomAutoMapping => _autoClassMapper != null;

        /// <summary>
        /// Indicates if a custom AttributeMapping delegate has been provided
        /// </summary>
        public bool HasCustomAttributeMapping => _attributeClassMapper != null;

        /// <summary>
        /// Indicates if auto mapping should default to flatten hierarchy on <see cref="IClassMap.WithoutSubTypeMapping"/>.
        /// </summary>
        public bool AutoMapWithoutSubTypeMapping { get; set; }

        /// <summary>
        /// Provide a delegate that takes an object type and returns the class map for it.
        /// </summary>
        /// <param name="attributeClassMapBuilder">The delegate.</param>
        /// <returns></returns>
        public IDirectoryMapper AttributeMapWith(Func<Type, IClassMap> attributeClassMapBuilder)
        {
            _attributeClassMapper = attributeClassMapBuilder;
            return this;
        }

        /// <summary>
        /// Adds all mappings in the assembly.
        /// </summary>
        /// <param name="assemblyName">
        /// The name of the assembly containing the mappings.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="assemblyName"/> is null, empty or white space.
        /// </exception>
        public void AddMappingsFrom(string assemblyName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName, nameof(assemblyName));

            assemblyName = assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                                ? assemblyName
                                : assemblyName + ".dll";

            var assembly = Assembly.LoadFrom(assemblyName);

            AddMappingsFrom(assembly);
        }

        /// <summary>
        /// Adds all mappings from <paramref name="assembly"/>.
        /// </summary>
        /// <param name="assembly">The assembly containing the mappings.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown if <paramref name="assembly"/> is null..
        /// </exception>
        public void AddMappingsFrom(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assembly));

            // Cache type lookups
            var classMapGenericType = typeof(ClassMap<>);

            // Pre-filter exportedTypes to reduce workload
            foreach (var type in assembly.GetExportedTypes())
            {
                // Skip abstract classes, interfaces, and generic type definitions
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                if (type.HasDirectorySchema())
                {
                    IClassMap mapping;
                    if (HasCustomAttributeMapping)
                    {
                        mapping = _attributeClassMapper.Invoke(type);
                    }
                    else
                    {
                        var classMapType = typeof(AttributeClassMap<>).MakeGenericType(type);
                        mapping = (IClassMap)Activator.CreateInstance(classMapType);
                    }

                    Map(mapping);
                }
                else if (type.BaseType != null && !type.BaseType.IsGenericTypeDefinition)
                {
                    // Use reflection cache to avoid repeated lookups
                    var baseType = type.BaseType;

                    // Optimize: check if any base type is ClassMap<> in one pass
                    while (baseType != null && baseType != typeof(object))
                    {
                        if (baseType.IsGenericType &&
                            baseType.GetGenericTypeDefinition() == classMapGenericType)
                        {
                            var mapping = (IClassMap)Activator.CreateInstance(type);
                            Map(mapping);
                            break;
                        }
                        baseType = baseType.BaseType;
                    }
                }
            }
        }

        /// <summary>
        /// Creates or retrieves the <see cref="IObjectMapping"/> from the classMap.
        /// </summary>
        /// <param name="classMap">The mapping.</param>
        /// <param name="objectCategory">The object category for the object.</param>
        /// <param name="includeObjectCategory">
        /// Indicates if the object category should be included in all queries.
        /// </param>
        /// <param name="includeSecurityMasks"></param>
        /// <param name="namingContext">The location of the objects in the directory.</param>
        /// <param name="objectClasses">The object classes for the object.</param>
        /// <param name="includeObjectClasses">Indicates if the object classes should be included in all queries.</param>
        /// <exception cref="MappingException">
        /// Thrown if the mapping is invalid.
        /// </exception>
        /// <returns></returns>
        public IObjectMapping Map(IClassMap classMap, string namingContext = null, IEnumerable<string> objectClasses = null, bool includeObjectClasses = true, string objectCategory = null, bool includeObjectCategory = true, SecurityMasks includeSecurityMasks = SecurityMasks.None)
        {
            ArgumentNullException.ThrowIfNull(classMap, nameof(classMap));

            return _mappings.GetOrAdd(classMap.Type, t =>
            {
                var mapped = classMap.PerformMapping(namingContext, objectCategory,
                                        includeObjectCategory,
                                        objectClasses, includeObjectClasses, includeSecurityMasks);

                mapped.Validate();

                var objectMapping = mapped.ToObjectMapping();

                if (!mapped.WithoutSubTypeMapping) MapSubTypes(objectMapping);

                return objectMapping;
            });
        }

        /// <summary>
        /// Creates or retrieves the <see cref="IObjectMapping"/> from <typeparam name="T"/>.
        /// </summary>
        /// <param name="namingContext">The optional naming context.  Used for <see cref="AutoClassMap{T}"/></param>
        /// <param name="objectClasses">The optional object classes.  Used for <see cref="AutoClassMap{T}"/></param>
        /// <param name="objectClass">The optional object class.  Used for <see cref="AutoClassMap{T}"/></param>
        /// <param name="objectCategory">The optional object category.  Used for <see cref="AutoClassMap{T}"/></param>
        /// <param name="includeSecurityMasks"></param>
        /// <exception cref="MappingException">
        /// Thrown if the mapping is invalid.
        /// </exception>
        /// <returns></returns>
        public IObjectMapping Map<T>(string namingContext = null, string objectClass = null, IEnumerable<string> objectClasses = null, string objectCategory = null, SecurityMasks includeSecurityMasks = SecurityMasks.None) where T : class
        {
            return _mappings.GetOrAdd(typeof(T), t =>
            {
                IClassMap classMap;
                if (t.HasDirectorySchema())
                {
                    classMap = !HasCustomAttributeMapping
                      ? new AttributeClassMap<T>()
                      : _attributeClassMapper.Invoke(typeof(T));
                }
                else
                {
                    if (objectClass != null)
                    {
                        if (objectClasses != null)
                            throw new ArgumentException("objectClass and objectClasses cannot both have a value.");

                        objectClasses = new[] { objectClass };
                    }
                    classMap = !HasCustomAutoMapping
                        ? new AutoClassMap<T>() { WithoutSubTypeMapping = AutoMapWithoutSubTypeMapping }
                        : _autoClassMapper.Invoke(typeof(T));
                }

                var mapped = classMap.PerformMapping(namingContext,
                                                     objectCategory: objectCategory,
                                                     objectClasses: objectClasses, 
                                                     includeSecurityMasks: includeSecurityMasks
                                                     );

                mapped.Validate();

                var objectMapping = mapped.ToObjectMapping();
                if (!mapped.WithoutSubTypeMapping) MapSubTypes(objectMapping);

                return objectMapping;
            });
        }

        /// <summary>
        /// Gets the mapping for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type for the mapping.</typeparam>
        /// <exception cref="MappingException">
        /// Thrown if the mapping is not found.
        /// </exception>
        /// <returns></returns>
        public IObjectMapping GetMapping<T>() where T : class
        {
            return GetMapping(typeof(T));
        }

        /// <summary>
        /// Gets the mapping for <param name="type"/>.
        /// </summary>
        /// <exception cref="MappingException">
        /// Thrown if the mapping is not found.
        /// </exception>
        /// <returns></returns>
        public IObjectMapping GetMapping(Type type)
        {
            return _mappings.GetOrAdd(type, t =>
            {
                if (t.HasDirectorySchema())
                {
                    var classMap = (IClassMap)(!HasCustomAttributeMapping
                           ? Activator.CreateInstance(typeof(AttributeClassMap<>).MakeGenericType(t))
                           : _attributeClassMapper.Invoke(t));
                    var mapped = classMap.PerformMapping();
                    mapped.Validate();

                    var objectMapping = mapped.ToObjectMapping();
                    if (!mapped.WithoutSubTypeMapping) MapSubTypes(objectMapping);
                    return objectMapping;
                }

                throw new MappingException($"Mapping not found for '{type.FullName}'");
            });
        }

        private void MapSubTypes(IObjectMapping mapping)
        {
            // Pre-calculate inheritance depth for the new mapping
            int newMappingDepth = GetInheritanceDepth(mapping.Type);

            foreach (var objectMapping in _mappings)
            {
                // Skip if types are unrelated
                if (!AreTypesRelated(mapping.Type, objectMapping.Key))
                    continue;

                // Check if already mapped instance is in new mappings inheritance chain
                if (objectMapping.Key.IsAssignableFrom(mapping.Type))
                {
                    ValidateObjectClasses(objectMapping.Value, mapping);
                    objectMapping.Value.AddSubTypeMapping(mapping);
                }
                // Check if new mapping is in the inheritance chain of an existing mapping
                else if (mapping.Type.IsAssignableFrom(objectMapping.Key))
                {
                    ValidateObjectClasses(mapping, objectMapping.Value);
                    mapping.AddSubTypeMapping(objectMapping.Value);
                }
            }
        }

        private static bool AreTypesRelated(Type type1, Type type2)
        {
            return type1.IsAssignableFrom(type2) || type2.IsAssignableFrom(type1);
        }

        private static int GetInheritanceDepth(Type type)
        {
            int depth = 0;
            var baseType = type.BaseType;
            while (baseType != null && baseType != typeof(object))
            {
                depth++;
                baseType = baseType.BaseType;
            }
            return depth;
        }

        internal static void ValidateObjectClasses(IObjectMapping baseTypeMapping, IObjectMapping subTypeMapping)
        {
            var baseClasses = baseTypeMapping.ObjectClasses;
            var subClasses = subTypeMapping.ObjectClasses;
    
            if (baseClasses == null || !baseClasses.Any())
            {
                throw new InvalidOperationException(
                    $"In order to use subclass mapping {baseTypeMapping.Type.Name} must be mapped with objectClasses");
            }
            if (subClasses == null || !subClasses.Any())
            {
                throw new InvalidOperationException(
                    $"In order to use subclass mapping {subTypeMapping.Type.Name} must be mapped with objectClasses");
            }

            // Avoid allocating arrays for null coalescing
            var currentMappings = baseTypeMapping.HasSubTypeMappings
                ? baseTypeMapping.SubTypeMappings.Prepend(baseTypeMapping)
                : new[] { baseTypeMapping };

            // Use HashSet for O(1) lookups instead of OrderBy + SequenceEqual
            var subClassSet = new HashSet<string>(subClasses, StringComparer.OrdinalIgnoreCase);
    
            foreach (var objectMapping in currentMappings)
            {
                if (objectMapping.ObjectClasses.Count() == subClassSet.Count &&
                    objectMapping.ObjectClasses.All(oc => subClassSet.Contains(oc)))
                {
                    throw new InvalidOperationException($"All sub types of {baseTypeMapping.Type.Name} must have a unique sequence of objectClasses.");
                }
            }
        }
    }
}