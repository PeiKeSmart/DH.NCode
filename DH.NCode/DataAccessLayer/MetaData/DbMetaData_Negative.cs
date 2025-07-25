﻿using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using NewLife;
using NewLife.Collections;
using NewLife.Reflection;
using NewLife.Security;

namespace XCode.DataAccessLayer;

/* 反向工程层次结构：
 *  SetTables
 *      OnSetTables
 *          CheckDatabase
 *          CheckAllTables
 *              GetTables
 *              CheckTable
 *                  CreateTable
 *                      DDLSchema.CreateTable
 *                      DDLSchema.AddTableDescription
 *                      DDLSchema.AddColumnDescription
 *                      DDLSchema.CreateIndex
 *                  CheckColumnsChange
 *                      DDLSchema.AddColumn
 *                      DDLSchema.AddColumnDescription
 *                      DDLSchema.DropColumn
 *                      IsColumnChanged
 *                          DDLSchema.AlterColumn
 *                      IsColumnDefaultChanged
 *                          ChangeColmnDefault
 *                              DDLSchema.DropDefault
 *                              DDLSchema.AddDefault
 *                      DropColumnDescription
 *                      AddColumnDescription
 *                  =>SQLite.CheckColumnsChange
 *                      ReBuildTable
 *                          CreateTableSQL
 *                  CheckTableDescriptionAndIndex
 *                      DropTableDescription
 *                      AddTableDescription
 *                      DDLSchema.DropIndex
 *                      DDLSchema.CreateIndex
 */

/* CreateTableSQL层次结构：
 *  CreateTableSQL
 *      FieldClause
 *          GetFieldType
 *              FindDataType
 *              GetFormatParam
 *                  GetFormatParamItem
 *          GetFieldConstraints
 *          GetFieldDefault
 *              CheckAndGetDefaultDateTimeNow
 */

internal partial class DbMetaData
{
    #region 属性
    private String ConnName => Database.ConnName;

    #endregion

    #region 反向工程
    /// <summary>设置表模型，检查数据表是否匹配表模型，反向工程</summary>
    /// <param name="mode">设置</param>
    /// <param name="tables"></param>
    public void SetTables(Migration mode, params IDataTable[] tables)
    {
        if (mode == Migration.Off) return;

        var set = XCodeSetting.Current;

        OnSetTables(tables, mode, set);
    }

    protected virtual void OnSetTables(IDataTable[] tables, Migration mode, XCodeSetting set)
    {
        var dbExist = CheckDatabase(mode);

        CheckAllTables(tables, mode, dbExist, set);
    }

    private Boolean? hasCheckedDatabase;
    private Boolean CheckDatabase(Migration mode)
    {
        if (hasCheckedDatabase != null) return hasCheckedDatabase.Value;

        //数据库检查
        var dbExist = false;
        try
        {
            dbExist = (Boolean)(SetSchema(DDLSchema.DatabaseExist) ?? false);
        }
        catch
        {
            // 如果异常，默认认为数据库存在
            dbExist = true;
        }

        if (!dbExist)
        {
            if (mode > Migration.ReadOnly)
            {
                WriteLog("创建数据库：{0}", ConnName);
                SetSchema(DDLSchema.CreateDatabase, [null, null]);

                dbExist = true;
            }
            else
            {
                var sql = GetSchemaSQL(DDLSchema.CreateDatabase, [null, null]);
                if (String.IsNullOrEmpty(sql))
                    WriteLog("请为连接{0}创建数据库！", ConnName);
                else
                    WriteLog("请为连接{0}创建数据库，使用以下语句：{1}", ConnName, Environment.NewLine + sql);
            }
        }

        hasCheckedDatabase = dbExist;
        return dbExist;
    }

    private void CheckAllTables(IDataTable[] tables, Migration mode, Boolean dbExit, XCodeSetting set)
    {
        IList<IDataTable>? dbtables = null;
        if (dbExit)
        {
            var tableNames = tables.Select(e => FormatName(e, false)).ToArray();
            WriteLog("[{0}]待检查数据表：{1}", Database.ConnName, tableNames.Join());
            dbtables = OnGetTables(tableNames);
        }

        foreach (var item in tables)
        {
            try
            {
                var name = FormatName(item, false);

                // 在MySql中，可能存在同名表（大小写不一致），需要先做确定查找，再做不区分大小写的查找
                var dbtable = dbtables?.FirstOrDefault(e => e.TableName == name);
                dbtable ??= dbtables?.FirstOrDefault(e => e.TableName.EqualIgnoreCase(name));

                // 判断指定表是否存在于数据库中，以决定是创建表还是修改表
                if (dbtable != null)
                    CheckTable(item, dbtable, mode, set);
                else
                    CheckTable(item, null, mode, set);
            }
            catch (Exception ex)
            {
                WriteLog(ex.ToString());
            }
        }
    }

