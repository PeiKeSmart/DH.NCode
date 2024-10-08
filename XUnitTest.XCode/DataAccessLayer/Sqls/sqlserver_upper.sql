Create Table [USER](
	ID int IDENTITY(1,1) Primary Key,
	NAME nvarchar(50) NOT NULL DEFAULT '',
	PASSWORD nvarchar(200) NULL,
	DISPLAYNAME nvarchar(50) NULL,
	SEX int NOT NULL DEFAULT 0,
	MAIL nvarchar(50) NULL,
	MAILVERIFIED bit NOT NULL DEFAULT 0,
	MOBILE nvarchar(50) NULL,
	MOBILEVERIFIED bit NOT NULL DEFAULT 0,
	CODE nvarchar(50) NULL,
	AREAID int NOT NULL DEFAULT 0,
	AVATAR nvarchar(200) NULL,
	ROLEID int NOT NULL DEFAULT 3,
	ROLEIDS nvarchar(200) NULL,
	DEPARTMENTID int NOT NULL DEFAULT 0,
	[ONLINE] bit NOT NULL DEFAULT 0,
	ENABLE bit NOT NULL DEFAULT 0,
	AGE int NOT NULL DEFAULT 0,
	BIRTHDAY datetime NULL,
	LOGINS int NOT NULL DEFAULT 0,
	LASTLOGIN datetime NULL,
	LASTLOGINIP nvarchar(50) NULL,
	REGISTERTIME datetime NULL,
	REGISTERIP nvarchar(50) NULL,
	ONLINETIME int NOT NULL DEFAULT 0,
	EX1 int NOT NULL DEFAULT 0,
	EX2 int NOT NULL DEFAULT 0,
	EX3 float NOT NULL DEFAULT 0,
	EX4 nvarchar(50) NULL,
	EX5 nvarchar(50) NULL,
	EX6 nvarchar(50) NULL,
	UPDATEUSER nvarchar(50) NOT NULL DEFAULT '',
	UPDATEUSERID int NOT NULL DEFAULT 0,
	UPDATEIP nvarchar(50) NULL,
	UPDATETIME datetime NOT NULL DEFAULT '0001-01-01',
	REMARK nvarchar(500) NULL
)