using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using CmsData;
using CmsWeb.Models;
using Dapper;
using Elmah;
using UtilityExtensions;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace CmsWeb.Areas.Manage.Controllers
{
    [RouteArea("Manage"), Route("~/Manage/Emails/{action}/{id?}")]
	public class EmailsController : CmsController
	{
        [Route("~/Emails")]
		public ActionResult Index()
		{
			var m = new EmailsModel();
			return View(m);
		}

		public ActionResult SentBy(int? id)
		{
			var m = new EmailsModel { senderid = id };
			return View("Index", m);
		}

		public ActionResult SentTo(int? id)
		{
			var m = new EmailsModel { peopleid = id };
			return View("Index", m);
		}

        [Route("~/Emails/Details/{id:int}")]
        [Route("~/Manage/Emails/Details/{id:int}")]
		public ActionResult Details(int id)
		{
			var m = new EmailModel(id);
		    if (m.queue == null)
		        return Content("no email found");
			var curruser = DbUtil.Db.LoadPersonById(Util.UserPeopleId ?? 0);
            if (curruser == null)
    			return Content("no user");
            if (User.IsInRole("Admin") 
                || User.IsInRole("ManageEmails")
                || User.IsInRole("Finance")
                || m.queue.FromAddr == curruser.EmailAddress 
                || m.queue.QueuedBy == curruser.PeopleId
                || m.queue.EmailQueueTos.Any(et => et.PeopleId == curruser.PeopleId))
    			return View(m);
			return Content("not authorized");
		}

		public ActionResult SeeHtml(int id)
		{
			var m = new EmailModel(id);
		    if (m.queue == null)
		        return Content("no email found");
			var curruser = DbUtil.Db.LoadPersonById(Util.UserPeopleId ?? 0);
            if (curruser == null)
    			return Content("no user");
            if (User.IsInRole("Admin") 
                || User.IsInRole("ManageEmails")
                || User.IsInRole("Finance")
                || m.queue.FromAddr == curruser.EmailAddress 
                || m.queue.QueuedBy == curruser.PeopleId
                || m.queue.EmailQueueTos.Any(et => et.PeopleId == curruser.PeopleId))
    			return View(m);
			return Content("not authorized");
		}

        public ActionResult GetEmailBody(int id)
        {
            var m = new EmailModel(id);
            if (m.queue == null)
                return Content("no email found");
            var curruser = DbUtil.Db.LoadPersonById(Util.UserPeopleId ?? 0);
            if (curruser == null)
                return Content("no user");
            if (User.IsInRole("Admin")
                || User.IsInRole("ManageEmails")
                || User.IsInRole("Finance")
                || m.queue.FromAddr == curruser.EmailAddress
                || m.queue.QueuedBy == curruser.PeopleId
                || m.queue.EmailQueueTos.Any(et => et.PeopleId == curruser.PeopleId))
                return Content(m.queue.Body);
            return Content("not authorized");
        }

		public ActionResult ConvertToSearch(int id)
		{
            var cc = DbUtil.Db.ScratchPadCondition();
            cc.Reset();
		    cc.AddNewClause(QueryType.EmailRecipient, CompareType.Equal, id);
            cc.Save(DbUtil.Db);
		    return Redirect("/Query/" + cc.Id);
		}

        public ActionResult Tracking(int id)
        {
            ViewBag.emailID = id;
            return View();
        }

		[Authorize(Roles = "Developer")]
		public ActionResult SendNow(int id)
		{
			string host = Util.Host;
			// save these from HttpContext to set again inside thread local storage
			var useremail = Util.UserEmail;
			var isinroleemailtest = User.IsInRole("EmailTest");

			System.Threading.Tasks.Task.Factory.StartNew(() =>
			{
				Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
				try
				{
					var Db = DbUtil.Create(host);
					var cul = Db.Setting("Culture", "en-US");
					Thread.CurrentThread.CurrentUICulture = new CultureInfo(cul);
					Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture(cul);
					// set these again inside thread local storage
					Util.UserEmail = useremail;
					Util.IsInRoleEmailTest = isinroleemailtest;
					Db.SendPeopleEmail(id);
				}
				catch (Exception ex)
				{
					var ex2 = new Exception("Emailing error for queueid " + id, ex);
					ErrorLog errorLog = ErrorLog.GetDefault(null);
					errorLog.Log(new Error(ex2));

					var Db = DbUtil.Create(host);
					var equeue = Db.EmailQueues.Single(ee => ee.Id == id);
					equeue.Error = ex.Message.Truncate(200);
					Db.SubmitChanges();
				}
			});
			return Redirect("/Emails/Details/" + id);
		}

		public ActionResult DeleteQueued(int id)
		{
			var m = new EmailModel(id);
		    if (m.queue == null)
		        return Redirect("/Emails");
            if (m.queue.Sent.HasValue || !m.queue.SendWhen.HasValue || !m.CanDelete())
				return Redirect("/");
		    DeleteEmail(id);
		    return Redirect("/Emails");
		}

	    private static void DeleteEmail(int id)
	    {
	        var cn = new SqlConnection(Util.ConnectionString);
	        cn.Open();
	        cn.Execute(@"
                DELETE dbo.EmailLinks WHERE EmailID = @id;
                DELETE dbo.EmailResponses WHERE Id = @id;
                DELETE dbo.EmailQueueTo WHERE Id = @id;
                DELETE dbo.EmailQueue WHERE Id = @id;
                ", new {id});
	    }

	    [Authorize(Roles = "Admin")]
		public ActionResult Delete(int id)
		{
			var m = new EmailModel(id);
            if (!m.CanDelete())
				return Redirect("/");
		    DeleteEmail(id);
			return Redirect("/Emails");
		}

		public ActionResult Resend(int id)
		{
			var email = (from e in DbUtil.Db.EmailQueues
						 where e.Id == id
						 select e).Single();
		    var et = email.EmailQueueTos.First();
		    var p = DbUtil.Db.LoadPersonById(et.PeopleId);

            if(email.FinanceOnly == true)
                DbUtil.Db.EmailFinanceInformation(email.FromAddr, p, email.Subject, email.Body);
            else
    			DbUtil.Db.Email(email.FromAddr, p, email.Subject, email.Body);

		    TempData["message"] = "Mail Resent";
			return RedirectToAction("Details", new { id });
		}

		public ActionResult MakePublic(int id)
		{
			var email = (from e in DbUtil.Db.EmailQueues
						 where e.Id == id
						 select e).Single();
			var m = new EmailModel(id);
			if (!User.IsInRole("Admin") && m.queue.QueuedBy != Util.UserPeopleId)
				return Redirect("/");
			email.PublicX = true;
			DbUtil.Db.SubmitChanges();
            return Redirect("/EmailView/" + id);
		}

		[HttpPost]
        public ActionResult Recipients(int id, FilterType filterType, int? page, int pageSize)
		{
			var m = new EmailModel(id, filterType, page, pageSize);
            return View(m);
		}

		public ActionResult List(EmailsModel m)
		{
			UpdateModel(m.Pager);
			return View(m);
		}

        public new ActionResult View(string id)
        {
            return Redirect("/EmailView/" + id);
        }

        [Route("~/Emails/Failed/{id?}")]
		public ActionResult Failed(int? id, string email)
		{
			var isadmin = User.IsInRole("Admin");
			var isdevel = User.IsInRole("Developer");
			var q = from e in DbUtil.Db.EmailQueueToFails
					where id == null || id == e.PeopleId
					where email == null || email == e.Email
					let et = DbUtil.Db.EmailQueueTos.SingleOrDefault(ef => ef.Id == e.Id && ef.PeopleId == e.PeopleId)
					orderby e.Time descending
					select new MailFail
						   {
							   time = e.Time,
							   eventx = e.EventX,
							   type = e.Bouncetype,
							   reason = e.Reason,
							   emailid = e.Id,
							   name = et != null ? et.Person.Name : "unknown",
							   subject = et != null ? et.EmailQueue.Subject : "unknown",
							   peopleid = e.PeopleId,
							   email = e.Email,
							   devel = isdevel,
							   admin = isadmin
						   };
			return View(q.Take(300));
		}

		[Authorize(Roles = "Admin")]
		[HttpPost]
		public ActionResult Unblock(string email)
		{
			var deletebounce = ConfigurationManager.AppSettings["DeleteBounce"] + email;
			var wc = new WebClient();
			var ret = wc.DownloadString(deletebounce);
			return Content(ret);
		}

		[Authorize(Roles = "Developer")]
		[HttpPost]
		public ActionResult Unspam(string email)
		{
			var deletespam = ConfigurationManager.AppSettings["DeleteSpamReport"] + email;
			var wc = new WebClient();
			var ret = wc.DownloadString(deletespam);
		    DbUtil.Db.SpamReporterRemove(email);
			return Content(ret);
		}

		public class MailFail
		{
			public DateTime? time { get; set; }
			public string eventx { get; set; }
			public string type { get; set; }
			public string reason { get; set; }
			public string name { get; set; }
			public int? peopleid { get; set; }
			public int? emailid { get; set; }
			public string subject { get; set; }
			public string email { get; set; }
			public bool admin { get; set; }
			public bool devel { get; set; }
			public bool canunblock
			{
				get
				{
					if (!admin || !email.HasValue())
						return false;
					if ((eventx != "bounce" || type != "blocked") && eventx != "dropped")
						return false;
					if (eventx == "dropped" && reason.Contains("spam", ignoreCase: true))
						return false;
					var deletebounce = ConfigurationManager.AppSettings["DeleteBounce"];
					if (!deletebounce.HasValue())
						return false;
					return true;
				}
			}
			public bool canunspam
			{
				get
				{
					if (!devel || !email.HasValue())
						return false;
					if ((eventx != "bounce" || type != "blocked") && eventx != "dropped")
						return false;
					if (eventx == "dropped" && !reason.Contains("spam", ignoreCase: true))
						return false;
					var deletespam = ConfigurationManager.AppSettings["DeleteSpamReport"];
					if (!deletespam.HasValue())
						return false;
					return true;
				}
			}
		}
	}

    [RouteArea("Manage")]
	public class EmailViewController : CmsControllerNoHttps
	{
        [Route("~/EmailView/{id}")]
        [Route("~/Emails/View/{id}")]
		public new ActionResult View(string id)
		{
		    var iid = id.ToInt();
			var email = DbUtil.Db.EmailQueues.SingleOrDefault(ee => ee.Id == iid);
			if (email == null)
				return Content("email document not found");
			if ((email.PublicX ?? false) == false)
				return Content("no email available");
			var em = new EmailQueue
			{
				Subject = email.Subject,
                Body = Regex.Replace(email.Body, "({first}|{tracklinks}|{track})", "", RegexOptions.IgnoreCase)
			};
			return View(em);
		}
	}
}