    protected virtual void CheckTable(IDataTable entitytable, IDataTable? dbtable, Migration mode, XCodeSetting set)
    {
        var @readonly = mode <= Migration.ReadOnly;
        if (dbtable == null)
        {
            // 没有字段的表不创建
            if (entitytable.Columns.Count <= 0) return;

            WriteLog("创建表：{0}({1})", entitytable.TableName, entitytable.Description);

            var sb = new StringBuilder();
            // 建表，如果不是onlySql，执行时DAL会输出SQL日志
            CreateTable(sb, entitytable, @readonly);

            // 仅获取语句
            if (@readonly) WriteLog($"DDL模式[{mode}]，请手工创建表：{entitytable.TableName}{Environment.NewLine}{sb}");
        }
        else
        {
            var onlyCreate = mode < Migration.Full;
            var sb = new StringBuilder();

            if (set.CheckComment)
            {
                var sql = CheckTableDescription(entitytable, dbtable, mode);
                if (!sql.IsNullOrEmpty()) Append(sb, ";" + Environment.NewLine, sql);
            }

            if (set.CheckDeleteIndex)
            {
                // 先删除索引，后面才有可能删除字段
                var sql = CheckDeleteIndex(entitytable, dbtable, mode);
                if (!sql.IsNullOrEmpty()) Append(sb, ";" + Environment.NewLine, sql);
            }

            {
                var sql = CheckColumnsChange(entitytable, dbtable, @readonly, onlyCreate, set);
                if (!sql.IsNullOrEmpty()) Append(sb, ";" + Environment.NewLine, sql);
            }

            if (set.CheckAddIndex)
            {
                // 新增字段后，可能需要删除索引
                var sql = CheckAddIndex(entitytable, dbtable, mode);
                if (!sql.IsNullOrEmpty()) Append(sb, ";" + Environment.NewLine, sql);
            }

            if (sb.Length > 0) WriteLog($"DDL模式[{mode}]，请手工修改表[{dbtable.TableName}]：{Environment.NewLine}{sb}");
        }
    }

    /// <summary>检查字段改变。某些数据库（如SQLite）没有添删改字段的DDL语法，可重载该方法，使用重建表方法ReBuildTable</summary>
    /// <param name="entitytable"></param>
    /// <param name="dbtable"></param>
    /// <param name="readonly"></param>
    /// <param name="onlyCreate"></param>
    /// <param name="set"></param>
    /// <returns>返回未执行语句</returns>
    protected virtual String CheckColumnsChange(IDataTable entitytable, IDataTable dbtable, Boolean @readonly, Boolean onlyCreate, XCodeSetting set)
    {
        var sb = new StringBuilder();
        var etdic = entitytable.Columns.ToDictionary(e => FormatName(e), e => e, StringComparer.OrdinalIgnoreCase);
        var dbdic = dbtable.Columns.ToDictionary(e => FormatName(e), e => e, StringComparer.OrdinalIgnoreCase);

        #region 新增列
        foreach (var item in entitytable.Columns)
        {
            if (!dbdic.ContainsKey(FormatName(item)))
            {
                // 非空字段需要重建表
                if (!item.Nullable)
                {
                    //var sql = ReBuildTable(entitytable, dbtable);
                    //if (noDelete)
                    //{
                    //    WriteLog("数据表新增非空字段[{0}]，需要重建表，请手工执行：\r\n{1}", item.Name, sql);
                    //    return sql;
                    //}

                    //Database.CreateSession().Execute(sql);
                    //return String.Empty;

                    // 非空字段作为可空字段新增，避开重建表
                    // 如果新字段是非空，但是没有默认值，那么强制改为允许空
                    if (item.DefaultValue == null)
                        item.Nullable = true;
                }

                PerformSchema(sb, @readonly, DDLSchema.AddColumn, item);
                if (!item.Description.IsNullOrEmpty()) PerformSchema(sb, @readonly, DDLSchema.AddColumnDescription, item);
            }
        }
        #endregion

        #region 删除列
        var sbDelete = new StringBuilder();
        for (var i = dbtable.Columns.Count - 1; i >= 0; i--)
        {
            var item = dbtable.Columns[i];
            if (!etdic.ContainsKey(FormatName(item)))
            {
                if (!String.IsNullOrEmpty(item.Description)) PerformSchema(sb, @readonly || onlyCreate, DDLSchema.DropColumnDescription, item);
                PerformSchema(sbDelete, @readonly || onlyCreate, DDLSchema.DropColumn, item);
            }
        }
        if (sbDelete.Length > 0)
        {
            //if (noDelete)
            //{
            //    // 不许删除列，显示日志
            //    WriteLog("数据表中发现有多余字段，请手工执行以下语句删除：" + Environment.NewLine + sbDelete);
            //}
            //else
            //{
            if (sb.Length > 0) sb.AppendLine(";");
            sb.Append(sbDelete);
            //}
        }
        #endregion

        #region 修改列
        // 开发时的实体数据库
        var entityDb = DbFactory.Create(entitytable.DbType);
        //if (entityDb == null) throw new NotSupportedException($"Not supported DbType [{entitytable.DbType}]");

        foreach (var item in entitytable.Columns)
        {
            if (!dbdic.TryGetValue(FormatName(item), out var dbf)) continue;

            // 对于修改列，只读或者只创建，都只要sql
            if (IsColumnTypeChanged(item, dbf))
            {
                WriteLog("字段[{0}.{1}]类型需要由数据库的[{2}]改变为实体的[{3}]，RawType={4}", entitytable.Name, item.Name, dbf.DataType?.Name, item.DataType?.FullName.TrimStart("System."), dbf.RawType);
                PerformSchema(sb, @readonly || onlyCreate, DDLSchema.AlterColumn, item, dbf);
            }
            else if (IsColumnLengthChanged(item, dbf, entityDb))
                PerformSchema(sb, @readonly, DDLSchema.AlterColumn, item, dbf);
            else if (IsColumnChanged(item, dbf, entityDb))
                PerformSchema(sb, @readonly || onlyCreate, DDLSchema.AlterColumn, item, dbf);

            //if (item.Description + "" != dbf.Description + "")
            if (set.CheckComment && FormatDescription(item.Description) != FormatDescription(dbf.Description))
            {
                // 先删除旧注释
                //if (dbf.Description != null) PerformSchema(sb, noDelete, DDLSchema.DropColumnDescription, dbf);

                // 加上新注释
                if (!item.Description.IsNullOrEmpty()) PerformSchema(sb, @readonly || onlyCreate, DDLSchema.AddColumnDescription, item);
            }
        }
        #endregion

        return sb.ToString();
    }

