using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using static FastMemberCore.TypeHelpers;

namespace FastMemberCore
{
    /// <inheritdoc />
    /// <summary>
    ///     Provides a means of reading a sequence of objects as a data-reader, for example
    ///     for use with SqlBulkCopy or other data-base oriented code
    /// </summary>
    public class ObjectReader : DbDataReader
    {
        private readonly TypeAccessor _accessor;
        private readonly BitArray _allowNull;
        private readonly Type[] _effectiveTypes;
        private readonly string[] _memberNames;
        private bool _active = true;

        private object _current;
        private IEnumerator _source;


        public override int Depth => 0;

        public override int FieldCount => _memberNames.Length;


        public override bool HasRows => _active;

        public override bool IsClosed => _source == null;

        public override object this[string name] => _accessor[_current, name] ?? DBNull.Value;

        /// <inheritdoc />
        /// <summary>
        ///     Gets the value of the current object in the member specified
        /// </summary>
        public override object this[int i] => _accessor[_current, _memberNames[i]] ?? DBNull.Value;

        public override int RecordsAffected => 0;

        /// <summary>
        ///     Creates a new ObjectReader instance for reading the supplied data
        /// </summary>
        /// <param name="type">The expected Type of the information to be read</param>
        /// <param name="source">The sequence of objects to represent</param>
        /// <param name="members">The members that should be exposed to the reader</param>
        public ObjectReader(Type type, IEnumerable source, params string[] members)
        {
            if (source == null)
            {
                throw new ArgumentOutOfRangeException("source");
            }


            var allMembers = members == null || members.Length == 0;

            _accessor = TypeAccessor.Create(type);
            if (_accessor.GetMembersSupported)
            {
                var typeMembers = _accessor.GetMembers();

                if (allMembers)
                {
                    members = new string[typeMembers.Count];
                    for (var i = 0; i < members.Length; i++)
                    {
                        members[i] = typeMembers[i].Name;
                    }
                }

                _allowNull = new BitArray(members.Length);
                _effectiveTypes = new Type[members.Length];
                for (var i = 0; i < members.Length; i++)
                {
                    Type memberType = null;
                    var allowNull = true;
                    var hunt = members[i];
                    foreach (var member in typeMembers)
                    {
                        if (member.Name != hunt)
                        {
                            continue;
                        }
                        if (memberType == null)
                        {
                            var tmp = member.Type;
                            memberType = Nullable.GetUnderlyingType(tmp) ?? tmp;

                            allowNull = !(IsValueType(memberType) && memberType == tmp);

                            // but keep checking, in case of duplicates
                        }
                        else
                        {
                            memberType = null; // duplicate found; say nothing
                            break;
                        }
                    }
                    _allowNull[i] = allowNull;
                    _effectiveTypes[i] = memberType ?? typeof(object);
                }
            }
            else if (allMembers)
            {
                throw new InvalidOperationException("Member information is not available for this type; the required members must be specified explicitly");
            }

            _current = null;
            _memberNames = (string[]) members.Clone();

            _source = source.GetEnumerator();
        }

        public override void Close()
        {
            Shutdown();
        }

        /// <summary>
        ///     Creates a new ObjectReader instance for reading the supplied data
        /// </summary>
        /// <param name="source">The sequence of objects to represent</param>
        /// <param name="members">The members that should be exposed to the reader</param>
        public static ObjectReader Create<T>(IEnumerable<T> source, params string[] members)
        {
            return new ObjectReader(typeof(T), source, members);
        }

        public override bool GetBoolean(int i)
        {
            return (bool) this[i];
        }

        public override byte GetByte(int i)
        {
            return (byte) this[i];
        }

        public override long GetBytes(int i, long fieldOffset, byte[] buffer, int bufferoffset, int length)
        {
            var s = (byte[]) this[i];
            var available = s.Length - (int) fieldOffset;
            if (available <= 0)
            {
                return 0;
            }

            var count = Min(length, available);
            Buffer.BlockCopy(s, (int) fieldOffset, buffer, bufferoffset, count);
            return count;
        }

