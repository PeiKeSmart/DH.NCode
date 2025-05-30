﻿using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using NewLife;
using NewLife.Log;
using XCode.DataAccessLayer;

namespace XCode.Configuration;

/// <summary>数据表元数据</summary>
public class TableItem
{
    #region 特性
    /// <summary>实体类型</summary>
    public Type EntityType { get; }

    /// <summary>绑定表特性</summary>
    private readonly BindTableAttribute? _Table;

    /// <summary>绑定索引特性</summary>
    private readonly BindIndexAttribute[] _Indexes;

    private String? _description;
    private readonly DescriptionAttribute? _Description;
    /// <summary>说明</summary>
    public String? Description
    {
        get
        {
            if (_description != null) return _description;

            if (_Description != null && !String.IsNullOrEmpty(_Description.Description)) return _description = _Description.Description;
            if (_Table != null && !String.IsNullOrEmpty(_Table.Description)) return _description = _Table.Description;

            return _description;
        }
    }
    #endregion

    #region 属性
    private String? _tableName;
    /// <summary>表名。来自实体类特性，合并文件模型</summary>
    public String TableName => _tableName ??= _Table?.Name ?? EntityType.Name;

    /// <summary>原始表名</summary>
    public String RawTableName => _Table?.Name ?? EntityType.Name;

    private String? _connName;
    /// <summary>连接名。来自实体类特性，合并文件模型</summary>
    public String ConnName => _connName ??= _Table?.ConnName + "";
    #endregion

    #region 扩展属性
    /// <summary>数据字段</summary>
    [XmlArray]
    [Description("数据字段")]
    public FieldItem[] Fields { get; private set; }

    /// <summary>所有字段</summary>
    [XmlIgnore, IgnoreDataMember]
    public FieldItem[] AllFields { get; private set; }

    /// <summary>标识列</summary>
    [XmlIgnore, IgnoreDataMember]
    public FieldItem Identity { get; private set; }

    /// <summary>主键。不会返回null</summary>
    [XmlIgnore, IgnoreDataMember]
    public FieldItem[] PrimaryKeys { get; private set; }

    /// <summary>主字段。主字段作为业务主要字段，代表当前数据行意义</summary>
    public FieldItem Master { get; private set; }