    /// <summary>检查表说明和索引</summary>
    /// <param name="entitytable"></param>
    /// <param name="dbtable"></param>
    /// <param name="mode"></param>
    /// <returns>返回未执行语句</returns>
    protected virtual String? CheckTableDescription(IDataTable entitytable, IDataTable dbtable, Migration mode)
    {
        var @readonly = mode <= Migration.ReadOnly;

        var sb = new StringBuilder();

        #region 表说明
        //if (entitytable.Description + "" != dbtable.Description + "")
        if (FormatDescription(entitytable.Description) != FormatDescription(dbtable.Description))
        {
            //// 先删除旧注释
            //if (!String.IsNullOrEmpty(dbtable.Description)) PerformSchema(sb, onlySql, DDLSchema.DropTableDescription, dbtable);

            // 加上新注释
            if (!String.IsNullOrEmpty(entitytable.Description)) PerformSchema(sb, @readonly, DDLSchema.AddTableDescription, entitytable);
        }
        #endregion

        if (!@readonly) return null;

        return sb.ToString();
    }

    /// <summary>根据字段名数组获取字段数组</summary>
    protected virtual IDataColumn[] MatchColumns(IDataTable table, String[] names)
    {
        if (names == null || names.Length <= 0) return [];
        var dcs = new List<IDataColumn>();
        foreach (var name in names)
        {
            var dc = MatchColumn(table, name);
            if (dc != null) dcs.Add(dc);
        }
        return dcs.ToArray();
    }

    private IDataColumn? MatchColumn(IDataTable table, String name)
    {
        foreach (var col in table.Columns)
        {
            if (String.Equals(col.Name, name, StringComparison.OrdinalIgnoreCase)) return col;
            if (String.Equals(col.ColumnName, name, StringComparison.OrdinalIgnoreCase)) return col;
            if (String.Equals(FormatName(col), name, StringComparison.OrdinalIgnoreCase)) return col;
        }
        return null;
    }

    /// <summary>检查新增索引</summary>
    /// <param name="entitytable"></param>
    /// <param name="dbtable"></param>
    /// <param name="mode"></param>
    /// <returns>返回未执行语句</returns>
    protected virtual String? CheckAddIndex(IDataTable entitytable, IDataTable dbtable, Migration mode)
    {
        var @readonly = mode <= Migration.ReadOnly;
        var onlyCreate = mode < Migration.Full;

        var sb = new StringBuilder();

        #region 新增索引
        var edis = entitytable.Indexes;
        if (edis != null)
        {
            var ids = new List<String>();
            foreach (var item in edis.ToArray())
            {
                if (item.PrimaryKey) continue;

                // 实体类中索引列名可能是属性名而不是字段名，需要转换
                var dcs = MatchColumns(entitytable, item.Columns);

                var di = ModelHelper.GetIndex(dbtable, dcs.Select(e => e.ColumnName).ToArray());
                //// 计算出来的索引，也表示没有，需要创建
                //if (di != null && di.Unique == item.Unique) continue;
                // 如果索引全部就是主键，无需创建索引
                if (entitytable.GetColumns(item.Columns).All(e => e.PrimaryKey)) continue;

                // 索引不能重复，不缺分大小写，但字段相同而顺序不同，算作不同索引
                var key = item.Columns.Join(",").ToLower();
                if (ids.Contains(key))
                    WriteLog("[{0}]索引重复 {1}({2})", entitytable.TableName, item.Name, item.Columns.Join(","));
                else
                {
                    ids.Add(key);

                    if (di == null)
                        PerformSchema(sb, @readonly, DDLSchema.CreateIndex, item);
                }

                if (di == null)
                    edis.Add(item.Clone(dbtable));
                //else
                //    di.Computed = false;
            }
        }
        #endregion

        if (!@readonly) return null;

        return sb.ToString();
    }

