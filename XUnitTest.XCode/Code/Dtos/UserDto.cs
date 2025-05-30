using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Xml.Serialization;

namespace XCode.Code;

/// <summary>用户。用户帐号信息，以身份验证为中心，拥有多种角色，可加入多个租户</summary>
public partial class UserDto
{
    #region 属性
    /// <summary>编号</summary>
    public Int32 ID { get; set; }

    /// <summary>名称。登录用户名</summary>
    public String Name { get; set; } = null!;

    /// <summary>密码</summary>
    public String? Password { get; set; }

    /// <summary>昵称</summary>
    public String? DisplayName { get; set; }

    /// <summary>性别。未知、男、女</summary>
    public XCode.Membership.SexKinds Sex { get; set; }

    /// <summary>邮件。支持登录</summary>
    public String? Mail { get; set; }

    /// <summary>邮箱验证。邮箱是否已通过验证</summary>
    public Boolean MailVerified { get; set; }

    /// <summary>手机。支持登录</summary>
    public String? Mobile { get; set; }

    /// <summary>手机验证。手机是否已通过验证</summary>
    public Boolean MobileVerified { get; set; }

    /// <summary>代码。身份证、员工编码等，支持登录</summary>
    public String? Code { get; set; }

    /// <summary>地区。省市区</summary>
    public Int32 AreaId { get; set; }

    /// <summary>头像</summary>
    public String? Avatar { get; set; }

    /// <summary>角色。主要角色</summary>
    public Int32 RoleID { get; set; }

    /// <summary>角色组。次要角色集合</summary>
    public String? RoleIds { get; set; }

    /// <summary>部门。组织机构</summary>
    public Int32 DepartmentID { get; set; }

    /// <summary>在线</summary>
    public Boolean Online { get; set; }

    /// <summary>启用</summary>
    public Boolean Enable { get; set; }

    /// <summary>年龄。周岁</summary>
    public Int32 Age { get; set; }

    /// <summary>生日。公历年月日</summary>
    public DateTime Birthday { get; set; }

    /// <summary>登录次数</summary>
    public Int32 Logins { get; set; }

    /// <summary>最后登录</summary>
    public DateTime LastLogin { get; set; }

    /// <summary>最后登录IP</summary>
    public String? LastLoginIP { get; set; }

    /// <summary>注册时间</summary>
    public DateTime RegisterTime { get; set; }

    /// <summary>注册IP</summary>
    public String? RegisterIP { get; set; }

    /// <summary>在线时间。累计在线总时间，单位秒</summary>
    public Int32 OnlineTime { get; set; }

    /// <summary>扩展1</summary>
    public Int32 Ex1 { get; set; }

    /// <summary>扩展2</summary>
    public Int32 Ex2 { get; set; }

    /// <summary>扩展3</summary>
    public Double Ex3 { get; set; }

    /// <summary>扩展4</summary>
    public String? Ex4 { get; set; }

    /// <summary>扩展5</summary>
    public String? Ex5 { get; set; }

    /// <summary>扩展6</summary>
    public String? Ex6 { get; set; }

    /// <summary>备注</summary>
    public String? Remark { get; set; }
    #endregion
}