        public override char GetChar(int i)
        {
            return (char) this[i];
        }

        public override long GetChars(int i, long fieldoffset, char[] buffer, int bufferoffset, int length)
        {
            var s = (string) this[i];
            var available = s.Length - (int) fieldoffset;
            if (available <= 0)
            {
                return 0;
            }

            var count = Min(length, available);
            s.CopyTo((int) fieldoffset, buffer, bufferoffset, count);
            return count;
        }

        public override string GetDataTypeName(int i)
        {
            return (_effectiveTypes == null ? typeof(object) : _effectiveTypes[i]).Name;
        }

        public override DateTime GetDateTime(int i)
        {
            return (DateTime) this[i];
        }

        public override decimal GetDecimal(int i)
        {
            return (decimal) this[i];
        }

        public override double GetDouble(int i)
        {
            return (double) this[i];
        }

        public override IEnumerator GetEnumerator()
        {
            return new DbEnumerator(this);
        }

        public override Type GetFieldType(int i)
        {
            return _effectiveTypes == null ? typeof(object) : _effectiveTypes[i];
        }

        public override float GetFloat(int i)
        {
            return (float) this[i];
        }

        public override Guid GetGuid(int i)
        {
            return (Guid) this[i];
        }

        public override short GetInt16(int i)
        {
            return (short) this[i];
        }

        public override int GetInt32(int i)
        {
            return (int) this[i];
        }

        public override long GetInt64(int i)
        {
            return (long) this[i];
        }

        public override string GetName(int i)
        {
            return _memberNames[i];
        }

        public override int GetOrdinal(string name)
        {
            return Array.IndexOf(_memberNames, name);
        }


        public override DataTable GetSchemaTable()
        {
            // these are the columns used by DataTable load
            var table = new DataTable
                        {
                            Columns =
                            {
                                {"ColumnOrdinal", typeof(int)},
                                {"ColumnName", typeof(string)},
                                {"DataType", typeof(Type)},
                                {"ColumnSize", typeof(int)},
                                {"AllowDBNull", typeof(bool)}
                            }
                        };
            var rowData = new object[5];
            for (var i = 0; i < _memberNames.Length; i++)
            {
                rowData[0] = i;
                rowData[1] = _memberNames[i];
                rowData[2] = _effectiveTypes == null ? typeof(object) : _effectiveTypes[i];
                rowData[3] = -1;
                rowData[4] = _allowNull?[i] ?? true;
                table.Rows.Add(rowData);
            }
            return table;
        }

        public override string GetString(int i)
        {
            return (string) this[i];
        }

        public override object GetValue(int i)
        {
            return this[i];
        }

        public override int GetValues(object[] values)
        {
            // duplicate the key fields on the stack
            var members = _memberNames;
            var current = _current;
            var accessor = _accessor;

            var count = Min(values.Length, members.Length);
            for (var i = 0; i < count; i++)
            {
                values[i] = accessor[current, members[i]] ?? DBNull.Value;
            }
            return count;
        }

        public override bool IsDBNull(int i)
        {
            return this[i] is DBNull;
        }

        public override bool NextResult()
        {
            _active = false;
            return false;
        }

        public override bool Read()
        {
            if (_active)
            {
                var tmp = _source;
                if (tmp != null && tmp.MoveNext())
                {
                    _current = tmp.Current;
                    return true;
                }
                _active = false;
            }
            _current = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Shutdown();
            }
        }

        protected override DbDataReader GetDbDataReader(int i)
        {
            throw new NotSupportedException();
        }

        private void Shutdown()
        {
            _active = false;
            _current = null;
            var tmp = _source as IDisposable;
            _source = null;
            tmp?.Dispose();
        }
    }
}