    /// <summary>检查删除索引</summary>
    /// <param name="entitytable"></param>
    /// <param name="dbtable"></param>
    /// <param name="mode"></param>
    /// <returns>返回未执行语句</returns>
    protected virtual String? CheckDeleteIndex(IDataTable entitytable, IDataTable dbtable, Migration mode)
    {
        var @readonly = mode <= Migration.ReadOnly;
        var onlyCreate = mode < Migration.Full;

        var sb = new StringBuilder();

        #region 删除索引
        var dbdis = dbtable.Indexes;
        if (dbdis != null)
        {
            foreach (var item in dbdis.ToArray())
            {
                // 主键的索引不能删
                if (item.PrimaryKey) continue;

                // 实体类中索引列名可能是属性名而不是字段名，需要转换
                var dcs = MatchColumns(entitytable, item.Columns);

                var di = ModelHelper.GetIndex(entitytable, dcs.Select(e => e.ColumnName).ToArray());
                if (di == null)
                {
                    PerformSchema(sb, onlyCreate, DDLSchema.DropIndex, item);
                    dbdis.Remove(item);
                }
            }
        }
        #endregion

        if (!@readonly) return null;

        return sb.ToString();
    }

    /// <summary>格式化注释，去除所有非单词字符</summary>
    /// <param name="str"></param>
    /// <returns></returns>
    private String? FormatDescription(String? str)
    {
        if (str.IsNullOrWhiteSpace()) return null;

        return Regex.Replace(
            str.Replace("\r\n", " ").Replace("\n", " ").Replace("\\", "\\\\").Replace("'", "")
            .Replace("\"", "").Replace("。", ""), @"\W", "");
    }

    /// <summary>检查字段是否有改变，除了默认值和备注以外</summary>
    /// <param name="entityColumn"></param>
    /// <param name="dbColumn"></param>
    /// <param name="entityDb"></param>
    /// <returns></returns>
    protected virtual Boolean IsColumnChanged(IDataColumn entityColumn, IDataColumn dbColumn, IDatabase? entityDb)
    {
        // 自增、主键、非空等，不再认为是字段修改，减轻反向工程复杂度
        //if (entityColumn.Identity != dbColumn.Identity) return true;
        //if (entityColumn.PrimaryKey != dbColumn.PrimaryKey) return true;
        //if (entityColumn.Nullable != dbColumn.Nullable && !entityColumn.Identity && !entityColumn.PrimaryKey) return true;

        // 是否已改变
        var isChanged = false;

        // 仅针对字符串类型比较长度
        if (!isChanged && entityColumn.DataType == typeof(String) && entityColumn.Length != dbColumn.Length)
        {
            isChanged = true;

            // 如果是大文本类型，长度可能不等
            if ((entityColumn.Length > Database.LongTextLength || entityColumn.Length <= 0)
                && (entityDb != null && dbColumn.Length > entityDb.LongTextLength || dbColumn.Length <= 0)
                || dbColumn.RawType.EqualIgnoreCase("ntext", "text", "sysname"))
                isChanged = false;
        }

        return isChanged;
    }

    /// <summary>检查字段长度是否扩大</summary>
    /// <param name="entityColumn"></param>
    /// <param name="dbColumn"></param>
    /// <param name="entityDb"></param>
    /// <returns></returns>
    protected virtual Boolean IsColumnLengthChanged(IDataColumn entityColumn, IDataColumn dbColumn, IDatabase? entityDb)
    {
        // 是否已改变
        var isChanged = false;

        // 仅针对字符串类型比较长度
        if (!isChanged && entityColumn.DataType == typeof(String) && entityColumn.Length > dbColumn.Length)
        {
            isChanged = true;

            // 如果是大文本类型，长度可能不等
            if ((entityColumn.Length > Database.LongTextLength || entityColumn.Length <= 0)
                && (entityDb != null && dbColumn.Length > entityDb.LongTextLength || dbColumn.Length <= 0)
                || dbColumn.RawType.EqualIgnoreCase("ntext", "text", "sysname"))
                isChanged = false;
        }

        return isChanged;
    }

    protected virtual Boolean IsColumnTypeChanged(IDataColumn entityColumn, IDataColumn dbColumn)
    {
        var type = entityColumn.DataType;
        if (type == null || dbColumn.DataType == null) return true;

        //if (type.IsEnum) type = typeof(Int32);
        if (type.IsEnum && (dbColumn.DataType.IsInt() || dbColumn.DataType == typeof(Boolean))) return false;
        if (type == dbColumn.DataType) return false;

        var type2 = Nullable.GetUnderlyingType(type);
        if (type2 == dbColumn.DataType) return false;

        //// 整型不做改变
        //if (type.IsInt() && dbColumn.DataType.IsInt()) return false;

        // 类型不匹配，不一定就是有改变，还要查找类型对照表是否有匹配的，只要存在任意一个匹配，就说明是合法的
        foreach (var item in FieldTypeMaps)
        {
            //if (entityColumn.DataType == item.Key && dbColumn.DataType == item.Value) return false;
            // 把不常用的类型映射到常用类型，比如数据库SByte映射到实体类Byte，UInt32映射到Int32，而不需要重新修改数据库
            if (dbColumn.DataType == item.Key && (type == item.Value || type2 == item.Value)) return false;
        }

        return true;
    }

