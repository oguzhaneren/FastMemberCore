using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using static FastMemberCore.TypeHelpers;


namespace FastMemberCore
{


    /// <summary>
    ///     Provides by-name member-access to objects of a given type
    /// </summary>
    public abstract class TypeAccessor
    {
        // hash-table has better read-without-locking semantics than dictionary
        private static readonly Hashtable PublicAccessorsOnly = new Hashtable(), NonPublicAccessors = new Hashtable();

        /// <summary>
        ///     Does this type support new instances via a parameterless constructor?
        /// </summary>
        public virtual bool CreateNewSupported => false;

        /// <summary>
        ///     Create a new instance of this type
        /// </summary>
        public virtual object CreateNew()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Can this type be queried for member availability?
        /// </summary>
        public virtual bool GetMembersSupported => false;

        /// <summary>
        ///     Query the members available for this type
        /// </summary>
        public virtual MemberSet GetMembers()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Provides a type-specific accessor, allowing by-name access for all objects of that type
        /// </summary>
        /// <remarks>The accessor is cached internally; a pre-existing accessor may be returned</remarks>
        public static TypeAccessor Create(Type type)
        {
            return Create(type, false);
        }

        /// <summary>
        ///     Provides a type-specific accessor, allowing by-name access for all objects of that type
        /// </summary>
        /// <remarks>The accessor is cached internally; a pre-existing accessor may be returned</remarks>
        public static TypeAccessor Create(Type type, bool allowNonPublicAccessors)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }
            var lookup = allowNonPublicAccessors ? NonPublicAccessors : PublicAccessorsOnly;
            var obj = (TypeAccessor) lookup[type];
            if (obj != null)
            {
                return obj;
            }

