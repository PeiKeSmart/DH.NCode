﻿using System.IO;
using NewLife.Security;
using XCode;
using XCode.Membership;
using Xunit;

namespace XUnitTest.XCode.DataAccessLayer;

[Collection("Database")]
[TestCaseOrderer("DH.UnitTest.DefaultOrderer", "DH.UnitTest")]
public class DAL_Backup_Tests
{
    [Fact]
    public void BackupTest()
    {
        var n = User.Meta.Count;
        var dal = User.Meta.Session.Dal;
        var table = User.Meta.Table.DataTable;

        {
            var file = $"data/{table.Name}.table";
            dal.Backup(table, file);
            var fi = file.AsFile();
            Assert.True(fi.Exists);
            Assert.True(fi.Length > 0);
        }
        {
            var file = $"data/{table.Name}.gz";
            dal.Backup(table, file);
            var fi = file.AsFile();
            Assert.True(fi.Exists);
            Assert.True(fi.Length > 0);
        }
    }

    [Fact]
    public void BackupAllTest()
    {
        var n = User.Meta.Count;
        var dal = User.Meta.Session.Dal;
        var tables = EntityFactory.GetTables(dal.ConnName, false);

        var f = $"data/{dal.ConnName}.zip".GetFullPath();
        if (File.Exists(f)) File.Delete(f);

        dal.BackupAll(tables, $"data/{dal.ConnName}.zip");
    }

    [Fact]
    public void RestoreAllTest()
    {
        try
        {
            var dal = User.Meta.Session.Dal;

            // 随机连接名，得到SQLite连接字符串，实行导入
            User.Meta.ConnName = Rand.NextString(8);

            var dal2 = User.Meta.Session.Dal;

            var rs = dal2.RestoreAll($"data/{dal.ConnName}.zip", null, true, false);
            Assert.NotNull(rs);
        }
        finally
        {
            User.Meta.ConnName = null;
        }
    }
}