    protected virtual String RebuildTable(IDataTable entitytable, IDataTable dbtable)
    {
        // 通过重建表的方式修改字段
        var tableName = dbtable.TableName;
        var tempTableName = "Temp_" + tableName + "_" + Rand.Next(1000, 10000);
        tableName = FormatName(dbtable);
        //tempTableName = FormatName(tempTableName);

        // 每个分号后面故意加上空格，是为了让DbMetaData执行SQL时，不要按照分号加换行来拆分这个SQL语句
        var sb = new StringBuilder();
        //sb.AppendLine("BEGIN TRANSACTION; ");

        // 释放旧索引
        foreach (var di in dbtable.Indexes)
        {
            sb.Append(DropIndexSQL(di));
            sb.AppendLine("; ");
        }

        sb.Append(RenameTable(tableName, tempTableName));
        sb.AppendLine("; ");
        sb.Append(CreateTableSQL(entitytable));
        sb.AppendLine("; ");

        // 如果指定了新列和旧列，则构建两个集合
        if (entitytable.Columns != null && entitytable.Columns.Count > 0 && dbtable.Columns != null && dbtable.Columns.Count > 0)
        {
            var db = Database;

            var sbName = new StringBuilder();
            var sbValue = new StringBuilder();
            foreach (var item in entitytable.Columns)
            {
                var fname = FormatName(item);
                var type = item.DataType;
                var field = dbtable.GetColumn(item.ColumnName);
                if (field == null)
                {
                    // 如果新增了不允许空的列，则处理一下默认值
                    if (!item.Nullable)
                    {
                        if (type == typeof(String))
                        {
                            if (sbName.Length > 0) sbName.Append(", ");
                            if (sbValue.Length > 0) sbValue.Append(", ");
                            sbName.Append(fname);
                            sbValue.Append("''");
                        }
                        else if (type == typeof(Int16) || type == typeof(Int32) || type == typeof(Int64) ||
                            type == typeof(Single) || type == typeof(Double) || type == typeof(Decimal))
                        {
                            if (sbName.Length > 0) sbName.Append(", ");
                            if (sbValue.Length > 0) sbValue.Append(", ");
                            sbName.Append(fname);
                            sbValue.Append('0');
                        }
                        else if (type == typeof(DateTime))
                        {
                            if (sbName.Length > 0) sbName.Append(", ");
                            if (sbValue.Length > 0) sbValue.Append(", ");
                            sbName.Append(fname);
                            sbValue.Append(db.FormatDateTime(item, DateTime.MinValue));
                        }
                        else if (type == typeof(Boolean))
                        {
                            if (sbName.Length > 0) sbName.Append(", ");
                            if (sbValue.Length > 0) sbValue.Append(", ");
                            sbName.Append(fname);
                            sbValue.Append(db.FormatValue(item, false));
                        }
                    }
                }
                else
                {
                    if (sbName.Length > 0) sbName.Append(", ");
                    if (sbValue.Length > 0) sbValue.Append(", ");
                    sbName.Append(fname);

                    var flag = false;

                    // 处理一下非空默认值
                    if (field.Nullable && !item.Nullable || !item.Nullable && db.Type == DatabaseType.SQLite)
                    {
                        flag = true;
                        if (type == typeof(String))
                            sbValue.Append($"ifnull({fname}, \'\')");
                        else if (type == typeof(Int16) || type == typeof(Int32) || type == typeof(Int64) ||
                           type == typeof(Single) || type == typeof(Double) || type == typeof(Decimal) || type != null && type.IsEnum)
                            sbValue.Append($"ifnull({fname}, 0)");
                        else if (type == typeof(DateTime))
                            sbValue.Append($"ifnull({fname}, {db.FormatDateTime(field, DateTime.MinValue)})");
                        else if (type == typeof(Boolean))
                            sbValue.Append($"ifnull({fname}, {db.FormatValue(item, false)})");
                        else
                            flag = false;
                    }

                    if (!flag)
                    {
                        //sbValue.Append(fname);

                        // 处理字符串不允许空，ntext不支持+""
                        if (type == typeof(String) && !item.Nullable && item.Length > 0 && item.Length < db.LongTextLength)
                            sbValue.Append(db.StringConcat(fname, "\'\'"));
                        else
                            sbValue.Append(fname);
                    }
                }
            }
            sb.AppendFormat("Insert Into {0}({2}) Select {3} From {1}", tableName, tempTableName, sbName, sbValue);
        }
        else
        {
            sb.AppendFormat("Insert Into {0} Select * From {1}", tableName, tempTableName);
        }
        sb.AppendLine("; ");

        // 创建新索引
        var sb2 = new StringBuilder();
        CreateIndexes(sb2, entitytable, true);
        if (sb2.Length > 0)
        {
            sb.Append(sb2.ToString().Replace(";" + Environment.NewLine, "; " + Environment.NewLine));
            sb.AppendLine("; ");
        }

        sb.AppendFormat("Drop Table {0}", tempTableName);
        //sb.AppendLine("; ");
        //sb.Append("COMMIT;");

        return sb.ToString();
    }

