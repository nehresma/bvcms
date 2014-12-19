CREATE FUNCTION [dbo].[OrgPeopleProspects](@oid INT, @showhidden BIT)
RETURNS TABLE
RETURN
(
	SELECT om.PeopleId
	, 'Prospects' Tab
	, om.AttendPct AttPct
	, a.MeetingDate LastAttended
	, om.EnrollmentDate Joined
	, CAST(NULL AS DATETIME) Dropped
	, om.InactiveDate
	, mt.Code MemberCode
	, mt.Description MemberType
	, ISNULL(om.Hidden, 0) Hidden
	, (SELECT STUFF((
			SELECT CHAR(10) + mt.Name 
			FROM dbo.OrgMemMemTags omt 
			JOIN dbo.MemberTags mt ON mt.Id = omt.MemberTagId 
			WHERE omt.OrgId = om.OrganizationId AND omt.PeopleId = om.PeopleId
			FOR XML PATH(''),TYPE
			).value('text()[1]','nvarchar(max)'),1,1,N''
	  )) Groups

	FROM dbo.OrganizationMembers om
	LEFT JOIN dbo.Attend a ON a.OrganizationId = om.OrganizationId 
			AND a.PeopleId = om.PeopleId AND a.AttendanceFlag = 1
			AND a.MeetingDate = (SELECT MAX(la.MeetingDate)
					FROM dbo.Attend la
					WHERE la.OrganizationId = @oid
					AND la.PeopleId = a.PeopleId
						AND la.AttendanceFlag = 1)
	LEFT JOIN lookup.MemberType mt ON mt.Id = om.MemberTypeId
	WHERE om.OrganizationId = @oid
	AND om.MemberTypeId = 311 -- prospect
	AND (@showhidden = 1 OR ISNULL(om.Hidden, 0) = 0)
)
GO
IF @@ERROR<>0 AND @@TRANCOUNT>0 ROLLBACK TRANSACTION
GO
IF @@TRANCOUNT=0 BEGIN INSERT INTO #tmpErrors (Error) SELECT 1 BEGIN TRANSACTION END
GO