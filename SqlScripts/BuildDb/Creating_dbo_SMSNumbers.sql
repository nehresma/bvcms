CREATE TABLE [dbo].[SMSNumbers]
(
[ID] [int] NOT NULL IDENTITY(1, 1),
[GroupID] [int] NOT NULL CONSTRAINT [DF_SMSNumber_GroupID] DEFAULT ((0)),
[Number] [nvarchar] (50) NOT NULL CONSTRAINT [DF_SMSNumbers_Number] DEFAULT (''),
[LastUpdated] [datetime] NOT NULL CONSTRAINT [DF_SMSNumbers_LastUpdated] DEFAULT ((0))
)
GO
IF @@ERROR<>0 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION
GO
IF @@TRANCOUNT=0 BEGIN INSERT INTO #tmpErrors (Error) SELECT 1 BEGIN TRANSACTION END
GO
