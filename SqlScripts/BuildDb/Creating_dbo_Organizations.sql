CREATE TABLE [dbo].[Organizations]
(
[OrganizationId] [int] NOT NULL IDENTITY(1, 1),
[CreatedBy] [int] NOT NULL,
[CreatedDate] [datetime] NOT NULL,
[OrganizationStatusId] [int] NOT NULL,
[DivisionId] [int] NULL,
[LeaderMemberTypeId] [int] NULL,
[GradeAgeStart] [int] NULL,
[GradeAgeEnd] [int] NULL,
[RollSheetVisitorWks] [int] NULL,
[SecurityTypeId] [int] NOT NULL,
[FirstMeetingDate] [datetime] NULL,
[LastMeetingDate] [datetime] NULL,
[OrganizationClosedDate] [datetime] NULL,
[Location] [nvarchar] (200) NULL,
[OrganizationName] [nvarchar] (100) NOT NULL,
[ModifiedBy] [int] NULL,
[ModifiedDate] [datetime] NULL,
[EntryPointId] [int] NULL,
[ParentOrgId] [int] NULL,
[AllowAttendOverlap] [bit] NOT NULL CONSTRAINT [DF_Organizations_AllowAttendOverlap] DEFAULT ((0)),
[MemberCount] [int] NULL,
[LeaderId] [int] NULL,
[LeaderName] [nvarchar] (50) NULL,
[ClassFilled] [bit] NULL,
[OnLineCatalogSort] [int] NULL,
[PendingLoc] [nvarchar] (40) NULL,
[CanSelfCheckin] [bit] NULL,
[NumCheckInLabels] [int] NULL,
[CampusId] [int] NULL,
[AllowNonCampusCheckIn] [bit] NULL,
[NumWorkerCheckInLabels] [int] NULL,
[ShowOnlyRegisteredAtCheckIn] [bit] NULL,
[Limit] [int] NULL,
[GenderId] [int] NULL,
[Description] [nvarchar] (max) NULL,
[BirthDayStart] [datetime] NULL,
[BirthDayEnd] [datetime] NULL,
[LastDayBeforeExtra] [datetime] NULL,
[RegistrationTypeId] [int] NULL,
[ValidateOrgs] [nvarchar] (60) NULL,
[PhoneNumber] [nvarchar] (25) NULL,
[RegistrationClosed] [bit] NULL,
[AllowKioskRegister] [bit] NULL,
[WorshipGroupCodes] [nvarchar] (100) NULL,
[IsBibleFellowshipOrg] [bit] NULL,
[NoSecurityLabel] [bit] NULL,
[AlwaysSecurityLabel] [bit] NULL,
[DaysToIgnoreHistory] [int] NULL,
[NotifyIds] [varchar] (50) NULL,
[lat] [float] NULL,
[long] [float] NULL,
[RegSetting] [nvarchar] (max) NULL,
[OrgPickList] [varchar] (max) NULL,
[Offsite] [bit] NULL,
[RegStart] [datetime] NULL,
[RegEnd] [datetime] NULL,
[LimitToRole] [nvarchar] (20) NULL,
[OrganizationTypeId] [int] NULL,
[MemberJoinScript] [nvarchar] (50) NULL,
[AddToSmallGroupScript] [nvarchar] (50) NULL,
[RemoveFromSmallGroupScript] [nvarchar] (50) NULL,
[SuspendCheckin] [bit] NULL,
[NoAutoAbsents] [bit] NULL,
[PublishDirectory] [int] NULL,
[ConsecutiveAbsentsThreshold] [int] NULL,
[IsRecreationTeam] [bit] NOT NULL CONSTRAINT [DF_Organizations_IsRecreationTeam] DEFAULT ((0)),
[NotWeekly] [bit] NULL,
[IsMissionTrip] [bit] NULL,
[NoCreditCards] [bit] NULL,
[GiftNotifyIds] [varchar] (50) NULL,
[VisitorDate] AS (CONVERT([datetime],dateadd(day, -((7)*isnull([RollSheetVisitorWks],(3))),CONVERT([date],getdate(),(0))),(0))),
[UseBootstrap] [bit] NULL,
[PublicSortOrder] [varchar] (15) NULL,
[UseRegisterLink2] [bit] NULL,
[AppCategory] [varchar] (15) NULL,
[RegistrationTitle] [nvarchar] (200) NULL,
[PrevMemberCount] [int] NULL,
[ProspectCount] [int] NULL,
[RegSettingXml] [xml] NULL,
[AttendanceBySubGroups] [bit] NULL
)
GO
IF @@ERROR<>0 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION
GO
IF @@TRANCOUNT=0 BEGIN INSERT INTO #tmpErrors (Error) SELECT 1 BEGIN TRANSACTION END
GO
