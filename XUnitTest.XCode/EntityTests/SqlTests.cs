﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using NewLife.Data;
using NewLife.Security;
using NewLife.UnitTest;
using XCode;
using XCode.DataAccessLayer;
using XCode.Membership;
using XCode.Shards;
using Xunit;
using XUnitTest.XCode.TestEntity;

namespace XUnitTest.XCode.EntityTests;

[Collection("Database")]
[TestCaseOrderer("DH.UnitTest.PriorityOrderer", "DH.UnitTest")]
public class SqlTests
{
    public SqlTests()
    {
        DAL.AddConnStr("mysql", "Server=.;Port=3306;Database=membership;Uid=root;Pwd=root", null, "mysql");
        DAL.AddConnStr("mysql_underline", "Server=.;Port=3306;Database=membership_underline;Uid=root;Pwd=root;NameFormat=underline", null, "mysql");
    }

    [TestOrder(10)]
    [Fact]
    public void InsertTestSQLite()
    {
        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        // 设置默认值
        var updater = User.Meta.Table.Fields.FirstOrDefault(e => e.Name == "UpdateUser");
        updater.Column.DefaultValue = "newlife";
        //var updater = User.Meta.Table.DataTable.Columns.FirstOrDefault(e => e.Name == "UpdateUser");
        //updater.DefaultValue = "newlife";

        var user = new User
        {
            Name = "Stone",
            DisplayName = "大石头",
            Enable = true,

            RegisterTime = new DateTime(2020, 8, 22),
            UpdateTime = new DateTime(2020, 9, 1),
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Insert);
        Assert.Equal(@"Insert Into User(Name,DisplayName,Sex,MailVerified,MobileVerified,AreaId,RoleID,DepartmentID,Online,Enable,Age,Logins,RegisterTime,OnlineTime,Ex1,Ex2,Ex3,UpdateUser,UpdateUserID,UpdateTime) Values('Stone','大石头',0,0,0,0,0,0,0,1,0,0,'2020-08-22 00:00:00',0,0,0,0,'newlife',0,'2020-09-01 00:00:00')", sql);
    }

    [TestOrder(10)]
    [Fact]
    public void InsertTestMySqlUnderline()
    {
        using var split = User.Meta.CreateSplit("mysql_underline", null);

        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        // 设置默认值
        var updater = User.Meta.Table.Fields.FirstOrDefault(e => e.Name == "UpdateUser");
        updater.Column.DefaultValue = "newlife";

        var user = new User
        {
            Name = "Stone",
            DisplayName = "大石头",
            Enable = true,

            RegisterTime = new DateTime(2020, 8, 22),
            UpdateTime = new DateTime(2020, 9, 1),
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Insert);
        Assert.Equal(@"Insert Into `user`(name,display_name,sex,mail_verified,mobile_verified,area_id,role_id,department_id,online,enable,age,logins,register_time,online_time,ex1,ex2,ex3,update_user,update_user_id,update_time) Values('Stone','大石头',0,0,0,0,0,0,0,1,0,0,'2020-08-22 00:00:00',0,0,0,0,'newlife',0,'2020-09-01 00:00:00')", sql);
    }

    [TestOrder(20)]
    [Fact]
    public void UpdateTestSQLite()
    {
        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        var user = new User
        {
            ID = 2,
            Name = "Stone",
            DisplayName = "大石头",
            Enable = true,

            RegisterTime = new DateTime(2020, 8, 22),
            UpdateTime = new DateTime(2020, 9, 1),
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Update);
        Assert.Equal(@"Update User Set Name='Stone',DisplayName='大石头',Enable=1,RegisterTime='2020-08-22 00:00:00',UpdateTime='2020-09-01 00:00:00' Where ID=2", sql);
    }

