using System;
using System.Reflection;
using System.Reflection.Emit;

namespace FastMemberCore
{

    internal static class TypeHelpers
    {

        public static readonly Type[] EmptyTypes = Type.EmptyTypes;

        public static bool IsValueType(Type type)
        {
            return type.IsValueType;
        }

        public static bool IsPublic(Type type)
        {
            return type.IsPublic;
        }

        public static bool IsNestedPublic(Type type)
        {
            return type.IsNestedPublic;
        }
        public static bool IsClass(Type type)
        {
            return type.IsClass;
        }

        public static bool IsAbstract(Type type)
        {
            return type.IsAbstract;
        }
        public static Type CreateType(TypeBuilder type)
        {
            return type.CreateTypeInfo().AsType();
        }

        public static int Min(int x, int y)
        {
            return x < y ? x : y;
        }
    }
}