    protected virtual String RenameTable(String tableName, String tempTableName) => $"Alter Table {tableName} Rename To {tempTableName}";

    /// <summary>
    /// 获取架构语句，该执行的已经执行。
    /// 如果取不到语句，则输出日志信息；
    /// 如果不是纯语句，则执行；
    /// </summary>
    /// <param name="sb"></param>
    /// <param name="onlySql"></param>
    /// <param name="schema"></param>
    /// <param name="values"></param>
    /// <returns>返回是否成功</returns>
    protected virtual Boolean PerformSchema(StringBuilder sb, Boolean onlySql, DDLSchema schema, params Object[] values)
    {
        var sql = GetSchemaSQL(schema, values);

        // 只有null才表示通过非SQL的方式处理，而String.Empty表示已经通过别的SQL处理，这里不用输出日志
        if (sql == null)
        {
            // 没办法形成SQL，输出日志信息
            var s = new StringBuilder();
            if (values.Length > 0)
            {
                foreach (var item in values)
                {
                    if (s.Length > 0) s.Append(' ');
                    s.Append(item);
                }
            }

            IDataColumn? dc = null;
            IDataTable? dt = null;
            if (values.Length > 0)
            {
                dc = values[0] as IDataColumn;
                dt = values[0] as IDataTable;
            }

            switch (schema)
            {
                case DDLSchema.AddTableDescription:
                    if (dt == null) throw new ArgumentNullException(nameof(values));
                    WriteLog("{0}({1},{2})", schema, dt.TableName, dt.Description);
                    break;
                case DDLSchema.DropTableDescription:
                    WriteLog("{0}({1})", schema, dt);
                    break;
                case DDLSchema.AddColumn:
                    WriteLog("{0}({1})", schema, dc);
                    break;
                //case DDLSchema.AlterColumn:
                //    break;
                case DDLSchema.DropColumn:
                    if (dc == null) throw new ArgumentNullException(nameof(values));
                    WriteLog("{0}({1})", schema, dc.ColumnName);
                    break;
                case DDLSchema.AddColumnDescription:
                    if (dc == null) throw new ArgumentNullException(nameof(values));
                    WriteLog("{0}({1},{2})", schema, dc.ColumnName, dc.Description);
                    break;
                case DDLSchema.DropColumnDescription:
                    if (dc == null) throw new ArgumentNullException(nameof(values));
                    WriteLog("{0}({1})", schema, dc.ColumnName);
                    break;
                default:
                    WriteLog("修改表：{0} {1}", schema.ToString(), s.ToString());
                    break;
            }
            //WriteLog("修改表：{0} {1}", schema.ToString(), s.ToString());
        }

        if (onlySql)
        {
            if (!String.IsNullOrEmpty(sql))
            {
                if (sb.Length > 0) sb.AppendLine(";");
                sb.Append(sql);
            }
        }
        else
        {
            try
            {
                SetSchema(schema, values);
            }
            catch (Exception ex)
            {
                WriteLog("修改表{0}失败！{1}", schema.ToString(), ex.Message);
                return false;
            }
        }

        return true;
    }

    /// <summary>创建数据表，包括注释与索引</summary>
    /// <param name="sb"></param>
    /// <param name="table"></param>
    /// <param name="onlySql"></param>
    public virtual void CreateTable(StringBuilder sb, IDataTable table, Boolean onlySql)
    {
        // 创建表失败后，不再处理注释和索引
        if (!PerformSchema(sb, onlySql, DDLSchema.CreateTable, table)) return;

        // 加上表注释
        if (!String.IsNullOrEmpty(table.Description)) PerformSchema(sb, onlySql, DDLSchema.AddTableDescription, table);

        // 加上字段注释
        foreach (var item in table.Columns)
        {
            if (!String.IsNullOrEmpty(item.Description)) PerformSchema(sb, onlySql, DDLSchema.AddColumnDescription, item);
        }

        // 加上索引
        CreateIndexes(sb, table, onlySql);
    }

    /// <summary>创建索引</summary>
    /// <param name="sb"></param>
    /// <param name="table"></param>
    /// <param name="onlySql"></param>
    public void CreateIndexes(StringBuilder sb, IDataTable table, Boolean onlySql)
    {
        if (table.Indexes != null)
        {
            var ids = new List<String>();
            foreach (var item in table.Indexes)
            {
                if (item.PrimaryKey) continue;
                // 如果索引全部就是主键，无需创建索引
                if (table.GetColumns(item.Columns).All(e => e.PrimaryKey)) continue;

                // 索引不能重复，不缺分大小写，但字段相同而顺序不同，算作不同索引
                var key = item.Columns.Join(",").ToLower();
                if (ids.Contains(key))
                    WriteLog("[{0}]索引重复 {1}({2})", table.TableName, item.Name, item.Columns.Join(","));
                else
                {
                    ids.Add(key);

                    PerformSchema(sb, onlySql, DDLSchema.CreateIndex, item);
                }
            }
        }
    }
    #endregion