    private ICollection<String> _FieldNames;
    /// <summary>字段名集合，不区分大小写的哈希表存储，外部不要修改元素数据</summary>
    [XmlIgnore, IgnoreDataMember]
    public ICollection<String> FieldNames
    {
        get
        {
            if (_FieldNames != null) return _FieldNames;

            var list = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            var dic = new Dictionary<String, String>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in Fields)
            {
                if (!list.Contains(item.Name))
                {
                    list.Add(item.Name);
                    dic.Add(item.Name, item.Name);
                }
                else
                    DAL.WriteLog("数据表{0}发现同名但不同大小写的字段{1}和{2}，违反设计原则！", TableName, dic[item.Name], item.Name);
            }
            //_FieldNames = new ReadOnlyCollection<String>(list);
            _FieldNames = list;

            return _FieldNames;
        }
    }

    private ICollection<String> _ExtendFieldNames;
    /// <summary>扩展属性集合，不区分大小写的哈希表存储，外部不要修改元素数据</summary>
    [XmlIgnore, IgnoreDataMember]
    public ICollection<String> ExtendFieldNames
    {
        get
        {
            if (_ExtendFieldNames != null) return _ExtendFieldNames;

            var list = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in AllFields)
            {
                if (!item.IsDataObjectField && !list.Contains(item.Name)) list.Add(item.Name);
            }
            _ExtendFieldNames = list;

            return _ExtendFieldNames;
        }
    }

    /// <summary>数据表架构</summary>
    [XmlIgnore, IgnoreDataMember]
    public IDataTable DataTable { get; private set; }

    /// <summary>模型检查模式</summary>
    public ModelCheckModes ModelCheckMode { get; } = ModelCheckModes.CheckAllTablesWhenInit;
    #endregion

    #region 构造
    private TableItem(Type type)
    {
        EntityType = type;
        _Table = type.GetCustomAttribute<BindTableAttribute>(true);
        if (_Table == null) throw new ArgumentOutOfRangeException(nameof(type), "类型" + type + "没有" + typeof(BindTableAttribute).Name + "特性！");

        _Indexes = type.GetCustomAttributes<BindIndexAttribute>(true).ToArray();
        //_Relations = type.GetCustomAttributes<BindRelationAttribute>(true).ToArray();
        _Description = type.GetCustomAttribute<DescriptionAttribute>(true);
        var att = type.GetCustomAttribute<ModelCheckModeAttribute>(true);
        if (att != null) ModelCheckMode = att.Mode;

        InitFields();
    }

    private static readonly ConcurrentDictionary<String, TableItem> _cache = new();
    /// <summary>创建数据表元数据信息。并合并连接名上的文件模型映射</summary>
    /// <param name="type">类型</param>
    /// <param name="connName">连接名。该类型的架构信息，则不同连接上可能存在文件模型映射，导致不一致</param>
    /// <returns></returns>
    public static TableItem Create(Type type, String? connName)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        var key = $"{type.FullName}#{connName}";
        if (_cache.TryGetValue(key, out var tableItem)) return tableItem;

        // 不能给没有BindTableAttribute特性的类型创建TableItem，否则可能会在InitFields中抛出异常
        if (type.GetCustomAttribute<BindTableAttribute>(true) == null) throw new ArgumentOutOfRangeException(nameof(type));

        // 先创建，然后合并外部文件模型
        var ti = new TableItem(type);

        if (connName.IsNullOrEmpty()) connName = ti.ConnName;
        if (!connName.IsNullOrEmpty())
        {
            // 根据默认连接名找到目标文件模型，如果存在则合并
            var table = DAL.Create(connName).ModelTables.FirstOrDefault(e => e.Name == ti.EntityType.Name);
            if (table != null) ti.Merge(table);
        }

        ti._connName = connName;

        return _cache.GetOrAdd(key, ti);
    }

    private void InitFields()
    {
        var bt = _Table;
        if (bt == null) return;

        var table = DAL.CreateTable();
        DataTable = table;
        table.TableName = bt.Name;
        //// 构建DataTable时也要注意表前缀，避免反向工程用错
        //table.TableName = GetTableName(bt);
        table.Name = EntityType.Name;
        table.DbType = bt.DbType;
        table.IsView = bt.IsView || bt.Name[0] == '#';
        table.Description = Description;
        //table.ConnName = ConnName;

        var allfields = new List<FieldItem>();
        var fields = new List<FieldItem>();
        var pkeys = new List<FieldItem>();
        foreach (var item in GetFields(EntityType))
        {
            var fi = item;
            allfields.Add(fi);

            if (fi.IsDataObjectField)
            {
                fields.Add(fi);

                var f = table.CreateColumn();
                fi.Fill(f);

                table.Columns.Add(f);
            }

            if (fi.PrimaryKey) pkeys.Add(fi);
            if (fi.IsIdentity) Identity = fi;
            if (fi.Master) Master = fi;
        }
        // 先完成allfields才能专门处理
        foreach (var item in allfields)
        {
            if (!item.IsDynamic)
            {
                // 如果不是数据字段，则检查绑定关系
                var map = item.Map;
                if (map != null)
                {
                    // 找到被关系映射的字段，拷贝相关属性
                    var fi = allfields.FirstOrDefault(e => e.Name.EqualIgnoreCase(map.Name));
                    if (fi != null)
                    {
                        if (item.OriField == null) item.OriField = fi;
                        if (item.DisplayName.IsNullOrEmpty()) item.DisplayName = fi.DisplayName;
                        if (item.Description.IsNullOrEmpty()) item.Description = fi.Description;
                        item.ColumnName = fi.ColumnName;
                    }
                }
            }
        }

        var ids = _Indexes;
        if (ids != null)
        {
            foreach (var item in ids)
            {
                var di = table.CreateIndex();
                item.Fill(di);

                if (table.GetIndex(di.Columns) != null) continue;

                // 如果索引全部就是主键，无需创建索引
                if (table.GetColumns(di.Columns).All(e => e.PrimaryKey)) continue;

                table.Indexes.Add(di);
            }

            // 检查索引重复，最左原则
            for (var i = 0; i < table.Indexes.Count && XCodeSetting.Current.CheckDuplicateIndex; i++)
            {
                var di = table.Indexes[i];
                for (var j = i + 1; j < table.Indexes.Count; j++)
                {
                    var di2 = table.Indexes[j];
                    //var flag = true;
                    //for (int k = 0; k < di.Columns.Length && k < di2.Columns.Length; k++)
                    //{
                    //    if (!di.Columns[k].Equals(di2.Columns[k]))
                    //    {
                    //        flag = false;
                    //        break;
                    //    }
                    //}
                    // 取最小长度，如果序列相等，说明前缀相同
                    var count = Math.Min(di.Columns.Length, di2.Columns.Length);
                    if (count > 0 && di.Columns.Take(count).SequenceEqual(di2.Columns.Take(count), StringComparer.OrdinalIgnoreCase))
                    {
                        var cs = di.Columns.Length == count ? di.Columns : di2.Columns;
                        var cs2 = di.Columns.Length == count ? di2.Columns : di.Columns;
                        XTrace.WriteLine("实体类[{0}]/数据表[{1}]的索引重复，可去除({2})，保留({3})", EntityType.FullName, TableName, cs.Join(), cs2.Join());
                    }
                }
            }
        }

        // 不允许为null
        AllFields = allfields.ToArray();
        Fields = fields.ToArray();
        PrimaryKeys = pkeys.ToArray();
    }

    /// <summary>获取属性，保证基类属性在前</summary>
    /// <param name="type">类型</param>
    /// <returns></returns>
    private IEnumerable<Field> GetFields(Type type)
    {
        // 先拿到所有属性，可能是先排子类，再排父类
        var list = new List<Field>();
        foreach (var item in type.GetProperties())
        {
            if (item.GetIndexParameters().Length <= 0) list.Add(new Field(this, item));
        }

        var att = type.GetCustomAttribute<ModelSortModeAttribute>(true);
        if (att == null || att.Mode == ModelSortModes.BaseFirst)
        {
            // 然后用栈来处理，基类优先
            var stack = new Stack<Field>();
            var t = type;
            while (t != null && t != typeof(EntityBase) && list.Count > 0)
            {
                // 反序入栈，因为属性可能是顺序的，这里先反序，待会出来再反一次
                // 没有数据属性的
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    if (item.DeclaringType == t && !item.IsDataObjectField)
                    {
                        stack.Push(item);
                        list.RemoveAt(i);
                    }
                }
                // 有数据属性的
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var item = list[i];
                    if (item.DeclaringType == t && item.IsDataObjectField)
                    {
                        stack.Push(item);
                        list.RemoveAt(i);
                    }
                }
                t = t.BaseType;
            }
            foreach (var item in stack)
            {
                yield return item;
            }
        }
        else
        {
            // 子类优先
            var t = type;
            while (t != null && t != typeof(EntityBase) && list.Count > 0)
            {
                // 有数据属性的
                foreach (var item in list)
                {
                    if (item.DeclaringType == t && item.IsDataObjectField) yield return item;
                }
                // 没有数据属性的
                foreach (var item in list)
                {
                    if (item.DeclaringType == t && !item.IsDataObjectField) yield return item;
                }
                t = t.BaseType;
            }
        }
    }
    #endregion

    #region 方法
    /// <summary>合并目标表到当前元数据</summary>
    /// <param name="table"></param>
    public void Merge(IDataTable table)
    {
        table = (table.Clone() as IDataTable)!;
        if (table == null) return;

        DataTable = table;

        // 合并字段
        _tableName = table.TableName;
        _description = table.Description;

        var allfields = AllFields.ToList();
        var fields = Fields.ToList();
        var pkeys = new List<FieldItem>();
        foreach (var column in table.Columns)
        {
            if (column.Name.IsNullOrEmpty()) continue;

            var fi = fields.FirstOrDefault(e => e.Name.EqualIgnoreCase(column.Name));
            if (fi is null)
            {
                fi = new Field(this, column.Name, column.DataType!, column.Description, column.Length)
                {
                    ColumnName = column.ColumnName,
                    IsNullable = column.Nullable,
                    IsIdentity = column.Identity,
                    PrimaryKey = column.PrimaryKey,
                    Master = column.Master,
                    DisplayName = column.DisplayName,
                    Description = column.Description,
                    //Map = column.Map,
                    IsDataObjectField = true,
                    Field = column,
                };

                fields.Add(fi);

                if (!allfields.Any(e => e.Name.EqualIgnoreCase(column.Name)))
                    allfields.Add(fi);
            }
            else
            {
                fi.ColumnName = column.ColumnName;
                fi.IsNullable = column.Nullable;
                fi.IsIdentity = column.Identity;
                fi.PrimaryKey = column.PrimaryKey;
                fi.Master = column.Master;
                fi.DisplayName = column.DisplayName;
                fi.Description = column.Description;
                fi.Field = column;
            }

            if (fi.PrimaryKey) pkeys.Add(fi);
            if (fi.IsIdentity) Identity = fi;
            if (fi.Master) Master = fi;
        }

        Fields = fields.ToArray();
        AllFields = allfields.ToArray();
        PrimaryKeys = pkeys.ToArray();
    }

    private IDictionary<String, Field?> _all;

    /// <summary>根据名称查找</summary>
    /// <param name="name">名称</param>
    /// <returns></returns>
    public Field? FindByName(String name)
    {
        //if (String.IsNullOrEmpty(name)) throw new ArgumentNullException("name");
        if (name.IsNullOrEmpty()) return null;
        // 特殊处理行号
        if (name.EqualIgnoreCase("RowNumber")) return null;

        // 借助字典，快速搜索数据列
        if (_all == null)
        {
            var dic = new ConcurrentDictionary<String, Field?>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in Fields)
            {
                if (item is not Field field) continue;

                if (!dic.ContainsKey(item.Name))
                    dic.TryAdd(item.Name, field);
            }
            foreach (var item in AllFields)
            {
                if (item is not Field field) continue;

                if (!dic.ContainsKey(item.Name))
                    dic.TryAdd(item.Name, field);
                else if (!item.ColumnName.IsNullOrEmpty() && !dic.ContainsKey(item.ColumnName))
                    dic.TryAdd(item.ColumnName, field);
            }

            // 宁可重复计算，也要避免锁
            _all = dic;
        }
        if (_all.TryGetValue(name, out var f)) return f;

        // 即使没有找到，也要缓存起来，避免下次重复查找
        foreach (var item in Fields)
        {
            if (item.Name.EqualIgnoreCase(name)) return _all[name] = item as Field;
        }

        foreach (var item in Fields)
        {
            if (item.ColumnName.EqualIgnoreCase(name)) return _all[name] = item as Field;
        }

        foreach (var item in AllFields)
        {
            if (item.Name.EqualIgnoreCase(name)) return _all[name] = item as Field;
        }

        foreach (var item in AllFields)
        {
            if (item.FormatedName.EqualIgnoreCase(name)) return _all[name] = item as Field;
        }

        return _all[name] = null;
    }

    /// <summary>已重载。</summary>
    /// <returns></returns>
    public override String ToString() => Description.IsNullOrEmpty() ? TableName : $"{TableName}（{Description}）";
    #endregion

    #region 动态增加字段
    ///// <summary>动态增加字段</summary>
    ///// <param name="name"></param>
    ///// <param name="type"></param>
    ///// <param name="description"></param>
    ///// <param name="length"></param>
    ///// <returns></returns>
    //public TableItem Add(String name, Type type, String? description = null, Int32 length = 0)
    //{
    //    var f = new Field(this, name, type, description, length);

    //    var list = new List<FieldItem>(Fields) { f };
    //    Fields = list.ToArray();

    //    list = new List<FieldItem>(AllFields) { f };
    //    AllFields = list.ToArray();

    //    var dc = DataTable.CreateColumn();
    //    f.Fill(dc);

    //    DataTable.Columns.Add(dc);

    //    return this;
    //}
    #endregion
}