    [TestOrder(20)]
    [Fact]
    public void UpdateTestMySqlUnderline()
    {
        using var split = User.Meta.CreateSplit("mysql_underline", null);

        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        var user = new User
        {
            ID = 2,
            Name = "Stone",
            DisplayName = "大石头",
            Enable = true,

            RegisterTime = new DateTime(2020, 8, 22),
            UpdateTime = new DateTime(2020, 9, 1),
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Update);
        Assert.Equal(@"Update `user` Set name='Stone',display_name='大石头',enable=1,register_time='2020-08-22 00:00:00',update_time='2020-09-01 00:00:00' Where id=2", sql);
    }

    [TestOrder(30)]
    [Fact]
    public void DeleteTestSQLite()
    {
        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        var user = new User
        {
            ID = 2,
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Delete);
        Assert.Equal(@"Delete From User Where ID=2", sql);
    }

    [TestOrder(30)]
    [Fact]
    public void DeleteTestMySqlUnderline()
    {
        using var split = User.Meta.CreateSplit("mysql_underline", null);

        var factory = User.Meta.Factory;
        var session = User.Meta.Session;

        var user = new User
        {
            ID = 2,
        };

        var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Delete);
        Assert.Equal(@"Delete From `user` Where id=2", sql);
    }

    [TestOrder(40)]
    [Fact]
    public void SelectTestSQLite()
    {
        var exp = User._.Name == "Stone" & User._.DisplayName == "大石头" & User._.Logins > 0 & User._.RegisterTime < new DateTime(2020, 9, 1);
        var builder = User.CreateBuilder(exp, User._.UpdateUserID.Desc(), null);
        var sql = builder.ToString();
        Assert.Equal(@"Select * From User Where Name='Stone' And DisplayName='大石头' And Logins>0 And RegisterTime<'2020-09-01 00:00:00' Order By UpdateUserID Desc", sql);
    }

    [TestOrder(40)]
    [Fact]
    public void SelectTestMySqlUnderline()
    {
        using var split = User.Meta.CreateSplit("mysql_underline", null);

        var exp = User._.Name == "Stone" & User._.DisplayName == "大石头" & User._.Logins > 0 & User._.RegisterTime < new DateTime(2020, 9, 1);
        var builder = User.CreateBuilder(exp, User._.UpdateUserID.Desc(), null);
        var sql = builder.ToString();
        Assert.Equal(@"Select * From `user` Where name='Stone' And display_name='大石头' And logins>0 And register_time<'2020-09-01 00:00:00' Order By update_user_id Desc", sql);
    }

    //[Fact]
    //public void SplitTestSQLite()
    //{
    //    User2.Meta.ShardTableName = e => $"User_{e.RegisterTime:yyyyMM}";

    //    var user = new User2
    //    {
    //        Name = "Stone",
    //        DisplayName = "大石头",
    //        Enable = true,

    //        RegisterTime = new DateTime(2020, 8, 22),
    //        UpdateTime = new DateTime(2020, 9, 1),
    //    };
    //    User2.Meta.CreateShard(user);

    //    var factory = User2.Meta.Factory;
    //    var session = User2.Meta.Session;

    //    var sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Insert);
    //    Assert.Equal(@"Insert Into User_202008(Name,Password,DisplayName,Sex,Mail,Mobile,Code,Avatar,RoleID,RoleIds,DepartmentID,Online,Enable,Logins,LastLogin,LastLoginIP,RegisterTime,RegisterIP,Ex1,Ex2,Ex3,Ex4,Ex5,Ex6,UpdateUser,UpdateUserID,UpdateIP,UpdateTime,Remark) Values('Stone',null,'大石头',0,null,null,null,null,0,null,0,0,1,0,null,null,'2020-08-22 00:00:00',null,0,0,0,null,null,null,null,0,null,'2020-09-01 00:00:00',null)", sql);

    //    user.ID = 2;
    //    sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Update);
    //    Assert.Equal(@"Update User_202008 Set Name='Stone',DisplayName='大石头',Enable=1,RegisterTime='2020-08-22 00:00:00',UpdateTime='2020-09-01 00:00:00' Where ID=2", sql);

    //    sql = factory.Persistence.GetSql(session, user, DataObjectMethodType.Delete);
    //    Assert.Equal(@"Delete From User_202008 Where ID=2", sql);

    //    // 恢复现场，避免影响其它测试用例
    //    User2.Meta.ShardTableName = null;
    //}

    [TestOrder(50)]
    [Fact]
    public void ShardTestSQLite()
    {
        // 配置自动分表策略，一般在实体类静态构造函数中配置
        var shard = new TimeShardPolicy("RegisterTime", User2.Meta.Factory)
        {
            //Field = User2._.RegisterTime,
            TablePolicy = "{0}_{1:yyyyMM}",
        };
        User2.Meta.ShardPolicy = shard;

        // 拦截Sql
        var sql = "";
        DAL.LocalFilter = s => sql = s;

        var user = new User2
        {
            Name = Rand.NextString(8),

            RegisterTime = new DateTime(2020, 8, 22),
            UpdateTime = new DateTime(2020, 9, 1),
        };

        // 添删改查全部使用新表名
        user.Insert();
        Assert.StartsWith(@"[test] Insert Into User2_202008(", sql);

        user.DisplayName = Rand.NextString(16);
        user.Update();
        Assert.StartsWith(@"[test] Update User2_202008 Set", sql);

        user.Delete();
        Assert.StartsWith(@"[test] Delete From User2_202008 Where", sql);

        // 恢复现场，避免影响其它测试用例
        User2.Meta.ShardPolicy = null;
    }

    [TestOrder(52)]
    [Fact]
    public void ShardTestSQLite2()
    {
        // 配置自动分表策略，一般在实体类静态构造函数中配置
        var shard = new TimeShardPolicy("ID", Log2.Meta.Factory)
        {
            //Field = Log2._.ID,
            ConnPolicy = "{0}_{1:yyyy}",
            TablePolicy = "{0}_{1:yyyyMMdd}",
        };
        Log2.Meta.ShardPolicy = shard;

        // 拦截Sql，仅为了断言，非业务代码
        var sqls = new List<String>();
        DAL.LocalFilter = s => sqls.Add(s);

        var time = DateTime.Now;
        var log = new Log2
        {
            Action = "分表",
            Category = Rand.NextString(8),
        };

        // 添删改查全部使用新表名
        log.Insert();
        Assert.StartsWith($"[test_{time:yyyy}] Insert Into Log2_{time:yyyyMMdd}(", sqls[^1]);

        log.Category = Rand.NextString(16);
        log.Update();
        Assert.StartsWith($"[test_{time:yyyy}] Update Log2_{time:yyyyMMdd} Set", sqls[^1]);

        log.Delete();
        Assert.StartsWith($"[test_{time:yyyy}] Delete From Log2_{time:yyyyMMdd} Where", sqls[^1]);

        var list = Log2.Search(null, null, -1, null, -1, time.AddHours(-24), time, null, new PageParameter { PageSize = 100 });
        sqls.RemoveAll(e => e.EndsWith("sqlite_master"));
        Assert.StartsWith($"[test_{time.AddHours(-24):yyyy}] Select * From Log2_{time.AddHours(-24):yyyyMMdd} Where", sqls[^2]);
        Assert.StartsWith($"[test_{time:yyyy}] Select * From Log2_{time.AddHours(-00):yyyyMMdd} Where", sqls[^1]);

        list = Log2.Search(null, null, -1, null, -1, time.AddHours(-24), time, null, new PageParameter { PageIndex = 2, PageSize = 100 });
        sqls.RemoveAll(e => e.EndsWith("sqlite_master"));
        Assert.StartsWith($"[test_{time.AddHours(-24):yyyy}] Select * From Log2_{time.AddHours(-24):yyyyMMdd} Where", sqls[^3]);
        Assert.StartsWith($"[test_{time.AddHours(-24):yyyy}] Select Count(*) From Log2_{time.AddHours(-24):yyyyMMdd} Where", sqls[^2]);
        Assert.StartsWith($"[test_{time:yyyy}] Select * From Log2_{time.AddHours(-00):yyyyMMdd} Where", sqls[^1]);

        // logs.Delete();
        // 恢复现场，避免影响其它测试用例
        Log2.Meta.ShardPolicy = null;
    }

    [TestOrder(54)]
    [Fact]
    public void ShardTestSQLite3()
    {
        // 配置自动分表策略，一般在实体类静态构造函数中配置
        var shard = new TimeShardPolicy(Log2._.CreateTime, Log2.Meta.Factory)
        {
            ConnPolicy = "{0}_{1:yyyy}",
            TablePolicy = "{0}_{1:yyyyMMdd}",
        };
        Log2.Meta.ShardPolicy = shard;

        // 拦截Sql，仅为了断言，非业务代码
        var sqls = new List<String>();
        DAL.LocalFilter = s => sqls.Add(s);

        var time = DateTime.Now;
        new Log2
        {
            Action = "分表1",
            Category = Rand.NextString(8),
            CreateTime = time,
        }.Save();
        Assert.StartsWith($"[test_{time:yyyy}] Insert Into Log2_{time:yyyyMMdd}(", sqls[^1]);

        time = time.AddDays(-1);
        new Log2
        {
            Action = "分表2",
            Category = Rand.NextString(8),
            CreateTime = time,
        }.Insert();
        Assert.StartsWith($"[test_{time:yyyy}] Insert Into Log2_{time:yyyyMMdd}(", sqls[^1]);

        time = time.AddDays(-1);
        new Log2
        {
            Action = "分表3",
            Category = Rand.NextString(8),
            CreateTime = time,
        }.Insert();
        Assert.StartsWith($"[test_{time:yyyy}] Insert Into Log2_{time:yyyyMMdd}(", sqls[^1]);

        // 配置自动分表策略，一般在实体类静态构造函数中配置 
        //var shard = new TimeShardPolicy("CreateTime", Log2.Meta.Factory)
        //{
        //    //Field = Log2._.ID,
        //    ConnPolicy = "{0}_{1:yyyy}",
        //    TablePolicy = "{0}_{1:yyyyMMdd}",
        //};
        //Log2.Meta.ShardPolicy = shard;
        //// 拦截Sql，仅为了断言，非业务代码
        //var sqls = new List<String>();
        //DAL.LocalFilter = s => sqls.Add(s);

        //var time = DateTime.Now;
        //var logs = new List<Log2> { new Log2 {Action = "分表",
        //    Category = Rand.NextString(8) } , new Log2 {Action = "分表",
        //    Category = Rand.NextString(8) } };
        //logs.Save();
        //Assert.StartsWith($"[test_{time:yyyy}] Insert Into Log2_{time:yyyyMMdd}(", sqls[^1]);

        var exp = new WhereExpression();
        exp &= Log2._.CreateTime.Between(time.Date, time.Date.AddDays(2));
        var list = Log2.FindAll(exp);
        sqls.RemoveAll(e => e.EndsWith("sqlite_master"));
        Assert.StartsWith($"[test_{time.AddDays(0):yyyy}] Select * From Log2_{time.AddDays(0):yyyyMMdd} Where CreateTime>=", sqls[^3]);
        Assert.StartsWith($"[test_{time.AddDays(1):yyyy}] Select * From Log2_{time.AddDays(1):yyyyMMdd} Where CreateTime>=", sqls[^2]);
        Assert.StartsWith($"[test_{time.AddDays(2):yyyy}] Select * From Log2_{time.AddDays(2):yyyyMMdd} Where CreateTime>=", sqls[^1]);
    }
}