    #region 数据定义
    /// <summary>获取数据定义语句</summary>
    /// <param name="schema">数据定义模式</param>
    /// <param name="values">其它信息</param>
    /// <returns></returns>
    public virtual String? GetSchemaSQL(DDLSchema schema, params Object?[] values)
    {
        return schema switch
        {
            DDLSchema.CreateDatabase => CreateDatabaseSQL((String)values[0]!, (String?)values[1]),
            DDLSchema.DropDatabase => DropDatabaseSQL((String)values[0]!),
            DDLSchema.DatabaseExist => DatabaseExistSQL((String)values[0]!),
            DDLSchema.CreateTable => CreateTableSQL((IDataTable)values[0]!),
            DDLSchema.DropTable => DropTableSQL((IDataTable)values[0]!),
            DDLSchema.AddTableDescription => AddTableDescriptionSQL((IDataTable)values[0]!),
            DDLSchema.DropTableDescription => DropTableDescriptionSQL((IDataTable)values[0]!),
            DDLSchema.AddColumn => AddColumnSQL((IDataColumn)values[0]!),
            DDLSchema.AlterColumn => AlterColumnSQL((IDataColumn)values[0]!, values.Length > 1 ? (IDataColumn)values[1]! : null),
            DDLSchema.DropColumn => DropColumnSQL((IDataColumn)values[0]!),
            DDLSchema.AddColumnDescription => AddColumnDescriptionSQL((IDataColumn)values[0]!),
            DDLSchema.DropColumnDescription => DropColumnDescriptionSQL((IDataColumn)values[0]!),
            DDLSchema.CreateIndex => CreateIndexSQL((IDataIndex)values[0]!),
            DDLSchema.DropIndex => DropIndexSQL((IDataIndex)values[0]!),
            DDLSchema.CompactDatabase => CompactDatabaseSQL(),
            _ => throw new NotSupportedException("不支持该操作！"),
        };
    }

    /// <summary>设置数据定义模式</summary>
    /// <param name="schema">数据定义模式</param>
    /// <param name="values">其它信息</param>
    /// <returns></returns>
    public virtual Object? SetSchema(DDLSchema schema, params Object?[] values)
    {
        if (Database is not DbBase db) return null;

        using var span = db.Tracer?.NewSpan($"db:{db.ConnName}:SetSchema:{schema}", values);

        var sql = GetSchemaSQL(schema, values);
        if (sql.IsNullOrEmpty()) return null;

        if (span != null) span.Tag += Environment.NewLine + sql;

        var session = Database.CreateSession();

        if (/*schema == DDLSchema.TableExist ||*/ schema == DDLSchema.DatabaseExist) return session.QueryCount(sql) > 0;

        // 分隔符是分号加换行，如果不想被拆开执行（比如有事务），可以在分号和换行之间加一个空格
        var sqls = sql.Split([";" + Environment.NewLine], StringSplitOptions.RemoveEmptyEntries);
        if (sqls == null || sqls.Length <= 1) return session.Execute(sql);

        session.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            foreach (var item in sqls)
            {
                session.Execute(item);
            }
            session.Commit();
        }
        catch (Exception ex)
        {
            span?.SetError(ex, null);

            session.Rollback();
            throw;
        }

