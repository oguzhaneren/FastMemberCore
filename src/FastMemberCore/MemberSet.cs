using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FastMemberCore
{
    /// <summary>
    ///     Represents an abstracted view of the members defined for a type
    /// </summary>
    public sealed class MemberSet : IList<Member>
    {
        private readonly Member[] _members;

        /// <summary>
        ///     Get a member by index
        /// </summary>
        public Member this[int index] => _members[index];

        internal MemberSet(Type type)
        {
            const BindingFlags publicInstance = BindingFlags.Public | BindingFlags.Instance;

            _members = type.GetProperties(publicInstance).Cast<MemberInfo>().Concat(type.GetFields(publicInstance)).OrderBy(x => x.Name)
                           .Select(member => new Member(member)).ToArray();
        }

        /// <summary>
        ///     Return a sequence of all defined members
        /// </summary>
        public IEnumerator<Member> GetEnumerator()
        {
            return ((IEnumerable<Member>) _members).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        /// <summary>
        ///     The number of members defined for this type
        /// </summary>
        public int Count => _members.Length;

        Member IList<Member>.this[int index]
        {
            get => _members[index];
            set => throw new NotSupportedException();
        }

        bool ICollection<Member>.Remove(Member item)
        {
            throw new NotSupportedException();
        }

        void ICollection<Member>.Add(Member item)
        {
            throw new NotSupportedException();
        }

        void ICollection<Member>.Clear()
        {
            throw new NotSupportedException();
        }

        void IList<Member>.RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        void IList<Member>.Insert(int index, Member item)
        {
            throw new NotSupportedException();
        }

        bool ICollection<Member>.Contains(Member item)
        {
            return _members.Contains(item);
        }

        void ICollection<Member>.CopyTo(Member[] array, int arrayIndex)
        {
            _members.CopyTo(array, arrayIndex);
        }

        bool ICollection<Member>.IsReadOnly => true;

        int IList<Member>.IndexOf(Member member)
        {
            return Array.IndexOf(_members, member);
        }
    }

    /// <summary>
    ///     Represents an abstracted view of an individual member defined for a type
    /// </summary>
    public sealed class Member
    {
        private readonly MemberInfo _member;

        /// <summary>
        ///     Property Can Read
        /// </summary>
        public bool CanRead
        {
            get
            {
                switch (_member.MemberType)
                {
                    case MemberTypes.Property: return ((PropertyInfo) _member).CanRead;
                    default: throw new NotSupportedException(_member.MemberType.ToString());
                }
            }
        }

        /// <summary>
        ///     Property Can Write
        /// </summary>
        public bool CanWrite
        {
            get
            {
                switch (_member.MemberType)
                {
                    case MemberTypes.Property: return ((PropertyInfo) _member).CanWrite;
                    default: throw new NotSupportedException(_member.MemberType.ToString());
                }
            }
        }

        /// <summary>
        ///     The name of this member
        /// </summary>
        public string Name => _member.Name;

        /// <summary>
        ///     The type of value stored in this member
        /// </summary>
        public Type Type
        {
            get
            {
                if (_member is FieldInfo info)
                {
                    return info.FieldType;
                }
                if (_member is PropertyInfo propertyInfo)
                {
                    return propertyInfo.PropertyType;
                }
                throw new NotSupportedException(_member.GetType().Name);
            }
        }

        internal Member(MemberInfo member)
        {
            _member = member;
        }

        /// <summary>
        ///     Getting Attribute Type
        /// </summary>
        public Attribute GetAttribute(Type attributeType, bool inherit)
        {
            return Attribute.GetCustomAttribute(_member, attributeType, inherit);
        }

        /// <summary>
        ///     Is the attribute specified defined on this type
        /// </summary>
        public bool IsDefined(Type attributeType)
        {
            if (attributeType == null)
            {
                throw new ArgumentNullException(nameof(attributeType));
            }

            return Attribute.IsDefined(_member, attributeType);
        }
    }
}