            lock (lookup)
            {
                // double-check
                obj = (TypeAccessor) lookup[type];
                if (obj != null)
                {
                    return obj;
                }

                obj = CreateNew(type, allowNonPublicAccessors);

                lookup[type] = obj;
                return obj;
            }
        }

        sealed class DynamicAccessor : TypeAccessor
        {
            public static readonly DynamicAccessor Singleton = new DynamicAccessor();
            private DynamicAccessor() { }
            public override object this[object target, string name]
            {
                get => CallSiteCache.GetValue(name, target);
                set => CallSiteCache.SetValue(name, target, value);
            }
        }

        private static AssemblyBuilder _assembly;
        private static ModuleBuilder _module;
        private static int _counter;


        private static int GetNextCounterValue()
        {
            return Interlocked.Increment(ref _counter);
        }

        private static readonly MethodInfo TryGetValue = typeof(Dictionary<string, int>).GetMethod("TryGetValue");

        private static void WriteMapImpl(ILGenerator il, Type type, List<MemberInfo> members, FieldBuilder mapField, bool allowNonPublicAccessors, bool isGet)
        {
            OpCode obj, index, value;

            var fail = il.DefineLabel();
            if (mapField == null)
            {
                index = OpCodes.Ldarg_0;
                obj = OpCodes.Ldarg_1;
                value = OpCodes.Ldarg_2;
            }
            else
            {
                il.DeclareLocal(typeof(int));
                index = OpCodes.Ldloc_0;
                obj = OpCodes.Ldarg_1;
                value = OpCodes.Ldarg_3;

                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, mapField);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldloca_S, (byte) 0);
                il.EmitCall(OpCodes.Callvirt, TryGetValue, null);
                il.Emit(OpCodes.Brfalse, fail);
            }
            var labels = new Label[members.Count];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = il.DefineLabel();
            }
            il.Emit(index);
            il.Emit(OpCodes.Switch, labels);
            il.MarkLabel(fail);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Newobj, typeof(ArgumentOutOfRangeException).GetConstructor(new[] {typeof(string)}));
            il.Emit(OpCodes.Throw);
            for (var i = 0; i < labels.Length; i++)
            {
                il.MarkLabel(labels[i]);
                var member = members[i];
                var isFail = true;
                FieldInfo field;
                PropertyInfo prop;
                if ((field = member as FieldInfo) != null)
                {
                    il.Emit(obj);
                    Cast(il, type, true);
                    if (isGet)
                    {
                        il.Emit(OpCodes.Ldfld, field);
                        if (IsValueType(field.FieldType))
                        {
                            il.Emit(OpCodes.Box, field.FieldType);
                        }
                    }
                    else
                    {
                        il.Emit(value);
                        Cast(il, field.FieldType, false);
                        il.Emit(OpCodes.Stfld, field);
                    }
                    il.Emit(OpCodes.Ret);
                    isFail = false;
                }
                else if ((prop = member as PropertyInfo) != null)
                {
                    MethodInfo accessor;
                    if (prop.CanRead && (accessor = isGet ? prop.GetGetMethod(allowNonPublicAccessors) : prop.GetSetMethod(allowNonPublicAccessors)) != null)
                    {
                        il.Emit(obj);
                        Cast(il, type, true);
                        if (isGet)
                        {
                            il.EmitCall(IsValueType(type) ? OpCodes.Call : OpCodes.Callvirt, accessor, null);
                            if (IsValueType(prop.PropertyType))
                            {
                                il.Emit(OpCodes.Box, prop.PropertyType);
                            }
                        }
                        else
                        {
                            il.Emit(value);
                            Cast(il, prop.PropertyType, false);
                            il.EmitCall(IsValueType(type) ? OpCodes.Call : OpCodes.Callvirt, accessor, null);
                        }
                        il.Emit(OpCodes.Ret);
                        isFail = false;
                    }
                }
                if (isFail)
                {
                    il.Emit(OpCodes.Br, fail);
                }
            }
        }

       
        /// <summary>
        ///     A TypeAccessor based on a Type implementation, with available member metadata
        /// </summary>
        protected abstract class RuntimeTypeAccessor : TypeAccessor
        {
            private MemberSet _members;

            /// <summary>
            ///     Can this type be queried for member availability?
            /// </summary>
            public override bool GetMembersSupported => true;

            /// <summary>
            ///     Returns the Type represented by this accessor
            /// </summary>
            protected abstract Type Type { get; }

            /// <summary>
            ///     Query the members available for this type
            /// </summary>
            public override MemberSet GetMembers()
            {
                return _members ?? (_members = new MemberSet(Type));
            }
        }

        private sealed class DelegateAccessor : RuntimeTypeAccessor
        {
            private readonly Func<object> _ctor;
            private readonly Func<int, object, object> _getter;
            private readonly Dictionary<string, int> _map;
            private readonly Action<int, object, object> _setter;
            public override bool CreateNewSupported => _ctor != null;

            public override object this[object target, string name]
            {
                get
                {
                    if (_map.TryGetValue(name, out var index))
                    {
                        return _getter(index, target);
                    }
                    throw new ArgumentOutOfRangeException("name");
                }
                set
                {
                    if (_map.TryGetValue(name, out var index))
                    {
                        _setter(index, target, value);
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException("name");
                    }
                }
            }

            protected override Type Type { get; }

            public DelegateAccessor(Dictionary<string, int> map, Func<int, object, object> getter, Action<int, object, object> setter, Func<object> ctor, Type type)
            {
                _map = map;
                _getter = getter;
                _setter = setter;
                _ctor = ctor;
                Type = type;
            }

            public override object CreateNew()
            {
                return _ctor != null ? _ctor() : base.CreateNew();
            }
        }

        private static bool IsFullyPublic(Type type, PropertyInfo[] props, bool allowNonPublicAccessors)
        {
            while (IsNestedPublic(type))
            {
                type = type.DeclaringType;
            }
            if (!IsPublic(type))
            {
                return false;
            }

            if (!allowNonPublicAccessors)
            {
                return true;
            }
            foreach (var t in props)
            {
                if (t.GetGetMethod(true) != null && t.GetGetMethod(false) == null)
                {
                    return false; // non-public getter
                }
                if (t.GetSetMethod(true) != null && t.GetSetMethod(false) == null)
                {
                    return false; // non-public setter
                }
            }

            return true;
        }

        private static TypeAccessor CreateNew(Type type, bool allowNonPublicAccessors)
        {
            if (typeof(IDynamicMetaObjectProvider).IsAssignableFrom(type))
            {
                return DynamicAccessor.Singleton;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var map = new Dictionary<string, int>();
            var members = new List<MemberInfo>(props.Length + fields.Length);
            var i = 0;
            foreach (var prop in props)
            {
                if (!map.ContainsKey(prop.Name) && prop.GetIndexParameters().Length == 0)
                {
                    map.Add(prop.Name, i++);
                    members.Add(prop);
                }
            }
            foreach (var field in fields)
            {
                if (!map.ContainsKey(field.Name))
                {
                    map.Add(field.Name, i++);
                    members.Add(field);
                }
            }

            ConstructorInfo ctor = null;
            if (IsClass(type) && !IsAbstract(type))
            {
                ctor = type.GetConstructor(EmptyTypes);
            }
            ILGenerator il;
            if (!IsFullyPublic(type, props, allowNonPublicAccessors))
            {
                DynamicMethod dynGetter = new DynamicMethod($"{type.FullName}_get", typeof(object), new[] {typeof(int), typeof(object)}, type, true),
                    dynSetter = new DynamicMethod($"{type.FullName}_set", null, new[] {typeof(int), typeof(object), typeof(object)}, type, true);
                WriteMapImpl(dynGetter.GetILGenerator(), type, members, null, allowNonPublicAccessors, true);
                WriteMapImpl(dynSetter.GetILGenerator(), type, members, null, allowNonPublicAccessors, false);
                DynamicMethod dynCtor = null;
                if (ctor != null)
                {
                    dynCtor = new DynamicMethod($"{type.FullName}_ctor", typeof(object), EmptyTypes, type, true);
                    il = dynCtor.GetILGenerator();
                    il.Emit(OpCodes.Newobj, ctor);
                    il.Emit(OpCodes.Ret);
                }
                return new DelegateAccessor(
                    map,
                    (Func<int, object, object>) dynGetter.CreateDelegate(typeof(Func<int, object, object>)),
                    (Action<int, object, object>) dynSetter.CreateDelegate(typeof(Action<int, object, object>)),
                    dynCtor == null ? null : (Func<object>) dynCtor.CreateDelegate(typeof(Func<object>)), type);
            }

            // note this region is synchronized; only one is being created at a time so we don't need to stress about the builders
            if (_assembly == null)
            {
                var name = new AssemblyName("FastMember_dynamic");

                _assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);

                _module = _assembly.DefineDynamicModule(name.Name);
            }

            var attribs = typeof(TypeAccessor).Attributes;
            var tb = _module.DefineType($"FastMember_dynamic.{type.Name}_{GetNextCounterValue()}",
                (attribs | TypeAttributes.Sealed | TypeAttributes.Public) & ~(TypeAttributes.Abstract | TypeAttributes.NotPublic), typeof(RuntimeTypeAccessor));

            il = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[]
                                                                                            {
                                                                                                typeof(Dictionary<string, int>)
                                                                                            }).GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            var mapField = tb.DefineField("_map", typeof(Dictionary<string, int>), FieldAttributes.InitOnly | FieldAttributes.Private);
            il.Emit(OpCodes.Stfld, mapField);
            il.Emit(OpCodes.Ret);


            var indexer = typeof(TypeAccessor).GetProperty("Item");
            MethodInfo baseGetter = indexer.GetGetMethod(), baseSetter = indexer.GetSetMethod();
            var body = tb.DefineMethod(baseGetter.Name, baseGetter.Attributes & ~MethodAttributes.Abstract, typeof(object), new[] {typeof(object), typeof(string)});
            il = body.GetILGenerator();
            WriteMapImpl(il, type, members, mapField, allowNonPublicAccessors, true);
            tb.DefineMethodOverride(body, baseGetter);

            body = tb.DefineMethod(baseSetter.Name, baseSetter.Attributes & ~MethodAttributes.Abstract, null, new[] {typeof(object), typeof(string), typeof(object)});
            il = body.GetILGenerator();
            WriteMapImpl(il, type, members, mapField, allowNonPublicAccessors, false);
            tb.DefineMethodOverride(body, baseSetter);

            MethodInfo baseMethod;
            if (ctor != null)
            {
                baseMethod = typeof(TypeAccessor).GetProperty("CreateNewSupported").GetGetMethod();
                body = tb.DefineMethod(baseMethod.Name, baseMethod.Attributes, baseMethod.ReturnType, EmptyTypes);
                il = body.GetILGenerator();
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                tb.DefineMethodOverride(body, baseMethod);

                baseMethod = typeof(TypeAccessor).GetMethod("CreateNew");
                body = tb.DefineMethod(baseMethod.Name, baseMethod.Attributes, baseMethod.ReturnType, EmptyTypes);
                il = body.GetILGenerator();
                il.Emit(OpCodes.Newobj, ctor);
                il.Emit(OpCodes.Ret);
                tb.DefineMethodOverride(body, baseMethod);
            }

            baseMethod = typeof(RuntimeTypeAccessor).GetProperty("Type", BindingFlags.NonPublic | BindingFlags.Instance).GetGetMethod(true);
            body = tb.DefineMethod(baseMethod.Name, baseMethod.Attributes & ~MethodAttributes.Abstract, baseMethod.ReturnType, EmptyTypes);
            il = body.GetILGenerator();
            il.Emit(OpCodes.Ldtoken, type);
            il.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"));
            il.Emit(OpCodes.Ret);
            tb.DefineMethodOverride(body, baseMethod);

            var accessor = (TypeAccessor) Activator.CreateInstance(CreateType(tb), map);
            return accessor;
        }

        private static void Cast(ILGenerator il, Type type, bool valueAsPointer)
        {
            if (type == typeof(object))
            {
            }
            else if (IsValueType(type))
            {
                il.Emit(valueAsPointer ? OpCodes.Unbox : OpCodes.Unbox_Any, type);
            }
            else
            {
                il.Emit(OpCodes.Castclass, type);
            }
        }

        /// <summary>
        ///     Get or set the value of a named member on the target instance
        /// </summary>
        public abstract object this[object target, string name] { get; set; }
    }
}