        return 0;
    }

    /// <summary>字段片段</summary>
    /// <param name="field">字段</param>
    /// <param name="onlyDefine">仅仅定义。定义操作才允许设置自增和使用默认值</param>
    /// <returns></returns>
    public virtual String FieldClause(IDataColumn field, Boolean onlyDefine)
    {
        var sb = new StringBuilder();

        // 字段名
        sb.AppendFormat("{0} ", FormatName(field));

        String? typeName = null;
        // 如果还是原来的数据库类型，则直接使用
        //if (Database.DbType == field.Table.DbType) typeName = field.RawType;
        // 每种数据库的自增差异太大，理应由各自处理，而不采用原始值
        if (Database.Type == field.Table.DbType && !field.Identity) typeName = field.RawType;

        if (String.IsNullOrEmpty(typeName)) typeName = GetFieldType(field);

        sb.Append(typeName);

        // 约束
        sb.Append(GetFieldConstraints(field, onlyDefine));

        return sb.ToString();
    }

    /// <summary>字段片段</summary>
    /// <param name="table">表</param>
    /// <param name="index">序号</param>
    /// <param name="onlyDefine">仅仅定义。定义操作才允许设置自增和使用默认值</param>
    /// <returns></returns>
    public virtual String FieldClause(IDataTable table, Int32 index, Boolean onlyDefine)
    {
        var sb = new StringBuilder();
        var field = table.Columns[index];
        // 字段名
        sb.AppendFormat("{0} ", FormatName(field));

        String? typeName = null;
        // 如果还是原来的数据库类型，则直接使用
        //if (Database.DbType == field.Table.DbType) typeName = field.RawType;
        // 每种数据库的自增差异太大，理应由各自处理，而不采用原始值
        if (Database.Type == field.Table.DbType && !field.Identity) typeName = field.RawType;

        if (String.IsNullOrEmpty(typeName)) typeName = GetFieldType(field);

        sb.Append(typeName);

        // 约束
        sb.Append(GetFieldConstraints(field, onlyDefine));

        return sb.ToString();
    }

    /// <summary>取得字段约束</summary>
    /// <param name="field">字段</param>
    /// <param name="onlyDefine">仅仅定义</param>
    /// <returns></returns>
    protected virtual String? GetFieldConstraints(IDataColumn field, Boolean onlyDefine)
    {
        if (field.PrimaryKey && field.Table.PrimaryKeys.Length <= 1) return " Primary Key";

        // 是否为空
        var str = field.Nullable ? " NULL" : " NOT NULL";

        // 默认值
        if (!field.Nullable && !field.Identity)
        {
            str += GetDefault(field, onlyDefine);
        }

        return str;
    }

    /// <summary>默认值</summary>
    /// <param name="field"></param>
    /// <param name="onlyDefine"></param>
    /// <returns></returns>
    protected virtual String? GetDefault(IDataColumn field, Boolean onlyDefine)
    {
        if (field.DataType.IsInt() || field.DataType.IsEnum)
            return $" DEFAULT {field.DefaultValue.ToInt()}";
        else if (field.DataType == typeof(Boolean))
            return $" DEFAULT {(field.DefaultValue.ToBoolean() ? 1 : 0)}";
        else if (field.DataType == typeof(Double) || field.DataType == typeof(Single) || field.DataType == typeof(Decimal))
            return $" DEFAULT {field.DefaultValue.ToDouble()}";
        else if (field.DataType == typeof(DateTime))
            return $" DEFAULT {field.DefaultValue ?? "'0001-01-01'"}";
        else if (field.DataType == typeof(String) && field.Length > 0)
            return $" DEFAULT {field.DefaultValue ?? "''"}";

        return null;
    }
    #endregion

    #region 数据定义语句
    public virtual String CreateDatabaseSQL(String dbname, String? file) => $"Create Database {Database.FormatName(dbname)}";

    public virtual String DropDatabaseSQL(String dbname) => $"Drop Database {Database.FormatName(dbname)}";

    public virtual String? DatabaseExistSQL(String dbname) => null;

    public virtual String CreateTableSQL(IDataTable table)
    {
        //var fs = new List<IDataColumn>(table.Columns);
        var sb = new StringBuilder();

        sb.AppendFormat("Create Table {0}(", FormatName(table));
        for (var i = 0; i < table.Columns.Count; i++)
        {
            sb.AppendLine();
            sb.Append('\t');
            sb.Append(FieldClause(table, i, true));
            if (i < table.Columns.Count - 1) sb.Append(',');
        }
        sb.AppendLine();
        sb.Append(')');

        return sb.ToString();
    }

    public virtual String DropTableSQL(IDataTable table) => $"Drop Table {FormatName(table)}";

    //public virtual String TableExistSQL(IDataTable table) => throw new NotSupportedException("该功能未实现！");

    public virtual String? AddTableDescriptionSQL(IDataTable table) => null;

    public virtual String? DropTableDescriptionSQL(IDataTable table) => null;

    public virtual String? AddColumnSQL(IDataColumn field) => $"Alter Table {FormatName(field.Table)} Add {FieldClause(field, true)}";

    public virtual String? AlterColumnSQL(IDataColumn field, IDataColumn? oldfield) => $"Alter Table {FormatName(field.Table)} Alter Column {FieldClause(field, false)}";

    public virtual String? DropColumnSQL(IDataColumn field) => $"Alter Table {FormatName(field.Table)} Drop Column {FormatName(field)}";

    public virtual String? AddColumnDescriptionSQL(IDataColumn field) => null;

    public virtual String? DropColumnDescriptionSQL(IDataColumn field) => null;

    public virtual String CreateIndexSQL(IDataIndex index)
    {
        var sb = Pool.StringBuilder.Get();
        if (index.Unique)
            sb.Append("Create Unique Index ");
        else
            sb.Append("Create Index ");

        sb.Append(index.Name);
        var dcs = index.Table.GetColumns(index.Columns);
        sb.AppendFormat(" On {0} ({1})", FormatName(index.Table), dcs.Join(",", FormatName));

        return sb.Return(true);
    }

    public virtual String DropIndexSQL(IDataIndex index) => $"Drop Index {index.Name} On {FormatName(index.Table)}";

    public virtual String? CompactDatabaseSQL() => null;
    #endregion

    #region 操作
    public virtual String? Backup(String dbname, String? bakfile, Boolean compressed) => null;

    //public virtual Int32 CompactDatabase() => -1;
    #endregion
}